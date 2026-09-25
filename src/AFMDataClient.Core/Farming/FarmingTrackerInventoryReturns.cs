using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private static readonly TimeSpan PickupInventoryWindow = TimeSpan.FromSeconds(10);
    private readonly Dictionary<(string Connection, long Id), PickupInventoryMembership> pickupInventoryMembership = new();
    private readonly HashSet<(string Connection, long Id)> seenPickupInventoryItems = [];
    private readonly Dictionary<string, Guid> pickupInventoryContainers = new();
    private readonly Dictionary<string, DateTime> pickupInventoryMoves = new();
    private readonly Dictionary<string, PickupReturnEvidence> pickupInventoryReturns = new();

    public void OnPickupInventoryPut(InventoryPutItemEvent packet) => Observe(() =>
    {
        if ((!joining && island is null) || packet.ItemObjectId <= 0 || packet.ContainerId == Guid.Empty) return;
        SetPickupInventoryMembership(packet.ConnectionId, packet.ItemObjectId, packet.ContainerId, packet.CapturedAt, true);
    });

    public void OnPickupInventoryState(InventoryStateEvent packet) => Observe(() =>
    {
        if ((!joining && island is null) || packet.ContainerId is not { } container) return;
        var present = packet.ItemObjectIds.Where(id => id > 0).ToHashSet();
        foreach (var (key, membership) in pickupInventoryMembership.ToArray())
        {
            if (key.Connection == packet.ConnectionId && membership.Container == container
                && membership.ObservedAt <= packet.CapturedAt && !present.Contains(key.Id))
                ForgetPickupInventoryItem(key.Connection, key.Id);
        }
        foreach (var id in present)
            SetPickupInventoryMembership(packet.ConnectionId, id, container, packet.CapturedAt, false);
    });

    // A local placement explicitly identifies its source inventory object. Its
    // observed container establishes the bag without treating every open chest
    // or every InventoryState packet as belonging to the local character.
    private void ConfirmPickupInventoryContainer(string connection, long sourceInventoryId)
    {
        if (!inventoryItems.TryGetValue((connection, sourceInventoryId), out var item) || item.Quantity <= 0
            || !pickupInventoryMembership.TryGetValue((connection, sourceInventoryId), out var membership)) return;
        if (pickupInventoryContainers.TryGetValue(connection, out var previous) && previous != membership.Container)
            InvalidatePickupReturns(connection);
        if (pickupInventoryContainers.Count < MaxTransientEntries || pickupInventoryContainers.ContainsKey(connection))
            pickupInventoryContainers[connection] = membership.Container;
    }

    public void OnPickupInventoryMove(string connection, DateTime capturedAt) => Observe(() =>
    {
        if (pickupInventoryMoves.Count >= MaxTransientEntries && !pickupInventoryMoves.ContainsKey(connection)) return;
        if (!pickupInventoryMoves.TryGetValue(connection, out var previous) || previous < capturedAt)
            pickupInventoryMoves[connection] = capturedAt;
        InvalidatePickupReturns(connection);
    });

    private void ObservePickupInventoryItem(string connection, long id, InventoryItem? previous, InventoryItem current)
    {
        var key = (connection, id);
        var firstObservation = !seenPickupInventoryItems.Contains(key);
        if (firstObservation && seenPickupInventoryItems.Count >= MaxTransientEntries) return;
        seenPickupInventoryItems.Add(key);

        // Any subsequent change to a candidate makes the single-item return
        // ambiguous. Repeated full snapshots with the same quantity are benign.
        foreach (var evidence in pickupInventoryReturns.Values.Where(value => value.Connection == connection
            && value.Candidate?.ObjectId == id))
        {
            if (evidence.Candidate is { } candidate && (candidate.Name != current.Name
                || candidate.Quality != current.Quality || candidate.StackQuantity != current.Quantity))
                evidence.Ambiguous = true;
        }
        if (island is null || !pickupInventoryContainers.TryGetValue(connection, out var bag)) return;
        var pending = actions.Where(entry => entry.Key.Connection == connection
            && entry.Value.RequestedAt <= current.ObservedAt
            && current.ObservedAt - entry.Value.RequestedAt <= PickupInventoryWindow).ToArray();
        if (pending.Length != 1 || pending[0].Key.Operation != (short)OperationCodes.PlaceableObjectPickup) return;
        var action = pending[0].Value;
        if (action.Source?.Kind != "farmable" || !SamePickupFamily(action.Source.UniqueName, current.Name)
            || HasPickupInventoryMove(connection, action.RequestedAt, current.ObservedAt)) return;

        pickupInventoryMembership.TryGetValue(key, out var membership);
        var knownBaseline = previous is not null && previous.ObservedAt <= action.RequestedAt
            && previous.Name == current.Name && previous.Quality == current.Quality
            && membership?.Container == bag && membership.JoinedAt <= action.RequestedAt;
        var newItem = previous is null && firstObservation
            && (membership is null || membership.JoinedAt >= action.RequestedAt);
        if (!knownBaseline && !newItem) return;
        var change = knownBaseline ? (long)current.Quantity - previous!.Quantity : current.Quantity;
        if (change <= 0) return;
        if (!pickupInventoryReturns.TryGetValue(action.EventId, out var evidenceForAction))
        {
            if (pickupInventoryReturns.Count >= MaxTransientEntries) return;
            evidenceForAction = new(connection, action.RequestedAt);
            pickupInventoryReturns[action.EventId] = evidenceForAction;
        }
        // One pickup removes one world animal. A larger increase could include
        // another transfer; do not charge or credit that inventory movement.
        if (change != 1 || evidenceForAction.Candidate is not null)
        {
            evidenceForAction.Ambiguous = true;
            return;
        }
        if (membership is not null && membership.Container != bag)
        {
            evidenceForAction.Ambiguous = true;
            return;
        }
        evidenceForAction.Candidate = new(id, current.Name, current.Quality, current.Quantity,
            current.ObservedAt, newItem, membership?.LastPutAt >= action.RequestedAt);
    }

    private List<FarmingActionItem> CompletePickupInventoryReturn(PendingAction action, FarmingActionResponse packet)
    {
        if (!pickupInventoryReturns.Remove(action.EventId, out var evidence) || evidence.Ambiguous
            || evidence.Candidate is not { } candidate || candidate.ObservedAt > packet.CapturedAt
            || packet.CapturedAt - action.RequestedAt > PickupInventoryWindow
            || HasPickupInventoryMove(packet.ConnectionId, action.RequestedAt, packet.CapturedAt)
            || (candidate.RequiresPut && !candidate.PutConfirmed)
            || !pickupInventoryContainers.TryGetValue(packet.ConnectionId, out var bag)
            || !pickupInventoryMembership.TryGetValue((packet.ConnectionId, candidate.ObjectId), out var membership)
            || membership.Container != bag
            || !inventoryItems.TryGetValue((packet.ConnectionId, candidate.ObjectId), out var current)
            || current.Name != candidate.Name || current.Quality != candidate.Quality
            || current.Quantity != candidate.StackQuantity) return [];
        // CompleteAction removes this request before calling us. Another pending
        // action in the same window means the quantity attribution is uncertain.
        if (actions.Any(entry => entry.Key.Connection == packet.ConnectionId
            && entry.Value.RequestedAt <= packet.CapturedAt
            && packet.CapturedAt - entry.Value.RequestedAt <= PickupInventoryWindow)) return [];
        return [ActivityItem(candidate.Name, 1, candidate.Quality, candidate.ObservedAt)];
    }

    private void SetPickupInventoryMembership(string connection, long id, Guid container, DateTime at, bool isPut)
    {
        var key = (connection, id);
        if (pickupInventoryMembership.Count >= MaxTransientEntries && !pickupInventoryMembership.ContainsKey(key)) return;
        pickupInventoryMembership.TryGetValue(key, out var previous);
        if (previous?.ObservedAt > at) return;
        var sameContainer = previous?.Container == container;
        pickupInventoryMembership[key] = new(container, sameContainer ? previous!.JoinedAt : at,
            at, isPut ? at : sameContainer ? previous!.LastPutAt : null);
        foreach (var evidence in pickupInventoryReturns.Values.Where(value => value.Connection == connection
            && value.Candidate?.ObjectId == id))
        {
            if (!pickupInventoryContainers.TryGetValue(connection, out var bag) || bag != container)
                evidence.Ambiguous = true;
            else if (isPut && at >= evidence.RequestedAt && at - evidence.RequestedAt <= PickupInventoryWindow)
                evidence.Candidate = evidence.Candidate! with { PutConfirmed = true };
        }
    }

    private void ForgetPickupInventoryItem(string connection, long id)
    {
        pickupInventoryMembership.Remove((connection, id));
        // Retain the seen identity until the visit ends: a deleted/re-added
        // stack is not evidence that a new inventory object was created.
        if (seenPickupInventoryItems.Count < MaxTransientEntries) seenPickupInventoryItems.Add((connection, id));
        foreach (var evidence in pickupInventoryReturns.Values.Where(value => value.Connection == connection
            && value.Candidate?.ObjectId == id)) evidence.Ambiguous = true;
    }

    private bool HasPickupInventoryMove(string connection, DateTime requestedAt, DateTime observedAt) =>
        pickupInventoryMoves.TryGetValue(connection, out var movedAt)
        && movedAt >= requestedAt - PickupInventoryWindow && movedAt <= observedAt;

    private void InvalidatePickupReturns(string connection)
    {
        foreach (var evidence in pickupInventoryReturns.Values.Where(value => value.Connection == connection))
            evidence.Ambiguous = true;
    }

    private static bool SamePickupFamily(string worldName, string itemName)
    {
        if (!worldName.Contains("_FARM_", StringComparison.Ordinal)
            || !itemName.Contains("_FARM_", StringComparison.Ordinal)) return false;
        // This only filters observed inventory identities. It never chooses the
        // adult/baby variant from a world tile, which can represent either.
        static string Family(string name) => name.Replace("_GROWN", "_BABY", StringComparison.Ordinal);
        return Family(worldName) == Family(itemName);
    }

    private void ResetPickupInventoryReturns()
    {
        pickupInventoryMembership.Clear();
        seenPickupInventoryItems.Clear();
        pickupInventoryContainers.Clear();
        pickupInventoryMoves.Clear();
        pickupInventoryReturns.Clear();
    }

    private void PrunePickupInventoryReturns()
    {
        var cutoff = DateTime.UtcNow - RequestLifetime;
        foreach (var key in pickupInventoryReturns.Where(entry => entry.Value.RequestedAt < cutoff).Select(entry => entry.Key).ToArray())
            pickupInventoryReturns.Remove(key);
        foreach (var key in pickupInventoryMoves.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray())
            pickupInventoryMoves.Remove(key);
    }

    private sealed record PickupInventoryMembership(Guid Container, DateTime JoinedAt, DateTime ObservedAt, DateTime? LastPutAt);
    private sealed record PickupReturnCandidate(long ObjectId, string Name, int Quality, int StackQuantity,
        DateTime ObservedAt, bool RequiresPut, bool PutConfirmed);
    private sealed class PickupReturnEvidence(string connection, DateTime requestedAt)
    {
        public string Connection { get; } = connection;
        public DateTime RequestedAt { get; } = requestedAt;
        public PickupReturnCandidate? Candidate { get; set; }
        public bool Ambiguous { get; set; }
    }
}
