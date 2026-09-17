# Shared library guidelines

- Use .NET 10, nullable reference types and four-space C# indentation. Run `dotnet format` for changed source.
- The Avalonia desktop Photon implementation is authoritative when the clients diverge. Keep decoder fixes centralized here.
- Add packet DTO constructors to `Protocol/Albion.Network/PacketFactory.cs`, including indirect subclasses. Do not use runtime reflection for packet construction.
- Register features explicitly. Shared handlers run before app adapters; subscribers must multicast and preserve packet order and response return codes.
- Keep UI, native capture, credentials, settings persistence and EF/database code in clients. Add no migrations here.
- Preserve wire JSON, endpoint paths, successful-upload deduplication, captured context, account isolation and the farming outbox format.
- Hosts own HTTP clients and logging configuration. Do not log credentials or enable verbose packet logging by default.
- Run the shared build and `tests/AFMDataClient.Core.Pow.Tests`. Automated tests are limited to PoW unless explicitly requested otherwise. Build both clients and document live-device checks still outstanding.
- Push a library commit before committing client submodule pins. CI must check out the pinned SHA recursively. Do not publish packages, app releases, or release tags without explicit instructions.
- Do not commit local caches, packet dumps, credentials, build output or personal application data. Preserve LICENSE and source attribution.
