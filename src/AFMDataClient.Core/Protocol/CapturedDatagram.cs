using System.Net;

namespace AFMDataClient.Core;

public sealed record CapturedDatagram(byte[] Payload, DateTime CapturedAt,
    IPEndPoint? Source = null, IPEndPoint? Destination = null)
{
    public string ConnectionId
    {
        get
        {
            if (Source is null || Destination is null) return string.Empty;
            var source = Source.ToString();
            var destination = Destination.ToString();
            return string.CompareOrdinal(source, destination) < 0
                ? $"{source}|{destination}" : $"{destination}|{source}";
        }
    }
}
