using System.Globalization;

namespace Albion.Network;

public enum PhotonMessageKind { Reliable, Unreliable, Fragmented }

/// <summary>Transport identity, independent of decoded payload or wall-clock rounding.</summary>
public sealed record PhotonMessageIdentity(Guid ReceiverGeneration, string DirectionalConnection,
    short PeerId, int Challenge, byte Channel, PhotonMessageKind Kind, int ReliableSequence,
    int? UnreliableSequence = null, int? FragmentStartSequence = null, DateTime? FirstFragmentCapturedAtUtc = null)
{
    public string OriginKey => string.Create(CultureInfo.InvariantCulture,
        $"{ReceiverGeneration:N}|{DirectionalConnection}|{PeerId}|{Challenge}|{Channel}|{(int)Kind}|{ReliableSequence}|{UnreliableSequence}|{FragmentStartSequence}");
}
