using Albion.Network;
using System.Text.Json;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>Destiny Board progress evidence; values are not additional fame awards.</summary>
public sealed class AchievementProgressInfoEvent : BaseEvent
{
    public long? ActorObjectId { get; }
    public PacketFieldState ActorObjectIdState { get; }
    public int? AchievementIndex { get; }
    public PacketFieldState AchievementIndexState { get; }
    public IReadOnlyList<double> ProgressValues { get; } = Array.Empty<double>();
    public PacketFieldState ProgressState { get; }

    public AchievementProgressInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var actor = ActivityPacketValues.Long(parameters, 0);
        ActorObjectIdState = actor.Optional is <= 0 ? PacketFieldState.Invalid : actor.State;
        ActorObjectId = ActorObjectIdState == PacketFieldState.Valid ? actor.Value : null;
        var achievement = ActivityPacketValues.Long(parameters, 1);
        AchievementIndexState = achievement.Optional is < 0 or > ushort.MaxValue ? PacketFieldState.Invalid : achievement.State;
        AchievementIndex = AchievementIndexState == PacketFieldState.Valid ? (int)achievement.Value : null;

        if (!parameters.TryGetValue(4, out var progress))
        {
            ProgressState = PacketFieldState.Missing;
            return;
        }
        ProgressState = PacketFieldState.Invalid;
        if (progress is not string text || text.Length is 0 or > 4096) return;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var values = new List<double>();
            if (document.RootElement.ValueKind != JsonValueKind.Array || !ReadProgress(document.RootElement, values)) return;
            ProgressValues = values.AsReadOnly();
            ProgressState = PacketFieldState.Valid;
        }
        catch (JsonException) { }
    }

    private static bool ReadProgress(JsonElement element, List<double> values)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in element.EnumerateArray())
            {
                if (!ReadProgress(entry, values)) return false;
            }
            return true;
        }
        if (element.ValueKind != JsonValueKind.Number || values.Count >= 64
            || !element.TryGetDouble(out var value) || !double.IsFinite(value) || value < 0) return false;
        values.Add(value);
        return true;
    }
}
