using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public class HealthUpdatesEvent : BaseEvent
{
    public IReadOnlyList<HealthUpdateEntry> HealthUpdates { get; } = [];

    public HealthUpdatesEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var target = ActivityPacketValues.Long(parameters, 0);
        if (target.State != PacketFieldState.Valid || target.Value <= 0) return;
        var times = ActivityPacketValues.Indexed(parameters, 1);
        var deltas = ActivityPacketValues.Indexed(parameters, 2);
        var health = ActivityPacketValues.Indexed(parameters, 3);
        var malformedHealthField = parameters.TryGetValue(3, out var rawHealth)
            && rawHealth is not System.Collections.IDictionary
            && (rawHealth is not System.Collections.IEnumerable || rawHealth is string)
            && ActivityPacketValues.Double(rawHealth).State == PacketFieldState.Invalid;
        var sources = ActivityPacketValues.Indexed(parameters, 6);
        var spells = ActivityPacketValues.Indexed(parameters, 7);
        var updates = new List<HealthUpdateEntry>(deltas.Count);
        foreach (var (index, rawDelta) in deltas.OrderBy(pair => pair.Key))
        {
            var delta = ActivityPacketValues.Double(rawDelta);
            if (delta.State != PacketFieldState.Valid) continue;
            var resulting = ActivityPacketValues.At(health, index, ActivityPacketValues.Double);
            var state = resulting.State == PacketFieldState.Missing && malformedHealthField
                ? PacketFieldState.Invalid : resulting.State;
            var source = ActivityPacketValues.At(sources, index, ActivityPacketValues.Long).Optional;
            var spell = ActivityPacketValues.At(spells, index, ActivityPacketValues.Long).Optional;
            updates.Add(new HealthUpdateEntry(target.Value, source ?? 0, delta.Value,
                resulting.Optional ?? double.NaN, spell is >= 0 and <= int.MaxValue ? (int)spell.Value : 0,
                ActivityPacketValues.At(times, index, ActivityPacketValues.Long).Optional)
            {
                EntryIndex = index,
                NewHealthState = state,
                ResultingHealth = resulting.Optional,
                SourceObjectId = source,
                SpellIndex = spell is >= 0 and <= int.MaxValue ? (int)spell.Value : null
            });
        }
        HealthUpdates = updates;
    }

}
