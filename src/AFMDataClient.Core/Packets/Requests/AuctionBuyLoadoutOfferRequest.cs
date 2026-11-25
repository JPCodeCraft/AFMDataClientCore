using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

// "Buy" in Quick Purchase Loadouts: the sell offers bought (1) and the loadout slot each fills (2).
public class AuctionBuyLoadoutOfferRequest : BaseOperation
{
    public long[] offerIds = Array.Empty<long>();
    public int[] slots = Array.Empty<int>();

    public AuctionBuyLoadoutOfferRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(1, out object? offers))
            {
                offerIds = offers.ToLongArray();
            }

            if (parameters.TryGetValue(2, out object? slots))
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
