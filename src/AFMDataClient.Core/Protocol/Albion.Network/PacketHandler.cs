using System.Threading.Tasks;

namespace Albion.Network
{
    public abstract class PacketHandler<TPacket> : IPacketHandler
    {
        public int Priority { get; set; }

        public Task HandleAsync(object request)
        {
            if (request is TPacket packet)
            {
                return OnHandleAsync(packet);
            }
            return Task.CompletedTask;
        }

        protected internal abstract Task OnHandleAsync(TPacket packet);
    }
}
