using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>Supplementary evidence; overlap with TakeSilver is not established.</summary>
public sealed class PartySilverGainedEvent : BaseEvent
{
    public long? GameTimeMilliseconds { get; }
    public long? TargetObjectId { get; }
    public long? NetRaw { get; }
    public long? GrossRaw { get; }
    public PacketFieldState NetState { get; }
    public PacketFieldState GrossState { get; }
    public PartySilverGainedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        GameTimeMilliseconds = ActivityPacketValues.Long(parameters, 0).Optional;
        TargetObjectId = ActivityPacketValues.Long(parameters, 1).Optional;
        var net = ActivityPacketValues.Long(parameters, 2);
        var gross = ActivityPacketValues.Long(parameters, 3);
        if (net.State == PacketFieldState.Valid && net.Value < 0) net = new(PacketFieldState.Invalid, default);
        if (gross.State == PacketFieldState.Valid && gross.Value < 0) gross = new(PacketFieldState.Invalid, default);
        NetState = net.State; GrossState = gross.State;
        NetRaw = net.Optional is >= 0 ? net.Optional : null;
        GrossRaw = gross.Optional is >= 0 ? gross.Optional : null;
    }
}
