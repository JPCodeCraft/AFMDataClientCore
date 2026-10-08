using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class NewSimpleItemEvent : BaseEvent
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
    public PacketFieldState ObjectIdState { get; }
    public PacketFieldState ItemIdState { get; }
    public PacketFieldState QuantityState { get; }
    public bool HasValidAcquisitionMetadata => ObjectIdState == PacketFieldState.Valid
        && ItemIdState == PacketFieldState.Valid && QuantityState == PacketFieldState.Valid;

    public NewSimpleItemEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        var objectIdentity = ActivityPacketValues.Long(parameters, 0);
        ObjectIdState = objectIdentity.Optional is <= 0 ? PacketFieldState.Invalid : objectIdentity.State;
        var itemIdentity = ActivityPacketValues.Long(parameters, 1);
        ItemIdState = itemIdentity.Optional is <= 0 or > int.MaxValue ? PacketFieldState.Invalid : itemIdentity.State;
        var observedQuantity = ActivityPacketValues.Long(parameters, 2);
        QuantityState = observedQuantity.Optional is <= 0 or > int.MaxValue ? PacketFieldState.Invalid : observedQuantity.State;
        try
        {
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

            if (parameters.TryGetValue(6, out object? crafterNameValue))
            {
                crafterName = crafterNameValue.ToString();
            }

            if (parameters.TryGetValue(7, out object? qualityValue))
            {
                quality = checked((int)qualityValue.ToLong());
            }

            if (parameters.TryGetValue(8, out object? durabilityValue))
            {
                durability = durabilityValue.ToLong() / 10000;
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
}
