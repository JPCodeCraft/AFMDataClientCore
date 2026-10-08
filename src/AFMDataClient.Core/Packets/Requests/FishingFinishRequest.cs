using Albion.Network;
using AlbionDataAvalonia.Network.Events;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class FishingFinishRequest : BaseOperation
{
    public bool Succeeded { get; }
    public PacketFieldState SucceededState { get; }
    public bool IsValid => SucceededState == PacketFieldState.Valid;

    public FishingFinishRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        var succeeded = ActivityPacketValues.Boolean(parameters, 1);
        SucceededState = succeeded.State;
        Succeeded = succeeded.Optional ?? false;
    }
}
