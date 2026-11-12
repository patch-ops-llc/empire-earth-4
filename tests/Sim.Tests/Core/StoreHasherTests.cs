using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Core;

/// <summary>The state hash taken apart: one hash for the world's own fields and one for each store.</summary>
public class StoreHasherTests
{
    private static ToyMatch MatchAfter(int ticks)
    {
        var match = new ToyMatch(seed: 3);
        match.Simulation.Run(ticks);
        return match;
    }

    [Fact]
    public void ThePartsAreTheWorldAndThenEveryStoreItHas()
    {
        var match = MatchAfter(5);

        Assert.Equal(["world", "movers", "scores"], StoreHasher.PartNames(match.World));
        Assert.Equal(3, StoreHasher.PartCount(match.World));

        // A store that another piece adds is a part without a word changed here.
        match.World.AddStore(new ComponentStore<ToyMatch.Mover>("extra"));
        Assert.Equal(["world", "extra", "movers", "scores"], StoreHasher.PartNames(match.World));
        Assert.Equal(4, new StoreHasher().Compute(match.World).Length);
    }

    /// <summary>
    /// The parts together are the walk of the state hash, field for field, and nothing else: the
    /// world's own fields, then, after the number of stores, each store's. A field the world
    /// gains of its own must join the first part, and this fails until it has.
    /// </summary>
    [Fact]
    public void ThePartsCoverExactlyWhatTheStateHashCovers()
    {
        var match = MatchAfter(40);
        var whole = new Recorder();
        match.World.Walk(whole);
        var stores = match.World.Stores.Select(store =>
        {
            var recorder = new Recorder();
            store.Walk(recorder);
            return recorder.Fields;
        }).ToArray();

        var own = whole.Fields.Count - stores.Sum(fields => fields.Count) - 1;
        Assert.True(own > 0);
        var expected = new List<ulong> { Hash(whole.Fields.Take(own)) };
        var next = own + 1;
        foreach (var fields in stores)
        {
            expected.Add(Hash(whole.Fields.Skip(next).Take(fields.Count)));
            next += fields.Count;
        }

        Assert.Equal(whole.Fields.Count, next);
        Assert.Equal(expected, new StoreHasher().Compute(match.World));
    }

    [Fact]
    public void AChangeShowsInItsOwnPartAndInNoOther()
    {
        var before = new StoreHasher().Compute(MatchAfter(40).World);

        int[] Changed(Action<ToyMatch> change)
        {
            var match = MatchAfter(40);
            change(match);
            var after = new StoreHasher().Compute(match.World);
            return [.. Enumerable.Range(0, before.Length).Where(part => before[part] != after[part])];
        }

        const int World = 0;
        const int Movers = 1;
        const int Scores = 2;
        Assert.Equal([], Changed(_ => { }));
        Assert.Equal([World], Changed(match => match.World.Random.NextUInt64()));
        Assert.Equal([World], Changed(match => match.World.Entities.Create()));
        Assert.Equal([Movers], Changed(match => match.Movers.ValueAt(0).X += Fixed.Epsilon));
        Assert.Equal([Movers], Changed(match => match.Movers.Remove(match.Movers.EntityAt(0))));
        Assert.Equal([Scores], Changed(match => match.Scores.Add(1, 1)));
        Assert.Equal([World, Movers, Scores], Changed(match => match.Simulation.Run(4)));
    }

    [Fact]
    public void TakingThePartsChangesNothing()
    {
        var match = MatchAfter(40);
        var hash = match.World.ComputeHash();
        var hasher = new StoreHasher();

        var first = hasher.Compute(match.World);
        var second = hasher.Compute(match.World);

        Assert.Equal(first, second);
        Assert.Equal(hash, match.World.ComputeHash());
        Assert.Equal(first, new StoreHasher().Compute(MatchAfter(40).World));
    }

    [Fact]
    public void RoomForTheWrongNumberOfHashesIsRefused()
    {
        var match = MatchAfter(1);

        Assert.Throws<ArgumentException>(() => new StoreHasher().Compute(match.World, new ulong[2]));
        Assert.Throws<ArgumentException>(() => new StoreHasher().Compute(match.World, new ulong[4]));
    }

    private static ulong Hash(IEnumerable<Feed> fields)
    {
        var hasher = default(StateHasher);
        foreach (var feed in fields)
        {
            feed(ref hasher);
        }

        return hasher.Finish();
    }

    private delegate void Feed(ref StateHasher hasher);

    /// <summary>Keeps every field of a walk, as what it adds to a hash.</summary>
    private sealed class Recorder : IStateWalker
    {
        public List<Feed> Fields { get; } = [];

        public bool IsRestoring => false;

        public bool Field(bool value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public int Field(int value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public uint Field(uint value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public long Field(long value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public ulong Field(ulong value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public Int128 Field(Int128 value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public EntityId Field(EntityId value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public Fixed Field(Fixed value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public Angle Field(Angle value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public Fraction Field(Fraction value, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(value));
            return value;
        }

        public int Count(int count, int minimumBytesPerItem = 1, string name = "")
        {
            Fields.Add((ref StateHasher hasher) => hasher.Add(count));
            return count;
        }
    }
}
