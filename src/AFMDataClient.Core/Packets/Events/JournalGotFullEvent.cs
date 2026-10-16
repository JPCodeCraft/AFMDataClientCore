using Albion.Network;
using Serilog;

namespace AlbionDataAvalonia.Network.Events;

/// <summary>A gathering journal in the player's inventory filled up and became its full item.</summary>
public sealed class JournalGotFullEvent : BaseEvent
{
    public long PlayerObjectId { get; }
    public int ItemId { get; }
    public int Count { get; }
    public bool IsValid => PlayerObjectId > 0 && ItemId > 0 && Count > 0;

    public JournalGotFullEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        var player = ActivityPacketValues.Long(parameters, 0);
        var item = ActivityPacketValues.Long(parameters, 1);
        var count = ActivityPacketValues.Long(parameters, 2);
        PlayerObjectId = player.State == PacketFieldState.Valid && player.Value > 0 ? player.Value : 0;
        ItemId = item.State == PacketFieldState.Valid && item.Value is > 0 and <= int.MaxValue ? (int)item.Value : 0;
        // One journal fills per event; key 2 has only been seen as 1.
        Count = count.State switch
        {
            PacketFieldState.Valid when count.Value is > 0 and <= int.MaxValue => (int)count.Value,
            PacketFieldState.Missing => 1,
            _ => 0
        };
    }
}
