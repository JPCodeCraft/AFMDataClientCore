using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private readonly Dictionary<(string Connection, long Id), InventoryItem> inventoryItems = new();
    private readonly Dictionary<(string Name, int Quality), EmvObservation> observedPrices = new();
    private readonly Dictionary<(string Connection, long Timestamp), Placement> placements = new();
    private readonly Dictionary<(string Connection, long Timestamp), DateTime> completedPlacements = new();
    private readonly Dictionary<(string Connection, long Id), ConfirmedPlacement> pendingPlacementObjects = new();
    private readonly Dictionary<(string Connection, byte Sequence, long Target), Boost> boosts = new();
    private readonly Dictionary<(string Connection, byte Sequence, long Target, long Timestamp), DateTime> completedBoosts = new();
    private readonly Dictionary<(string Connection, long Timestamp), DateTime> seenFocusChanges = new();

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
        ObservePickupInventoryItem(connection, id, previous, inventoryItems[key]);
    });

    public void OnInventoryItemDeleted(InventoryDeleteItemEvent packet) => Observe(() =>
    {
        inventoryItems.Remove((packet.ConnectionId, packet.ItemObjectId));
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

    public void OnPlacementRequest(PlaceableObjectPlaceRequest packet) => Observe(() =>
    {
        if (island is null || packet.ActionTimestamp is not { } stamp || packet.InventoryItemObjectId is not { } itemId) return;
        PruneActivity();
        var key = (packet.ConnectionId, stamp);
        if (placements.ContainsKey(key) || completedPlacements.ContainsKey(key) || placements.Count >= MaxTransientEntries) return;
        inventoryItems.TryGetValue((packet.ConnectionId, itemId), out var item);
        ConfirmPickupInventoryContainer(packet.ConnectionId, itemId);
        // Capture the price before the request removes the inventory stack.
        var input = item is null ? null : ActivityItem(item.Name, 1, item.Quality, packet.CapturedAt);
        placements[key] = new(Guid.NewGuid().ToString(), packet.CapturedAt, island, input);
    });

    public void OnPlacementResponse(PlaceableObjectPlaceResponse packet) => Observe(() =>
    {
        if (packet.ActionTimestamp is not { } stamp) return;
        PruneActivity();
        var key = (packet.ConnectionId, stamp);
        if (!placements.Remove(key, out var placement)) return;
        completedPlacements[key] = packet.CapturedAt;
        if (packet.ReturnCode != 0) return;
        objects.TryGetValue(packet.PlacedObjectId ?? -1, out var placed);
        // Successful placement is used for furniture too. Require either the
        // inventory farmable identity or an observed farmable destination.
        if (placement.Item?.UniqueName.Contains("_FARM_", StringComparison.Ordinal) != true && placed?.Kind != "farmable")
        {
            if (placed is null && packet.PlacedObjectId is { } id && pendingPlacementObjects.Count < MaxTransientEntries)
                pendingPlacementObjects[(packet.ConnectionId, id)] = new(placement, packet.CapturedAt);
            return;
        }
        EnqueuePlacementAction(placement, packet.CapturedAt, placed);
    });

    private void ObservePlacementDestination(NewBuildingEvent packet, FarmingObjectObservation destination)
    {
        if (!pendingPlacementObjects.Remove((packet.ConnectionId, packet.SessionId), out var pending)) return;
        if (destination.Kind == "farmable" && packet.CapturedAt >= pending.Placement.RequestedAt
            && packet.CapturedAt - pending.OccurredAt <= RequestLifetime)
            EnqueuePlacementAction(pending.Placement, pending.OccurredAt, destination);
    }

    private void EnqueuePlacementAction(Placement placement, DateTime occurredAt, FarmingObjectObservation? placed)
    {
        uploader.EnqueueAction(accountId!, new FarmingAction
        {
            ServerId = placement.Island.ServerId,
            CharacterId = placement.Island.CharacterId,
            CharacterName = placement.Island.CharacterName,
            IslandId = placement.Island.IslandId,
            EventId = placement.EventId,
            OccurredAt = occurredAt,
            Operation = "place",
            SourceObjectId = placed?.ObjectId,
            Inputs = placement.Item is { } item ? [item] : [],
            InputsComplete = placement.Item is not null,
            FocusUsed = 0
        });
    }

    public void OnBoostRequest(BoostFarmableRequest packet) => Observe(() =>
    {
        if (island is null || packet.ActionSequence is not { } sequence || packet.TargetId is not { } target) return;
        PruneActivity();
        var key = (packet.ConnectionId, sequence, target);
        if (packet.IsCancel)
        {
            if (boosts.Remove(key, out var cancelled)) completedBoosts[(key.ConnectionId, sequence, target, cancelled.ActionTimestamp)] = packet.CapturedAt;
            return;
        }
        if (!packet.IsStart || packet.ActionTimestamp is not { } stamp) return;
        if (completedBoosts.ContainsKey((packet.ConnectionId, sequence, target, stamp))) return;
        if (boosts.TryGetValue(key, out var previous) && previous.ActionTimestamp == stamp) return;
        if (boosts.Count >= MaxTransientEntries && !boosts.ContainsKey(key)) return;
        objects.TryGetValue(target, out var source);
        boosts[key] = new(Guid.NewGuid().ToString(), packet.CapturedAt, stamp, island, source?.ObjectId);
    });

    public void OnFocusUpdate(CraftingFocusUpdateEvent packet) => Observe(() =>
    {
        if (island is null || localObjectId == 0 || packet.ActorId != localObjectId
            || packet.Timestamp is not { } stamp || packet.FocusDelta is not (< 0 and >= -1_000_000)) return;
        PruneActivity();
        var key = (packet.ConnectionId, stamp);
        if (seenFocusChanges.Count >= MaxTransientEntries) return;
        if (!seenFocusChanges.TryAdd(key, packet.CapturedAt)) return;
        var amount = -packet.FocusDelta.Value;
        var rounded = Math.Round(amount);
        // Fractional regeneration is not expenditure. Unsupported cost shapes
        // stay unknown instead of rounding a different resource change.
        if (Math.Abs(rounded - amount) > 0.001 || rounded < 1) return;
        var candidates = boosts.Where(entry => entry.Key.Connection == packet.ConnectionId
            && entry.Value.RequestedAt <= packet.CapturedAt
            && packet.CapturedAt - entry.Value.RequestedAt <= TimeSpan.FromSeconds(10)).ToArray();
        if (candidates.Length != 1) return;
        candidates[0].Value.FocusChanges.Add((int)rounded);
    });

    public void OnBoostEvent(BoostFarmableEvent packet) => Observe(() =>
    {
        if (island is null || localObjectId == 0 || packet.ActorId != localObjectId
            || packet.ActionSequence is not { } sequence || packet.TargetId is not { } target) return;
        if (!packet.IsCompleted && !packet.IsCancelled) return;
        PruneActivity();
        if (!boosts.Remove((packet.ConnectionId, sequence, target), out var boost)) return;
        completedBoosts[(packet.ConnectionId, sequence, target, boost.ActionTimestamp)] = packet.CapturedAt;
        if (!packet.IsCompleted) return;
        uploader.EnqueueAction(accountId!, new FarmingAction
        {
            ServerId = boost.Island.ServerId,
            CharacterId = boost.Island.CharacterId,
            CharacterName = boost.Island.CharacterName,
            IslandId = boost.Island.IslandId,
            EventId = boost.EventId,
            OccurredAt = packet.CapturedAt,
            Operation = "boost",
            SourceObjectId = boost.SourceObjectId,
            FocusUsed = boost.FocusChanges.Count == 1 ? boost.FocusChanges[0] : null
        });
    });

    private void ResetActivityState()
    {
        inventoryItems.Clear();
        observedPrices.Clear();
        placements.Clear();
        completedPlacements.Clear();
        pendingPlacementObjects.Clear();
        boosts.Clear();
        completedBoosts.Clear();
        seenFocusChanges.Clear();
        ResetPickupInventoryReturns();
    }

    private void PruneActivity()
    {
        var cutoff = DateTime.UtcNow - RequestLifetime;
        foreach (var key in placements.Where(entry => entry.Value.RequestedAt < cutoff).Select(entry => entry.Key).ToArray()) placements.Remove(key);
        foreach (var key in completedPlacements.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray()) completedPlacements.Remove(key);
        foreach (var key in pendingPlacementObjects.Where(entry => entry.Value.OccurredAt < cutoff).Select(entry => entry.Key).ToArray()) pendingPlacementObjects.Remove(key);
        foreach (var key in boosts.Where(entry => entry.Value.RequestedAt < cutoff).Select(entry => entry.Key).ToArray()) boosts.Remove(key);
        foreach (var key in completedBoosts.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray()) completedBoosts.Remove(key);
        foreach (var key in seenFocusChanges.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray()) seenFocusChanges.Remove(key);
        PrunePickupInventoryReturns();
    }

    private static bool ValidItemName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 200 && name is not ("Unset" or "Unknown Item");
    private sealed record InventoryItem(string Name, int Quality, int Quantity, DateTime ObservedAt);
    private sealed record EmvObservation(long Value, DateTime ObservedAt);
    private sealed record Placement(string EventId, DateTime RequestedAt, FarmingIslandObservation Island, FarmingActionItem? Item);
    private sealed record ConfirmedPlacement(Placement Placement, DateTime OccurredAt);
    private sealed record Boost(string EventId, DateTime RequestedAt, long ActionTimestamp, FarmingIslandObservation Island, string? SourceObjectId)
    {
        public List<int> FocusChanges { get; } = [];
    }
}
