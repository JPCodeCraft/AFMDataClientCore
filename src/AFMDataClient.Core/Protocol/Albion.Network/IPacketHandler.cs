using System.Threading.Tasks;

namespace Albion.Network
{
    public interface IPacketHandler
    {
        int Priority { get; }
        Task HandleAsync(object request);
    }
}
