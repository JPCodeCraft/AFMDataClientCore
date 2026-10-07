using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public class TakeSilverEvent : BaseEvent
{
    public long? ObjectId { get; }
    public long? TargetEntityId { get; }
    public long TimeStamp { get; }
    public long? GrossRaw { get; }
    public long? ClusterTaxRaw { get; }
    public long? GuildTaxRaw { get; }
    public long? AlliancePenaltyRaw { get; }
    public long? NetRaw { get; }
    public PacketFieldState GrossState { get; }
    public PacketFieldState ClusterTaxState { get; }
    public PacketFieldState GuildTaxState { get; }
    public PacketFieldState AlliancePenaltyState { get; }
    public double YieldPreTax => (GrossRaw ?? 0) / 10000d;
    public double GuildTax => (GuildTaxRaw ?? 0) / 10000d;
    public double ClusterTax => (ClusterTaxRaw ?? 0) / 10000d;
    public double AlliancePenalty => (AlliancePenaltyRaw ?? 0) / 10000d;
    public bool IsPremiumBonus { get; }
    public double Multiplier { get; }
    public double SilverGained => (NetRaw ?? 0) / 10000d;

    public TakeSilverEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        ObjectId = ActivityPacketValues.Long(parameters, 0).Optional;
        TimeStamp = ActivityPacketValues.Long(parameters, 1).Optional ?? 0;
        TargetEntityId = ActivityPacketValues.Long(parameters, 2).Optional;
        var gross = NonNegative(ActivityPacketValues.Long(parameters, 3));
        var cluster = NonNegative(ActivityPacketValues.Long(parameters, 4));
        var guild = NonNegative(ActivityPacketValues.Long(parameters, 5));
        var alliance = NonNegative(ActivityPacketValues.Long(parameters, 6));
        GrossState = gross.State; ClusterTaxState = cluster.State; GuildTaxState = guild.State; AlliancePenaltyState = alliance.State;
        GrossRaw = gross.Optional; ClusterTaxRaw = cluster.Optional; GuildTaxRaw = guild.Optional; AlliancePenaltyRaw = alliance.Optional;
        // Omitted taxes mean no tax. Malformed taxes leave the net unknown.
        if (gross.State == PacketFieldState.Valid && cluster.State != PacketFieldState.Invalid
            && guild.State != PacketFieldState.Invalid && alliance.State != PacketFieldState.Invalid)
        {
            var taxes = (decimal)(cluster.Optional ?? 0) + (guild.Optional ?? 0) + (alliance.Optional ?? 0);
            if (taxes <= gross.Value) NetRaw = gross.Value - (long)taxes;
        }
        IsPremiumBonus = ActivityPacketValues.Boolean(parameters, 7).Optional ?? false;
        Multiplier = (ActivityPacketValues.Long(parameters, 8).Optional ?? 10000) / 10000d;
    }

    private static PacketField<long> NonNegative(PacketField<long> field) =>
        field.State == PacketFieldState.Valid && field.Value < 0 ? new(PacketFieldState.Invalid, default) : field;
}
