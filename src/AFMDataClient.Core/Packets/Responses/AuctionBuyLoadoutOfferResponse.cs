using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

// The reply to "Buy" in Quick Purchase Loadouts: the sell offers bought (3) and the loadout slot
// each one filled (4). Unticked and unavailable items are left out.
public class AuctionBuyLoadoutOfferResponse : BaseOperation
{
    public long[] boughtOfferIds = Array.Empty<long>();
    public int[] slots = Array.Empty<int>();

    public AuctionBuyLoadoutOfferResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(3, out object? ids))
            {
                boughtOfferIds = ids.ToLongArray();
            }

            if (parameters.TryGetValue(4, out object? slots))
            {
                this.slots = slots.ToIntArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
