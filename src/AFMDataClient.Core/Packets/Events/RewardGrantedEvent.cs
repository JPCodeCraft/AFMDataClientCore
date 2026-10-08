using Albion.Network;
using Serilog;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>A reward whose recipient and activity need client-side evidence.</summary>
public sealed class RewardGrantedEvent : BaseEvent
{
    public int ItemId { get; }
    public PacketFieldState ItemIdState { get; }
    public long? Quantity { get; }
    public PacketFieldState QuantityState { get; }
    public bool IsValid => ItemIdState == PacketFieldState.Valid && ItemId > 0
        && QuantityState == PacketFieldState.Valid && Quantity is > 0;

    public RewardGrantedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        var item = ActivityPacketValues.Long(parameters, 1);
        ItemIdState = item.Optional is <= 0 or > int.MaxValue ? PacketFieldState.Invalid : item.State;
        ItemId = ItemIdState == PacketFieldState.Valid ? (int)item.Value : 0;
        var amount = ActivityPacketValues.Long(parameters, 3);
        QuantityState = amount.Optional is <= 0 ? PacketFieldState.Invalid : amount.State;
        Quantity = QuantityState == PacketFieldState.Valid ? amount.Value : null;
    }
}
