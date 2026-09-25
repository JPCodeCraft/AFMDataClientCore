using Albion.Network;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Events;

public sealed class BoostFarmableEvent : BaseEvent
{
    public long? ActorId { get; private set; }
    public byte? ActionSequence { get; private set; }
    public int? State { get; private set; }
    public long? TargetId { get; private set; }
    public int? RawParameter4 { get; private set; }
    public long? RawTimestamp5 { get; private set; }
    public long? RawTimestamp6 { get; private set; }
    public bool IsCompleted => State == 3;
    public bool IsCancelled => State == 2;

    public BoostFarmableEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.ContainsKey(0) || !parameters.ContainsKey(2)) return;
            var actorId = Number(parameters, 0);
            var state = checked((int)Number(parameters, 2));
            if (state is < 0 or > 3) return;
            var sequence = parameters.ContainsKey(1) ? checked((byte)Number(parameters, 1)) : (byte?)null;
            var targetId = parameters.ContainsKey(3) ? Number(parameters, 3) : (long?)null;
            ActorId = actorId;
            ActionSequence = sequence;
            State = state;
            TargetId = targetId;
        });
        // These fields describe the channelled action, but their precise meaning
        // is not needed to confirm completion and must not be treated as Focus cost.
        TryRead(() =>
        {
            if (parameters.ContainsKey(4)) RawParameter4 = checked((int)Number(parameters, 4));
            if (parameters.ContainsKey(5)) RawTimestamp5 = Number(parameters, 5);
            if (parameters.ContainsKey(6)) RawTimestamp6 = Number(parameters, 6);
        });
    }
}
