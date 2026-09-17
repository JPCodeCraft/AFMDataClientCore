using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.ReferenceData;
using AlbionDataAvalonia.Shared;
using Serilog;

namespace AFMDataClient.Core;

public sealed class ClientCore : IDisposable
{
    private readonly HashSet<string> features;
    private readonly CoreSettings settings;
    private readonly IUploadAuthSession auth;
    private readonly Dictionary<(string Connection, ulong Id), PendingHistory> histories = new();
    private readonly Dictionary<(int Server, int Item, int Quality, DateOnly Day), PendingEmv> pendingEmv = new();
    private readonly object packetGate = new();
    private readonly FarmingTrackerService? farming;
    private readonly FarmingUploadService? farmingUploads;
    private readonly ItemsIdsService? items;
    private readonly AchievementsService? achievements;
    private readonly Timer? emvTimer;
    private Task referenceInitialization = Task.CompletedTask;
    private bool disposed;
    private DateTime lastBandit;
    private (int? Server, string? Player, string? Location) lastSession;
    public ClientSession Session { get; } = new();
    public UploadCoordinator Uploads { get; }
    public ItemsIdsService Items => items ?? throw new InvalidOperationException("Item reference data requires the EMV feature.");
    public AchievementsService Achievements => achievements ?? throw new InvalidOperationException("Achievement reference data requires the specs feature.");
    public IUploadAuthSession Auth => auth;
    public int QueueCount => Uploads.QueueCount + (farmingUploads?.PendingCount ?? 0);
    public int RunningCount => Uploads.RunningCount + (farmingUploads?.RunningCount ?? 0);
    public event Action<ClientUploadResult>? UploadResult;
    public event Action? QueueChanged;
    public event Action<double>? PowSolved;

    internal ClientCore(ClientCoreOptions options, IUploadAuthSession auth, HttpClient publicClient,
        HttpClient afmClient, HttpClient backendClient, IEnumerable<string> features)
    {
        this.features = new(features, StringComparer.Ordinal);
        this.auth = auth;
        settings = new(options with { PublicItemFilters = options.PublicItemFilters.ToArray() });
        Uploads = new(settings, auth, Session, publicClient, afmClient, backendClient);
        Uploads.UploadResult += result => UploadResult.Publish(result);
        Uploads.QueueChanged += () => QueueChanged.Publish();
        Uploads.PowSolved += elapsed => PowSolved.Publish(elapsed);
        if (this.features.Contains("Specs")) achievements = new();
        if (this.features.Contains("Emv"))
        {
            items = new();
            emvTimer = new(_ => FlushEmv(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        if (this.features.Contains("Islands"))
        {
            farmingUploads = new(settings, auth, backendClient);
            farming = new(auth, Session, farmingUploads, settings);
            farmingUploads.UploadCompleted += (status, id, summary) => UploadResult.Publish(new(id, "Islands", status, UploadScope.Private,
                summary.ServerIds.Count == 1 ? AlbionServers.Get(summary.ServerIds[0]) : null, 1, summary));
            farmingUploads.ActivityChanged += () => QueueChanged.Publish();
        }
        auth.AccountChanged += AccountChanged;
        Session.Changed += SessionChanged;
        InitializeReferences();
    }

    public void UpdateOptions(ClientCoreOptions options)
    {
        var previous = settings.Options;
        settings.Update(options);
        if (previous.IslandTracking != options.IslandTracking) farming?.ResetTransientState();
    }

    public void ResetCapture()
    {
        lock (packetGate) { histories.Clear(); pendingEmv.Clear(); }
        farming?.ResetTransientState();
        Session.Reset();
        InitializeReferences();
    }

    private void InitializeReferences()
    {
        if (!referenceInitialization.IsCompleted) return;
        referenceInitialization = Task.WhenAll(items is { IsInitialized: false } ? items.InitializeAsync() : Task.CompletedTask,
            achievements is not null && achievements.Achievements.Count == 0 ? achievements.InitializeAsync() : Task.CompletedTask,
            features.Overlaps(["MarketOrders", "MarketHistory", "Islands"]) && !AlbionLocations.IsInitialized ? AlbionLocations.InitializeAsync() : Task.CompletedTask);
    }
    public Task InitializeAsync() => referenceInitialization;
    private void SessionChanged()
    {
        lock (packetGate)
        {
            var context = (Session.AlbionServer?.Id, Session.PlayerName, Session.RawLocationId);
            if (context == lastSession) return;
            histories.Clear();
            lastSession = context;
        }
    }
    private void AccountChanged() { lock (packetGate) pendingEmv.Clear(); }

    public void RegisterHandlers(ReceiverBuilder builder)
    {
        if (features.Count == 0) return;
        builder.SubscribeRequest<JoinRequest>((int)OperationCodes.Join, _ =>
        {
            farming?.OnJoinStarted();
            lock (packetGate) histories.Clear();
            return Task.CompletedTask;
        }, 100);
        builder.SubscribeResponse<JoinResponse>((int)OperationCodes.Join, value =>
        {
            if (value.ReturnCode == 0)
            {
                Session.Join(value); farming?.OnJoin(value);
                if (features.Contains("GlobalMultiplier") && value.globalMultiplier is { } multiplier && Session.AlbionServer is { } server)
                    Uploads.UploadGlobalMultiplier(new() { ServerId = server.Id, GlobalMultiplier = multiplier });
            }
            return Task.CompletedTask;
        }, 100);
        builder.SubscribeEvent<PremiumChangedEvent>((int)EventCodes.PremiumChanged, value =>
        {
            if (value.userObjectId == Session.UserObjectId) Session.Premium(value.premiumExpirationTicks);
            return Task.CompletedTask;
        }, 100);
        builder.SubscribeEvent<LeaveEvent>((int)EventCodes.Leave, value =>
        {
            farming?.OnLeave(value.userObjectId);
            if (value.userObjectId == Session.UserObjectId) Session.Reset();
            return Task.CompletedTask;
        }, 100);
        if (features.Contains("MarketOrders"))
        {
            builder.SubscribeResponse<AuctionGetOffersResponse>((int)OperationCodes.AuctionGetOffers, value => Orders(value.marketOrders, value.ReturnCode), 100);
            builder.SubscribeResponse<AuctionGetRequestsResponse>((int)OperationCodes.AuctionGetRequests, value => Orders(value.marketOrders, value.ReturnCode), 100);
            if (features.Contains("Loadouts")) builder.SubscribeResponse<AuctionGetLoadoutOffersResponse>((int)OperationCodes.AuctionGetLoadoutOffers, value => Orders(value.marketOrders, value.ReturnCode), 100);
        }
        if (features.Contains("MarketHistory"))
        {
            builder.SubscribeRequest<AuctionGetItemAverageStatsRequest>((int)OperationCodes.AuctionGetItemAverageStats, HistoryRequest, 100);
            builder.SubscribeResponse<AuctionGetItemAverageStatsResponse>((int)OperationCodes.AuctionGetItemAverageStats, HistoryResponse, 100);
        }
        if (features.Contains("Gold")) builder.SubscribeResponse<AuctionGetGoldAverageStatsResponse>((int)OperationCodes.GoldMarketGetAverageInfo, value =>
        {
            if (value.ReturnCode == 0 && value.prices.Length == value.timeStamps.Length)
                Uploads.Enqueue("Gold", new GoldPriceUpload { Prices = value.prices, Timestamps = value.timeStamps }, value.prices.Length);
            return Task.CompletedTask;
        }, 100);
        if (features.Contains("Bandit")) builder.SubscribeEvent<RedZoneWorldMapEvent>((int)EventCodes.RedZoneWorldMapEvent, value =>
        {
            if (Session.AlbionServer is not null && DateTime.UtcNow - lastBandit >= TimeSpan.FromSeconds(60))
            { lastBandit = DateTime.UtcNow; Uploads.Enqueue("Bandit", new BanditEventUpload { EventTime = value.EventTime, Phase = value.Phase }, 1); }
            return Task.CompletedTask;
        }, 100);
        if (features.Contains("Festivities")) builder.SubscribeEvent<FestivitiesUpdateEvent>((int)EventCodes.FestivitiesUpdate, Festivities, 100);
        if (features.Contains("Specs")) builder.SubscribeEvent<FullAchievementInfoEvent>((int)EventCodes.FullAchievementInfo, Specs, 100);
        if (features.Contains("Emv")) RegisterEmv(builder);
        if (farming is not null) RegisterFarming(builder);
    }

    private Task Orders(List<MarketOrder> orders, short returnCode)
    {
        var location = Session.Location?.Id;
        if (returnCode != 0 || !CanUploadMarket() || string.IsNullOrEmpty(location)) return Task.CompletedTask;
        foreach (var order in orders) if (string.IsNullOrEmpty(order.LocationId)) order.LocationId = location;
        Uploads.UploadMarket(new() { Orders = orders });
        return Task.CompletedTask;
    }

    private bool CanUploadMarket() => Session.AlbionServer is not null
        && Session.Location is { } location && location.Id != AlbionLocations.Unknown.Id && location.Id != AlbionLocations.Unset.Id
        && !string.IsNullOrEmpty(Session.RawLocationId) && AlbionLocations.GetIslandId(Session.RawLocationId) is null;

    private Task HistoryRequest(AuctionGetItemAverageStatsRequest value)
    {
        var context = Uploads.Snapshot();
        var location = Session.Location?.Id;
        if (context is null || !CanUploadMarket() || string.IsNullOrEmpty(location) || value.albionId == 0 || value.quality is < 1 or > 5 || (int)value.timescale is < 0 or > 2) return Task.CompletedTask;
        lock (packetGate)
        {
            foreach (var key in histories.Where(entry => DateTime.UtcNow - entry.Value.Created > TimeSpan.FromMinutes(2)).Select(entry => entry.Key).ToArray()) histories.Remove(key);
            if (histories.Count >= 1024) histories.Remove(histories.MinBy(entry => entry.Value.Created).Key);
            histories[(value.ConnectionId, value.messageID)] = new(value.albionId, (byte)value.quality, value.timescale, location, context, DateTime.UtcNow);
        }
        return Task.CompletedTask;
    }

    private Task HistoryResponse(AuctionGetItemAverageStatsResponse value)
    {
        PendingHistory? pending;
        lock (packetGate) if (!histories.Remove((value.ConnectionId, value.messageID), out pending)) return Task.CompletedTask;
        if (value.ReturnCode != 0 || DateTime.UtcNow - pending.Created > TimeSpan.FromMinutes(2) || value.itemAmounts.Length != value.silverAmounts.Length || value.itemAmounts.Length != value.timeStamps.Length) return Task.CompletedTask;
        var upload = new MarketHistoriesUpload { AlbionId = pending.Item, QualityLevel = pending.Quality, Timescale = pending.Timescale, LocationId = pending.Location };
        for (var i = 0; i < value.itemAmounts.Length; i++)
        {
            var amount = value.itemAmounts[i];
            if (amount < 0) amount += 256;
            if (amount < 0) continue;
            upload.MarketHistories.Add(new() { ItemAmount = (ulong)amount, SilverAmount = value.silverAmounts[i], Timestamp = value.timeStamps[i] });
        }
        Uploads.Enqueue("MarketHistory", upload, upload.MarketHistories.Count, pending.Context);
        return Task.CompletedTask;
    }

    private async Task Specs(FullAchievementInfoEvent value)
    {
        var context = Uploads.Snapshot();
        if (!settings.Options.UploadSpecs || context?.AccountId is null || string.IsNullOrWhiteSpace(context.CharacterName)) return;
        await referenceInitialization.ConfigureAwait(false);
        var levels = new Dictionary<int, byte>();
        var indices = value.AchievementsIndex ?? [];
        var values = value.AchievementLevels ?? [];
        for (var i = 0; i < Math.Min(indices.Length, values.Length); i++) levels[indices[i]] = values[i];
        foreach (var index in value.AchievementsIndexLevel100 ?? [])
        {
            if (index < 0 || index >= achievements!.Achievements.Count) return;
            levels[index] = achievements.GetAchievementInfoByIndex(index).IsTemplate ? (byte)100 : (byte)1;
        }
        if (levels.Keys.Any(index => index < 0 || index >= achievements!.Achievements.Count)) return;
        var upload = new AchievementUpload
        {
            CharacterName = context.CharacterName,
            ServerId = context.Server.Id,
            Achievements = levels.OrderBy(entry => entry.Key).Select(entry => new AchievementUploadEntry { Id = achievements!.GetAchievementInfoByIndex(entry.Key).Id, Level = entry.Value }).ToList()
        };
        Uploads.Enqueue("Specs", upload, upload.Achievements.Count, context);
    }

    private Task Festivities(FestivitiesUpdateEvent value)
    {
        if (!value.IsValid || Session.AlbionServer is not { } server) return Task.CompletedTask;
        var upload = new FestivitiesUpload { ServerId = server.Id };
        for (var i = 0; i < value.Kinds.Length; i++)
        {
            var name = value.UniqueNames[i]?.Trim();
            var start = value.StartTimeTicks[i]; var end = value.EndTimeTicks[i];
            if (string.IsNullOrWhiteSpace(name) || start < DateTime.UnixEpoch.Ticks || end <= start || end > DateTime.MaxValue.Ticks) return Task.CompletedTask;
            upload.Events.Add(new()
            {
                Kind = value.Kinds[i],
                Category = value.Categories[i]?.Trim() ?? "",
                UniqueName = name,
                StartTime = new(start, DateTimeKind.Utc),
                EndTime = new(end, DateTimeKind.Utc)
            });
        }
        Uploads.UploadFestivities(upload);
        return Task.CompletedTask;
    }

    private void RegisterFarming(ReceiverBuilder builder)
    {
        var tracker = farming!;
        builder.SubscribeResponse<ChangeClusterResponse>((int)OperationCodes.ChangeCluster, value => { tracker.OnClusterChanged(value); return Task.CompletedTask; }, 100);
        builder.SubscribeResponse<GetIslandInfosResponse>((int)OperationCodes.GetIslandInfos, value => { tracker.OnIslandList(value); return Task.CompletedTask; }, 100);
        builder.SubscribeEvent<NewBuildingEvent>((int)EventCodes.NewBuilding, value => { tracker.OnBuilding(value); return Task.CompletedTask; }, 100);
        builder.SubscribeEvent<FarmableObjectInfoEvent>((int)EventCodes.FarmableObjectInfo, value => { tracker.OnFarmable(value); return Task.CompletedTask; }, 100);
        builder.SubscribeEvent<FarmBuildingInfoEvent>((int)EventCodes.FarmBuildingInfo, value => { tracker.OnFarmBuilding(value); return Task.CompletedTask; }, 100);
        builder.SubscribeRequest<BuildingRenovationRequest>((int)OperationCodes.BuildingChangeRenovationState, value => { tracker.OnRenovationRequest(value); return Task.CompletedTask; }, 100);
        foreach (var operation in new[] { OperationCodes.FarmableHarvest, OperationCodes.FarmableFinishGrownItem, OperationCodes.FarmableGetProduct, OperationCodes.FarmableDestroy, OperationCodes.PlaceableObjectPickup })
        {
            builder.SubscribeRequest<FarmingActionRequest>((int)operation, value => { tracker.OnActionRequest(operation, value); return Task.CompletedTask; }, 100);
            builder.SubscribeResponse<FarmingActionResponse>((int)operation, value => { tracker.OnActionResponse(operation, value); return Task.CompletedTask; }, 100);
        }
    }

    private void RegisterEmv(ReceiverBuilder builder)
    {
        builder.SubscribeEvent<NewSimpleItemEvent>((int)EventCodes.NewSimpleItem, value => Item(value.Item, value.CapturedAt), 100);
        builder.SubscribeEvent<NewEquipmentItemEvent>((int)EventCodes.NewEquipmentItem, value => Item(value.Item, value.CapturedAt), 100);
        builder.SubscribeEvent<NewFurnitureItemEvent>((int)EventCodes.NewFurnitureItem, value => Item(value.Item, value.CapturedAt), 100);
        builder.SubscribeEvent<NewJournalItemEvent>((int)EventCodes.NewJournalItem, value => Item(value.Item, value.CapturedAt), 100);
        builder.SubscribeEvent<NewLaborerItemEvent>((int)EventCodes.NewLaborerItem, value => Item(value.Item, value.CapturedAt), 100);
        builder.SubscribeEvent<NewKillTrophyItemEvent>((int)EventCodes.NewKillTrophyItem, value => Item(value.Item, value.CapturedAt), 100);
        builder.SubscribeEvent<NewSiegeBannerItemEvent>((int)EventCodes.NewSiegeBannerItem, value => Item(value.Item, value.CapturedAt), 100);
        builder.SubscribeEvent<EstimatedMarketValueUpdateEvent>((int)EventCodes.EstimatedMarketValueUpdate, value =>
        {
            foreach (var entry in value.Entries)
            {
                var mapping = items!.GetItemById(entry.ItemId);
                entry.ItemUniqueName = mapping.UniqueName; entry.ItemUsName = mapping.UsName;
                Emv(entry.ItemId, entry.Quality, entry.EstimatedMarketValue, null, value.CapturedAt);
            }
            return Task.CompletedTask;
        }, 100);
    }

    private Task Item(NewItem? item, DateTime timestamp)
    {
        if (item is null) return Task.CompletedTask;
        var mapping = items!.GetItemById(item.ItemIndex);
        item.ItemUniqueName = mapping.UniqueName; item.ItemUsName = mapping.UsName;
        Emv(item.ItemIndex, item.Quality, item.EstimatedMarketValue, item.BlackMarketEstimatedMarketValue, timestamp);
        return Task.CompletedTask;
    }

    private void Emv(int index, int quality, long value, long? blackMarketValue, DateTime timestamp)
    {
        var context = Uploads.Snapshot();
        if (context?.AccountId is null || index <= 0 || quality is < 1 or > 5 || value <= 0) return;
        lock (packetGate)
        {
            var current = Uploads.Snapshot();
            if (current?.AccountId != context.AccountId || current.AccountGeneration != context.AccountGeneration) return;
            var wasEmpty = pendingEmv.Count == 0;
            var key = (context.Server.Id, index, quality, DateOnly.FromDateTime(timestamp.ToUniversalTime()));
            if (pendingEmv.TryGetValue(key, out var previous)
                && previous.Context.AccountId == context.AccountId
                && previous.Context.AccountGeneration == context.AccountGeneration)
                blackMarketValue ??= previous.BlackMarketValue;
            pendingEmv[key] = new(context, index, quality, key.Item4, value, blackMarketValue);
            if (wasEmpty) emvTimer!.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushEmv()
    {
        lock (packetGate)
        {
            if (disposed) return;
            foreach (var group in pendingEmv.Values.GroupBy(entry => (entry.Context.AccountId, entry.Context.AccountGeneration, entry.Context.Server.Id, entry.Context.CharacterName)))
                foreach (var chunk in group.Chunk(500))
                {
                    var context = chunk[0].Context;
                    var upload = new ItemEstimatedMarketValueUpload { ServerId = context.Server.Id };
                    foreach (var entry in chunk)
                    {
                        var name = items!.GetItemById(entry.Item).UniqueName;
                        if (name is "Unknown Item" or "Unset" || string.IsNullOrEmpty(name)) continue;
                        upload.Items.Add(new() { ItemUniqueName = name, Quality = entry.Quality, Day = entry.Day, Emv = entry.Value, BlackMarketEmv = entry.BlackMarketValue });
                    }
                    Uploads.Enqueue("Emv", upload, upload.Items.Count, context);
                }
            pendingEmv.Clear();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        auth.AccountChanged -= AccountChanged; Session.Changed -= SessionChanged;
        emvTimer?.Dispose(); farming?.Dispose(); farmingUploads?.Dispose(); Uploads.Dispose();
    }
    private sealed record PendingHistory(uint Item, byte Quality, Timescale Timescale, string Location, UploadCoordinator.UploadContext Context, DateTime Created);
    private sealed record PendingEmv(UploadCoordinator.UploadContext Context, int Item, int Quality, DateOnly Day, long Value, long? BlackMarketValue);
}
