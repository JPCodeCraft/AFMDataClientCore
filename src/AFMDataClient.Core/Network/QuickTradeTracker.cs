using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using Serilog;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Services;

public readonly record struct QuickTrade(MarketOrder Order, int Amount);

// Quick Sell and Quick Purchase Loadouts finish several instant trades with one message that
// names orders only by id. This keeps the orders those windows listed and, once the game
// confirms, returns the ones that went through with their amounts.
public sealed class QuickTradeTracker
{
    private const int MaxListed = 500;

    private readonly object gate = new();
    private readonly Dictionary<ulong, MarketOrder> quickSellOrders = new();
    private readonly Dictionary<(int Slot, ulong OfferId), QuickTrade> loadoutOffers = new();
    private List<(ulong OrderId, int Amount)> pendingSale = new();

    // These windows send orders without a location, so the market the player is at fills it.
    public void QuickSellListed(QuickSellAuctionQueryActionResponse value, string? location)
    {
        if (value.ReturnCode != 0) return;

        lock (gate)
        {
            if (quickSellOrders.Count > MaxListed) quickSellOrders.Clear();

            foreach (var order in value.marketOrders)
            {
                if (order == null) continue;
                FillLocation(order, location);
                quickSellOrders[order.Id] = order;
            }
        }
    }

    public void QuickSellRequested(QuickSellAuctionSellActionRequest value)
    {
        lock (gate)
        {
            pendingSale = value.orderIds
                .Select((id, index) => (OrderId: (ulong)id, Amount: index < value.amounts.Length ? value.amounts[index] : 0))
                .Where(sale => sale.Amount > 0)
                .ToList();
        }
    }

    // The reply lists the buy orders sold into. Without that list every requested sale counts.
    public IReadOnlyList<QuickTrade> QuickSellCompleted(QuickSellAuctionSellActionResponse value)
    {
        lock (gate)
        {
            var pending = pendingSale;
            pendingSale = new();
            if (value.ReturnCode != 0) return [];

            var sold = value.soldOrderIds?.Select(id => (ulong)id).ToList();
            var trades = new List<QuickTrade>();
            foreach (var (orderId, amount) in pending)
            {
                if (sold != null && !sold.Remove(orderId)) continue;

                if (quickSellOrders.TryGetValue(orderId, out var order))
                {
                    trades.Add(new QuickTrade(order, amount));
                }
                else
                {
                    Log.Warning("Quick Sell order not found: {OrderId}", orderId);
                }
            }
            return trades;
        }
    }

    public void LoadoutOffersListed(AuctionGetLoadoutOffersResponse value, string? location)
    {
        if (value.ReturnCode != 0) return;

        lock (gate)
        {
            if (loadoutOffers.Count > MaxListed) loadoutOffers.Clear();

            for (var slotIndex = 0; slotIndex < value.offersBySlot.Count; slotIndex++)
            {
                var slot = slotIndex < value.slots.Length ? value.slots[slotIndex] : slotIndex;
                var offers = value.offersBySlot[slotIndex];
                var amounts = slotIndex < value.amountsBySlot.Count ? value.amountsBySlot[slotIndex] : [];

                for (var offerIndex = 0; offerIndex < offers.Count && offerIndex < amounts.Length; offerIndex++)
                {
                    var offer = offers[offerIndex];
                    if (offer == null || amounts[offerIndex] <= 0) continue;
                    FillLocation(offer, location);
                    loadoutOffers[(slot, offer.Id)] = new QuickTrade(offer, amounts[offerIndex]);
                }
            }
        }
    }

    // "Buy" sends only the offers the player unticked; the reply lists the offers bought and
    // their slots. Each is matched to the listing for the amount the window showed.
    public IReadOnlyList<QuickTrade> LoadoutPurchaseCompleted(AuctionBuyLoadoutOfferResponse value)
    {
        if (value.ReturnCode != 0) return [];

        lock (gate)
        {
            var trades = new List<QuickTrade>();
            for (var index = 0; index < value.boughtOfferIds.Length; index++)
            {
                var offerId = (ulong)value.boughtOfferIds[index];
                var slot = index < value.slots.Length ? value.slots[index] : -1;

                if (loadoutOffers.TryGetValue((slot, offerId), out var listed))
                {
                    trades.Add(listed);
                }
                else
                {
                    Log.Warning("Loadout offer not found: {OfferId} in slot {Slot}", offerId, slot);
                }
            }
            return trades;
        }
    }

    // Capture stopped: the windows will be listed again before anything else can be traded.
    public void Clear()
    {
        lock (gate)
        {
            quickSellOrders.Clear();
            loadoutOffers.Clear();
            pendingSale = new();
        }
    }

    private static void FillLocation(MarketOrder order, string? location)
    {
        if (string.IsNullOrEmpty(order.LocationId) && !string.IsNullOrEmpty(location))
        {
            order.LocationId = location;
        }
    }
}
