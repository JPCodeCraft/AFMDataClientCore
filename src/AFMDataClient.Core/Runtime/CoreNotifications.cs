using Serilog;

namespace AFMDataClient.Core;

internal static class CoreNotifications
{
    public static void Publish(this Action? handlers)
    {
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch (Exception exception) { Log.Warning(exception, "Client notification observer failed"); }
    }
    public static void Publish<T>(this Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (Action<T> handler in handlers.GetInvocationList())
            try { handler(value); } catch (Exception exception) { Log.Warning(exception, "Client notification observer failed"); }
    }
    public static void Publish<T1, T2, T3>(this Action<T1, T2, T3>? handlers, T1 value1, T2 value2, T3 value3)
    {
        if (handlers is null) return;
        foreach (Action<T1, T2, T3> handler in handlers.GetInvocationList())
            try { handler(value1, value2, value3); } catch (Exception exception) { Log.Warning(exception, "Client notification observer failed"); }
    }
}
