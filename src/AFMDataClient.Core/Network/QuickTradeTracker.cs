using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using Serilog;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Services;

public readonly record struct QuickTrade(MarketOrder Order, int Amount);

// Quick Sell and Quick Purchase Loadouts finish several instant trades with one message, and
// the request names only order ids. This keeps the orders those windows listed and, once the
// game confirms, returns the ones that went through with their amounts.
public sealed class QuickTradeTracker
{
    private const int MaxListed = 500;

    private readonly object gate = new();
    private readonly Dictionary<ulong, MarketOrder> quickSellOrders = new();
    private readonly Dictionary<(int Slot, ulong OfferId), QuickTrade> loadoutOffers = new();
    private List<(ulong OrderId, int Amount)> pendingSale = new();
    private List<QuickTrade> pendingPurchase = new();

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

    public void LoadoutPurchaseRequested(AuctionBuyLoadoutOfferRequest value)
    {
        lock (gate)
        {
            pendingPurchase = new();
            for (var index = 0; index < value.offerIds.Length; index++)
            {
                var slot = index < value.slots.Length ? value.slots[index] : -1;
                var offerId = (ulong)value.offerIds[index];

                if (loadoutOffers.TryGetValue((slot, offerId), out var listed))
                {
                    pendingPurchase.Add(listed);
                }
                else
                {
                    Log.Warning("Loadout offer not found: {OfferId} in slot {Slot}", offerId, slot);
                }
            }
        }
    }

    // The reply's layout is not confirmed yet. Its ids narrow the purchase only when they name
    // offers that were requested; otherwise a successful reply counts every requested offer.
    public IReadOnlyList<QuickTrade> LoadoutPurchaseCompleted(AuctionBuyLoadoutOfferResponse value)
    {
        lock (gate)
        {
            var pending = pendingPurchase;
            pendingPurchase = new();
            Log.Debug("Loadout purchase reply (return {ReturnCode}): {Shape}", value.ReturnCode, value.Shape);
            if (value.ReturnCode != 0) return [];

            var bought = value.boughtOfferIds?.Select(id => (ulong)id).ToList();
            if (bought == null || !pending.Any(trade => bought.Contains(trade.Order.Id))) return pending;

            return pending.Where(trade => bought.Remove(trade.Order.Id)).ToList();
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
            pendingPurchase = new();
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
