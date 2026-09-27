using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private static readonly TimeSpan PickupInventoryWindow = TimeSpan.FromSeconds(10);
    private readonly Dictionary<(string Connection, long Id), PickupInventoryMembership> pickupInventoryMembership = new();
    private readonly Dictionary<(string Connection, Guid Container), long[]> pickupInventorySlots = new();
    private readonly HashSet<(string Connection, long Id)> seenPickupInventoryItems = [];
    private readonly Dictionary<string, Guid> pickupInventoryContainers = new();
    private readonly List<RelevantInventoryMove> pickupInventoryMoves = [];
    private readonly Dictionary<string, PickupReturnEvidence> pickupInventoryReturns = new();
    private readonly List<PickupInventoryGain> pickupInventoryGains = [];
    private readonly List<OtherInventoryReturn> otherInventoryReturns = [];

    private void InitializePickupInventoryFromJoin(JoinResponse packet)
    {
        if (packet.ReturnCode != 0 || island is null || localObjectId != packet.userObjectId
            || packet.MainInventoryContainerId is not { } container) return;
        var connection = packet.ConnectionId;
        if (pickupInventoryContainers.Count >= MaxTransientEntries && !pickupInventoryContainers.ContainsKey(connection)) return;
        if (pickupInventoryContainers.TryGetValue(connection, out var previous) && previous != container)
            InvalidatePickupReturns(connection);
        pickupInventoryContainers[connection] = container;
        ApplyPickupInventoryState(connection, container, packet.MainInventoryItemObjectIds, packet.CapturedAt);
    }

    public void OnPickupInventoryPut(InventoryPutItemEvent packet) => Observe(() =>
    {
        if ((!joining && island is null) || packet.ItemObjectId <= 0 || packet.ContainerId == Guid.Empty) return;
        pickupInventorySlots.Remove((packet.ConnectionId, packet.ContainerId));
        SetPickupInventoryMembership(packet.ConnectionId, packet.ItemObjectId, packet.ContainerId, packet.CapturedAt, true);
        FlushPickupReturns(packet.CapturedAt);
    });

    public void OnPickupInventoryState(InventoryStateEvent packet) => Observe(() =>
    {
        if ((!joining && island is null) || packet.ContainerId is not { } container) return;
        ApplyPickupInventoryState(packet.ConnectionId, container, packet.ItemObjectIds, packet.CapturedAt);
    });

    private void ApplyPickupInventoryState(string connection, Guid container, IReadOnlyList<long> itemIds, DateTime observedAt)
    {

        var present = itemIds.Where(id => id > 0).ToHashSet();
        foreach (var (key, membership) in pickupInventoryMembership.ToArray())
        {
            if (key.Connection == connection && membership.Container == container
                && membership.ObservedAt <= observedAt && !present.Contains(key.Id))
                ForgetPickupInventoryItem(key.Connection, key.Id);
        }
        foreach (var id in present)
            SetPickupInventoryMembership(connection, id, container, observedAt, false);
        var slotKey = (connection, container);
        if (pickupInventorySlots.Count < MaxTransientEntries || pickupInventorySlots.ContainsKey(slotKey))
            pickupInventorySlots[slotKey] = itemIds.ToArray();
    }

    private void ConfirmPickupInventoryContainer(string connection, long sourceInventoryId)
    {
        if (!inventoryItems.TryGetValue((connection, sourceInventoryId), out var item) || item.Quantity <= 0
            || !pickupInventoryMembership.TryGetValue((connection, sourceInventoryId), out var membership)) return;
        if (pickupInventoryContainers.TryGetValue(connection, out var previous) && previous != membership.Container)
            InvalidatePickupReturns(connection);
        if (pickupInventoryContainers.Count < MaxTransientEntries || pickupInventoryContainers.ContainsKey(connection))
            pickupInventoryContainers[connection] = membership.Container;
    }

    public void OnPickupInventoryMove(InventoryMoveItemRequest packet) => Observe(() =>
    {
        var ids = new HashSet<long>();
        var sourceKnown = TryGetInventorySlot(packet.ConnectionId, packet.SourceContainerId, packet.SourceSlot, out var sourceId);
        var destinationKnown = TryGetInventorySlot(packet.ConnectionId, packet.DestinationContainerId, packet.DestinationSlot, out var destinationId);
        if (sourceId > 0) ids.Add(sourceId);
        if (destinationId > 0) ids.Add(destinationId);
        ObserveRelevantInventoryMove(packet.ConnectionId, packet.CapturedAt, packet.SourceContainerId,
            packet.DestinationContainerId, ids, sourceKnown && destinationKnown);
    });

    public void OnPickupInventoryMove(InventoryMoveGivenItemsRequest packet) => Observe(() =>
        ObserveRelevantInventoryMove(packet.ConnectionId, packet.CapturedAt, packet.SourceContainerId,
            packet.DestinationContainerId, packet.ItemObjectIds.Where(id => id > 0).ToHashSet(), packet.ItemObjectIds.Count > 0));

    private bool TryGetInventorySlot(string connection, Guid container, int slot, out long id)
    {
        id = 0;
        if (!pickupInventorySlots.TryGetValue((connection, container), out var slots) || slot < 0 || slot >= slots.Length) return false;
        id = slots[slot];
        return true;
    }

    private void ObserveRelevantInventoryMove(string connection, DateTime at, Guid source, Guid destination,
        HashSet<long> ids, bool identitiesKnown)
    {
        PrunePickupInventoryReturns();
        if (pickupInventoryMoves.Count >= MaxTransientEntries) return;
        var names = ids.Select(id => inventoryItems.GetValueOrDefault((connection, id))?.Name)
            .Where(name => name is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        identitiesKnown &= ids.Count > 0 && ids.All(id => inventoryItems.ContainsKey((connection, id)));
        pickupInventorySlots.Remove((connection, source));
        pickupInventorySlots.Remove((connection, destination));
        pickupInventoryMoves.Add(new(connection, at, source, destination, ids, names, identitiesKnown));
    }

    private bool HasRelevantInventoryMove(string connection, long itemId, DateTime requestedAt, DateTime observedAt,
        string? recordedName = null, Guid? recordedContainer = null)
    {
        inventoryItems.TryGetValue((connection, itemId), out var item);
        pickupInventoryMembership.TryGetValue((connection, itemId), out var membership);
        return pickupInventoryMoves.Any(move => move.Connection == connection
            && move.At >= requestedAt - PickupInventoryWindow && move.At <= observedAt
            && MoveTouchesItem(move, itemId, recordedName ?? item?.Name, recordedContainer ?? membership?.Container));
    }

    private static bool MoveTouchesItem(RelevantInventoryMove move, long id, string? name, Guid? container) =>
        move.ItemIds.Contains(id) || (name is not null && move.Names.Contains(name))
        || (!move.IdentitiesKnown && (container is null || move.Source == Guid.Empty || move.Destination == Guid.Empty
            || move.Source == container || move.Destination == container));

    private void BeginPickupInventoryReturn(string connection, PendingAction action)
    {
        PrunePickupInventoryReturns();
        if (action.Source?.Kind != "farmable" || pickupInventoryReturns.Count >= MaxTransientEntries
            || pickupInventoryReturns.ContainsKey(action.EventId)) return;
        pickupInventoryContainers.TryGetValue(connection, out var bag);
        // Preserve the request-time baseline. Repeated unchanged item snapshots
        // after the request must not make a known inventory stack look new.
        var baselines = inventoryItems.Where(entry => entry.Key.Connection == connection
            && entry.Value.ObservedAt <= action.RequestedAt && SamePickupFamily(action.Source.UniqueName, entry.Value.Name)
            && pickupInventoryMembership.TryGetValue(entry.Key, out var membership)
            && membership.Container == bag && membership.JoinedAt <= action.RequestedAt)
            .ToDictionary(entry => entry.Key.Id, entry => entry.Value);
        pickupInventoryReturns[action.EventId] = new(connection, accountId!, action, bag, baselines);
    }

    private void CancelPickupInventoryReturn(PendingAction action) => pickupInventoryReturns.Remove(action.EventId);

    private void QueuePickupInventoryReturn(PendingAction action, FarmingActionResponse packet)
    {
        if (!pickupInventoryReturns.TryGetValue(action.EventId, out var evidence))
        {
            // A capacity limit or unsupported initial context must still retain
            // the successful pickup, with an explicitly unknown returned item.
            EnqueuePickupReturn(accountId!, action, packet.CapturedAt, null);
            return;
        }
        evidence.CompletedAt = packet.CapturedAt;
        evidence.CompletedReceivedAt = DateTime.UtcNow;
        FlushPickupReturns(packet.CapturedAt);
    }

    private void ObserveOtherFarmingReturn(string connection, PendingAction action, FarmingActionResponse packet)
    {
        foreach (var item in packet.Items.Where(item => item.UniqueName.Contains("_FARM_", StringComparison.Ordinal)))
        {
            if (otherInventoryReturns.Count >= MaxTransientEntries) break;
            otherInventoryReturns.Add(new(connection, action.RequestedAt, item.UniqueName, item.Quantity));
        }
        ReserveOtherInventoryReturns();
        FlushPickupReturns(packet.CapturedAt);
    }

    private void ObservePickupInventoryItem(string connection, long id, InventoryItem? previous, InventoryItem current)
    {
        var key = (connection, id);
        var firstObservation = !seenPickupInventoryItems.Contains(key);
        if (firstObservation && seenPickupInventoryItems.Count >= MaxTransientEntries) return;
        seenPickupInventoryItems.Add(key);
        pickupInventoryMembership.TryGetValue(key, out var membership);
        if (island is null || pickupInventoryGains.Count >= MaxTransientEntries
            || !current.Name.Contains("_FARM_", StringComparison.Ordinal)) return;
        var knownPrevious = previous is not null && previous.Name == current.Name && previous.Quality == current.Quality;
        if (!knownPrevious && !(previous is null && firstObservation)) return;
        var change = (long)current.Quantity - (knownPrevious ? previous!.Quantity : 0);
        if (change <= 0 || change > MaxTransientEntries) return;
        // Keep immutable increments rather than one mutable latest stack. Two
        // pickups may grow the same stack before either response is processed.
        pickupInventoryGains.Add(new(connection, id, current.Name, current.Quality, (int)change,
            current.ObservedAt, previous is null && firstObservation, membership?.Container, membership?.LastPutAt,
            ActivityItem(current.Name, 1, current.Quality, current.ObservedAt)));
        ReserveOtherInventoryReturns();
        FlushPickupReturns(current.ObservedAt);
    }

    private void ReserveOtherInventoryReturns()
    {
        foreach (var other in otherInventoryReturns.Where(other => other.Remaining > 0))
        {
            foreach (var gain in pickupInventoryGains.Where(gain => gain.Connection == other.Connection
                && gain.Name == other.Name && gain.ObservedAt >= other.RequestedAt
                && gain.ObservedAt - other.RequestedAt <= PickupInventoryWindow && gain.Remaining > 0))
            {
                var consumed = Math.Min(other.Remaining, gain.Remaining);
                other.Remaining -= consumed;
                gain.Remaining -= consumed;
                if (other.Remaining == 0) break;
            }
        }
    }

    private bool MatchesPickupGain(PickupReturnEvidence evidence, PickupInventoryGain gain)
    {
        if (evidence.Ambiguous || evidence.Bag == Guid.Empty || gain.Connection != evidence.Connection
            || gain.Remaining == 0 || gain.ObservedAt < evidence.Action.RequestedAt
            || gain.ObservedAt - evidence.Action.RequestedAt > PickupInventoryWindow
            || !SamePickupFamily(evidence.Action.Source!.UniqueName, gain.Name)
            || gain.Container != evidence.Bag
            || HasRelevantInventoryMove(gain.Connection, gain.ItemId, evidence.Action.RequestedAt, gain.ObservedAt, gain.Name, gain.Container)) return false;
        if (evidence.Baselines.TryGetValue(gain.ItemId, out var baseline))
            return baseline.Name == gain.Name && baseline.Quality == gain.Quality;
        return gain.FirstObservation && gain.PutAt >= evidence.Action.RequestedAt
            && gain.PutAt <= evidence.Action.RequestedAt + PickupInventoryWindow;
    }

    private void FlushPickupReturns(DateTime now)
    {
        var processingNow = DateTime.UtcNow;
        ReserveOtherInventoryReturns();
        var remaining = pickupInventoryReturns.Values.OrderBy(value => value.Action.RequestedAt).ToList();
        while (remaining.Count > 0)
        {
            var group = new List<PickupReturnEvidence> { remaining[0] };
            remaining.RemoveAt(0);
            // Overlapping windows are one attribution group. A later successful
            // response can disambiguate earlier increments without discarding them.
            for (var i = 0; i < group.Count; i++)
            {
                var member = group[i];
                foreach (var other in remaining.Where(other => other.Connection == member.Connection
                    && SamePickupFamily(member.Action.Source!.UniqueName, other.Action.Source!.UniqueName)
                    && Math.Abs((other.Action.RequestedAt - member.Action.RequestedAt).TotalSeconds)
                        <= PickupInventoryWindow.TotalSeconds).ToArray())
                {
                    group.Add(other);
                    remaining.Remove(other);
                }
            }
            var successful = group.Where(value => value.CompletedAt.HasValue).ToArray();
            if (successful.Length == 0) continue;
            var waiting = group.Any(value => value.CompletedAt is null && processingNow - value.ReceivedAt <= PickupInventoryWindow);
            // A harvest/finish response names its outputs. Let that explicit
            // evidence claim its inventory increment before attributing a pickup.
            waiting |= actions.Any(entry => entry.Key.Connection == group[0].Connection
                && entry.Key.Operation is not ((short)OperationCodes.PlaceableObjectPickup) and not ((short)OperationCodes.FarmableFill)
                && group.Any(value => Math.Abs((entry.Value.RequestedAt - value.Action.RequestedAt).TotalSeconds)
                        <= PickupInventoryWindow.TotalSeconds
                    && (entry.Value.Source is null
                        || SamePickupFamily(value.Action.Source!.UniqueName, entry.Value.Source.UniqueName))));
            if (!waiting)
            {
                var gains = pickupInventoryGains.Where(gain => successful.Any(value => MatchesPickupGain(value, gain))).ToArray();
                var quantity = gains.Sum(gain => gain.Remaining);
                // Equal totals plus an eligible one-to-one assignment are needed;
                // surplus inventory growth is not silently credited as a pickup.
                if (quantity == successful.Length && TryAssignPickupGains(successful, gains, out var assigned))
                {
                    foreach (var (evidence, gain) in assigned)
                    {
                        gain.Remaining--;
                        EnqueuePickupReturn(evidence.AccountId, evidence.Action, evidence.CompletedAt!.Value, gain.Item);
                        pickupInventoryReturns.Remove(evidence.Action.EventId);
                    }
                    continue;
                }
            }
            if (!waiting && successful.All(value => processingNow - value.CompletedReceivedAt > PickupInventoryWindow))
            {
                // Resolve an ambiguous connected group together. Removing one
                // completion first would let its increment shift onto a peer.
                foreach (var evidence in successful)
                    EnqueuePickupReturn(evidence.AccountId, evidence.Action, evidence.CompletedAt!.Value, null);
                foreach (var evidence in group) pickupInventoryReturns.Remove(evidence.Action.EventId);
                foreach (var gain in pickupInventoryGains.Where(gain => group.Any(value => gain.Connection == value.Connection
                    && gain.ObservedAt >= value.Action.RequestedAt
                    && gain.ObservedAt - value.Action.RequestedAt <= PickupInventoryWindow
                    && SamePickupFamily(value.Action.Source!.UniqueName, gain.Name)))) gain.Remaining = 0;
            }
        }
    }

    private bool TryAssignPickupGains(PickupReturnEvidence[] evidence, PickupInventoryGain[] gains,
        out List<(PickupReturnEvidence Evidence, PickupInventoryGain Gain)> assigned)
    {
        assigned = [];
        // Identical returned items are interchangeable. If multiple identities
        // fit one world object, preserve unknown attribution rather than guess.
        if (evidence.Any(value => gains.Where(gain => MatchesPickupGain(value, gain))
            .Select(gain => (gain.Name, gain.Quality)).Distinct().Skip(1).Any())) return false;
        var units = gains.SelectMany(gain => Enumerable.Repeat(gain, gain.Remaining)).ToArray();
        var owners = Enumerable.Repeat(-1, units.Length).ToArray();
        bool Assign(int index, bool[] visited)
        {
            for (var i = 0; i < units.Length; i++)
            {
                if (visited[i] || !MatchesPickupGain(evidence[index], units[i])) continue;
                visited[i] = true;
                if (owners[i] >= 0 && !Assign(owners[i], visited)) continue;
                owners[i] = index;
                return true;
            }
            return false;
        }
        for (var i = 0; i < evidence.Length; i++)
            if (!Assign(i, new bool[units.Length])) return false;
        for (var i = 0; i < units.Length; i++) assigned.Add((evidence[owners[i]], units[i]));
        return true;
    }

    private void EnqueuePickupReturn(string originalAccountId, PendingAction action, DateTime occurredAt, FarmingActionItem? item)
    {
        uploader.EnqueueAction(originalAccountId, new FarmingAction
        {
            ServerId = action.Island.ServerId,
            CharacterId = action.Island.CharacterId,
            CharacterName = action.Island.CharacterName,
            IslandId = action.Island.IslandId,
            EventId = action.EventId,
            OccurredAt = occurredAt,
            Operation = "pickup",
            SourceObjectId = action.Source?.ObjectId,
            Outputs = item is null ? [] : [item],
            OutputsComplete = item is not null,
            FocusUsed = 0
        });
    }

    private void SetPickupInventoryMembership(string connection, long id, Guid container, DateTime at, bool isPut)
    {
        var key = (connection, id);
        if (pickupInventoryMembership.Count >= MaxTransientEntries && !pickupInventoryMembership.ContainsKey(key)) return;
        pickupInventoryMembership.TryGetValue(key, out var previous);
        if (previous?.ObservedAt > at) return;
        var sameContainer = previous?.Container == container;
        if (!sameContainer && previous is not null) pickupInventorySlots.Remove((connection, previous.Container));
        pickupInventoryMembership[key] = new(container, sameContainer ? previous!.JoinedAt : at,
            at, isPut ? at : sameContainer ? previous!.LastPutAt : null);
        if (!isPut) return;
        foreach (var gain in pickupInventoryGains.Where(gain => gain.Connection == connection
            && gain.ItemId == id && gain.FirstObservation && at >= gain.ObservedAt
            && at - gain.ObservedAt <= PickupInventoryWindow))
        {
            gain.Container = container;
            gain.PutAt = at;
        }
    }

    private void ForgetPickupInventoryItem(string connection, long id)
    {
        if (pickupInventoryMembership.Remove((connection, id), out var membership))
            pickupInventorySlots.Remove((connection, membership.Container));
        if (seenPickupInventoryItems.Count < MaxTransientEntries) seenPickupInventoryItems.Add((connection, id));
        // A later deletion/placement cannot erase a previously observed return.
    }

    private void InvalidatePickupReturns(string connection)
    {
        foreach (var evidence in pickupInventoryReturns.Values.Where(value => value.Connection == connection))
            evidence.Ambiguous = true;
    }

    private static bool SamePickupFamily(string worldName, string itemName)
    {
        if (!worldName.Contains("_FARM_", StringComparison.Ordinal)
            || !itemName.Contains("_FARM_", StringComparison.Ordinal)) return false;
        static string Family(string name) => name.Replace("_GROWN", "_BABY", StringComparison.Ordinal);
        return Family(worldName) == Family(itemName);
    }

    private void ResetPickupInventoryReturns()
    {
        FlushPickupReturns(DateTime.UtcNow);
        foreach (var evidence in pickupInventoryReturns.Values.Where(value => value.CompletedAt.HasValue))
            EnqueuePickupReturn(evidence.AccountId, evidence.Action, evidence.CompletedAt!.Value, null);
        pickupInventoryMembership.Clear();
        pickupInventorySlots.Clear();
        seenPickupInventoryItems.Clear();
        pickupInventoryContainers.Clear();
        pickupInventoryMoves.Clear();
        pickupInventoryReturns.Clear();
        pickupInventoryGains.Clear();
        otherInventoryReturns.Clear();
    }

    private void PrunePickupInventoryReturns()
    {
        var cutoff = DateTime.UtcNow - RequestLifetime;
        FlushPickupReturns(DateTime.UtcNow);
        foreach (var key in pickupInventoryReturns.Where(entry => entry.Value.ReceivedAt < cutoff).Select(entry => entry.Key).ToArray())
            pickupInventoryReturns.Remove(key);
        pickupInventoryMoves.RemoveAll(value => value.At < cutoff);
        pickupInventoryGains.RemoveAll(value => value.ObservedAt < cutoff || value.Remaining == 0);
        otherInventoryReturns.RemoveAll(value => value.RequestedAt < cutoff || value.Remaining == 0);
    }

    private sealed record PickupInventoryMembership(Guid Container, DateTime JoinedAt, DateTime ObservedAt, DateTime? LastPutAt);
    private sealed record RelevantInventoryMove(string Connection, DateTime At, Guid Source, Guid Destination,
        HashSet<long> ItemIds, HashSet<string> Names, bool IdentitiesKnown);
    private sealed record PickupReturnEvidence(string Connection, string AccountId, PendingAction Action, Guid Bag,
        Dictionary<long, InventoryItem> Baselines)
    {
        public DateTime ReceivedAt { get; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime? CompletedReceivedAt { get; set; }
        public bool Ambiguous { get; set; }
    }
    private sealed record PickupInventoryGain(string Connection, long ItemId, string Name, int Quality, int Quantity,
        DateTime ObservedAt, bool FirstObservation, Guid? InitialContainer, DateTime? InitialPutAt, FarmingActionItem Item)
    {
        public int Remaining { get; set; } = Quantity;
        public Guid? Container { get; set; } = InitialContainer;
        public DateTime? PutAt { get; set; } = InitialPutAt;
    }
    private sealed record OtherInventoryReturn(string Connection, DateTime RequestedAt, string Name, int Quantity)
    {
        public int Remaining { get; set; } = Quantity;
    }
}
