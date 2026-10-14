using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class NewEquipmentItemEvent : BaseEvent
{
    private readonly long? objectId;
    private readonly int itemId;
    private readonly int quantity;
    private readonly string? crafterName;
    private readonly long estimatedMarketValue;
    private readonly long? blackMarketEstimatedMarketValue;
    private readonly long durability;
    private readonly int quality = 1;
    private readonly bool isAwakened;

    public NewItem? Item { get; }

    public NewEquipmentItemEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            // Crafter-name packets shift quality/durability by one field.
            // Without that string, parameter 8 carries spells, not durability.
            var shifted = parameters.TryGetValue(6, out var parameterSix) && parameterSix is string;
            var qualityKey = shifted ? (byte)7 : (byte)6;
            var durabilityKey = shifted ? (byte)8 : (byte)7;
            if (parameters.TryGetValue(0, out object? objectIdValue))
            {
                objectId = objectIdValue.ToLong();
            }

            if (parameters.TryGetValue(1, out object? itemIdValue))
            {
                itemId = checked((int)itemIdValue.ToLong());
            }

            if (parameters.TryGetValue(2, out object? quantityValue))
            {
                quantity = checked((int)quantityValue.ToLong());
            }

            if (parameters.TryGetValue(4, out object? estimatedMarketValueValue))
            {
                estimatedMarketValue = estimatedMarketValueValue.ToLong() / 10000;
            }

            if (parameters.TryGetValue(5, out object? blackMarketEstimatedMarketValueValue))
            {
                var parsedBlackMarketEstimatedMarketValue = blackMarketEstimatedMarketValueValue.ToLong() / 10000;
                blackMarketEstimatedMarketValue = parsedBlackMarketEstimatedMarketValue > 0 ? parsedBlackMarketEstimatedMarketValue : null;
            }

            if (parameterSix is string crafterNameValue)
            {
                crafterName = crafterNameValue;
            }

            if (parameters.TryGetValue(qualityKey, out var qualityValue))
            {
                var parsedQuality = ReadInteger(qualityValue);
                quality = parsedQuality is >= 1 and <= 5 ? (int)parsedQuality.Value : 0;
            }

            var parsedDurability = parameters.TryGetValue(durabilityKey, out var durabilityValue)
                ? ReadInteger(durabilityValue) : null;
            if (parsedDurability is >= 0)
            {
                durability = parsedDurability.Value / 10000;
            }

            if (parameters.TryGetValue(11, out object? isAwakenedValue))
            {
                isAwakened = isAwakenedValue.ToBool();
            }

            if (objectId != null)
            {
                Item = new NewItem(
                    objectId.Value,
                    itemId,
                    quantity,
                    durability,
                    estimatedMarketValue,
                    blackMarketEstimatedMarketValue,
                    quality,
                    crafterName,
                    isAwakened);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    private static long? ReadInteger(object? value) => value switch
    {
        byte or sbyte or short or ushort or int or uint or long => Convert.ToInt64(value),
        ulong number when number <= long.MaxValue => (long)number,
        _ => null
    };
}
