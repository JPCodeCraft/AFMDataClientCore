using Albion.Network;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Events;

public sealed class CraftingFocusUpdateEvent : BaseEvent
{
    public long? ActorId { get; private set; }
    public long? Timestamp { get; private set; }
    public DateTime? UpdatedAt => Timestamp is { } timestamp ? UtcTicks(timestamp) : null;
    public double? FocusDelta { get; private set; }
    public double? FocusBalance { get; private set; }

    public CraftingFocusUpdateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.ContainsKey(0) || !parameters.ContainsKey(1)) return;
            var actorId = Number(parameters, 0);
            var timestamp = Number(parameters, 1);
            ActorId = actorId;
            Timestamp = timestamp;
        });
        TryRead(() =>
        {
            // The game's resource update adds field 2 as a signed delta and uses
            // field 3 as the authoritative resulting balance. Both are floats,
            // not the fixed-point values used by farming growth and nutrition.
            if (parameters.TryGetValue(2, out var rawDelta))
            {
                var delta = rawDelta.ToDouble();
                if (double.IsFinite(delta)) FocusDelta = delta;
            }
            if (parameters.TryGetValue(3, out var rawBalance))
            {
                var balance = rawBalance.ToDouble();
                if (double.IsFinite(balance) && balance >= 0) FocusBalance = balance;
            }
        });
    }
}
