using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public sealed class KnockedDownEvent : BaseEvent
{
    public long? VictimObjectId { get; }
    public string VictimName { get; }
    public long? KillerObjectId { get; }
    public string KillerName { get; }
    public KnockedDownEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var player = ActivityPacketValues.Long(parameters, 4).Optional;
        VictimObjectId = player > 0 ? player : ActivityPacketValues.Long(parameters, 0).Optional;
        VictimName = ActivityPacketValues.Text(parameters, 5);
        KillerObjectId = ActivityPacketValues.Long(parameters, 2).Optional;
        KillerName = ActivityPacketValues.Text(parameters, 3);
    }
}
