using Albion.Network;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>A container's current item identities; this packet does not carry quantities.</summary>
public sealed class InventoryStateEvent : BaseEvent
{
    public IReadOnlyList<long> ItemObjectIds { get; private set; } = [];
    public Guid? ContainerId { get; private set; }
    public long? RawParameter2 { get; private set; }

    public InventoryStateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.TryGetValue(0, out var rawItems) || rawItems is not Array array
                || array.Length > 4096 || !parameters.TryGetValue(1, out var rawContainer)) return;
            var container = rawContainer.ToGuid();
            if (container is null || container == Guid.Empty) return;
            var items = array.Cast<object>().Select(value => value.ToLong()).ToArray();
            ItemObjectIds = items;
            ContainerId = container;
        });
        TryRead(() =>
        {
            if (parameters.ContainsKey(2)) RawParameter2 = Number(parameters, 2);
        });
    }
}
