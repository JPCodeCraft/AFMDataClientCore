using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class JoinResponse : BaseOperation
{
    public string? IslandId { get; }
    public string? RawLocationId { get; }
    public string? IslandHomeCluster { get; }
    public string? ParentClusterId { get; }
    public string? SourceClusterId { get; }
    public ActivityLocationDescriptor ActivityLocation => AlbionLocations.ResolveActivityLocation(RawLocationId, ParentClusterId, SourceClusterId);
    public Guid? MainInventoryContainerId { get; private set; }
    public IReadOnlyList<long> MainInventoryItemObjectIds { get; private set; } = [];
    public readonly AlbionLocation playerLocation;
    public readonly string playerName;
    public readonly long userObjectId;
    public readonly Guid? userGuid;
    public readonly string? guildName;
    public readonly string? allianceName;
    public readonly double? globalMultiplier;
    public readonly long? premiumExpirationTicks;

    public JoinResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object objectId))
            {
                userObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out object? guidData))
            {
                userGuid = guidData.ToGuid();
            }

            if (parameters.TryGetValue(2, out object nameData))
            {
                playerName = (string)nameData;
            }

            if (parameters.TryGetValue(8, out object locationData))
            {
                string location = (string)locationData;
                RawLocationId = location;
                playerLocation = AlbionLocations.ResolveLocation(location);
                IslandId = AlbionLocations.GetIslandId(location);
            }

            if (parameters.TryGetValue(81, out var homeCluster) && homeCluster is string home
                && !string.IsNullOrWhiteSpace(home) && home.Trim().Length <= 200)
                IslandHomeCluster = home.Trim();
            if (parameters.TryGetValue(65, out var parentCluster) && parentCluster is string parent && !string.IsNullOrWhiteSpace(parent) && parent.Length <= 200)
                ParentClusterId = parent.Trim();
            if (parameters.TryGetValue(66, out var sourceCluster) && sourceCluster is string source && !string.IsNullOrWhiteSpace(source) && source.Length <= 200)
                SourceClusterId = source.Trim();

            if (parameters.TryGetValue(58, out object? guildNameData))
            {
                guildName = guildNameData?.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(79, out object? allianceNameData))
            {
                allianceName = allianceNameData?.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(84, out object globalMultiplierData))
            {
                try
                {
                    globalMultiplier = globalMultiplierData.ToLong() / 10000d;
                }
                catch (InvalidCastException)
                {
                    Log.Warning("Join response param 84 was present but could not be parsed into a global multiplier. Type: {Type}", globalMultiplierData?.GetType());
                }
            }

            if (parameters.TryGetValue(90, out object? premiumExpirationData))
            {
                try
                {
                    premiumExpirationTicks = premiumExpirationData.ToLong();
                }
                catch (InvalidCastException)
                {
                    Log.Warning(
                        "Join response param 90 was present but could not be parsed into Premium expiration ticks. Type: {Type}",
                        premiumExpirationData?.GetType());
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
        FarmingPacketValues.TryRead(() =>
        {
            // Join supplies the main inventory container/items at 54/55.
            // Container identity remains usable even when initial membership
            // is omitted. Subsequent inventory events provide membership.
            if (!parameters.TryGetValue(54, out var rawContainer)) return;
            var container = rawContainer.ToGuid();
            if (container is null || container == Guid.Empty) return;
            MainInventoryContainerId = container;
            if (!parameters.TryGetValue(55, out var rawItems)
                || rawItems is not Array { Rank: 1 } array || array.Length > 4096) return;
            var items = rawItems.ToLongArray();
            if (items.Any(id => id < 0)) return;
            MainInventoryItemObjectIds = items;
        });
    }
}
