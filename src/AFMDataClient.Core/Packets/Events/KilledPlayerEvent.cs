using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public sealed class KilledPlayerEvent : BaseEvent
{
    public long? KillerObjectId { get; }
    public long? VictimObjectId { get; }
    public string VictimName { get; }
    public KilledPlayerEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        KillerObjectId = ActivityPacketValues.Long(parameters, 0).Optional;
        VictimObjectId = ActivityPacketValues.Long(parameters, 1).Optional;
        VictimName = ActivityPacketValues.Text(parameters, 2);
    }
}
