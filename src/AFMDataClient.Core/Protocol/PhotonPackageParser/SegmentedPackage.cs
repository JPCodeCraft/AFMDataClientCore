using System;
using System.Collections.Generic;
using Albion.Network;

namespace PhotonPackageParser
{
    internal readonly struct SegmentedPackageKey : IEquatable<SegmentedPackageKey>
    {
        public SegmentedPackageKey(
            string directionalConnection,
            short peerId,
            int challenge,
            byte channelId,
            int startSequenceNumber)
        {
            DirectionalConnection = directionalConnection;
            PeerId = peerId;
            Challenge = challenge;
            ChannelId = channelId;
            StartSequenceNumber = startSequenceNumber;
        }

        public short PeerId { get; }
        public string DirectionalConnection { get; }
        public int Challenge { get; }
        public byte ChannelId { get; }
        public int StartSequenceNumber { get; }

        public bool Equals(SegmentedPackageKey other)
        {
            return DirectionalConnection == other.DirectionalConnection && PeerId == other.PeerId
                && Challenge == other.Challenge
                && ChannelId == other.ChannelId
                && StartSequenceNumber == other.StartSequenceNumber;
        }

        public override bool Equals(object? obj)
        {
            return obj is SegmentedPackageKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = StringComparer.Ordinal.GetHashCode(DirectionalConnection);
                hashCode = (hashCode * 397) ^ PeerId.GetHashCode();
                hashCode = (hashCode * 397) ^ Challenge;
                hashCode = (hashCode * 397) ^ ChannelId.GetHashCode();
                hashCode = (hashCode * 397) ^ StartSequenceNumber;
                return hashCode;
            }
        }
    }

    internal sealed class SegmentedPackage
    {
        public int TotalLength;
        public PhotonMessageIdentity? MessageIdentity;
        public DateTime CapturedAtUtc;
        public string ConnectionId = string.Empty;
        public int FragmentCount;
        public long BytesWritten;
        public byte[] TotalPayload = Array.Empty<byte>();
        public DateTime CreatedUtc;
        public DateTime LastUpdatedUtc;
        public Dictionary<int, FragmentRange> ReceivedFragments = new Dictionary<int, FragmentRange>();
    }

    internal readonly struct FragmentRange
    {
        public FragmentRange(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }
    }
}
