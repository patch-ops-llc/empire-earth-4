using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Core;

public class StateHasherTests
{
    private static ulong Hash(params ulong[] words)
    {
        var hasher = default(StateHasher);
        foreach (var word in words)
        {
            hasher.Add(word);
        }

        return hasher.Finish();
    }

    /// <summary>
    /// Values computed with a separate implementation of the same routine while writing this test.
    /// They pin the hash: a change here changes every recorded hash and breaks play between builds.
    /// </summary>
    [Fact]
    public void TheHashOfKnownInputIsTheRecordedValue()
    {
        Assert.Equal(0UL, Hash());
        Assert.Equal(0x9FB0856A37ADFC13UL, Hash(0));
        Assert.Equal(0x7EC7BC097524F62CUL, Hash(0, 0));
        Assert.Equal(0x8A714B16190A32F5UL, Hash(1));
        Assert.Equal(0xC691A98BADB051A8UL, Hash(1, 2, 3));
        Assert.Equal(0xC7C076D067133B8DUL, Hash(ulong.MaxValue));
        Assert.Equal(0x33AA14C99D344168UL, Hash(ulong.MaxValue, 1UL << 63, 0x0123456789ABCDEF));
    }

    [Fact]
    public void TheHashDependsOnOrderCountAndEveryBit()
    {
        Assert.NotEqual(Hash(1, 2), Hash(2, 1));
        Assert.NotEqual(Hash(), Hash(0));
        Assert.NotEqual(Hash(0), Hash(0, 0));
        Assert.NotEqual(Hash(0, 0), Hash(0, 0, 0));
        Assert.NotEqual(Hash(5), Hash(5, 0));

        var seen = new HashSet<ulong> { Hash(0, 0) };
        for (var bit = 0; bit < 64; bit++)
        {
            Assert.True(seen.Add(Hash(1UL << bit, 0)));
            Assert.True(seen.Add(Hash(0, 1UL << bit)));
        }
    }

    /// <summary>Changing one input bit should change about half of the 64 output bits.</summary>
    [Fact]
    public void OneChangedBitChangesAboutHalfTheHash()
    {
        var total = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            var changed = System.Numerics.BitOperations.PopCount(Hash(12345, 678) ^ Hash(12345 ^ (1UL << bit), 678));
            Assert.InRange(changed, 12, 52);
            total += changed;
        }

        Assert.InRange(total / 64, 28, 36);
    }

    [Fact]
    public void EveryOverloadAddsWholeWordsInADefinedWay()
    {
        Assert.Equal(Hash(ulong.MaxValue), HashOf((ref StateHasher h) => h.Add(-1L)));
        Assert.Equal(Hash(ulong.MaxValue), HashOf((ref StateHasher h) => h.Add(-1)));
        Assert.Equal(Hash(uint.MaxValue), HashOf((ref StateHasher h) => h.Add(uint.MaxValue)));
        Assert.Equal(Hash(1), HashOf((ref StateHasher h) => h.Add(true)));
        Assert.Equal(Hash(0), HashOf((ref StateHasher h) => h.Add(false)));
        Assert.Equal(Hash(7), HashOf((ref StateHasher h) => h.Add(new EntityId(7))));
        Assert.Equal(Hash(98_304), HashOf((ref StateHasher h) => h.Add(Fixed.FromRaw(98_304))));
        Assert.Equal(Hash(unchecked((ulong)-98_304L)), HashOf((ref StateHasher h) => h.Add(Fixed.FromRaw(-98_304))));
        Assert.Equal(Hash(1024), HashOf((ref StateHasher h) => h.Add(Angle.QuarterTurn)));
        Assert.Equal(Hash(5, 0), HashOf((ref StateHasher h) => h.Add((Int128)5)));
        Assert.Equal(Hash(ulong.MaxValue, ulong.MaxValue), HashOf((ref StateHasher h) => h.Add((Int128)(-1))));
        Assert.Equal(Hash(0, 1), HashOf((ref StateHasher h) => h.Add((Int128)1 << 64)));
        Assert.Equal(Hash(5, 0, 4, 0), HashOf((ref StateHasher h) => h.Add(new Fraction(5, 4))));
        Assert.Equal(Hash(0, 0, 1, 0), HashOf((ref StateHasher h) => h.Add(default(Fraction))));
    }

    [Fact]
    public void FinishDoesNotEndTheHash()
    {
        var hasher = default(StateHasher);
        hasher.Add(1);
        Assert.Equal(Hash(1), hasher.Finish());
        Assert.Equal(Hash(1), hasher.Finish());
        hasher.Add(2);
        Assert.Equal(Hash(1, 2), hasher.Finish());
    }

    private static ulong HashOf(AddToHasher add)
    {
        var hasher = default(StateHasher);
        add(ref hasher);
        return hasher.Finish();
    }

    private delegate void AddToHasher(ref StateHasher hasher);
}
