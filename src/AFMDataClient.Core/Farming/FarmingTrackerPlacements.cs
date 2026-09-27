using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private static readonly TimeSpan PlacementEvidenceWindow = TimeSpan.FromSeconds(10);
    private readonly Dictionary<(string Connection, long Timestamp), Placement> placements = new();
    private readonly Dictionary<(string Connection, long Timestamp), DateTime> completedPlacements = new();
    private readonly HashSet<string> seenPlacementObjects = new(StringComparer.Ordinal);
    private readonly List<PlacementLoss> placementLosses = new();
    private readonly List<PlacementDestination> placementDestinations = new();
    private readonly Dictionary<(string Connection, long Session), PlacementObjectOwner> placementObjectOwners = new();

    public void OnPlacementRequest(PlaceableObjectPlaceRequest packet) => Observe(() =>
    {
        if (island is null || packet.ActionTimestamp is not { } stamp || packet.InventoryItemObjectId is not { } itemId) return;
        PruneActivity();
        var key = (packet.ConnectionId, stamp);
        if (placements.ContainsKey(key) || completedPlacements.ContainsKey(key) || placements.Count >= MaxTransientEntries) return;
        inventoryItems.TryGetValue((packet.ConnectionId, itemId), out var item);
        ConfirmPickupInventoryContainer(packet.ConnectionId, itemId);
        if (item?.ObservedAt > packet.CapturedAt || item?.Quantity <= 0) item = null;
        placements[key] = new(accountId!, Guid.NewGuid().ToString(), packet.CapturedAt, island,
            item is null ? null : ActivityItem(item.Name, 1, item.Quality, packet.CapturedAt),
            itemId, packet.PlaceableTypeIndex, packet.PositionX, packet.PositionY);
        ReconcilePlacements(packet.ConnectionId);
    });

    public void OnPlacementCancelled(PlaceableObjectPlaceCancelRequest packet) => Observe(() =>
    {
        PruneActivity();
        // Keep earlier successes recognizable when inbound evidence trails the
        // cancel, but prefer a subsequent equivalent attempt for later evidence.
        foreach (var placement in placements.Where(entry => entry.Key.Connection == packet.ConnectionId
            && entry.Value.RequestedAt <= packet.CapturedAt).Select(entry => entry.Value))
            placement.CancelledAt ??= packet.CapturedAt;
        ReconcilePlacements(packet.ConnectionId);
    });

    public void OnPlacementResponse(PlaceableObjectPlaceResponse packet) => Observe(() =>
    {
        if (packet.ActionTimestamp is not { } stamp) return;
        PruneActivity();
        var key = (packet.ConnectionId, stamp);
        if (!placements.TryGetValue(key, out var placement)) return;
        if (packet.ReturnCode != 0)
        {
            placements.Remove(key);
            completedPlacements[key] = packet.CapturedAt;
            ReconcilePlacements(packet.ConnectionId);
            return;
        }
        if (packet.PlacedObjectId is { } sessionId
            && !ReservePlacementObject(key, placement, sessionId, packet.CapturedAt))
        {
            // A fallback may already have confirmed this same object through an
            // equivalent retry. A later authoritative response cannot count it twice.
            placements.Remove(key);
            completedPlacements[key] = packet.CapturedAt;
            ReconcilePlacements(packet.ConnectionId);
            return;
        }
        placement.ConfirmedAt ??= packet.CapturedAt;
        placement.PlacedSessionId = packet.PlacedObjectId;
        if (objects.TryGetValue(packet.PlacedObjectId ?? -1, out var placed) && placed.Kind == "farmable")
        {
            placement.Destination = placed;
            placement.DestinationAt = packet.CapturedAt;
        }
        ReconcilePlacements(packet.ConnectionId);
    });

    private bool ReservePlacementObject((string Connection, long Timestamp) key, Placement placement, long sessionId, DateTime at)
    {
        var objectKey = (key.Connection, sessionId);
        if (placementObjectOwners.TryGetValue(objectKey, out var owner))
        {
            if (owner.Key != key) return false;
            placementDestinations.RemoveAll(value => value.Connection == key.Connection && value.SessionId == sessionId);
            return true;
        }
        if (placementObjectOwners.Count >= MaxTransientEntries) return false;
        placementObjectOwners[objectKey] = new(key, placement, at);
        placementDestinations.RemoveAll(value => value.Connection == key.Connection && value.SessionId == sessionId);
        return true;
    }

    private void ObservePlacementDestination(NewBuildingEvent packet, FarmingObjectObservation destination)
    {
        if (seenPlacementObjects.Count >= MaxTransientEntries || !seenPlacementObjects.Add(destination.ObjectId)
            || island is null || destination.Kind != "farmable" || packet.PlaceableTypeIndex is null) return;
        PruneActivity();
        if (placementObjectOwners.TryGetValue((packet.ConnectionId, packet.SessionId), out var owner))
        {
            // Success responses reserve their object even if the inventory update
            // already retired the request before its world event arrives.
            if (placements.TryGetValue(owner.Key, out var placement)
                && packet.CapturedAt >= placement.RequestedAt)
            {
                placement.Destination = destination;
                placement.DestinationAt = packet.CapturedAt;
                ReconcilePlacements(packet.ConnectionId);
            }
            return;
        }
        if (placementDestinations.Count >= MaxTransientEntries) return;
        placementDestinations.Add(new(packet.ConnectionId, packet.SessionId, packet.PlaceableTypeIndex.Value, packet.CapturedAt, destination));
        ReconcilePlacements(packet.ConnectionId);
    }

    private void ObservePlacementInventoryChange(string connection, long id, InventoryItem? previous,
        InventoryItem? current, DateTime capturedAt)
    {
        if (island is null || previous is null || !previous.Name.Contains("_FARM_", StringComparison.Ordinal)
            || (current is not null && (current.Name != previous.Name || current.Quality != previous.Quality))) return;
        var quantity = (long)previous.Quantity - (current?.Quantity ?? 0);
        if (quantity <= 0 || quantity > MaxTransientEntries || placementLosses.Count >= MaxTransientEntries) return;
        // A multi-unit update is usable only when that many local placement
        // requests exist. A stack disappearing on its own is not planting.
        var possible = placements.Count(entry => entry.Key.Connection == connection
            && entry.Value.InventoryItemId == id && entry.Value.ConsumedAt is null
            && entry.Value.Item is { } item && item.UniqueName == previous.Name && item.Quality == previous.Quality
            && entry.Value.RequestedAt <= capturedAt && capturedAt - entry.Value.RequestedAt <= PlacementEvidenceWindow);
        if (quantity > possible) return;
        placementLosses.Add(new(connection, id, previous.Name, previous.Quality, capturedAt, (int)quantity));
        ReconcilePlacements(connection);
    }

    private void ReconcilePlacements(string connection)
    {
        foreach (var evidence in placementDestinations.Where(value => value.Connection == connection).ToArray())
        {
            var candidates = placements.Where(entry => entry.Key.Connection == connection
                && entry.Value.Destination is null && entry.Value.RequestedAt <= evidence.ObservedAt
                && evidence.ObservedAt - entry.Value.RequestedAt <= PlacementEvidenceWindow
                && (entry.Value.PlacedSessionId == evidence.SessionId
                    || (entry.Value.PlaceableTypeIndex == evidence.TypeIndex
                        && entry.Value.PositionX is { } x && Math.Abs(x - evidence.Object.PositionX) < 0.01
                        && entry.Value.PositionY is { } y && Math.Abs(y - evidence.Object.PositionY) < 0.01
                        && entry.Value.Item is { } item && SamePickupFamily(evidence.Object.UniqueName, item.UniqueName))))
                .OrderBy(entry => entry.Value.RequestedAt).ToArray();
            if (candidates.Length == 0) continue;
            var candidate = candidates[0];
            if (candidates.Length > 1)
            {
                // Retries at one tile can be indistinguishable. They may share
                // exactly one confirmed world object, never multiply its cost.
                // Differing inventory identities remain ambiguous.
                if (candidates.Any(entry => !EquivalentPlacement(candidate.Value, entry.Value))) continue;
                var eligible = candidates.Where(entry => FindPlacementLoss(connection, entry.Value, evidence.ObservedAt) is not null)
                    .OrderBy(entry => entry.Value.CancelledAt is null ? 0 : 1)
                    .ThenByDescending(entry => entry.Value.RequestedAt).ToArray();
                if (eligible.Length == 0) continue;
                candidate = eligible[0];
            }
            if (!ReservePlacementObject(candidate.Key, candidate.Value, evidence.SessionId, evidence.ObservedAt)) continue;
            candidate.Value.Destination = evidence.Object;
            candidate.Value.DestinationAt = evidence.ObservedAt;
        }

        foreach (var (key, placement) in placements.Where(entry => entry.Key.Connection == connection
            && (entry.Value.Destination is not null || entry.Value.ConfirmedAt is not null))
            .OrderBy(entry => entry.Value.RequestedAt).ToArray())
        {
            if (placement.ConsumedAt is null && placement.Item is not null)
            {
                var loss = FindPlacementLoss(connection, placement, placement.DestinationAt);
                if (loss is not null)
                {
                    loss.Remaining--;
                    placement.ConsumedAt = loss.ObservedAt;
                }
            }

            // A success response is authoritative. Without one, require a unique
            // new farmable plus a separately observed unit removed from its stack.
            var confirmed = placement.ConfirmedAt is not null
                && (placement.Item?.UniqueName.Contains("_FARM_", StringComparison.Ordinal) == true
                    || placement.Destination?.Kind == "farmable");
            var inferred = placement.ConsumedAt is not null && placement.DestinationAt is not null;
            if (!placement.Uploaded && (confirmed || inferred))
            {
                var occurredAt = placement.ConfirmedAt
                    ?? (placement.ConsumedAt > placement.DestinationAt ? placement.ConsumedAt : placement.DestinationAt)!.Value;
                EnqueuePlacementAction(placement, occurredAt, placement.Destination);
                placement.Uploaded = true;
                completedPlacements[key] = occurredAt;
            }
            // Retain a response-confirmed placement until its inventory loss has
            // been reserved, so a following placement cannot reuse that loss.
            if (placement.Uploaded && placement.ConsumedAt is not null) placements.Remove(key);
        }
        placementLosses.RemoveAll(value => value.Remaining == 0);
    }

    private PlacementLoss? FindPlacementLoss(string connection, Placement placement, DateTime? destinationAt)
    {
        if (placement.Item is not { } item) return null;
        return placementLosses.FirstOrDefault(value => value.Connection == connection
            && value.InventoryItemId == placement.InventoryItemId && value.Remaining > 0
            && value.Name == item.UniqueName && value.Quality == item.Quality
            && value.ObservedAt >= placement.RequestedAt
            && value.ObservedAt - placement.RequestedAt <= PlacementEvidenceWindow
            && !HasRelevantInventoryMove(connection, placement.InventoryItemId, placement.RequestedAt,
                destinationAt is { } at && at > value.ObservedAt ? at : value.ObservedAt, item.UniqueName));
    }

    private static bool EquivalentPlacement(Placement left, Placement right) =>
        left.AccountId == right.AccountId && left.Island.IslandId == right.Island.IslandId
        && left.Island.CharacterId == right.Island.CharacterId && left.Island.ServerId == right.Island.ServerId
        && left.InventoryItemId == right.InventoryItemId && left.PlaceableTypeIndex == right.PlaceableTypeIndex
        && left.PositionX == right.PositionX && left.PositionY == right.PositionY
        && left.Item is { } first && right.Item is { } second
        && first.UniqueName == second.UniqueName && first.Quality == second.Quality;

    private void EnqueuePlacementAction(Placement placement, DateTime occurredAt, FarmingObjectObservation? placed)
    {
        uploader.EnqueueAction(placement.AccountId, new FarmingAction
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

    private void ResetPlacementState()
    {
        placements.Clear();
        completedPlacements.Clear();
        seenPlacementObjects.Clear();
        placementLosses.Clear();
        placementDestinations.Clear();
        placementObjectOwners.Clear();
    }

    private void PrunePlacementState(DateTime cutoff)
    {
        foreach (var key in placements.Where(entry => entry.Value.RequestedAt < cutoff).Select(entry => entry.Key).ToArray())
            placements.Remove(key);
        foreach (var key in completedPlacements.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray())
            completedPlacements.Remove(key);
        placementLosses.RemoveAll(value => value.ObservedAt < cutoff);
        placementDestinations.RemoveAll(value => value.ObservedAt < cutoff);
        foreach (var key in placementObjectOwners.Where(entry => entry.Value.ObservedAt < cutoff).Select(entry => entry.Key).ToArray())
            placementObjectOwners.Remove(key);
    }

    private sealed record Placement(string AccountId, string EventId, DateTime RequestedAt, FarmingIslandObservation Island,
        FarmingActionItem? Item, long InventoryItemId, int? PlaceableTypeIndex, double? PositionX, double? PositionY)
    {
        public DateTime? ConsumedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public long? PlacedSessionId { get; set; }
        public FarmingObjectObservation? Destination { get; set; }
        public DateTime? DestinationAt { get; set; }
        public bool Uploaded { get; set; }
    }

    private sealed record PlacementObjectOwner((string Connection, long Timestamp) Key, Placement Placement, DateTime ObservedAt);
    private sealed record PlacementDestination(string Connection, long SessionId, int TypeIndex, DateTime ObservedAt, FarmingObjectObservation Object);
    private sealed class PlacementLoss(string connection, long inventoryItemId, string name, int quality, DateTime observedAt, int remaining)
    {
        public string Connection { get; } = connection;
        public long InventoryItemId { get; } = inventoryItemId;
        public string Name { get; } = name;
        public int Quality { get; } = quality;
        public DateTime ObservedAt { get; } = observedAt;
        public int Remaining { get; set; } = remaining;
    }
}
