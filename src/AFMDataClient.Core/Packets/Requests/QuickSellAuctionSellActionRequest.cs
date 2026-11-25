using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

// "Sell Selected" in the Quick Sell window: parallel lists of the inventory items (1), the buy
// orders they go to (2) and the amounts (3).
public class QuickSellAuctionSellActionRequest : BaseOperation
{
    public long[] objectIds = Array.Empty<long>();
    public long[] orderIds = Array.Empty<long>();
    public int[] amounts = Array.Empty<int>();

    public QuickSellAuctionSellActionRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(1, out object? ids))
            {
                objectIds = ids.ToLongArray();
            }

            if (parameters.TryGetValue(2, out object? orders))
            {
                orderIds = orders.ToLongArray();
            }

            if (parameters.TryGetValue(3, out object? amounts))
            {
                this.amounts = amounts.ToIntArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
