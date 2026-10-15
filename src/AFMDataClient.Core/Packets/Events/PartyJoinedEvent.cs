using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Events;

public class PartyJoinedEvent : BaseEvent
{
    // Current clients send member GUIDs and names at 8/9; older captures used 5/6.
    private static readonly (byte Guids, byte Names)[] MemberKeys = [(8, 9), (5, 6)];

    public Dictionary<Guid, string> PartyUsers { get; } = new();

    public PartyJoinedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            foreach (var (guidKey, nameKey) in MemberKeys)
            {
                if (!parameters.TryGetValue(guidKey, out object? guidData)
                    || !parameters.TryGetValue(nameKey, out object? nameData)
                    || nameData is not string[] and not object[])
                {
                    continue;
                }

                var guids = guidData.ToGuidArray();
                var names = nameData.ToStringArray();
                var count = Math.Min(guids.Length, names.Length);

                for (var i = 0; i < count; i++)
                {
                    if (guids[i] != Guid.Empty && !string.IsNullOrWhiteSpace(names[i]))
                    {
                        PartyUsers[guids[i]] = names[i];
                    }
                }

                if (PartyUsers.Count > 0)
                {
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
