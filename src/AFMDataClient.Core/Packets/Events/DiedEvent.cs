using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public sealed class DiedEvent : BaseEvent
{
    public long? VictimObjectId { get; }
    public string VictimName { get; }
    public string VictimGuild { get; }
    public long? KillerObjectId { get; }
    public string KillerName { get; }
    public string KillerGuild { get; }
    public bool? IsLethal { get; }
    public PacketFieldState LethalityState { get; }
    public DiedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        VictimObjectId = ActivityPacketValues.Long(parameters, 1).Optional;
        VictimName = ActivityPacketValues.Text(parameters, 2); VictimGuild = ActivityPacketValues.Text(parameters, 3);
        KillerObjectId = ActivityPacketValues.Long(parameters, 9).Optional;
        KillerName = ActivityPacketValues.Text(parameters, 10); KillerGuild = ActivityPacketValues.Text(parameters, 11);
        // A death after a knockdown flags lethality at key 17. Arena deaths have no knockdown
        // and carry only key 8; key 17 has never been seen false.
        var lethal = ActivityPacketValues.Boolean(parameters, 17);
        if (lethal.State == PacketFieldState.Missing && ActivityPacketValues.Boolean(parameters, 8) is { State: PacketFieldState.Valid, Value: true } death)
            lethal = death;
        IsLethal = lethal.Optional; LethalityState = lethal.State;
    }
}
