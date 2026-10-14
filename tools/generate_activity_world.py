"""Generate compact activity map descriptors; never retain the full world dump."""
import json
import re
from pathlib import Path
from urllib.request import urlopen

REVISION = "d5a47d4ba49d5bf2c0253b224be85fe26121cfa1"
SOURCE = f"https://raw.githubusercontent.com/JPCodeCraft/ao-bin-dumps/{REVISION}/cluster/world.json"
MISTS_SOURCE = f"https://raw.githubusercontent.com/JPCodeCraft/ao-bin-dumps/{REVISION}/mists.json"


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


if __name__ == "__main__":
    with urlopen(SOURCE, timeout=30) as response:
        clusters = json.load(response)["world"]["clusters"]["cluster"]
    entries = {
        cluster["@id"]: [cluster.get("@displayname") or cluster["@id"],
                         classify(cluster.get("@type", "")), tier(cluster), zone_type(cluster)]
        for cluster in clusters if classify(cluster.get("@type", "")) != "Unknown"
    }
    with urlopen(MISTS_SOURCE, timeout=30) as response:
        mists = mists_entries(json.load(response)["mists"]["mistsmaps"]["mapset"])
    target = Path(__file__).resolve().parents[1] / "src/AFMDataClient.Core/Locations/activity-world.json"
    target.write_text(json.dumps({"source": SOURCE, "mistsSource": MISTS_SOURCE,
                                 "revision": REVISION, "maps": entries, "mists": mists},
                                 ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Generated {len(entries)} activity maps and {len(mists)} Mists templates ({target.stat().st_size} bytes)")
