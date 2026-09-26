using Albion.Network;

namespace AlbionDataAvalonia.Network.Requests;

// Operation 70 has no action identifier or other payload fields.
public sealed class PlaceableObjectPlaceCancelRequest(Dictionary<byte, object> parameters) : BaseOperation(parameters);
