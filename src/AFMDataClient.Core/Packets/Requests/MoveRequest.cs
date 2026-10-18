using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;

namespace AlbionDataAvalonia.Network.Requests;

/// <summary>The local character walking. The client sends it while moving, so it also reports where the character is.</summary>
public sealed class MoveRequest : BaseOperation
{
    // Map coordinates stay within a few thousand units; anything larger is a misread field.
    private const double MaxCoordinate = 100_000;

    public ActivityWorldPosition? Position { get; }
    public ActivityWorldPosition? Destination { get; }
    /// <summary>Where the character faces, in radians as Math.Atan2(dy, dx) gives for a step. Sent while standing too.</summary>
    public double? Heading { get; }

    public MoveRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        // Parameter 0 is a timestamp, 1 the current position, 2 the direction, 3 the target position and 4 the speed.
        Position = Valid(ActivityPacketValues.Position(parameters, 1));
        Destination = Valid(ActivityPacketValues.Position(parameters, 3));
        // The client sends degrees clockwise from +Y, so 90 points along +X.
        Heading = ActivityPacketValues.Double(parameters, 2).Optional is { } degrees ? (90 - degrees) * Math.PI / 180 : null;
    }

    private static ActivityWorldPosition? Valid(ActivityWorldPosition? position) =>
        position is { } point && Math.Abs(point.X) <= MaxCoordinate && Math.Abs(point.Y) <= MaxCoordinate ? point : null;
}
