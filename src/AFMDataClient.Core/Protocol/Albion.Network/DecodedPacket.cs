namespace Albion.Network;

/// <summary>Shares a single typed decoding across all consumers of a packet.</summary>
public abstract class DecodedPacket
{
    private readonly Dictionary<Type, object> decoded = new();
    public string ConnectionId { get; internal set; } = string.Empty;
    public DateTime CapturedAt { get; internal set; } = DateTime.UtcNow;

    internal T GetDecoded<T>(Dictionary<byte, object> parameters) where T : class
    {
        if (!decoded.TryGetValue(typeof(T), out var value))
        {
            value = PacketFactory.Create<T>(parameters);
            if (value is BaseOperation operation)
            {
                operation.ConnectionId = ConnectionId;
                operation.CapturedAt = CapturedAt;
            }
            if (value is BaseEvent eventValue)
            {
                eventValue.ConnectionId = ConnectionId;
                eventValue.CapturedAt = CapturedAt;
            }
            decoded.Add(typeof(T), value);
        }
        return (T)value;
    }
}
