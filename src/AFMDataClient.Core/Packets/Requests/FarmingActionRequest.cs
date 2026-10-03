using Albion.Network;
using System.Collections.Generic;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class FarmingActionRequest : BaseOperation
{
    public long? RequestId { get; private set; }
    public long? TargetId { get; private set; }

    public FarmingActionRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (parameters.ContainsKey(0)) TargetId = Number(parameters, 0);
            if (parameters.ContainsKey(255)) RequestId = Number(parameters, 255);
        });
    }
}
