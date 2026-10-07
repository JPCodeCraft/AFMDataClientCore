using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class OtherGrabbedLootEvent : BaseEvent
{
    public long ObjectId { get; }
    public string SourceName { get; } = string.Empty;
    public string PlayerName { get; } = string.Empty;
    public bool IsSilver { get; }
    public int ItemId { get; }
    public long Amount { get; }
    public long? RawAmount { get; }
    public long? SilverRaw => IsSilver ? RawAmount : null;
    public PacketFieldState AmountState { get; }

    public OtherGrabbedLootEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out var sourceName))
            {
                SourceName = sourceName.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(2, out var playerName))
            {
                PlayerName = playerName.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(3, out var isSilver))
            {
                IsSilver = isSilver.ToBool();
            }

            if (parameters.TryGetValue(4, out var itemId))
            {
                ItemId = checked((int)itemId.ToLong());
            }

            var amount = ActivityPacketValues.Long(parameters, 5);
            AmountState = amount.State;
            RawAmount = amount.Optional;
            if (RawAmount is { } rawAmount) Amount = IsSilver ? rawAmount / 10000 : rawAmount;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
