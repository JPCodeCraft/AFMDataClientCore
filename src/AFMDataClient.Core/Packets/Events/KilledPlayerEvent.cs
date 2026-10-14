using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public sealed class KilledPlayerEvent : BaseEvent
{
    public long? KillerObjectId { get; }
    public long? VictimObjectId { get; }
    public string VictimName { get; }
    public long? RawBaseFame { get; }
    public PacketFieldState BaseFameState { get; }
    // Matching evidence for the host; UpdateFame carries the awarded total and bonuses.
    public double? BaseFame => RawBaseFame / 10000d;

    public KilledPlayerEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        KillerObjectId = ActivityPacketValues.Long(parameters, 0).Optional;
        VictimObjectId = ActivityPacketValues.Long(parameters, 1).Optional;
        VictimName = ActivityPacketValues.Text(parameters, 2);
        var fame = ActivityPacketValues.Long(parameters, 3);
        if (fame.State == PacketFieldState.Valid && fame.Value < 0)
            fame = new(PacketFieldState.Invalid, default);
        RawBaseFame = fame.Optional;
        BaseFameState = fame.State;
    }
}
