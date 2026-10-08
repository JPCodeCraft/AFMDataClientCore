using Albion.Network;
using Serilog;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>Server-confirmed harvested units, retaining optional component presence.</summary>
public sealed class HarvestFinishedEvent : BaseEvent
{
    public long UserObjectId { get; }
    public PacketFieldState UserObjectIdState { get; }
    public long? ResourceObjectId { get; }
    public PacketFieldState ResourceObjectIdState { get; }
    public int ItemId { get; }
    public PacketFieldState ItemIdState { get; }
    public long? StandardAmount { get; }
    public PacketFieldState StandardAmountState { get; }
    public long? GatheringBonusAmount { get; }
    public PacketFieldState GatheringBonusAmountState { get; }
    public long? PremiumBonusAmount { get; }
    public PacketFieldState PremiumBonusAmountState { get; }
    public long? TotalQuantity { get; }
    public PacketFieldState TotalQuantityState { get; } = PacketFieldState.Invalid;
    public bool IsValid => UserObjectIdState == PacketFieldState.Valid && UserObjectId > 0
        && ItemIdState == PacketFieldState.Valid && ItemId > 0 && TotalQuantity is > 0;

    public HarvestFinishedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        var actor = Nonnegative(parameters, 0);
        UserObjectIdState = actor.Optional is <= 0 ? PacketFieldState.Invalid : actor.State;
        UserObjectId = actor.Optional ?? 0;
        var resource = Nonnegative(parameters, 3);
        ResourceObjectIdState = resource.Optional is <= 0 ? PacketFieldState.Invalid : resource.State;
        ResourceObjectId = resource.Optional is > 0 ? resource.Optional : null;
        var item = Nonnegative(parameters, 4);
        ItemIdState = item.Optional is <= 0 or > int.MaxValue ? PacketFieldState.Invalid : item.State;
        ItemId = ItemIdState == PacketFieldState.Valid ? (int)item.Value : 0;
        var standard = Nonnegative(parameters, 5);
        var gathering = Nonnegative(parameters, 6);
        var premium = Nonnegative(parameters, 7);
        StandardAmount = standard.Optional;
        StandardAmountState = standard.State;
        GatheringBonusAmount = gathering.Optional;
        GatheringBonusAmountState = gathering.State;
        PremiumBonusAmount = premium.Optional;
        PremiumBonusAmountState = premium.State;
        // Ordinary packets can omit bonus fields. Preserve their absence;
        // a present malformed component must never silently become zero.
        if (standard.State == PacketFieldState.Valid && gathering.State != PacketFieldState.Invalid
            && premium.State != PacketFieldState.Invalid)
        {
            try
            {
                TotalQuantity = checked(standard.Value + (gathering.Optional ?? 0) + (premium.Optional ?? 0));
                TotalQuantityState = PacketFieldState.Valid;
            }
            catch (OverflowException) { }
        }
        else if (standard.State == PacketFieldState.Missing && gathering.State != PacketFieldState.Invalid
            && premium.State != PacketFieldState.Invalid) TotalQuantityState = PacketFieldState.Missing;
    }

    private static PacketField<long> Nonnegative(Dictionary<byte, object> parameters, byte key)
    {
        var value = ActivityPacketValues.Long(parameters, key);
        return value.Optional is < 0 ? new(PacketFieldState.Invalid, 0) : value;
    }
}
