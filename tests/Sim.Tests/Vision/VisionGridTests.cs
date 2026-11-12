using System.Buffers.Binary;
using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Tests.Setup;
using Sim.Vision;

namespace Sim.Tests.Vision;

/// <summary>
/// The store of the fog of war on its own: which tiles one unit's sight reaches (design 8.4, row
/// 38), the three states of a tile, and what the store hands to the state walk.
/// </summary>
public class VisionGridTests
{
    private static VisionGrid Stamped(int width, int height, string x, string y, string sight, int player = 0, int players = 2)
    {
        var grid = new VisionGrid(players, width, height);
        grid.BeginRefresh();
        grid.Reveal(player, TestMatches.Point(x, y), Fraction.Parse(sight).ToFixed());
        grid.EndRefresh();
        return grid;
    }

    [Fact]
    public void ATileAtExactlyTheSightDistanceIsVisibleAndTheNextOneIsNot()
    {
        // From the center of tile (10, 10), the center of tile (16, 10) is exactly 6 tiles away.
        var grid = Stamped(30, 30, "10.5", "10.5", "6");

        Assert.True(grid.IsVisible(0, 16, 10));
        Assert.False(grid.IsVisible(0, 17, 10));
        Assert.True(grid.IsVisible(0, 4, 10));
        Assert.False(grid.IsVisible(0, 3, 10));
        Assert.True(grid.IsVisible(0, 10, 16));
        Assert.False(grid.IsVisible(0, 10, 17));
        Assert.True(grid.IsVisible(0, 10, 4));
        Assert.False(grid.IsVisible(0, 10, 3));
    }

    [Fact]
    public void TheDistanceIsStraightAndNotAlongTheAxes()
    {
        // 3, 4, 5: the center of tile (13, 14) is exactly 5 tiles from the center of (10, 10).
        var grid = Stamped(30, 30, "10.5", "10.5", "5");

        Assert.True(grid.IsVisible(0, 13, 14));
        Assert.True(grid.IsVisible(0, 14, 13));
        Assert.True(grid.IsVisible(0, 7, 6));
        Assert.False(grid.IsVisible(0, 14, 14));
        Assert.False(grid.IsVisible(0, 15, 11));

        // A circle of radius 5 about a tile's center holds the centers of 81 tiles.
        Assert.Equal(81, grid.CountVisible(0));
    }

    [Fact]
    public void SightIsMeasuredFromWhereTheUnitStandsAndNotFromTheCenterOfItsTile()
    {
        // Standing a quarter of a tile to the right, it reaches one tile less far to the left.
        var grid = Stamped(30, 30, "10.75", "10.5", "6");

        Assert.True(grid.IsVisible(0, 16, 10));
        Assert.False(grid.IsVisible(0, 17, 10));
        Assert.True(grid.IsVisible(0, 5, 10));
        Assert.False(grid.IsVisible(0, 4, 10));
    }

    [Fact]
    public void OneRawUnitDecidesAtTheEdge()
    {
        var epsilon = Fixed.Epsilon;
        var center = TestMatches.Point("10.5", "10.5");

        var short1 = new VisionGrid(1, 30, 30);
        short1.BeginRefresh();
        short1.Reveal(0, center, Fixed.FromInt(6) - epsilon);
        Assert.False(short1.IsVisible(0, 16, 10));
        Assert.True(short1.IsVisible(0, 15, 10));

        var moved = new VisionGrid(1, 30, 30);
        moved.BeginRefresh();
        moved.Reveal(0, new FixedVector2(center.X - epsilon, center.Y), Fixed.FromInt(6));
        Assert.False(moved.IsVisible(0, 16, 10));
        Assert.True(moved.IsVisible(0, 4, 10));
    }

    /// <summary>The rows of words agree with the plain rule, tile by tile, wherever the unit stands and however far it sees.</summary>
    [Fact]
    public void EveryTileWhoseCenterIsWithinSightIsVisibleAndNoOther()
    {
        var random = new DeterministicRandom(17);
        for (var round = 0; round < 400; round++)
        {
            var width = 1 + random.NextInt(70);
            var height = 1 + random.NextInt(70);
            var center = new FixedVector2(
                Fixed.FromRaw(random.NextInt(-3 * Fixed.RawOne, (width + 3) * Fixed.RawOne)),
                Fixed.FromRaw(random.NextInt(-3 * Fixed.RawOne, (height + 3) * Fixed.RawOne)));
            var sight = Fixed.FromRaw(random.NextInt(0, 14 * Fixed.RawOne));
            var grid = new VisionGrid(1, width, height);
            grid.BeginRefresh();
            grid.Reveal(0, center, sight);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var tile = new FixedVector2(Fixed.FromInt(x) + Fixed.Half, Fixed.FromInt(y) + Fixed.Half);
                    var within = FixedVector2.DistanceSquaredRaw(center, tile) <= FixedVector2.SquareRaw(sight);
                    Assert.True(
                        within == grid.IsVisible(0, x, y),
                        $"Round {round}: tile ({x}, {y}) of a {width} by {height} map, seen from {center} with sight {sight}.");
                }
            }
        }
    }

    [Fact]
    public void SightStopsAtTheEdgeOfTheMap()
    {
        var grid = Stamped(8, 5, "0.5", "0.5", "3");

        Assert.True(grid.IsVisible(0, 3, 0));
        Assert.False(grid.IsVisible(0, 4, 0));
        Assert.False(grid.IsVisible(0, -1, 0));
        Assert.False(grid.IsVisible(0, 0, -1));
        Assert.False(grid.IsVisible(0, 8, 0));
        Assert.False(grid.IsVisible(0, 0, 5));
        Assert.Equal(FogState.Unexplored, grid.StateOf(0, -1, 0));

        // A unit that sees the whole of a small map: every tile, and nothing past the last.
        var all = Stamped(8, 5, "4", "2.5", "40");
        Assert.Equal(40, all.CountVisible(0));
        Assert.Equal(40, all.CountExplored(0));
    }

    [Fact]
    public void WhatOnePlayerSeesIsNotWhatAnotherSees()
    {
        var grid = Stamped(30, 30, "10.5", "10.5", "6", player: 1, players: 3);

        Assert.True(grid.IsVisible(1, 10, 10));
        Assert.False(grid.IsVisible(0, 10, 10));
        Assert.False(grid.IsVisible(2, 10, 10));
        Assert.Equal(0, grid.CountExplored(0));
        Assert.Equal(0, grid.CountExplored(2));

        // A player who is not in the match sees nothing.
        Assert.False(grid.IsVisible(3, 10, 10));
        Assert.False(grid.IsVisible(-1, 10, 10));
    }

    [Fact]
    public void ATileSeenOnceStaysExploredWhenItIsNoLongerVisible()
    {
        var grid = Stamped(30, 30, "5.5", "5.5", "2");
        Assert.Equal(FogState.Visible, grid.StateOf(0, 5, 5));
        Assert.Equal(FogState.Unexplored, grid.StateOf(0, 20, 20));

        grid.BeginRefresh();
        grid.Reveal(0, TestMatches.Point("20.5", "20.5"), Fixed.FromInt(2));
        grid.EndRefresh();

        Assert.Equal(FogState.Explored, grid.StateOf(0, 5, 5));
        Assert.True(grid.IsExplored(0, 5, 5));
        Assert.False(grid.IsVisible(0, 5, 5));
        Assert.Equal(FogState.Visible, grid.StateOf(0, 20, 20));
        Assert.Equal(FogState.Unexplored, grid.StateOf(0, 12, 12));
        Assert.Equal(13, grid.CountVisible(0));
        Assert.Equal(26, grid.CountExplored(0));
    }

    [Fact]
    public void TheStatesAreCopiedRowByRowAsTheirNumbers()
    {
        var grid = Stamped(6, 4, "0.5", "0.5", "1");
        grid.BeginRefresh();
        grid.Reveal(0, TestMatches.Point("5.5", "3.5"), Fixed.Zero);
        grid.EndRefresh();

        var states = new byte[24];
        grid.CopyStates(0, states);

        byte[] expected =
        [
            1, 1, 0, 0, 0, 0,
            1, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 2,
        ];
        Assert.Equal(expected, states);
        Assert.Equal(0, (int)FogState.Unexplored);
        Assert.Equal(1, (int)FogState.Explored);
        Assert.Equal(2, (int)FogState.Visible);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.CopyStates(0, new byte[23]));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.CopyStates(2, new byte[24]));
    }

    [Fact]
    public void TheRevisionGoesUpOnlyWhenThePictureChanges()
    {
        var grid = new VisionGrid(2, 30, 30);
        var start = grid.Revision;

        grid.BeginRefresh();
        grid.EndRefresh();
        Assert.Equal(start, grid.Revision);

        grid.BeginRefresh();
        grid.Reveal(0, TestMatches.Point("5.5", "5.5"), Fixed.FromInt(2));
        grid.EndRefresh();
        Assert.Equal(start + 1, grid.Revision);

        // The same picture again, from a unit that has moved within its tile's reach of the same centers.
        grid.BeginRefresh();
        grid.Reveal(0, TestMatches.Point("5.5", "5.5"), Fixed.FromInt(2));
        grid.EndRefresh();
        Assert.Equal(start + 1, grid.Revision);

        // Another player's picture changes it too.
        grid.BeginRefresh();
        grid.Reveal(0, TestMatches.Point("5.5", "5.5"), Fixed.FromInt(2));
        grid.Reveal(1, TestMatches.Point("5.5", "5.5"), Fixed.FromInt(2));
        grid.EndRefresh();
        Assert.Equal(start + 2, grid.Revision);
    }

    [Fact]
    public void TheHashCoversBothStatesOfEveryTileOfEveryPlayer()
    {
        static ulong HashOf(VisionGrid grid)
        {
            var world = new World(1);
            world.AddStore(grid);
            return world.ComputeHash();
        }

        var empty = HashOf(new VisionGrid(2, 30, 30));
        var seen = Stamped(30, 30, "5.5", "5.5", "2");
        var seenByTheOther = Stamped(30, 30, "5.5", "5.5", "2", player: 1);
        var lastTile = Stamped(30, 30, "29.5", "29.5", "0", player: 1);

        // Explored and no longer visible is another state than visible, and than never seen.
        var explored = Stamped(30, 30, "5.5", "5.5", "2");
        explored.BeginRefresh();
        explored.EndRefresh();

        ulong[] hashes = [empty, HashOf(seen), HashOf(seenByTheOther), HashOf(lastTile), HashOf(explored)];
        Assert.Equal(hashes.Length, hashes.Distinct().Count());
        Assert.Equal(HashOf(seen), HashOf(Stamped(30, 30, "5.5", "5.5", "2")));
    }

    [Fact]
    public void ASnapshotRestoresThePictureAndRaisesTheRevision()
    {
        var source = new World(1);
        var grid = source.AddStore(Stamped(30, 21, "5.5", "5.5", "3"));
        grid.BeginRefresh();
        grid.Reveal(0, TestMatches.Point("20.5", "12.5"), Fixed.FromInt(4));
        grid.Reveal(1, TestMatches.Point("29.5", "20.5"), Fixed.FromInt(1));
        grid.EndRefresh();
        var writer = new ByteWriter();
        Snapshot.Write(source, writer);

        var target = new World(2);
        var restored = target.AddStore(Stamped(30, 21, "15.5", "15.5", "9", player: 1));
        var revision = restored.Revision;
        Snapshot.Restore(new Simulation(target), new ByteReader(writer.ToArray()));

        VisionAssert.SamePicture(grid, restored);
        Assert.Equal(source.ComputeHash(), target.ComputeHash());
        Assert.Equal(revision + 1, restored.Revision);
    }

    [Fact]
    public void ASnapshotWithATileThatIsVisibleAndNotExploredIsRefused()
    {
        var bytes = SnapshotOf(new VisionGrid(1, 10, 10));
        var words = WordsOf(bytes, 4);

        // The grid of one player on a 10 by 10 map is two words explored, then two words visible.
        words[2] = 1;
        var error = Assert.Throws<InvalidDataException>(() => RestoreInto(new VisionGrid(1, 10, 10), WithWords(bytes, words)));
        Assert.Contains("visible and not explored", error.Message, StringComparison.Ordinal);

        // With the tile explored as well, the grid has nothing against it. The snapshot is still
        // refused, later, for the hash it recorded.
        words[0] = 1;
        error = Assert.Throws<InvalidDataException>(() => RestoreInto(new VisionGrid(1, 10, 10), WithWords(bytes, words)));
        Assert.Contains("hashes to", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnapshotWithATileThatIsNotOnTheMapIsRefused()
    {
        var bytes = SnapshotOf(new VisionGrid(1, 10, 10));
        var words = WordsOf(bytes, 4);

        // 100 tiles fill the first word and 36 bits of the second. Bit 36 is the first past the map.
        words[1] = 1UL << 36;
        var error = Assert.Throws<InvalidDataException>(() => RestoreInto(new VisionGrid(1, 10, 10), WithWords(bytes, words)));
        Assert.Contains("not on the map", error.Message, StringComparison.Ordinal);

        // Bit 35 is the last tile of the map, and the grid has nothing against it.
        words[1] = 1UL << 35;
        error = Assert.Throws<InvalidDataException>(() => RestoreInto(new VisionGrid(1, 10, 10), WithWords(bytes, words)));
        Assert.Contains("hashes to", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoreIsNamedVision()
    {
        Assert.Equal("vision", VisionGrid.StoreName);
        Assert.Equal("vision", new VisionGrid(1, 1, 1).Name);
    }

    /// <summary>The desync tool names a word that differs by its player and its tiles.</summary>
    [Fact]
    public void TheWalkNamesEachWordForItsPlayerAndItsTiles()
    {
        var walker = new NamingWalker();

        new VisionGrid(2, 10, 10).Walk(walker);

        Assert.Equal(
            [
                "explored[0][0..63]", "explored[0][64..99]", "explored[1][0..63]", "explored[1][64..99]",
                "visible[0][0..63]", "visible[0][64..99]", "visible[1][0..63]", "visible[1][64..99]",
            ],
            walker.Names);

        // A map whose tiles fill its last word.
        walker.Names.Clear();
        new VisionGrid(1, 16, 8).Walk(walker);
        Assert.Equal(["explored[0][0..63]", "explored[0][64..127]", "visible[0][0..63]", "visible[0][64..127]"], walker.Names);
    }

    private sealed class NamingWalker : IStateWalker
    {
        public List<string> Names { get; } = [];

        public bool IsRestoring => false;

        public ulong Field(ulong value, string name = "")
        {
            Names.Add(name);
            return value;
        }

        public bool Field(bool value, string name = "") => throw new NotSupportedException();

        public int Field(int value, string name = "") => throw new NotSupportedException();

        public uint Field(uint value, string name = "") => throw new NotSupportedException();

        public long Field(long value, string name = "") => throw new NotSupportedException();

        public Int128 Field(Int128 value, string name = "") => throw new NotSupportedException();

        public EntityId Field(EntityId value, string name = "") => throw new NotSupportedException();

        public Fixed Field(Fixed value, string name = "") => throw new NotSupportedException();

        public Angle Field(Angle value, string name = "") => throw new NotSupportedException();

        public Fraction Field(Fraction value, string name = "") => throw new NotSupportedException();

        public int Count(int count, int minimumBytesPerItem = 1, string name = "") => throw new NotSupportedException();
    }

    private static byte[] SnapshotOf(VisionGrid grid)
    {
        var world = new World(1);
        world.AddStore(grid);
        var writer = new ByteWriter();
        Snapshot.Write(world, writer);
        return writer.ToArray();
    }

    private static void RestoreInto(VisionGrid grid, byte[] snapshot)
    {
        var world = new World(1);
        world.AddStore(grid);
        Snapshot.Restore(new Simulation(world), new ByteReader(snapshot));
    }

    /// <summary>
    /// The last <paramref name="count"/> 64-bit fields of the state in a snapshot: those of the
    /// only store of the world. After them comes the hash of the state, which is 8 bytes.
    /// </summary>
    private static ulong[] WordsOf(byte[] snapshot, int count)
    {
        var words = new ulong[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt64LittleEndian(snapshot.AsSpan(OffsetOf(snapshot, count, i), 8));
        }

        Assert.All(words, word => Assert.Equal(0UL, word));
        return words;
    }

    private static byte[] WithWords(byte[] snapshot, ulong[] words)
    {
        var changed = (byte[])snapshot.Clone();
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(changed.AsSpan(OffsetOf(changed, words.Length, i), 8), words[i]);
        }

        return changed;
    }

    private static int OffsetOf(byte[] snapshot, int count, int word) => snapshot.Length - 8 - (8 * (count - word));
}
