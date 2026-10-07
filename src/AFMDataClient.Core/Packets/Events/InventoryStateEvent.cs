using Albion.Network;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>A container's current item identities; this packet does not carry quantities.</summary>
public sealed class InventoryStateEvent : BaseEvent
{
    public IReadOnlyList<long> ItemObjectIds { get; private set; } = [];
    public Guid? ContainerId { get; private set; }
    public long? RawParameter3 { get; private set; }

    public InventoryStateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            // Live container snapshots carry item identities at p0 and the
            // container GUID at p2; p3 is retained without assigning meaning.
            if (!parameters.TryGetValue(0, out var rawItems) || rawItems is not Array { Rank: 1 } array
                || array.Length > 4096 || !parameters.TryGetValue(2, out var rawContainer)) return;
            var container = rawContainer.ToGuid();
            if (container is null || container == Guid.Empty) return;
            var items = array.Cast<object>().Select(value => value.ToLong()).ToArray();
            if (items.Any(value => value < 0)) return;
            ItemObjectIds = items;
            ContainerId = container;
        });
        TryRead(() =>
        {
            if (parameters.ContainsKey(3)) RawParameter3 = Number(parameters, 3);
        });
    }
}
