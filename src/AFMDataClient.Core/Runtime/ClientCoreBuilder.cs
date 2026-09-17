namespace AFMDataClient.Core;

/// <summary>Explicit feature selection; features that are not registered have no workers or reference downloads.</summary>
public sealed class ClientCoreBuilder(ClientCoreOptions options, IUploadAuthSession auth,
    HttpClient publicClient, HttpClient afmClient, HttpClient backendClient)
{
    private readonly HashSet<string> features = new(StringComparer.Ordinal);
    public ClientCoreBuilder WithMarketOrders(bool loadouts = true) { features.Add("MarketOrders"); if (loadouts) features.Add("Loadouts"); return this; }
    public ClientCoreBuilder WithMarketHistory() { features.Add("MarketHistory"); return this; }
    public ClientCoreBuilder WithSpecs() { features.Add("Specs"); return this; }
    public ClientCoreBuilder WithEstimatedMarketValues() { features.Add("Emv"); return this; }
    public ClientCoreBuilder WithIslands() { features.Add("Islands"); return this; }
    public ClientCoreBuilder WithGold() { features.Add("Gold"); return this; }
    public ClientCoreBuilder WithBanditEvents() { features.Add("Bandit"); return this; }
    public ClientCoreBuilder WithGlobalMultipliers() { features.Add("GlobalMultiplier"); return this; }
    public ClientCoreBuilder WithFestivities() { features.Add("Festivities"); return this; }
    public ClientCoreBuilder WithWorldEvents() => WithBanditEvents().WithGlobalMultipliers().WithFestivities();
    public ClientCore Build() => new(options, auth, publicClient, afmClient, backendClient, features);
}
