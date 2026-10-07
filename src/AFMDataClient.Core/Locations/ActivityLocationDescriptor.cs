using System.Text.Json;

namespace AlbionDataAvalonia.Locations;

public enum ActivityMapKind
{
    Unknown, OpenWorld, Hub, RandomDungeon, StaticDungeon, CorruptedDungeon, Hellgate,
    Expedition, Mists, MistsDungeon, AbyssalDepths, Arena, DragonArea, Island, Hideout
}

public sealed record ActivityLocationDescriptor(string RawLocationId, string DisplayName, ActivityMapKind Kind,
    Guid? InstanceId, string? ParentClusterId, string? SourceClusterId)
{
    public bool IsInstance => Kind is ActivityMapKind.RandomDungeon or ActivityMapKind.StaticDungeon
        or ActivityMapKind.CorruptedDungeon or ActivityMapKind.Hellgate or ActivityMapKind.Expedition
        or ActivityMapKind.Mists or ActivityMapKind.MistsDungeon or ActivityMapKind.AbyssalDepths
        or ActivityMapKind.Arena or ActivityMapKind.DragonArea;
    public bool IsHub => Kind is ActivityMapKind.Hub or ActivityMapKind.Island or ActivityMapKind.Hideout;
}

internal static class ActivityWorldReference
{
    public const string SourceRevision = "d5a47d4ba49d5bf2c0253b224be85fe26121cfa1";
    private static readonly IReadOnlyDictionary<string, (string Name, ActivityMapKind Kind)> Maps = Load();
    private static readonly (string Token, string Name, ActivityMapKind Kind)[] Generated =
    [
        ("CORRUPTEDDUNGEON", "Corrupted Dungeon", ActivityMapKind.CorruptedDungeon),
        ("RANDOMDUNGEON", "Dungeon", ActivityMapKind.RandomDungeon),
        ("MISTSDUNGEON", "Mists Dungeon", ActivityMapKind.MistsDungeon),
        ("MISTS", "Mists", ActivityMapKind.Mists),
        ("HELLDUNGEON", "Abyssal Depths", ActivityMapKind.AbyssalDepths),
        ("HELLCLUSTER", "Hellgate", ActivityMapKind.Hellgate),
        ("EXPEDITION", "Expedition", ActivityMapKind.Expedition),
        ("DRAGONAREA", "Dragon Area", ActivityMapKind.DragonArea),
        ("ARENA", "Arena", ActivityMapKind.Arena),
        ("ISLAND", "Island", ActivityMapKind.Island),
        ("HIDEOUT", "Hideout", ActivityMapKind.Hideout)
    ];

    public static ActivityLocationDescriptor Resolve(string? raw, string? parent, string? source)
    {
        var id = raw?.Trim().Trim('"', '\'') ?? string.Empty;
        var parts = id.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var guid = parts.Select(part => Guid.TryParse(part, out var instance) ? (Guid?)instance : null).FirstOrDefault(value => value.HasValue);
        if (id.Contains('@'))
        {
            foreach (var generated in Generated)
            {
                var tokenIndex = Array.FindIndex(parts, part => part.Equals(generated.Token, StringComparison.OrdinalIgnoreCase));
                if (tokenIndex >= 0)
                {
                    var instance = parts.Skip(tokenIndex + 1)
                        .Select(part => Guid.TryParse(part, out var parsed) ? (Guid?)parsed : null)
                        .FirstOrDefault(value => value.HasValue);
                    return new(id, generated.Name, generated.Kind, instance, parent, source);
                }
            }
        }
        foreach (var candidate in new[] { id }.Concat(parts.Reverse()))
        {
            if (Maps.TryGetValue(candidate, out var map))
                return new(id, map.Name, map.Kind, guid, parent, source);
        }
        return new(id, string.IsNullOrEmpty(id) ? "Unknown" : id, ActivityMapKind.Unknown, guid, parent, source);
    }

    private static IReadOnlyDictionary<string, (string Name, ActivityMapKind Kind)> Load()
    {
        using var stream = typeof(ActivityWorldReference).Assembly.GetManifestResourceStream("AFMDataClient.Core.Locations.activity-world.json")
            ?? throw new InvalidOperationException("Activity world classification resource is missing.");
        using var document = JsonDocument.Parse(stream);
        var maps = new Dictionary<string, (string, ActivityMapKind)>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in document.RootElement.GetProperty("maps").EnumerateObject())
        {
            var values = map.Value.EnumerateArray().ToArray();
            if (values.Length == 2 && Enum.TryParse<ActivityMapKind>(values[1].GetString(), out var kind))
                maps[map.Name] = (values[0].GetString() ?? map.Name, kind);
        }
        return maps;
    }
}
