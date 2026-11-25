using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace AlbionDataAvalonia.Network.Responses;

// Quick Purchase Loadouts: the loadout slots (0), the sell offers picked for each slot (1) and
// how many to buy from each of those offers (2).
public class AuctionGetLoadoutOffersResponse : BaseOperation
{
    public List<MarketOrder> marketOrders = new();
    public int[] slots = Array.Empty<int>();
    public List<List<MarketOrder?>> offersBySlot = new();
    public List<int[]> amountsBySlot = new();

    public AuctionGetLoadoutOffersResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(0, out object? slotIndexes))
            {
                slots = slotIndexes.ToIntArray();
            }

            if (parameters.TryGetValue(1, out object? orders))
            {
                foreach (var slotOffers in (string[][])orders)
                {
                    var offers = new List<MarketOrder?>();
                    foreach (var auctionOfferString in slotOffers)
                    {
                        var order = string.IsNullOrEmpty(auctionOfferString) ? null : JsonSerializer.Deserialize<MarketOrder>(auctionOfferString);
                        offers.Add(order);
                        if (order != null) marketOrders.Add(order);
                    }
                    offersBySlot.Add(offers);
                }
            }

            if (parameters.TryGetValue(2, out object? buyAmounts) && buyAmounts is IEnumerable slotAmounts)
            {
                foreach (var amounts in slotAmounts)
                {
                    amountsBySlot.Add(amounts?.ToIntArray() ?? Array.Empty<int>());
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "{message}", MethodBase.GetCurrentMethod()?.DeclaringType);
        }
    }
}
