using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private readonly Dictionary<(string Connection, long Id), InventoryItem> inventoryItems = new();
    private readonly Dictionary<(string Name, int Quality), EmvObservation> observedPrices = new();

    // Inventory identity is captured before consumption. World tiles can use a
    // baby's name for an adult animal, so they are not inventory item identities.
    public void OnInventoryItem(NewItem? item, string connection, DateTime capturedAt) => Observe(() =>
    {
        if ((!joining && island is null) || item?.ObjectId is not { } id || !ValidItemName(item.ItemUniqueName)
            || item.Quantity < 0 || item.Quality is < 1 or > 5) return;
        var key = (connection, id);
        if (inventoryItems.Count >= MaxTransientEntries && !inventoryItems.ContainsKey(key)) return;
        if (inventoryItems.TryGetValue(key, out var previous) && previous.ObservedAt > capturedAt) return;
        inventoryItems[key] = new(item.ItemUniqueName, item.Quality, item.Quantity, capturedAt);
        ObservePrice(item.ItemUniqueName, item.Quality, item.EstimatedMarketValue, capturedAt);
        ObservePlacementInventoryChange(connection, id, previous, inventoryItems[key], capturedAt);
        ObservePickupInventoryItem(connection, id, previous, inventoryItems[key]);
    });

    public void OnInventoryItemDeleted(InventoryDeleteItemEvent packet) => Observe(() =>
    {
        if (inventoryItems.TryGetValue((packet.ConnectionId, packet.ItemObjectId), out var existing)
            && existing.ObservedAt > packet.CapturedAt) return;
        if (inventoryItems.Remove((packet.ConnectionId, packet.ItemObjectId), out var previous))
            ObservePlacementInventoryChange(packet.ConnectionId, packet.ItemObjectId, previous, null, packet.CapturedAt);
        ForgetPickupInventoryItem(packet.ConnectionId, packet.ItemObjectId);
    });

    public void OnEstimatedMarketValue(string name, int quality, long value, DateTime capturedAt) => Observe(() =>
    {
        ObservePrice(name, quality, value, capturedAt);
    });

    private void ObservePrice(string name, int quality, long value, DateTime capturedAt)
    {
        if (!ValidItemName(name) || quality is < 1 or > 5 || value <= 0) return;
        var key = (name, quality);
        if (observedPrices.Count >= MaxTransientEntries && !observedPrices.ContainsKey(key)) return;
        if (!observedPrices.TryGetValue(key, out var previous) || previous.ObservedAt <= capturedAt)
            observedPrices[key] = new(value, capturedAt);
    }

    private FarmingActionItem ActivityItem(string name, int quantity, int quality, DateTime at)
    {
        observedPrices.TryGetValue((name, quality), out var quote);
        if (quote?.ObservedAt > at) quote = null;
        return new()
        {
            UniqueName = name,
            Quantity = quantity,
            Quality = quality,
            ObservedEmv = quote?.Value,
            EmvObservedAt = quote?.ObservedAt
        };
    }

    private void FlushActivity() => Observe(PruneActivity);

    private void ResetActivityState()
    {
        ResetFocusState();
        ResetPickupInventoryReturns();
        ResetPlacementState();
        inventoryItems.Clear();
        observedPrices.Clear();
    }

    private void PruneActivity()
    {
        var cutoff = DateTime.UtcNow - RequestLifetime;
        PrunePlacementState(cutoff);
        PruneFocusState(cutoff);
        PrunePickupInventoryReturns();
    }

    private static bool ValidItemName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 200 && name is not ("Unset" or "Unknown Item");
    private sealed record InventoryItem(string Name, int Quality, int Quantity, DateTime ObservedAt);
    private sealed record EmvObservation(long Value, DateTime ObservedAt);
}
