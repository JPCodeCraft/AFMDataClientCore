using AlbionDataAvalonia.Network.Models;

namespace AFMDataClient.Core;

public sealed record ClientCoreOptions
{
    public bool PrivateMarketOrders { get; init; }
    public bool ContributeToPublic { get; init; }
    public bool ShareWithFriends { get; init; }
    public string[] PublicItemFilters { get; init; } = [];
    public bool UploadSpecs { get; init; } = true;
    public bool IslandTracking { get; init; } = true;
    public int DesiredConcurrency { get; init; } = 2;
    public string StorageDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "data");
    public string ClientIdentification { get; init; } = "AFMDataClient";
    public string MarketOrdersIngestSubject { get; init; } = "marketorders.ingest";
    public string MarketHistoriesIngestSubject { get; init; } = "markethistories.ingest";
    public string GoldDataIngestSubject { get; init; } = "goldprices.ingest";
    public string BanditEventIngestSubject { get; init; } = "banditevent.ingest";
}

/// <summary>Credentials remain host-owned. A token must belong to the expected account, including after refresh.</summary>
public interface IUploadAuthSession
{
    string? AccountId { get; }
    event Action? AccountChanged;
    Task<string?> GetTokenAsync(string expectedAccountId, bool forceRefresh, CancellationToken cancellationToken);
}

public sealed record ClientUploadResult(Guid Identifier, string Kind, UploadStatus Status,
    UploadScope Scope, AlbionServer? Server, int Count, object? Payload);

internal sealed class CoreSettings(ClientCoreOptions options)
{
    public ClientCoreOptions Options { get; private set; } = options;
    public ClientCoreOptions Previous { get; private set; } = options;
    public event Action? Changed;
    public void Update(ClientCoreOptions options)
    {
        if (Options == options || Options with { PublicItemFilters = options.PublicItemFilters } == options
            && Options.PublicItemFilters.SequenceEqual(options.PublicItemFilters, StringComparer.Ordinal)) return;
        Previous = Options;
        Options = options with { PublicItemFilters = options.PublicItemFilters.ToArray() };
        Changed.Publish();
    }
}
