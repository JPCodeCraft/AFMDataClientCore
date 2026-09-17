# AFM Data Client Core

Shared protocol and upload implementation for the [Avalonia desktop client](https://github.com/JPCodeCraft/AlbionDataAvalonia) and [MAUI mobile client](https://github.com/JPCodeCraft/AFMDataClientMaui). One .NET 10 library, consumed directly from a pinned Git submodule. There is no package feed or automatic update at application startup.

## What belongs here

| Shared library | Client applications |
| --- | --- |
| Photon framing, fragmentation, Protocol16/18 decoding and parameter conversions | Native packet capture, VPN service and network-device selection |
| Packet events, requests, responses and typed multicast dispatch | Platform startup, permissions and lifecycle |
| Session, location, item and achievement reference data | Settings persistence, credentials and authentication UI |
| Orders, loadout offers, history, gold, specs, EMV, bandit events, global multipliers and festivities | UI, upload statistics presentation and logging sinks |
| Island observations, farming actions and durable account-specific upload outbox | Desktop combat, party, gathering, fishing, loot, legendary-item, mail and trade trackers, SQLite/EF |
| Public upload protocol and PoW; AFM authenticated requests and retry | Choice of enabled packet features and local subscriptions |
| Portfolio import HTTP logic and private-order-sharing API | Portfolio selection/UI and local trade persistence |

The desktop Photon decoder is the source of truth. Fix protocol divergences here, then update the client pins. Some existing `Albion.Network`, `PhotonPackageParser` and `AlbionDataAvalonia.*` namespaces are retained to keep the extraction small; the library has no reference to either application, UI framework or database.

## Choose features

The host supplies HTTP clients, an `IUploadAuthSession` adapter and an app-owned storage directory. Configure reference-data caching before constructing the core, and keep the supplied clients alive until after core disposal.

```csharp
using AFMDataClient.Core;
using Albion.Network;
using AlbionDataAvalonia.ReferenceData;

ReferenceDataLoader.ConfigureShared(referenceHttp, referenceCacheDirectory);
using var core = new ClientCoreBuilder(options, auth, publicHttp, afmHttp, backendHttp)
    .WithMarketOrders(loadouts: true)
    .WithMarketHistory()
    .WithSpecs()
    .WithEstimatedMarketValues()
    .WithIslands()
    .WithGold()
    .WithWorldEvents()
    .Build();
await core.InitializeAsync();

var builder = ReceiverBuilder.Create();
core.RegisterHandlers(builder);
// Add local typed subscriptions here, if this app needs them.
var receiver = builder.Build();

core.Session.SetServer(detectedServer);
receiver.Receive(new CapturedDatagram(udpPayload, capturedAtUtc, source, destination));
```

Omit any `With...` call to omit that feature's packet handlers, reference downloads and timers. `WithWorldEvents()` is shorthand for `WithBanditEvents()`, `WithGlobalMultipliers()` and `WithFestivities()`, which can be selected independently. `WithMarketOrders(loadouts: false)` omits quick-buy/loadout offers. Mobile registers no combat or damage-tracker consumers.

Use `ReceiverBuilder.SubscribeEvent<T>`, `SubscribeRequest<T>` or `SubscribeResponse<T>` to listen to individual packets. Each packet is decoded once per concrete DTO type and delivered to every matching subscriber. Shared handlers run at priority 100; local subscriptions default to 0. Equal priorities retain registration order. Handler failures are logged and do not prevent other subscribers from receiving the packet. Nonzero response return codes remain available to subscribers. `ObservePackets` is for optional raw diagnostic presentation.

Feed packets in capture order. Processing of each datagram waits for subscribers, while HTTP uploads run independently with bounded concurrency. Supply both endpoints so history request/response IDs are scoped to the captured connection. On capture restart, call `ResetCapture()` and create a new receiver to clear framing state. Apply preference changes using `UpdateOptions`.

Listen to `UploadResult`, `QueueChanged` and `PowSolved` for presentation. Results carry the observed server, kind, count, payload, scope and status; `Skipped` is not a successful network upload. Queued payloads retain their captured context, and private requests require the same AFM account after token refresh. Only successful submissions enter deduplication caches. Island outboxes persist under `StorageDirectory/farming-outbox`, retaining the existing desktop JSON format and retry behavior.

The host configures Serilog or bridges it into its logger. Keep packet detail logging behind host-controlled debug settings. AFM and backend clients need their respective base addresses; public requests use the captured Albion server's PoW URL. The host owns authentication storage and must raise `AccountChanged` for identity changes, not routine token refreshes.

## Consume and update the library

Both applications keep the same relative layout:

```text
Repos/
  AFMDataClientCore/                 # Independent library checkout
  AlbionDataAvalonia/
    shared/AFMDataClientCore/        # Pinned submodule
  AFMDataClientMaui/
    shared/AFMDataClientCore/        # Pinned submodule
```

Clone applications with submodules, or initialize an existing checkout:

```sh
git clone --recurse-submodules <client-repository-url>
git submodule update --init --recursive
```

The app's project references `shared/AFMDataClientCore/src/AFMDataClient.Core/AFMDataClient.Core.csproj`. Builds and CI use the committed submodule SHA; they never use `git submodule update --remote`.

For a library change, work in `Repos/AFMDataClientCore` (or a submodule checkout on a working branch), validate it, and push its commit first. Then update each client deliberately:

```sh
git -C shared/AFMDataClientCore fetch origin
git -C shared/AFMDataClientCore checkout <reviewed-core-commit>
git add shared/AFMDataClientCore
# Build this client, then commit the new submodule pin with the client changes.
```

Clients may move at different times. There is no requirement to release them together. CI checkouts must use `submodules: recursive`; Android links preserve shared DTO metadata for JSON serialization.

## Validation

```sh
dotnet build src/AFMDataClient.Core/AFMDataClient.Core.csproj -c Release
dotnet test tests/AFMDataClient.Core.Pow.Tests/AFMDataClient.Core.Pow.Tests.csproj -c Release
dotnet format src/AFMDataClient.Core/AFMDataClient.Core.csproj
```

Automated tests are deliberately limited to PoW. The shared suite contains both clients' existing tests: scalar, eight-lane Vector256, four-lane Vector128, counter boundaries and cancellation. The desktop `PowBench` project continues to benchmark the shared solver and variant implementations.

Also build both applications after changing the shared pin. Live capture checks should cover regular and loadout market browsing, history requests, gold, character/spec updates, the enabled world events, island entry and farming actions, capture restart, account switches, preference changes and offline retry. Never send fabricated observations to production to validate transport code.

## License and origin

This extraction retains the [desktop project's custom license](LICENSE) and its required attribution. Original repository: [JPCodeCraft/AlbionDataAvalonia](https://github.com/JPCodeCraft/AlbionDataAvalonia). Mobile PoW and test contributions originated in [JPCodeCraft/AFMDataClientMaui](https://github.com/JPCodeCraft/AFMDataClientMaui). Redistribution must retain the license and these source links.

