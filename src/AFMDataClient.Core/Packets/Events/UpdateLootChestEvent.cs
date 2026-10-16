using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Events;

public sealed class UpdateLootChestEvent : BaseEvent
{
    public long ObjectId { get; }
    public int State { get; }
    // Union of both lists below, kept for callers that only need everyone involved.
    public IReadOnlyList<Guid> PlayerGuids { get; } = Array.Empty<Guid>();
    // Players the chest loot was reserved for.
    public IReadOnlyList<Guid> EligibleGuids { get; } = Array.Empty<Guid>();
    // Players whose reserved loot is still in the chest; empty once all of it was taken.
    public IReadOnlyList<Guid> RemainingLootGuids { get; } = Array.Empty<Guid>();
    public bool HasRemainingLootGuids { get; }
    // Server-clock times: when the chest entered this state, and when reserved loot becomes public.
    public DateTime? StateChangedAtUtc { get; }
    public DateTime? PublicAtUtc { get; }

    public UpdateLootChestEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out var state))
            {
                State = state.ToInt();
            }

            if (parameters.TryGetValue(2, out var stateChangedAt))
            {
                StateChangedAtUtc = FromTicks(stateChangedAt.ToLong());
            }

            if (parameters.TryGetValue(8, out var publicAt))
            {
                PublicAtUtc = FromTicks(publicAt.ToLong());
            }

            if (parameters.TryGetValue(3, out var eligibleGuids))
            {
                EligibleGuids = Distinct(eligibleGuids.ToGuidArray());
            }

            if (parameters.TryGetValue(5, out var remainingLootGuids))
            {
                HasRemainingLootGuids = true;
                RemainingLootGuids = Distinct(remainingLootGuids.ToGuidArray());
            }

            PlayerGuids = Distinct(EligibleGuids.Concat(RemainingLootGuids));
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    private static Guid[] Distinct(IEnumerable<Guid> guids) => guids.Where(guid => guid != Guid.Empty).Distinct().ToArray();

    // Unset times are sent as long.MinValue.
    private static DateTime? FromTicks(long ticks) => ticks > 0 && ticks <= DateTime.MaxValue.Ticks
        ? new DateTime(ticks, DateTimeKind.Utc) : null;
}
