using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AlbionDataAvalonia.Network.Responses;

// The Quick Sell window's prices: for each inventory item (0), the buy order it would sell
// into (1, the market's order JSON without a location) and the item's stack size (2).
public class QuickSellAuctionQueryActionResponse : BaseOperation
{
    public long[] objectIds = Array.Empty<long>();
    public List<MarketOrder?> marketOrders = new();

    public QuickSellAuctionQueryActionResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(0, out object? ids))
            {
                objectIds = ids.ToLongArray();
            }

            if (parameters.TryGetValue(1, out object? orders) && orders is IEnumerable<string> orderStrings)
            {
                foreach (var orderString in orderStrings)
                {
                    marketOrders.Add(string.IsNullOrEmpty(orderString) ? null : JsonSerializer.Deserialize<MarketOrder>(orderString));
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
