using Sim.Core;

namespace Sim.Tests.Core;

public class DeterministicRandomTests
{
    /// <summary>
    /// The published reference output of SplitMix64, also reproduced with a separate implementation
    /// while writing this test. A change here would change every recorded replay.
    /// </summary>
    [Fact]
    public void TheSequenceIsTheSplitMix64ReferenceSequence()
    {
        var fromZero = new DeterministicRandom(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, fromZero.NextUInt64());
        Assert.Equal(7960286522194355700UL, fromZero.NextUInt64());
        Assert.Equal(487617019471545679UL, fromZero.NextUInt64());

        var random = new DeterministicRandom(1234567);
        ulong[] expected =
        [
            6457827717110365317, 3203168211198807973, 9817491932198370423, 4593380528125082431, 16408922859458223821,
        ];
        foreach (var value in expected)
        {
            Assert.Equal(value, random.NextUInt64());
        }
    }

    [Fact]
    public void TheStateIsEnoughToContinueTheSequence()
    {
        var original = new DeterministicRandom(42);
        Assert.Equal(42UL, original.State);
        for (var i = 0; i < 10; i++)
        {
            original.NextInt(1000);
        }

        var restored = new DeterministicRandom(original.State);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt64(), restored.NextUInt64());
        }

        Assert.Equal(original.State, restored.State);
    }

    [Fact]
    public void BoundedNumbersStayInRangeAndReachEveryValue()
    {
        var random = new DeterministicRandom(7);
        var seen = new int[6];
        for (var i = 0; i < 6000; i++)
        {
            seen[random.NextInt(6)]++;
        }

        // Each value is expected 1,000 times. The run is fixed by the seed, so this cannot flake.
        Assert.All(seen, count => Assert.InRange(count, 900, 1100));

        var seenInRange = new bool[7];
        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextInt(-3, 4);
            Assert.InRange(value, -3, 3);
            seenInRange[value + 3] = true;
        }

        Assert.All(seenInRange, wasSeen => Assert.True(wasSeen));
        Assert.Equal(0, random.NextInt(1));
        Assert.Equal(5, random.NextInt(5, 6));
        Assert.Equal(0UL, random.NextUInt64(1));
    }

    [Fact]
    public void BoundedNumbersCoverTheWidestRanges()
    {
        var random = new DeterministicRandom(99);
        var sawNegative = false;
        var sawPositive = false;
        for (var i = 0; i < 200; i++)
        {
            var value = random.NextInt(int.MinValue, int.MaxValue);
            sawNegative |= value < 0;
            sawPositive |= value > 0;
            Assert.InRange(random.NextInt(int.MaxValue), 0, int.MaxValue - 1);
            Assert.InRange(random.NextUInt64(ulong.MaxValue), 0UL, ulong.MaxValue - 1);
        }

        Assert.True(sawNegative && sawPositive);
    }

    /// <summary>
    /// A bound of three quarters of the 64-bit range makes one draw in four fall into the part that
    /// must be drawn again, so this exercises the redraw and pins its results.
    /// </summary>
    [Fact]
    public void BoundedNumbersAreUnbiasedForABoundThatNeedsRedraws()
    {
        const ulong Bound = 3UL << 62;
        var random = new DeterministicRandom(5);
        var reference = new DeterministicRandom(5);
        var redraws = 0;

        for (var i = 0; i < 2000; i++)
        {
            // The same method written out with the widest integers: accept a draw only if its low
            // half is at or above 2^64 mod Bound.
            UInt128 product;
            while (true)
            {
                product = (UInt128)reference.NextUInt64() * Bound;
                if (unchecked((ulong)product) >= (ulong)(((UInt128)1 << 64) % Bound))
                {
                    break;
                }

                redraws++;
            }

            var value = random.NextUInt64(Bound);
            Assert.Equal((ulong)(product >> 64), value);
            Assert.True(value < Bound);
        }

        Assert.Equal(reference.State, random.State);
        Assert.InRange(redraws, 400, 900);
    }

    [Fact]
    public void BoundsMustBeValid()
    {
        var random = new DeterministicRandom(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(6, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextUInt64(0));
        Assert.Equal(1UL, random.State);
    }
}
