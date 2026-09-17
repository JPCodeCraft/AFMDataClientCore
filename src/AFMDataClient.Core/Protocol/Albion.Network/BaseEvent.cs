using System.Collections.Generic;

namespace Albion.Network
{
    public abstract class BaseEvent
    {
        public string ConnectionId { get; internal set; } = string.Empty;
        public DateTime CapturedAt { get; internal set; } = DateTime.UtcNow;
        public BaseEvent(Dictionary<byte, object> parameters) { }
    }
}
