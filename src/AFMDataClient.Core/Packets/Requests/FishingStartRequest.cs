using Albion.Network;
using AlbionDataAvalonia.Network.Events;

namespace AlbionDataAvalonia.Network.Requests;

/// <summary>Local fishing intent, never an earned reward.</summary>
public sealed class FishingStartRequest : BaseOperation
{
    public long EventId { get; }
    public PacketFieldState EventIdState { get; }
    public long UsedRodObjectId { get; }
    public PacketFieldState UsedRodObjectIdState { get; }
    public bool IsValid => EventIdState == PacketFieldState.Valid && EventId >= 0
        && UsedRodObjectIdState == PacketFieldState.Valid && UsedRodObjectId > 0;

    public FishingStartRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        var id = ActivityPacketValues.Long(parameters, 0);
        EventIdState = id.Optional is < 0 ? PacketFieldState.Invalid : id.State;
        EventId = id.Optional ?? 0;
        var rod = ActivityPacketValues.Long(parameters, 2);
        UsedRodObjectIdState = rod.Optional is <= 0 ? PacketFieldState.Invalid : rod.State;
        UsedRodObjectId = rod.Optional ?? 0;
    }
}
