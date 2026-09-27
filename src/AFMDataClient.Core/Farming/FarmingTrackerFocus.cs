using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private static readonly TimeSpan FocusEvidenceWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FocusSettlementDelay = TimeSpan.FromSeconds(2);
    private readonly Dictionary<(string Connection, byte Sequence, long Target), Boost> boosts = new();
    private readonly Dictionary<(string Connection, byte Sequence, long Target, long Timestamp), DateTime> completedBoosts = new();
    private readonly Dictionary<(string Connection, long Timestamp), DateTime> seenFocusChanges = new();
    private readonly List<FocusChange> pendingFocusChanges = new();
    private readonly List<CompletedBoost> pendingFocusActions = new();

    public void OnBoostRequest(BoostFarmableRequest packet) => Observe(() =>
    {
        if (island is null) return;
        PruneActivity();
        if (packet.IsCancel)
        {
            CancelBoosts(packet.ConnectionId, packet.ActionSequence, packet.TargetId, packet.CapturedAt);
            return;
        }
        if (!packet.IsStart || packet.ActionTimestamp is not { } stamp
            || packet.ActionSequence is not { } sequence || packet.TargetId is not { } target) return;
        var key = (packet.ConnectionId, sequence, target);
        if (completedBoosts.ContainsKey((packet.ConnectionId, sequence, target, stamp))) return;
        if (boosts.TryGetValue(key, out var previous) && previous.ActionTimestamp == stamp) return;
        if (boosts.Count >= MaxTransientEntries && !boosts.ContainsKey(key)) return;
        objects.TryGetValue(target, out var source);
        boosts[key] = new(Guid.NewGuid().ToString(), accountId!, packet.CapturedAt, stamp, island, source?.ObjectId);
    });

    private void CancelBoosts(string connection, byte? sequence, long? target, DateTime capturedAt)
    {
        // Client cancellation uses sequence zero and omits the target. Keep the
        // request recognizable in case the server already completed it, but do
        // not let canceled attempts compete with confirmed work for Focus.
        foreach (var entry in boosts.Where(entry => entry.Key.Connection == connection
            && entry.Value.RequestedAt <= capturedAt
            && (target is null || entry.Key.Target == target)
            && (sequence is null or 0 || entry.Key.Sequence == sequence)))
        {
            entry.Value.Cancelled = true;
            completedBoosts[(entry.Key.Connection, entry.Key.Sequence, entry.Key.Target, entry.Value.ActionTimestamp)] = capturedAt;
        }
    }

    public void OnFocusUpdate(CraftingFocusUpdateEvent packet) => Observe(() =>
    {
        if (island is null || localObjectId == 0 || packet.ActorId != localObjectId
            || packet.Timestamp is not { } stamp || packet.FocusDelta is not (< 0 and >= -1_000_000)) return;
        PruneActivity();
        var amount = -packet.FocusDelta.Value;
        var rounded = Math.Round(amount);
        // Regeneration and unsupported resource shapes must not become expenses.
        if (Math.Abs(rounded - amount) > 0.001 || rounded < 1) return;
        if (seenFocusChanges.Count >= MaxTransientEntries || pendingFocusChanges.Count >= MaxTransientEntries) return;
        var key = (packet.ConnectionId, stamp);
        if (!seenFocusChanges.TryAdd(key, packet.CapturedAt)) return;
        pendingFocusChanges.Add(new(packet.ConnectionId, stamp, packet.CapturedAt, DateTime.UtcNow, (int)rounded));
    });

    public void OnBoostEvent(BoostFarmableEvent packet) => Observe(() =>
    {
        if (island is null || localObjectId == 0 || packet.ActorId != localObjectId) return;
        PruneActivity();
        if (packet.IsCancelled)
        {
            CancelBoosts(packet.ConnectionId, packet.ActionSequence, packet.TargetId, packet.CapturedAt);
            return;
        }
        if (!packet.IsCompleted || packet.ActionSequence is not { } sequence || packet.TargetId is not { } target) return;
        if (!boosts.Remove((packet.ConnectionId, sequence, target), out var boost)) return;
        completedBoosts[(packet.ConnectionId, sequence, target, boost.ActionTimestamp)] = packet.CapturedAt;
        var completed = new CompletedBoost(packet.ConnectionId, boost, packet.CapturedAt, DateTime.UtcNow);
        if (pendingFocusActions.Count >= MaxTransientEntries)
        {
            EnqueueBoostAction(completed, null);
            return;
        }
        pendingFocusActions.Add(completed);
    });

    private static bool CanMatchFocus(CompletedBoost action, FocusChange change) =>
        action.Connection == change.Connection
        && change.CapturedAt >= action.Boost.RequestedAt
        && change.CapturedAt >= action.CompletedAt - FocusEvidenceWindow
        // Allow a short arrival-order reversal, not unrelated spending long
        // after the channel completed. Retention remains longer for late batches.
        && change.CapturedAt <= action.CompletedAt + FocusSettlementDelay;

    private static bool CanMatchPendingFocus(Boost boost, FocusChange change) =>
        !boost.Cancelled && change.CapturedAt >= boost.RequestedAt
        && change.CapturedAt - boost.RequestedAt <= FocusEvidenceWindow;

    // Called under the tracker lock by the existing timer. Settlement uses the
    // processing clock: capture batches can already be several seconds old.
    private void FlushFocusActions(DateTime now) => FlushFocusActions(now, false);

    private void FlushFocusActions(DateTime now, bool force)
    {
        var remaining = pendingFocusActions.ToHashSet();
        while (remaining.Count > 0)
        {
            var actions = new HashSet<CompletedBoost> { remaining.First() };
            var changes = new HashSet<FocusChange>();
            bool expanded;
            do
            {
                expanded = false;
                foreach (var change in pendingFocusChanges)
                    if (!changes.Contains(change) && actions.Any(action => CanMatchFocus(action, change)))
                        expanded |= changes.Add(change);
                foreach (var action in remaining)
                    if (!actions.Contains(action) && changes.Any(change => CanMatchFocus(action, change)))
                        expanded |= actions.Add(action);
            } while (expanded);
            remaining.ExceptWith(actions);

            var pendingCandidate = boosts.Any(entry => changes.Any(change => entry.Key.Connection == change.Connection
                && CanMatchPendingFocus(entry.Value, change)));
            var settled = force || (actions.All(action => now - action.ObservedAt >= FocusSettlementDelay)
                && changes.All(change => now - change.ObservedAt >= FocusSettlementDelay));
            var matched = settled && !pendingCandidate && actions.Count == changes.Count;
            var orderedActions = actions.OrderBy(action => action.Boost.ActionTimestamp).ThenBy(action => action.CompletedAt).ToArray();
            var anchor = changes.OrderBy(change => change.CapturedAt).FirstOrDefault()?.Timestamp ?? 0;
            var orderedChanges = changes.OrderBy(change => FocusTimestampOffset(change.Timestamp, anchor)).ToArray();
            if (matched)
            {
                // A single player completes these channels in request order.
                // Equal counts are essential: a missing deduction must never
                // shift the following animal's cost onto the previous one.
                for (var index = 0; index < orderedActions.Length; index++)
                    if (!CanMatchFocus(orderedActions[index], orderedChanges[index])) matched = false;
            }
            if (!matched && !force && actions.Any(action => now - action.ObservedAt < FocusEvidenceWindow)) continue;
            for (var index = 0; index < orderedActions.Length; index++)
            {
                EnqueueBoostAction(orderedActions[index], matched ? orderedChanges[index].Amount : null);
                pendingFocusActions.Remove(orderedActions[index]);
            }
            // Retire ambiguous evidence too, so it cannot leak into a later action.
            pendingFocusChanges.RemoveAll(change => changes.Contains(change));
        }
    }

    private static long FocusTimestampOffset(long timestamp, long anchor) =>
        timestamp is >= int.MinValue and <= int.MaxValue && anchor is >= int.MinValue and <= int.MaxValue
            ? unchecked((int)(timestamp - anchor))
            : timestamp - anchor;

    private void EnqueueBoostAction(CompletedBoost action, int? focusUsed)
    {
        var boost = action.Boost;
        uploader.EnqueueAction(boost.AccountId, new FarmingAction
        {
            ServerId = boost.Island.ServerId,
            CharacterId = boost.Island.CharacterId,
            CharacterName = boost.Island.CharacterName,
            IslandId = boost.Island.IslandId,
            EventId = boost.EventId,
            OccurredAt = action.CompletedAt,
            Operation = "boost",
            SourceObjectId = boost.SourceObjectId,
            FocusUsed = focusUsed
        });
    }

    private void ResetFocusState()
    {
        FlushFocusActions(DateTime.UtcNow, true);
        boosts.Clear();
        completedBoosts.Clear();
        seenFocusChanges.Clear();
        pendingFocusChanges.Clear();
        pendingFocusActions.Clear();
    }

    private void PruneFocusState(DateTime cutoff)
    {
        FlushFocusActions(DateTime.UtcNow);
        foreach (var key in boosts.Where(entry => entry.Value.RequestedAt < cutoff).Select(entry => entry.Key).ToArray()) boosts.Remove(key);
        foreach (var key in completedBoosts.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray()) completedBoosts.Remove(key);
        foreach (var key in seenFocusChanges.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray()) seenFocusChanges.Remove(key);
        pendingFocusChanges.RemoveAll(change => change.ObservedAt < cutoff);
    }

    private sealed record Boost(string EventId, string AccountId, DateTime RequestedAt, long ActionTimestamp,
        FarmingIslandObservation Island, string? SourceObjectId)
    {
        public bool Cancelled { get; set; }
    }
    private sealed record CompletedBoost(string Connection, Boost Boost, DateTime CompletedAt, DateTime ObservedAt);
    private sealed record FocusChange(string Connection, long Timestamp, DateTime CapturedAt, DateTime ObservedAt, int Amount);
}
