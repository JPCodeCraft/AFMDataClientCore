using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public class NewMobEvent : BaseEvent
{
    public long? ObjectId { get; }
    public int? MobIndex { get; }
    public double? CurrentHealth { get; }
    public double? MaximumHealth { get; }
    public PacketFieldState CurrentHealthState { get; }
    public PacketFieldState MaximumHealthState { get; }

    public NewMobEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        ObjectId = ActivityPacketValues.Long(parameters, 0).Optional;
        var index = ActivityPacketValues.Long(parameters, 1).Optional;
        MobIndex = index is >= 0 and <= int.MaxValue ? (int)index.Value : null;
        var health = ActivityPacketValues.Double(parameters, 13);
        var maximum = ActivityPacketValues.Double(parameters, 14);
        if (health.State == PacketFieldState.Valid && health.Value < 0) health = new(PacketFieldState.Invalid, default);
        if (maximum.State == PacketFieldState.Valid && maximum.Value <= 0) maximum = new(PacketFieldState.Invalid, default);
        CurrentHealth = health.Optional; MaximumHealth = maximum.Optional;
        CurrentHealthState = health.State; MaximumHealthState = maximum.State;
    }
}
