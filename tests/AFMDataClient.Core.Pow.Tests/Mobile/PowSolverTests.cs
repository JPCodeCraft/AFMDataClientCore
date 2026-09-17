using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlbionDataAvalonia.Network.Pow;
using Xunit;

namespace AFMDataClientMaui.Pow.Tests;

public class PowSolverTests
{

    [Theory]
    [InlineData(0UL, "0000000000000000")]
    [InlineData(9UL, "0000000000000009")]
    [InlineData(10UL, "000000000000000a")]
    [InlineData(15UL, "000000000000000f")]
    [InlineData(16UL, "0000000000000010")]
    [InlineData(0x0123456789abcdefUL, "0123456789abcdef")]
    [InlineData(ulong.MaxValue, "ffffffffffffffff")]
    public void FormatsCounterAsSixteenLowercaseHexDigits(ulong counter, string expected)
    {
        Span<byte> buffer = stackalloc byte[16];
        PowSolver.WriteCounterHex(buffer, counter);
        Assert.Equal(expected, Encoding.ASCII.GetString(buffer));
    }

    [Fact]
    public void AdvancesCounterAcrossDigitChangesCarriesAndWraparound()
    {
        using var solver = new PowSolver();
        ulong[] counters = [0, 8, 9, 14, 15, 0xff, 0xffff, 0x0123456789abcdef, ulong.MaxValue];
        foreach (ulong counter in counters)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(counter.ToString("x16", CultureInfo.InvariantCulture));
            ulong next = unchecked(counter + 1);
            PowSolver.IncrementHexAsciiInPlace(buffer);
            Assert.Equal(next.ToString("x16", CultureInfo.InvariantCulture), Encoding.ASCII.GetString(buffer));
        }
    }

    [Fact]
    public void HashesKnownSha256Vector()
    {
        using var solver = new PowSolver();
        Span<byte> hash = stackalloc byte[32];
        // Repeated calls also exercise reuse of the instance-based hasher.
        for (int i = 0; i < 2; i++)
        {
            solver.TryComputeHash("abc"u8, hash);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Convert.ToHexStringLower(hash));
        }
    }

    [Fact]
    public void ReusedHasherMatchesSha256AcrossBlockBoundaries()
    {
        using var solver = new PowSolver();
        Span<byte> hash = stackalloc byte[32];
        int[] lengths = [0, 1, 27, 55, 56, 63, 64, 65, 119, 120, 127, 128, 129, 1024, 0];
        foreach (int length in lengths)
        {
            byte[] input = Enumerable.Range(0, length).Select(i => (byte)(i * 37)).ToArray();
            solver.TryComputeHash(input, hash);
            Assert.Equal(SHA256.HashData(input), hash.ToArray());
        }
    }

    [Fact]
    public void MatchesBitsOfLowercaseHexTextIncludingPartialBytes()
    {
        using var solver = new PowSolver();
        byte[] hash = SHA256.HashData("abc"u8);
        string bits = ToAsciiBits(Convert.ToHexStringLower(hash));

        // All 512 prefix lengths cover partial bytes, odd nibbles and the full digest.
        for (int length = 0; length <= bits.Length; length++)
        {
            string wanted = bits[..length];
            Assert.True(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create(wanted)));
            if (length > 0)
            {
                string mismatch = wanted[..^1] + (wanted[^1] == '0' ? '1' : '0');
                Assert.False(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create(mismatch)));
            }
        }

        Assert.True(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create(null)));
        // The first raw hash byte is 0xba, but the first ASCII byte is 'b' (0x62).
        Assert.False(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create("1011")));
    }

    [Fact]
    public void MatchesEveryFirstByteAndStillChecksTheRemainingPrefix()
    {
        using var solver = new PowSolver();
        var hash = new byte[32];
        for (int value = 0; value < 256; value++)
        {
            hash[0] = (byte)value;
            var difficulty = PowSolver.PowDifficulty.Create(ToAsciiBits(value.ToString("x2")));
            Assert.True(solver.CheckLeadingBits(hash, difficulty));

            hash[0] ^= 1;
            Assert.False(solver.CheckLeadingBits(hash, difficulty));

            hash[0] = (byte)value;
            var longerDifficulty = PowSolver.PowDifficulty.Create(ToAsciiBits(value.ToString("x2") + "1"));
            Assert.False(solver.CheckLeadingBits(hash, longerDifficulty));
        }
    }

    [Fact]
    public void FindsFirstSolutionForKnownChallenge()
    {
        using var solver = new PowSolver();
        var challenge = new PowRequest { Key = "test-key", Wanted = "01100011011000110011100" };

        string solution = solver.ProcessPow(challenge);

        Assert.Equal("0000000000000200", solution);
        Assert.StartsWith(challenge.Wanted, HashBits(solution, challenge.Key));
        Assert.Equal("0000000000000201", solver.ProcessPow(new PowRequest { Key = "next", Wanted = "" }));
    }

    [Fact]
    public async Task AsyncSolveSupportsUtf8KeysAndNonzeroStartingCounters()
    {
        const string key = "Albion-ação-雪";
        const ulong start = 32;
        string wanted = HashBits("0000000000000040", key)[..23];
        // Independent, bounded string-based search; counter 64 is known to match.
        ulong expected = start;
        while (expected < 64 && !HashBits(expected.ToString("x16"), key).StartsWith(wanted, StringComparison.Ordinal))
        {
            expected++;
        }

        using var solver = new PowSolver();
        solver.ResetCounter(start);
        string solution = await solver.SolvePow(new PowRequest { Key = key, Wanted = wanted }, CancellationToken.None);

        Assert.Equal(expected.ToString("x16"), solution);
        Assert.StartsWith(wanted, HashBits(solution, key));
    }

    [Fact]
    public void RechecksCandidatesAndReturnsTheEarliestMatch()
    {
        // Counters 0 and 4 both start with raw hash byte 0x06; only 4 starts with hex "06c".
        using var solver = new PowSolver();
        Assert.Equal("0000000000000000", solver.ProcessPow(new PowRequest { Key = "batch-0", Wanted = ToAsciiBits("06") }));
        solver.ResetCounter(0);
        Assert.Equal("0000000000000004", solver.ProcessPow(new PowRequest { Key = "batch-0", Wanted = ToAsciiBits("06c") }));
    }

    [Fact]
    public void SolvesEveryLaneAndFallsBackForLongKeys()
    {
        using var solver = new PowSolver();
        string[] keys = ["", "test-key", new string('x', 34), new string('x', 35), new string('x', 128), new string('雪', 11), new string('雪', 12)];
        foreach (string key in keys)
        {
            for (ulong lane = 0; lane < 8; lane++)
            {
                string wanted = HashBits(lane.ToString("x16"), key);
                solver.ResetCounter(0);
                Assert.Equal(lane.ToString("x16"), solver.ProcessPow(new PowRequest { Key = key, Wanted = wanted }));
                Assert.Equal((lane + 1).ToString("x16"), solver.ProcessPow(new PowRequest { Key = key, Wanted = "" }));
            }
        }
    }

    [Fact]
    public void EmptyDifficultyAcceptsStartingCounterAndSuccessWrapsToZero()
    {
        using var solver = new PowSolver();
        solver.ResetCounter(ulong.MaxValue);
        var challenge = new PowRequest { Key = "test-key", Wanted = "" };

        Assert.Equal("ffffffffffffffff", solver.ProcessPow(challenge));
        Assert.Equal("0000000000000000", solver.ProcessPow(challenge));
        Assert.Equal("0000000000000001", solver.ProcessPow(challenge));
    }

    [Fact]
    public void SearchWrapsToZeroAfterRejectedMaximumCounter()
    {
        const string key = "test-key";
        string wanted = HashBits("0000000000000000", key)[..23];
        Assert.False(HashBits("ffffffffffffffff", key).StartsWith(wanted, StringComparison.Ordinal));
        using var solver = new PowSolver();
        solver.ResetCounter(ulong.MaxValue);

        Assert.Equal("0000000000000000", solver.ProcessPow(new PowRequest { Key = key, Wanted = wanted }));
    }


    [Fact]
    public async Task CanceledSolveDoesNotAdvanceCounter()
    {
        using var solver = new PowSolver();
        solver.ResetCounter(42);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new PowRequest { Key = "test-key", Wanted = "" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => solver.SolvePow(request, cancellation.Token));
        Assert.Equal("000000000000002a", await solver.SolvePow(request, CancellationToken.None));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(35)]
    public async Task CanCancelAnOngoingSearch(int keyLength)
    {
        using var solver = new PowSolver();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        // A full digest target keeps both the batch and long-key fallback busy until canceled.
        var request = new PowRequest { Key = new string('x', keyLength), Wanted = ToAsciiBits(new string('0', 64)) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await solver.SolvePow(request, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static string HashBits(string counter, string key) =>
        ToAsciiBits(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"aod^{counter}^{key}"))));

    private static string ToAsciiBits(string hex) =>
        string.Concat(hex.Select(character => Convert.ToString(character, 2).PadLeft(8, '0')));
}
