using Albion.Network;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class BoostFarmableRequest : BaseOperation
{
    public long? ActionTimestamp { get; private set; }
    public byte? ActionSequence { get; private set; }
    public int? State { get; private set; }
    public long? TargetId { get; private set; }
    public bool IsStart => State == 1;
    public bool IsCancel => State == 2;

    public BoostFarmableRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.ContainsKey(0) || !parameters.ContainsKey(2)) return;
            var timestamp = Number(parameters, 0);
            var state = checked((int)Number(parameters, 2));
            if (state is < 0 or > 2) return;
            var sequence = parameters.ContainsKey(1) ? checked((byte)Number(parameters, 1)) : (byte?)null;
            var targetId = parameters.ContainsKey(3) ? Number(parameters, 3) : (long?)null;
            ActionTimestamp = timestamp;
            ActionSequence = sequence;
            State = state;
            TargetId = targetId;
        });
    }
}
