using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Pow;
using AlbionDataAvalonia.Locations;
using Serilog;

namespace AFMDataClient.Core;

/// <summary>Shared upload transport and queue; every job retains the context observed at capture time.</summary>
public sealed class UploadCoordinator : IDisposable
{
    internal static readonly JsonSerializerOptions AfmJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly JsonSerializerOptions PublicJson = new() { IncludeFields = true };
    private readonly IUploadAuthSession auth;
    private readonly CoreSettings settings;
    private readonly ClientSession session;
    private readonly HttpClient publicClient, afmClient, backendClient;
    private readonly Channel<Job> queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(4096) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource shutdown = new();
    private readonly object gate = new();
    private CancellationTokenSource accountCancellation = new();
    private CancellationTokenSource marketCancellation = new();
    private readonly ConcurrentDictionary<string, byte> inFlight = new();
    private readonly HashSet<string> successful = new();
    private readonly HashSet<string> successfulPublic = new();
    private readonly Queue<string> successfulPublicOrder = new();
    private readonly Dictionary<string, string> lastSnapshots = new();
    private readonly Queue<string> successfulOrder = new();
    private readonly List<Task> workers = new();
    private readonly SemaphoreSlim afmDataGate = new(1, 1);
    private readonly Task worker;
    private long accountGeneration, settingsGeneration, marketGeneration;
    private int running;
    private bool disposed;

    public int QueueCount => queue.Reader.Count;
    public int RunningCount => Volatile.Read(ref running);
    public event Action<ClientUploadResult>? UploadResult;
    public event Action? QueueChanged;
    public event Action<double>? PowSolved;
    internal UploadCoordinator(CoreSettings settings, IUploadAuthSession auth, ClientSession session,
        HttpClient publicClient, HttpClient afmClient, HttpClient backendClient)
    {
        this.settings = settings; this.auth = auth; this.session = session;
        this.publicClient = publicClient; this.afmClient = afmClient; this.backendClient = backendClient;
        auth.AccountChanged += AccountChanged;
        settings.Changed += SettingsChanged;
        worker = Task.Run(ProcessAsync);
    }

    internal UploadContext? Snapshot()
    {
        lock (gate)
        {
            var server = session.AlbionServer;
            return server is null ? null : new(new AlbionServer(server.Id, server.Name, server.UploadUrl, server.HostIps.ToArray()),
                session.PlayerName, auth.AccountId, accountGeneration, settingsGeneration, marketGeneration, settings.Options);
        }
    }

    internal void Enqueue(string kind, object payload, int count, UploadContext? context = null)
    {
        context ??= Snapshot();
        if (disposed || context is null || count < 0 || count == 0 && kind != "Festivities") return;
        // Freeze mutable packet data before returning control to another subscriber.
        var options = kind is "MarketOrders" or "MarketHistory" or "Gold" or "Bandit" ? PublicJson : AfmJson;
        var copy = JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), options), payload.GetType(), options)!;
        var identifier = copy is BaseUpload upload ? upload.Identifier : Guid.NewGuid();
        if (!queue.Writer.TryWrite(new(identifier, kind, copy, count, context)))
            Log.Warning("Shared upload queue is full; skipping {Kind} observation", kind);
        QueueChanged.Publish();
    }

    public void UploadAchievements(AchievementUpload payload) => Enqueue("Specs", payload, payload.Achievements.Count);
    public void UploadEstimatedMarketValues(ItemEstimatedMarketValueUpload payload) => Enqueue("Emv", payload, payload.Items.Count);
    public void UploadGlobalMultiplier(GlobalMultiplierUpload payload) => Enqueue("GlobalMultiplier", payload, 1);
    public void UploadFestivities(FestivitiesUpload payload) => Enqueue("Festivities", payload, payload.Events.Count);
    public void UploadMarket(MarketUpload payload) => Enqueue("MarketOrders", payload, payload.Orders.Count);

    private void SettingsChanged()
    {
        lock (gate)
        {
            var previous = settings.Previous;
            var current = settings.Options;
            if (previous.UploadSpecs != current.UploadSpecs) settingsGeneration++;
            if (previous.PrivateMarketOrders != current.PrivateMarketOrders || previous.ContributeToPublic != current.ContributeToPublic
                || previous.ShareWithFriends != current.ShareWithFriends || !previous.PublicItemFilters.SequenceEqual(current.PublicItemFilters, StringComparer.Ordinal))
            {
                marketGeneration++;
                marketCancellation.Cancel(); marketCancellation.Dispose(); marketCancellation = new();
            }
        }
    }
    private void AccountChanged()
    {
        lock (gate)
        {
            accountGeneration++;
            accountCancellation.Cancel();
            accountCancellation.Dispose();
            accountCancellation = new();
        }
    }

    private bool IsCurrent(Job job)
    {
        lock (gate)
            return !disposed && job.Context.AccountGeneration == accountGeneration
                && string.Equals(job.Context.AccountId, auth.AccountId, StringComparison.Ordinal)
                && (job.Kind != "MarketOrders" || job.Context.MarketGeneration == marketGeneration)
                && (job.Kind != "Specs" || (settings.Options.UploadSpecs && job.Context.SettingsGeneration == settingsGeneration));
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(shutdown.Token).ConfigureAwait(false))
            {
                workers.RemoveAll(task => task.IsCompleted);
                while (workers.Count >= Math.Clamp(settings.Options.DesiredConcurrency, 1, 32))
                {
                    await Task.WhenAny(workers).ConfigureAwait(false);
                    workers.RemoveAll(task => task.IsCompleted);
                }
                if (queue.Reader.TryRead(out var job))
                {
                    Interlocked.Increment(ref running);
                    QueueChanged.Publish();
                    workers.Add(ProcessJobAsync(job));
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally { await Task.WhenAll(workers).ConfigureAwait(false); }
    }

    private async Task ProcessJobAsync(Job job)
    {
        try { await UploadAsync(job).ConfigureAwait(false); }
        catch (OperationCanceledException) when (WasInvalidated(job)) { }
        catch (Exception ex) { Log.Warning(ex, "Shared {Kind} upload failed", job.Kind); Report(job, UploadStatus.Failed, IsPublic(job) ? UploadScope.Public : UploadScope.Private); }
        finally { Interlocked.Decrement(ref running); QueueChanged.Publish(); }
    }

    private bool WasInvalidated(Job job, UploadScope? scope = null)
    {
        lock (gate)
            return shutdown.IsCancellationRequested
                || job.Kind == "MarketOrders" && job.Context.MarketGeneration != marketGeneration
                || (scope == UploadScope.Private || scope is null && !IsPublic(job))
                    && (job.Context.AccountGeneration != accountGeneration || job.Context.AccountId != auth.AccountId)
                || job.Kind == "Specs" && job.Context.SettingsGeneration != settingsGeneration;
    }

    private static bool IsPublic(Job job) => job.Kind is "MarketHistory" or "Gold" or "Bandit" || job.Kind == "MarketOrders" && !job.Context.Options.PrivateMarketOrders;

    private async Task UploadAsync(Job job)
    {
        if (job.Kind == "MarketOrders")
        {
            lock (gate) if (job.Context.MarketGeneration != marketGeneration) { Report(job, UploadStatus.Skipped, IsPublic(job) ? UploadScope.Public : UploadScope.Private); return; }
            var orders = (MarketUpload)job.Payload;
            if (job.Context.Options.PrivateMarketOrders)
            {
                var privateOrders = JsonSerializer.Deserialize<MarketUpload>(JsonSerializer.SerializeToUtf8Bytes(orders, PublicJson), PublicJson)!;
                foreach (var order in privateOrders.Orders)
                    order.LocationId = order.Location.MarketLocation?.IdInt?.ToString() ?? "0";
                await UploadPrivateAsync(job, new AfmMarketUpload(privateOrders, job.Context.Server.Id, job.Context.AccountId ?? ""),
                    $"flipperOrders?contributeToPublic={job.Context.Options.ContributeToPublic}&shareWithFriends={job.Context.Options.ShareWithFriends}", false).ConfigureAwait(false);
                var publicOrders = orders.Orders.Where(order => job.Context.Options.PublicItemFilters.Any(filter => order.ItemTypeId.Contains(filter, StringComparison.Ordinal))).ToList();
                if (publicOrders.Count == 0) return;
                lock (gate) if (job.Context.MarketGeneration != marketGeneration) return;
                var payload = new MarketUpload { Orders = publicOrders };
                job = job with { Identifier = payload.Identifier, Payload = payload, Count = publicOrders.Count };
            }
            await UploadPublicAsync(job, job.Context.Options.MarketOrdersIngestSubject).ConfigureAwait(false);
        }
        else if (IsPublic(job))
        {
            var topic = job.Kind switch
            {
                "MarketHistory" => job.Context.Options.MarketHistoriesIngestSubject,
                "Gold" => job.Context.Options.GoldDataIngestSubject,
                _ => job.Context.Options.BanditEventIngestSubject
            };
            await UploadPublicAsync(job, topic).ConfigureAwait(false);
        }
        else
        {
            var path = job.Kind switch
            {
                "Specs" => "be/achievements",
                "Emv" => "itemEstimatedMarketValues",
                "GlobalMultiplier" => "be/globalMultiplier",
                "Festivities" => "be/festivities",
                _ => throw new InvalidOperationException(job.Kind)
            };
            await UploadPrivateAsync(job, job.Payload, path, true).ConfigureAwait(false);
        }
    }

    private async Task UploadPublicAsync(Job job, string topic)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(job.Payload, job.Payload.GetType(), PublicJson);
        var key = $"public:{job.Context.Server.Id}:{topic}:{Hash(bytes)}";
        if (!Claim(key)) { Report(job, UploadStatus.Skipped, UploadScope.Public); return; }
        try
        {
            using var cancellation = CreatePublicCancellation(job);
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            using var solver = new PowSolver();
            var origin = new Uri(job.Context.Server.UploadUrl);
            using var challengeResponse = await publicClient.GetAsync(new Uri(origin, "/pow"), token).ConfigureAwait(false);
            challengeResponse.EnsureSuccessStatusCode();
            var challenge = await challengeResponse.Content.ReadFromJsonAsync<PowRequest>(cancellationToken: token).ConfigureAwait(false);
            if (challenge == null) { Report(job, UploadStatus.Failed, UploadScope.Public); return; }
            var stopwatch = Stopwatch.StartNew();
            var solution = await solver.SolvePow(challenge, token).ConfigureAwait(false);
            PowSolved.Publish(stopwatch.Elapsed.TotalMilliseconds);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = challenge.Key,
                ["solution"] = solution,
                ["serverid"] = job.Context.Server.Id.ToString(),
                ["natsmsg"] = Encoding.UTF8.GetString(bytes),
                ["identifier"] = job.Identifier.ToString()
            });
            using var response = await publicClient.PostAsync(new Uri(origin, "/pow/" + topic), content, token).ConfigureAwait(false);
            var status = response.IsSuccessStatusCode ? UploadStatus.Success : UploadStatus.Failed;
            if (status == UploadStatus.Success) Remember(key);
            Report(job, status, UploadScope.Public);
        }
        catch (OperationCanceledException) when (WasInvalidated(job, UploadScope.Public)) { throw; }
        catch (Exception exception)
        {
            Log.Warning(exception, "Public {Kind} upload failed", job.Kind);
            Report(job, UploadStatus.Failed, UploadScope.Public);
        }
        finally { inFlight.TryRemove(key, out _); }
    }

    private async Task UploadPrivateAsync(Job job, object payload, string path, bool deduplicate)
    {
        // Keep AFM observations in queue order so an older schedule cannot overwrite a newer snapshot.
        await afmDataGate.WaitAsync(shutdown.Token).ConfigureAwait(false);
        try { await UploadPrivateCoreAsync(job, payload, path, deduplicate).ConfigureAwait(false); }
        finally { afmDataGate.Release(); }
    }

    private async Task UploadPrivateCoreAsync(Job job, object payload, string path, bool deduplicate)
    {
        if (!IsCurrent(job) || string.IsNullOrWhiteSpace(job.Context.AccountId)) { Report(job, UploadStatus.Skipped, UploadScope.Private); return; }
        var entryKeys = new List<string>();
        if (payload is ItemEstimatedMarketValueUpload emv)
        {
            var filtered = new ItemEstimatedMarketValueUpload { ServerId = emv.ServerId };
            foreach (var entry in emv.Items)
            {
                var fingerprint = $"emv:{job.Context.AccountId}:{emv.ServerId}:{Hash(JsonSerializer.SerializeToUtf8Bytes(entry, AfmJson))}";
                lock (gate) { if (successful.Contains(fingerprint)) continue; }
                filtered.Items.Add(entry); entryKeys.Add(fingerprint);
            }
            if (filtered.Items.Count == 0) { Report(job, UploadStatus.Skipped, UploadScope.Private); return; }
            payload = filtered;
            job = job with { Payload = filtered, Identifier = filtered.Identifier, Count = filtered.Items.Count };
        }
        // Packet ordering and duplicate entries do not change the observed schedule.
        // Normalize only its fingerprint; preserve the original endpoint payload.
        object fingerprintPayload = payload is FestivitiesUpload festivities
            ? new FestivitiesUpload
            {
                ServerId = festivities.ServerId,
                Events = festivities.Events
                    .DistinctBy(value => (value.Kind, value.Category, value.UniqueName, value.StartTime, value.EndTime))
                    .OrderBy(value => value.StartTime).ThenBy(value => value.EndTime)
                    .ThenBy(value => value.Category, StringComparer.Ordinal)
                    .ThenBy(value => value.UniqueName, StringComparer.Ordinal).ThenBy(value => value.Kind)
                    .ToList()
            }
            : payload;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(fingerprintPayload, fingerprintPayload.GetType(), AfmJson);
        var key = $"private:{job.Context.AccountId}:{job.Context.Server.Id}:{path}:{Hash(bytes)}";
        var snapshotKey = job.Kind is "Specs" or "GlobalMultiplier" or "Festivities"
            ? $"{job.Context.AccountId}:{job.Context.Server.Id}:{job.Kind}:{(job.Kind == "Specs" ? job.Context.CharacterName : "")}" : null;
        if (snapshotKey is not null)
        {
            lock (gate) if (lastSnapshots.GetValueOrDefault(snapshotKey) == key) { Report(job, UploadStatus.Skipped, UploadScope.Private); return; }
        }
        else if (deduplicate && !Claim(key)) { Report(job, UploadStatus.Skipped, UploadScope.Private); return; }
        try
        {
            using var cancellation = CreatePrivateCancellation(job.Kind == "MarketOrders");
            if (!IsCurrent(job)) return;
            using var response = await SendAfmAsync(HttpMethod.Post, path, payload, job.Context.AccountId, cancellation.Token,
                isCurrent: () => IsCurrent(job)).ConfigureAwait(false);
            var status = response is null ? UploadStatus.Skipped : response.IsSuccessStatusCode ? UploadStatus.Success : UploadStatus.Failed;
            if (status == UploadStatus.Success && deduplicate)
            {
                if (snapshotKey is not null)
                {
                    lock (gate)
                    {
                        lastSnapshots[snapshotKey] = key;
                        if (lastSnapshots.Count > 128) lastSnapshots.Remove(lastSnapshots.Keys.First());
                    }
                }
                else foreach (var entryKey in entryKeys) Remember(entryKey);
            }
            Report(job, status, UploadScope.Private);
        }
        catch (OperationCanceledException) when (WasInvalidated(job, UploadScope.Private)) { throw; }
        catch (Exception exception)
        {
            Log.Warning(exception, "Private {Kind} upload failed", job.Kind);
            Report(job, UploadStatus.Failed, UploadScope.Private);
        }
        finally { if (deduplicate) inFlight.TryRemove(key, out _); }
    }

    /// <summary>Caller owns the returned response. Credentials are attached to this request only.</summary>
    public async Task<HttpResponseMessage?> SendAfmAsync(HttpMethod method, string path, object? payload, string expectedAccountId,
        CancellationToken token = default, bool backend = false, Func<bool>? isCurrent = null)
    {
        var client = backend ? backendClient : afmClient;
        var bytes = payload is null ? null : JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), AfmJson);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (auth.AccountId != expectedAccountId || isCurrent?.Invoke() == false) return null;
            var bearer = await auth.GetTokenAsync(expectedAccountId, attempt > 0, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bearer) || auth.AccountId != expectedAccountId || isCurrent?.Invoke() == false) return null;
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            request.Headers.TryAddWithoutValidation("X-User-Id", expectedAccountId);
            if (bytes != null) { request.Content = new ByteArrayContent(bytes); request.Content.Headers.ContentType = new("application/json"); }
            var response = await client.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt != 0) return response;
            response.Dispose();
        }
        return null;
    }

    private CancellationTokenSource CreatePrivateCancellation(bool market = false)
    {
        lock (gate) return market
            ? CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, accountCancellation.Token, marketCancellation.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, accountCancellation.Token);
    }
    private CancellationTokenSource CreatePublicCancellation(Job job)
    {
        lock (gate)
        {
            var cancellation = job.Kind == "MarketOrders"
                ? CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, marketCancellation.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            if (job.Kind == "MarketOrders" && job.Context.MarketGeneration != marketGeneration) cancellation.Cancel();
            return cancellation;
        }
    }
    private static string Hash(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload));
    private bool Claim(string key)
    {
        lock (gate) return !(key.StartsWith("public:", StringComparison.Ordinal) ? successfulPublic : successful).Contains(key)
            && inFlight.TryAdd(key, 0);
    }
    private void Remember(string key)
    {
        lock (gate)
        {
            if (key.StartsWith("public:", StringComparison.Ordinal))
            {
                if (successfulPublic.Add(key)) successfulPublicOrder.Enqueue(key);
                while (successfulPublicOrder.Count > 64) successfulPublic.Remove(successfulPublicOrder.Dequeue());
            }
            else
            {
                if (successful.Add(key)) successfulOrder.Enqueue(key);
                while (successfulOrder.Count > 5000) successful.Remove(successfulOrder.Dequeue());
            }
        }
    }
    private void Report(Job job, UploadStatus status, UploadScope scope) => UploadResult.Publish(new(job.Identifier, job.Kind, status, scope, job.Context.Server, job.Count, job.Payload));
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        auth.AccountChanged -= AccountChanged; settings.Changed -= SettingsChanged;
        shutdown.Cancel(); queue.Writer.TryComplete();
        worker.GetAwaiter().GetResult();
        lock (gate) { accountCancellation.Cancel(); accountCancellation.Dispose(); marketCancellation.Cancel(); marketCancellation.Dispose(); }
        shutdown.Dispose();
        afmDataGate.Dispose();
    }
    internal sealed record UploadContext(AlbionServer Server, string? CharacterName, string? AccountId, long AccountGeneration, long SettingsGeneration, long MarketGeneration, ClientCoreOptions Options);
    private sealed record Job(Guid Identifier, string Kind, object Payload, int Count, UploadContext Context);
}
