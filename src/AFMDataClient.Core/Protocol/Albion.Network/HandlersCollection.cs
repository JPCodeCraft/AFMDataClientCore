using Serilog;

namespace Albion.Network;

internal sealed class HandlersCollection
{
    private readonly List<IPacketHandler> handlers = new();

    public void Add<TPacket>(PacketHandler<TPacket> handler)
    {
        // Stable ordering within a priority; no linked chain that swallows other consumers.
        var index = handlers.FindIndex(existing => existing.Priority < handler.Priority);
        if (index < 0) handlers.Add(handler);
        else handlers.Insert(index, handler);
    }

    public async Task HandleAsync(object packet)
    {
        foreach (var handler in handlers)
        {
            try { await handler.HandleAsync(packet).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "Packet subscriber {Handler} failed", handler.GetType().Name); }
        }
    }
}
