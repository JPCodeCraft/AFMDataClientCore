using Albion.Network;
using AlbionDataAvalonia.Locations;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>A Mists wisp spawn or border exit. Both stand at fixed points of the instance's layout.</summary>
public sealed class MistsLandmarkEvent : BaseEvent
{
    public long? ObjectId { get; }
    public ActivityWorldPosition? Position { get; }

    public MistsLandmarkEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var id = ActivityPacketValues.Long(parameters, 0).Optional;
        ObjectId = id > 0 ? id : null;
        Position = ActivityPacketValues.Position(parameters, 1);
    }
}
