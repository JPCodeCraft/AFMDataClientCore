using Albion.Network;

namespace AlbionDataAvalonia.Network.Events;

public class UpdateFameEvent : BaseEvent
{
    public long? ActorObjectId { get; }
    public PacketFieldState ActorObjectIdState { get; }
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
    // Keys 12 and 13 (fixed point, equal in every capture): bonus fame bought with Quick Learn.
    // The game reports it as "bonus Fame from Quick Learn" and counts it in the fame gained.
    public double? QuickLearnFame { get; }
    // Key 14 (fixed point): the silver Quick Learn charged for that bonus, e.g. 3,918 for "Cost: 0.39".
    public double? QuickLearnSilverCost { get; }
    public double? ReportedBonusIncrement { get; }
    public IReadOnlyDictionary<byte, PacketFieldState> ComponentStates { get; }
    public IReadOnlyDictionary<byte, long?> RawFixedPointComponents { get; }

    public UpdateFameEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var actor = ActivityPacketValues.Long(parameters, 0);
        ActorObjectIdState = actor.Optional is <= 0 ? PacketFieldState.Invalid : actor.State;
        ActorObjectId = ActorObjectIdState == PacketFieldState.Valid ? actor.Value : null;
        var states = new Dictionary<byte, PacketFieldState>();
        var raw = new Dictionary<byte, long?>();
        foreach (byte key in new byte[] { 1, 2, 3, 4, 10, 12, 13 })
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
        var quickLearnCost = ActivityPacketValues.Long(parameters, 14);
        QuickLearnSilverCost = quickLearnCost.Optional is > 0 ? quickLearnCost.Value / 10000d : null;
        ComponentStates = states; RawFixedPointComponents = raw;
        TotalPlayerFame = (raw[1] ?? 0) / 10000d;
        FameWithZoneMultiplier = (raw[2] ?? 0) / 10000d;
        ZoneFame = (raw[3] ?? 0) / 10000d;
        Multiplier = (raw[4] ?? 10000) / 10000d;
        SatchelFame = (raw[10] ?? 0) / 10000d;
        IsPremiumBonus = premium.Optional ?? false;
        UsedBagInsightItemIndex = ActivityPacketValues.Long(parameters, 8).Optional ?? -1;
        ReportedBonusIncrement = factor.Optional;
        // Captured Destiny Board increments confirm that parameter 2 excludes
        // premium and parameter 17's bonus. Parameter 1 is a base-only cumulative
        // statistic and must not be used as an awarded-fame delta.
        BonusFactor = 1 + (factor.Optional ?? 0);
        PremiumFame = IsPremiumBonus ? FameWithZoneMultiplier * .5d : 0;
        if (states[2] == PacketFieldState.Valid && states[10] != PacketFieldState.Invalid
            && premium.State != PacketFieldState.Invalid && factor.State != PacketFieldState.Invalid)
        {
            var award = (FameWithZoneMultiplier + PremiumFame + SatchelFame) * BonusFactor;
            if (double.IsFinite(award) && award >= 0) ObservedAward = award;
        }
        // A Quick Learn harvest adds keys 12, 13 and 14: a 90 fame swing with premium reported
        // 12 = 13 = 5,400,000 and 14 = 3,918 for the game's "675 Fame (45 + 540)" and "540 bonus
        // Fame from Quick Learn (Cost: 0.39)". ObservedAward is the normal 135; hosts add
        // QuickLearnFame to it for the total the game reports.
        if (states[12] == PacketFieldState.Valid && states[13] == PacketFieldState.Valid
            && raw[12] is > 0 && raw[12] == raw[13])
            QuickLearnFame = raw[12]!.Value / 10000d;
    }
}
