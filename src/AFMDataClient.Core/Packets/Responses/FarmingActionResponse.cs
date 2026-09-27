using Albion.Network;
using AlbionDataAvalonia.Farming.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Responses;

public sealed class FarmingActionResponse : BaseOperation
{
    public long? RequestId { get; private set; }
    public List<FarmingPickupItem> Items { get; private set; } = [];
    public bool ItemsDecoded { get; private set; }

    public FarmingActionResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (parameters.ContainsKey(255)) RequestId = Number(parameters, 255);
        });
        // Preserve correlation even when an unknown reward shape cannot be decoded.
        // Successful removal still needs to clear the observed slot in that case.
        TryRead(() =>
        {
            if (!parameters.TryGetValue(0, out var rawNames) || !parameters.TryGetValue(1, out var rawAmounts)) return;
            // A duplicate feed can succeed with explicit empty arrays: zero was
            // consumed. Missing fields or unsupported empty shapes stay unknown.
            var explicitEmpty = rawNames is string[] { Length: 0 }
                && rawAmounts is byte[] { Length: 0 } or short[] { Length: 0 } or int[] { Length: 0 } or long[] { Length: 0 };
            var names = Strings(parameters, 0);
            var amounts = rawAmounts.ToLongArray();
            if ((names.Length > 0 || explicitEmpty) && names.Length <= 32 && names.Length == amounts.Length
                && names.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 200)
                && amounts.All(n => n > 0 && n <= 1_000_000_000))
            {
                Items = names.Select((name, i) => new FarmingPickupItem { UniqueName = name, Quantity = (int)amounts[i] }).ToList();
                ItemsDecoded = true;
            }
        });
    }
}
