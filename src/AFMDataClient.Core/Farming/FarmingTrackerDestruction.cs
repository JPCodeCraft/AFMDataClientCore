using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;

namespace AlbionDataAvalonia.Farming;

public sealed partial class FarmingTrackerService
{
    private static readonly TimeSpan DestructionEvidenceWindow = TimeSpan.FromSeconds(2);
    private readonly Dictionary<(string Connection, long Target), Destruction> destructions = new();

    private void ObserveDestructionRequest(FarmingActionRequest packet, long target)
    {
        foreach (var key in destructions.Where(entry => packet.CapturedAt - entry.Value.RequestedAt > DestructionEvidenceWindow)
            .Select(entry => entry.Key).ToArray()) destructions.Remove(key);
        if (!objects.TryGetValue(target, out var source) || source.Kind != "farmable" || source.Removed
            || destructions.Count >= MaxTransientEntries) return;
        // This one-way operation has no request ID in current captures. The
        // prompt server Leave for the exact target supplies completion evidence;
        // an outgoing request or an unrelated visibility loss alone never does.
        destructions[(packet.ConnectionId, target)] = new(source, packet.CapturedAt, packet.RequestId);
    }

    private void CompleteDestruction(LeaveEvent packet)
    {
        if (!destructions.Remove((packet.ConnectionId, packet.userObjectId), out var pending)
            || packet.CapturedAt < pending.RequestedAt
            || packet.CapturedAt - pending.RequestedAt > DestructionEvidenceWindow
            || !objects.TryGetValue(packet.userObjectId, out var current)
            || current.ObjectId != pending.Source.ObjectId || current.Removed) return;
        RemoveConfirmedObject(pending.Source, packet.userObjectId);
    }

    private void CompleteDestructionResponse(FarmingActionResponse packet)
    {
        if (packet.RequestId is not { } requestId) return;
        foreach (var (key, pending) in destructions.Where(entry => entry.Key.Connection == packet.ConnectionId
            && entry.Value.RequestId == requestId).ToArray())
        {
            destructions.Remove(key);
            if (packet.ReturnCode == 0 && packet.CapturedAt >= pending.RequestedAt
                && objects.TryGetValue(key.Target, out var current) && current.ObjectId == pending.Source.ObjectId)
                RemoveConfirmedObject(pending.Source, key.Target);
        }
    }

    private void RemoveConfirmedObject(FarmingObjectObservation source, long target)
    {
        if (!removedObjects.Add(source.ObjectId)) return;
        var now = DateTime.UtcNow;
        var removed = source with
        {
            Removed = true,
            RemovalAssumed = null,
            State = null,
            ObservedAt = now < source.ObservedAt ? source.ObservedAt : now,
            OccupantObservedAt = source.ObservedAt
        };
        if (removed.Kind == "plot") demolitions.Remove(KeyOf(removed));
        uploader.EnqueueObject(accountId!, removed);
        if (objects.TryGetValue(target, out var current) && current.ObjectId == removed.ObjectId)
            objects[target] = removed;
    }

    private sealed record Destruction(FarmingObjectObservation Source, DateTime RequestedAt, long? RequestId);
}
