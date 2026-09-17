using AlbionDataAvalonia.Locations.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;

namespace AFMDataClient.Core;

public sealed class ClientSession
{
    public AlbionServer? AlbionServer { get; private set; }
    public long UserObjectId { get; private set; }
    public string? PlayerName { get; private set; }
    public Guid? CharacterId { get; private set; }
    public AlbionLocation? Location { get; private set; }
    public string? RawLocationId { get; private set; }
    public long? PremiumExpirationTicks { get; private set; }
    private bool premiumKnown;
    public bool? HasPremium => !premiumKnown ? null : PremiumExpirationTicks is { } ticks && ticks > DateTime.UtcNow.Ticks;
    public event Action? Changed;

    public void SetServer(AlbionServer? server)
    {
        if (AlbionServer?.Id == server?.Id) return;
        AlbionServer = server;
        ResetCharacter();
        Changed.Publish();
    }

    internal void Join(JoinResponse value)
    {
        if (value.ReturnCode != 0) return;
        UserObjectId = value.userObjectId;
        PlayerName = value.playerName;
        CharacterId = value.userGuid;
        Location = value.playerLocation;
        RawLocationId = value.RawLocationId;
        PremiumExpirationTicks = value.premiumExpirationTicks is >= 0 and <= 3155378975999999999 ? value.premiumExpirationTicks : null;
        premiumKnown = true;
        Changed.Publish();
    }

    internal void Reset()
    {
        ResetCharacter();
        Changed.Publish();
    }

    internal void Premium(long? ticks)
    {
        PremiumExpirationTicks = ticks is >= 0 and <= 3155378975999999999 ? ticks : null;
        premiumKnown = true;
        Changed.Publish();
    }

    private void ResetCharacter()
    {
        UserObjectId = 0;
        PlayerName = null;
        CharacterId = null;
        Location = null;
        RawLocationId = null;
        PremiumExpirationTicks = null;
        premiumKnown = false;
    }
}
