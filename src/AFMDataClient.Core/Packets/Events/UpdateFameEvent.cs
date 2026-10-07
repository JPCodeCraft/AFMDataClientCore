using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public class UpdateFameEvent : BaseEvent
{
    public double BonusFactor { get; }
    public double BonusFactorInPercent => (ReportedBonusIncrement ?? 0) * 100;
    public double FameWithZoneMultiplier { get; }
    public bool IsPremiumBonus { get; }
    public double SatchelFame { get; }
    public bool IsBonusFactorActive => ReportedBonusIncrement is > 0;
    public long UsedBagInsightItemIndex { get; }
    public double TotalPlayerFame { get; }
    public double Multiplier { get; }
    public double PremiumFame { get; }
    public double ZoneFame { get; }
    public double TotalGainedFame => ObservedAward ?? 0;
    public double? ObservedAward { get; }
    public double? ReportedBonusIncrement { get; }
    public IReadOnlyDictionary<byte, PacketFieldState> ComponentStates { get; }
    public IReadOnlyDictionary<byte, long?> RawFixedPointComponents { get; }

    public UpdateFameEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var states = new Dictionary<byte, PacketFieldState>();
        var raw = new Dictionary<byte, long?>();
        foreach (byte key in new byte[] { 1, 2, 3, 4, 10 })
        {
            var field = ActivityPacketValues.Long(parameters, key);
            if (field.State == PacketFieldState.Valid && field.Value < 0) field = new(PacketFieldState.Invalid, default);
            states[key] = field.State;
            raw[key] = field.Optional;
        }
        var premium = ActivityPacketValues.Boolean(parameters, 5);
        var factor = ActivityPacketValues.Double(parameters, 17);
        if (factor.State == PacketFieldState.Valid && factor.Value < -1) factor = new(PacketFieldState.Invalid, default);
        states[5] = premium.State; states[17] = factor.State;
        ComponentStates = states; RawFixedPointComponents = raw;
        TotalPlayerFame = (raw[1] ?? 0) / 10000d;
        FameWithZoneMultiplier = (raw[2] ?? 0) / 10000d;
        ZoneFame = (raw[3] ?? 0) / 10000d;
        Multiplier = (raw[4] ?? 10000) / 10000d;
        SatchelFame = (raw[10] ?? 0) / 10000d;
        IsPremiumBonus = premium.Optional ?? false;
        UsedBagInsightItemIndex = ActivityPacketValues.Long(parameters, 8).Optional ?? -1;
        ReportedBonusIncrement = factor.Optional;
        // The existing reference suppresses positive factors because those may
        // already be included in the reported award. Retain evidence separately.
        BonusFactor = factor.Optional is > 0 ? 1 : 1 + (factor.Optional ?? 0);
        PremiumFame = IsPremiumBonus ? FameWithZoneMultiplier * .5d : 0;
        if (states[2] == PacketFieldState.Valid && states[10] != PacketFieldState.Invalid
            && premium.State != PacketFieldState.Invalid && factor.State != PacketFieldState.Invalid
            && (factor.Optional is null or 0))
        {
            var award = (FameWithZoneMultiplier + PremiumFame + SatchelFame) * BonusFactor;
            if (double.IsFinite(award) && award >= 0) ObservedAward = award;
        }
    }
}
