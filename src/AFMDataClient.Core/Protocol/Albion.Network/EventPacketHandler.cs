using System;
using System.Linq;
using System.Threading.Tasks;
using AlbionDataAvalonia.Shared;

namespace Albion.Network
{
    public abstract class EventPacketHandler<TEvent> : PacketHandler<EventPacket> where TEvent : BaseEvent
    {
        private readonly int[]? eventCodes;
        private readonly int? singleEventCode;
        private readonly bool isSingleCode;

        public EventPacketHandler(int eventCode)
        {
            this.singleEventCode = eventCode;
            this.isSingleCode = true;
        }

        public EventPacketHandler(int[] eventCodes)
        {
            this.eventCodes = eventCodes;
            this.isSingleCode = false;
        }

        protected abstract Task OnActionAsync(TEvent value);

        public override bool CanHandle(object packet) => packet is EventPacket eventPacket && Handles(eventPacket.EventCode);

        private bool Handles(int eventCode) => isSingleCode ? eventCode == singleEventCode : eventCodes?.Contains(eventCode) == true;

        protected internal override Task OnHandleAsync(EventPacket packet)
        {
            if (!Handles(packet.EventCode))
            {
                return Task.CompletedTask;
            }

            TEvent instance = packet.GetDecoded<TEvent>(packet.Parameters);

            return OnActionAsync(instance);
        }
    }
}
