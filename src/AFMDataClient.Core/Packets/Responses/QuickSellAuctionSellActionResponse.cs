using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

// The buy orders the Quick Sell went through to (0). Items that could not be sold are left out.
public class QuickSellAuctionSellActionResponse : BaseOperation
{
    public long[]? soldOrderIds;

    public QuickSellAuctionSellActionResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(0, out object? ids))
            {
                soldOrderIds = ids.ToLongArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
