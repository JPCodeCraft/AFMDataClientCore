using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private static readonly TimeSpan PlacementEvidenceWindow = TimeSpan.FromSeconds(10);
    private readonly Dictionary<(string Connection, long Id), InventoryItem> inventoryItems = new();
    private readonly Dictionary<(string Name, int Quality), EmvObservation> observedPrices = new();
    private readonly Dictionary<(string Connection, long Timestamp), Placement> placements = new();
    private readonly Dictionary<(string Connection, long Timestamp), DateTime> completedPlacements = new();
    private readonly Dictionary<(string Connection, long Id), ConfirmedPlacement> pendingPlacementObjects = new();
    private readonly HashSet<string> seenPlacementObjects = new(StringComparer.Ordinal);
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
        ObservePlacementInventoryChange(connection, id, previous, inventoryItems[key], capturedAt);
        ObservePickupInventoryItem(connection, id, previous, inventoryItems[key]);
    });

    public void OnInventoryItemDeleted(InventoryDeleteItemEvent packet) => Observe(() =>
    {
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

    public void OnPlacementRequest(PlaceableObjectPlaceRequest packet) => Observe(() =>
    {
        if (island is null || packet.ActionTimestamp is not { } stamp || packet.InventoryItemObjectId is not { } itemId) return;
        PruneActivity();
        var key = (packet.ConnectionId, stamp);
        if (placements.ContainsKey(key) || completedPlacements.ContainsKey(key) || placements.Count >= MaxTransientEntries) return;
        inventoryItems.TryGetValue((packet.ConnectionId, itemId), out var item);
        ConfirmPickupInventoryContainer(packet.ConnectionId, itemId);
        // Capture the price before the request removes the inventory stack.
        if (item?.ObservedAt > packet.CapturedAt || item?.Quantity <= 0) item = null;
        var input = item is null ? null : ActivityItem(item.Name, 1, item.Quality, packet.CapturedAt);
        placements[key] = new(Guid.NewGuid().ToString(), packet.CapturedAt, island, input,
            itemId, item, packet.PlaceableTypeIndex, packet.PositionX, packet.PositionY);
    });

    public void OnPlacementCancelled(PlaceableObjectPlaceCancelRequest packet) => Observe(() =>
    {
        PruneActivity();
        // This request has no identifier. Retire pending, unconsumed requests on
        // its connection; an already observed loss can still await its world event.
        foreach (var key in placements.Where(entry => entry.Key.Connection == packet.ConnectionId
            && entry.Value.RequestedAt <= packet.CapturedAt && entry.Value.ConsumedAt is null)
            .Select(entry => entry.Key).ToArray())
        {
            placements.Remove(key);
            completedPlacements[key] = packet.CapturedAt;
        }
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
        if (pendingPlacementObjects.Remove((packet.ConnectionId, packet.SessionId), out var pending)
            && destination.Kind == "farmable" && packet.CapturedAt >= pending.Placement.RequestedAt
            && packet.CapturedAt - pending.OccurredAt <= RequestLifetime)
            EnqueuePlacementAction(pending.Placement, pending.OccurredAt, destination);

        // Live placements can omit the operation response. A new world object
        // alone is not usage: require the local request and exact inventory loss.
        // Remember identities across visibility loss so re-entry cannot confirm one.
        if (seenPlacementObjects.Count >= MaxTransientEntries || !seenPlacementObjects.Add(destination.ObjectId)
            || island is null || destination.Kind != "farmable" || packet.PlaceableTypeIndex is null) return;
        PruneActivity();
        var candidates = placements.Where(entry => entry.Key.Connection == packet.ConnectionId
            && !entry.Value.Ambiguous && entry.Value.RequestedAt <= packet.CapturedAt
            && packet.CapturedAt - entry.Value.RequestedAt <= PlacementEvidenceWindow
            && entry.Value.PlaceableTypeIndex == packet.PlaceableTypeIndex
            && entry.Value.PositionX is { } x && Math.Abs(x - destination.PositionX) < 0.01
            && entry.Value.PositionY is { } y && Math.Abs(y - destination.PositionY) < 0.01
            && entry.Value.Item is { } item && SamePickupFamily(destination.UniqueName, item.UniqueName)).ToArray();
        if (candidates.Length != 1) return;
        var candidate = candidates[0];
        if (candidate.Value.Destination is not null) candidate.Value.Ambiguous = true;
        candidate.Value.Destination = destination;
        candidate.Value.DestinationAt = packet.CapturedAt;
        TryCompleteObservedPlacement(candidate.Key, candidate.Value);
    }

    private void ObservePlacementInventoryChange(string connection, long id, InventoryItem? previous,
        InventoryItem? current, DateTime capturedAt)
    {
        if (island is null || previous is null || (current is null && previous.Quantity == 0)
            || (current is not null && previous.Name == current.Name
            && previous.Quality == current.Quality && previous.Quantity == current.Quantity)) return;
        var candidates = placements.Where(entry => entry.Key.Connection == connection
            && entry.Value.InventoryItemId == id && entry.Value.ConsumedAt is null && !entry.Value.Ambiguous
            && entry.Value.RequestedAt <= capturedAt
            && capturedAt - entry.Value.RequestedAt <= PlacementEvidenceWindow).ToArray();
        // A previously consumed seed can still be waiting for its world event
        // while the next request consumes another seed from this same stack.
        foreach (var candidate in candidates)
        {
            var baseline = candidate.Value.Baseline;
            if (baseline is null || baseline.Name != previous.Name || baseline.Quality != previous.Quality
                || baseline.Quantity != previous.Quantity) candidate.Value.Ambiguous = true;
        }
        candidates = candidates.Where(candidate => !candidate.Value.Ambiguous).ToArray();
        foreach (var candidate in candidates)
        {
            var placement = candidate.Value;
            var baseline = placement.Baseline;
            if (candidates.Length != 1 || baseline is null
                || (current is not null && (current.Name != baseline.Name || current.Quality != baseline.Quality))
                || (long)previous.Quantity - (current?.Quantity ?? 0) != 1)
            {
                placement.Ambiguous = true;
                continue;
            }
            placement.ConsumedAt = capturedAt;
            TryCompleteObservedPlacement(candidate.Key, placement);
        }
    }

    private void TryCompleteObservedPlacement((string Connection, long Timestamp) key, Placement placement)
    {
        if (placement.Ambiguous || placement.ConsumedAt is not { } consumedAt
            || placement.DestinationAt is not { } destinationAt || placement.Destination is not { } destination) return;
        var occurredAt = consumedAt > destinationAt ? consumedAt : destinationAt;
        if (pickupInventoryMoves.TryGetValue(key.Connection, out var movedAt)
            && movedAt >= placement.RequestedAt && movedAt <= occurredAt) return;
        if (!placements.Remove(key)) return;
        completedPlacements[key] = occurredAt;
        EnqueuePlacementAction(placement, occurredAt, destination);
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
        seenPlacementObjects.Clear();
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
    private sealed record Placement(string EventId, DateTime RequestedAt, FarmingIslandObservation Island,
        FarmingActionItem? Item, long InventoryItemId, InventoryItem? Baseline, int? PlaceableTypeIndex,
        double? PositionX, double? PositionY)
    {
        public DateTime? ConsumedAt { get; set; }
        public FarmingObjectObservation? Destination { get; set; }
        public DateTime? DestinationAt { get; set; }
        public bool Ambiguous { get; set; }
    }
    private sealed record ConfirmedPlacement(Placement Placement, DateTime OccurredAt);
    private sealed record Boost(string EventId, DateTime RequestedAt, long ActionTimestamp, FarmingIslandObservation Island, string? SourceObjectId)
    {
        public List<int> FocusChanges { get; } = [];
    }
}
