using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

// Sent by "Sell Now" on a buy order list, such as the Black Market's Buy Orders tab.
// The sell window opened from an inventory item sends AuctionSellSpecificItemRequest instead.
public class AuctionSellRequestRequest : BaseOperation
{
    public int amount;
    public ulong orderId;

    public AuctionSellRequestRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(1, out object? orderId))
            {
                this.orderId = Convert.ToUInt64(orderId);
            }

            if (parameters.TryGetValue(2, out object? amount))
            {
                this.amount = Convert.ToInt32(amount);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
