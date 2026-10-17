using System.Threading.Tasks;

namespace Albion.Network
{
    public interface IPacketHandler
    {
        int Priority { get; }
        bool CanHandle(object packet);
        Task HandleAsync(object request);
    }
}
