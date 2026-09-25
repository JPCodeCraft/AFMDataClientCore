using Albion.Network;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class PlaceableObjectPlaceRequest : BaseOperation
{
    public long? RequestId { get; private set; }
    public long? ActionTimestamp { get; private set; }
    public long? InventoryItemObjectId { get; private set; }
    public int? PlaceableTypeIndex { get; private set; }
    public double? PositionX { get; private set; }
    public double? PositionY { get; private set; }
    public double? Rotation { get; private set; }

    public PlaceableObjectPlaceRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (parameters.ContainsKey(255)) RequestId = Number(parameters, 255);
            if (parameters.ContainsKey(0)) ActionTimestamp = Number(parameters, 0);
        });
        TryRead(() =>
        {
            if (!parameters.ContainsKey(1) || !parameters.ContainsKey(2)
                || !parameters.TryGetValue(3, out var rawPosition)) return;
            var itemObjectId = Number(parameters, 1);
            var placeableTypeIndex = checked((int)Number(parameters, 2));
            var position = rawPosition.ToDoubleArray();
            if (position.Length != 2 || !position.All(n => double.IsFinite(n) && Math.Abs(n) <= 1_000_000)) return;
            var rotation = parameters.TryGetValue(4, out var rawRotation) ? rawRotation.ToDouble() : (double?)null;
            if (rotation is { } angle && (!double.IsFinite(angle) || Math.Abs(angle) > 100_000)) return;
            InventoryItemObjectId = itemObjectId;
            // This is a placeable/world definition index, not an inventory item index.
            PlaceableTypeIndex = placeableTypeIndex;
            PositionX = position[0];
            PositionY = position[1];
            Rotation = rotation;
        });
    }
}
