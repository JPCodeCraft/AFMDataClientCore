using System.Text.Json;

namespace AlbionDataAvalonia.Locations;

/// <summary>Optional facts supplied by an observed entrance or an exact instance layout.
/// A Mists instance's Layout is its fixed master layout, which names its minimap.</summary>
public sealed record ActivityInstanceMetadata(int? Tier = null, string? ZoneType = null, string? Mode = null,
    string? Faction = null, int? Enchantment = null, string? Rarity = null, string? Layout = null)
{
    private static readonly IReadOnlyDictionary<string, (ActivityMapKind Kind, ActivityInstanceMetadata Metadata)> Mists = LoadMists();
    private static readonly IReadOnlyDictionary<string, string> MistsLayouts = LoadMistsLayouts();

    public static ActivityInstanceMetadata FromEntrance(string? uniqueName, string? dungeonType,
        int? level, bool? isAlreadyEntered)
    {
        var name = uniqueName?.Trim().ToUpperInvariant() ?? string.Empty;
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["MISTS", "SOLO" or "DUO", ..])
            return new(ZoneType: parts.Length >= 3 ? parts[2] switch { "YELLOW" => "Yellow", "BLACK" => "Black", _ => null } : null,
                Mode: parts[1] == "SOLO" ? "Solo" : "Duo", Rarity: RarityName(level));

        // A tiered random-dungeon entrance explicitly describes its own tier;
        // the tier of the surrounding world map is never substituted here.
        if (parts.Length < 2 || parts[0].Length != 2 || parts[0][0] != 'T' || parts[0][1] is < '1' or > '8')
            return new();
        var tokens = parts.Concat((dungeonType ?? string.Empty).ToUpperInvariant().Split('_')).ToHashSet(StringComparer.Ordinal);
        var mode = tokens.Contains("AVALON") ? "Avalonian" : tokens.Contains("SOLO") ? "Solo"
            : tokens.Contains("VETERAN") || tokens.Contains("PORTAL") || parts.Any(IsFaction) ? "Group" : null;
        var faction = parts.Select(FactionName).FirstOrDefault(value => value is not null);
        return new(Tier: parts[0][1] - '0', Mode: mode, Faction: faction,
            Enchantment: isAlreadyEntered == false && level is >= 0 and <= 4 ? level : null);
    }

    public static ActivityInstanceMetadata FromTransition(ActivityMapKind kind, string? layoutId, byte[]? dungeonInformation)
    {
        if (kind is not (ActivityMapKind.Mists or ActivityMapKind.MistsDungeon)) return new();
        var result = layoutId is not null && Mists.TryGetValue(layoutId, out var map) && map.Kind == kind
            ? map.Metadata : new ActivityInstanceMetadata();
        if (layoutId is not null && MistsLayouts.TryGetValue(layoutId, out var layout)) result = result with { Layout = layout };
        // Mists layout names describe templates, not the instance's rarity.
        // The trailing dungeon-information byte carries rarity for Mists only.
        if (kind == ActivityMapKind.Mists && dungeonInformation is { Length: > 0 and <= 1024 })
            result = result with { Rarity = RarityName(dungeonInformation[^1]) };
        return result;
    }

    private static string? RarityName(int? value) => value switch
    {
        0 => "Common",
        1 => "Uncommon",
        2 => "Rare",
        3 => "Epic",
        4 => "Legendary",
        _ => null
    };

    private static bool IsFaction(string value) => FactionName(value) is not null;
    private static string? FactionName(string value) => value switch
    {
        "HERETIC" => "Heretics",
        "KEEPER" or "KPR" => "Keepers",
        "MORGANA" or "MOR" => "Morgana",
        "UNDEAD" or "UND" => "Undead",
        "AVALON" => "Avalonian",
        _ => null
    };

    private static IReadOnlyDictionary<string, (ActivityMapKind Kind, ActivityInstanceMetadata Metadata)> LoadMists()
    {
        using var document = LoadWorld();
        var result = new Dictionary<string, (ActivityMapKind, ActivityInstanceMetadata)>(StringComparer.Ordinal);
        foreach (var map in document.RootElement.GetProperty("mists").EnumerateObject())
        {
            var values = map.Value.EnumerateArray().ToArray();
            if (values.Length != 4 || !Enum.TryParse<ActivityMapKind>(values[3].GetString(), out var kind)) continue;
            result.Add(map.Name, (kind, new(values[0].GetInt32(), values[1].GetString(), values[2].GetString())));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadMistsLayouts()
    {
        using var document = LoadWorld();
        return document.RootElement.TryGetProperty("mistsLayouts", out var layouts)
            ? layouts.EnumerateObject().Where(x => x.Value.GetString() is { Length: > 0 })
                .ToDictionary(x => x.Name, x => x.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>();
    }

    private static JsonDocument LoadWorld()
    {
        using var stream = typeof(ActivityInstanceMetadata).Assembly.GetManifestResourceStream("AFMDataClient.Core.Locations.activity-world.json")
            ?? throw new InvalidOperationException("Activity world classification resource is missing.");
        return JsonDocument.Parse(stream);
    }
}
