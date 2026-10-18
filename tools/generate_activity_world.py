"""Generate compact activity map descriptors; never retain the full world dump."""
import json
import re
from pathlib import Path
import time
from urllib.request import Request, urlopen

REVISION = "d5a47d4ba49d5bf2c0253b224be85fe26121cfa1"
SOURCE = f"https://raw.githubusercontent.com/JPCodeCraft/ao-bin-dumps/{REVISION}/cluster/world.json"
MISTS_SOURCE = f"https://raw.githubusercontent.com/JPCodeCraft/ao-bin-dumps/{REVISION}/mists.json"
# AlbionWorld publishes each Mists layout's features, including its wisp spawns and border exits.
WORLD_SOURCE = "https://cdn.albionfreemarket.com/AlbionWorld"
MISTS_LANDMARKS = ("mistswispspawn", "mistsborderexit")


def classify(value):
    if value.startswith("PLAYERCITY") or value in {"CITY", "STARTINGCITY"}:
        return "Hub"
    if value == "HIDEOUT":
        return "Hideout"
    if value in {"PLAYERISLAND", "GUILDISLAND", "SHOWROOMISLAND"}:
        return "Island"
    if value.startswith("CORRUPTED_DUNGEON"):
        return "CorruptedDungeon"
    if value.startswith("DUNGEON_HELL"):
        return "Hellgate"
    if "EXPEDITION" in value:
        return "Expedition"
    if value.startswith("ARENA"):
        return "Arena"
    if value.startswith("DUNGEON"):
        return "StaticDungeon"
    if value.startswith(("OPENPVP", "PASSAGE", "TUNNEL")) or value in {"SAFEAREA", "STARTAREA"}:
        return "OpenWorld"
    return "Unknown"


def tier(cluster):
    match = re.search(r"(?:^|_)T([1-8])(?:_|\.)", cluster.get("@file", ""))
    return int(match[1]) if match else None


def zone_type(cluster):
    value = cluster.get("@type", "")
    # City/island names can contain BLACK while their interior remains safe.
    # Leave these without a combat-zone label rather than using that suffix.
    if classify(value) in {"Hub", "Island", "Hideout"}:
        return None
    if "SAFEAREA" in value or value == "STARTAREA":
        return "Blue"
    for token in ("YELLOW", "RED", "BLACK"):
        if token in value.split("_"):
            return token.title()
    return None


def mists_entries(mapsets):
    result = {}
    for mapset in mapsets:
        parts = mapset["@name"].split("_")
        # Resource maps are unrelated content and do not identify solo/duo Mists.
        if "SOLO" not in parts and "DUO" not in parts:
            continue
        mode = "Solo" if "SOLO" in parts else "Duo"
        zone = next((part.title() for part in parts if part in {"YELLOW", "BLACK"}), None)
        kind = "MistsDungeon" if "DUNGEON" in parts else "Mists"
        for entry in mapset["map"]:
            entry_tier = int(entry["@clustertier"])
            if not 1 <= entry_tier <= 8:
                continue
            metadata = [entry_tier, zone, mode, kind]
            previous = result.setdefault(entry["@id"], metadata)
            if previous != metadata:
                raise ValueError(f"Conflicting Mists descriptor: {entry['@id']}")
    return result


def mists_layouts(mapsets):
    """Each Mists map id names one fixed master layout; only its slot contents are rolled."""
    result = {}
    for mapset in mapsets:
        for entry in mapset["map"]:
            previous = result.setdefault(entry["@id"], entry["@mastertemplate"])
            if previous != entry["@mastertemplate"]:
                raise ValueError(f"Conflicting Mists layout: {entry['@id']}")
    return result


def mists_landmarks(layouts):
    """Wisp spawns and border exits stand at fixed points of a layout, so seen ones identify it.

    Each layout's points are flattened to x, y pairs at half-unit precision. The Mists dungeon has no
    published features and is skipped.
    """
    def fetch(url):
        # The CDN refuses Python's default user agent.
        return urlopen(Request(url, headers={"User-Agent": "AFMDataClientCore/generate_activity_world"}), timeout=60)

    # The version file is edge-cached; a per-minute query reads the current one.
    with fetch(f"{WORLD_SOURCE}/map-data-version.json?t={int(time.time() // 60)}") as response:
        version = json.load(response)["version"]
    result = {}
    for layout in sorted(set(layouts.values())):
        if "_DNG_" in layout:
            continue
        with fetch(f"{WORLD_SOURCE}/map-features/maps/{layout}.json?v={version}") as response:
            features = json.load(response)["features"]
        points = sorted({(round(feature["x"] * 2) / 2, round(feature["z"] * 2) / 2)
                         for feature in features if feature.get("type") in MISTS_LANDMARKS})
        if not points:
            raise ValueError(f"Mists layout {layout} has no landmarks")
        result[layout] = [value for point in points for value in point]
    return result, version


if __name__ == "__main__":
    with urlopen(SOURCE, timeout=30) as response:
        clusters = json.load(response)["world"]["clusters"]["cluster"]
    entries = {
        cluster["@id"]: [cluster.get("@displayname") or cluster["@id"],
                         classify(cluster.get("@type", "")), tier(cluster), zone_type(cluster)]
        for cluster in clusters if classify(cluster.get("@type", "")) != "Unknown"
    }
    with urlopen(MISTS_SOURCE, timeout=30) as response:
        mapsets = json.load(response)["mists"]["mistsmaps"]["mapset"]
    mists = mists_entries(mapsets)
    layouts = mists_layouts(mapsets)
    landmarks, world_version = mists_landmarks(layouts)
    target = Path(__file__).resolve().parents[1] / "src/AFMDataClient.Core/Locations/activity-world.json"
    target.write_text(json.dumps({"source": SOURCE, "mistsSource": MISTS_SOURCE,
                                 "revision": REVISION, "maps": entries, "mists": mists, "mistsLayouts": layouts,
                                 "mistsLandmarksSource": f"{WORLD_SOURCE}@{world_version}", "mistsLandmarks": landmarks},
                                 ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Generated {len(entries)} activity maps, {len(mists)} Mists templates, "
          f"{len(set(layouts.values()))} Mists layouts and landmarks for {len(landmarks)} "
          f"({target.stat().st_size} bytes)")
