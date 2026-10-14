using Albion.Network;
using AlbionDataAvalonia.Network.Events;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class ExitEnterStartRequest : BaseOperation
{
    public long? TargetObjectId { get; }

    public ExitEnterStartRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        // Parameter 0 is a timestamp; parameter 1 identifies the selected exit.
        var id = ActivityPacketValues.Long(parameters, 1).Optional;
        TargetObjectId = id > 0 ? id : null;
    }
}
