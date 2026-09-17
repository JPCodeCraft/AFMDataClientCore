using PhotonPackageParser;

namespace Albion.Network
{
    public interface IPhotonReceiver
    {
        PacketStatus ReceivePacket(byte[] payload);
        PacketReceiveResult Receive(AFMDataClient.Core.CapturedDatagram datagram);
    }
}
