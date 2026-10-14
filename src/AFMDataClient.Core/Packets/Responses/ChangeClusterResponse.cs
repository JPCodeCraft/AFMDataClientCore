using Albion.Network;
using System;
using System.Collections.Generic;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Responses;

public sealed class ChangeClusterResponse : BaseOperation
{
    public string? Destination { get; }
    public string? LayoutId { get; }
    public string? IslandLayoutId => LayoutId is { Length: <= 100 }
        && LayoutId.StartsWith("ISLAND-", StringComparison.Ordinal) ? LayoutId : null;
    public string? OwnerName { get; }
    public byte[]? DungeonInformation { get; }

    public ChangeClusterResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Destination = Text(parameters, 0);
        LayoutId = OptionalText(Text(parameters, 1), 200);
        OwnerName = OptionalText(Text(parameters, 2), 100);
        if (parameters.TryGetValue(3, out var information) && information is byte[] { Length: > 0 and <= 1024 } bytes)
            DungeonInformation = bytes.ToArray();
    }
}
