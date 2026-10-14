using Albion.Network;
using AlbionDataAvalonia.Locations;

namespace AlbionDataAvalonia.Network.Events;

public sealed class NewRandomDungeonExitEvent : BaseEvent
{
    public long? ObjectId { get; }
    public ActivityWorldPosition? SourceExitPosition { get; }
    public string? UniqueName { get; }
    public string? DungeonType { get; }
    public int? Level { get; }
    public bool? IsAlreadyEntered { get; }

    public NewRandomDungeonExitEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        var id = ActivityPacketValues.Long(parameters, 0).Optional;
        ObjectId = id > 0 ? id : null;
        SourceExitPosition = ActivityPacketValues.Position(parameters, 1);
        UniqueName = FarmingPacketValues.OptionalText(FarmingPacketValues.Text(parameters, 3), 200);
        DungeonType = FarmingPacketValues.OptionalText(FarmingPacketValues.Text(parameters, 4), 200);
        var level = ActivityPacketValues.Long(parameters, 9).Optional;
        Level = level is >= 0 and <= 4 ? (int)level.Value : null;
        IsAlreadyEntered = ActivityPacketValues.Boolean(parameters, 10).Optional;
    }
}
