"""Generate only activity map names/types; never retain the full world dump."""
import json
from pathlib import Path
from urllib.request import urlopen

REVISION = "d5a47d4ba49d5bf2c0253b224be85fe26121cfa1"
SOURCE = f"https://raw.githubusercontent.com/JPCodeCraft/ao-bin-dumps/{REVISION}/cluster/world.json"


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


if __name__ == "__main__":
    with urlopen(SOURCE, timeout=30) as response:
        clusters = json.load(response)["world"]["clusters"]["cluster"]
    entries = {
        cluster["@id"]: [cluster.get("@displayname") or cluster["@id"], classify(cluster.get("@type", ""))]
        for cluster in clusters if classify(cluster.get("@type", "")) != "Unknown"
    }
    target = Path(__file__).resolve().parents[1] / "src/AFMDataClient.Core/Locations/activity-world.json"
    target.write_text(json.dumps({"source": SOURCE, "revision": REVISION, "maps": entries},
                                 ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Generated {len(entries)} activity map descriptors ({target.stat().st_size} bytes)")
