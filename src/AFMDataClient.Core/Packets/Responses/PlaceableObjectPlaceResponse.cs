using Albion.Network;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Responses;

public sealed class PlaceableObjectPlaceResponse : BaseOperation
{
    public long? RequestId { get; private set; }
    public long? ActionTimestamp { get; private set; }
    public long? PlacedObjectId { get; private set; }

    public PlaceableObjectPlaceResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        // Keep the echoed action timestamp even when a failed response has no object.
        TryRead(() =>
        {
            if (parameters.ContainsKey(255)) RequestId = Number(parameters, 255);
            if (parameters.ContainsKey(0)) ActionTimestamp = Number(parameters, 0);
        });
        TryRead(() =>
        {
            if (parameters.ContainsKey(1)) PlacedObjectId = Number(parameters, 1);
        });
    }
}
