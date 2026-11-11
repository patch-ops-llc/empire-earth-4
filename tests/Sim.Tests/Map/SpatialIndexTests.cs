using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Setup;

namespace Sim.Tests.Map;

/// <summary>The index that answers "which entities are near here".</summary>
public class SpatialIndexTests
{
    private static readonly Fixed QuarterTile = Fixed.One / 4;

    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    [Fact]
    public void ACircleFindsTheEntitiesWithinItsRadiusInIdOrder()
    {
        var match = TestMatches.Create(TestMaps.Open(20, 20));
        var far = match.SpawnUnit(0, Point("18.5", "18.5"), QuarterTile);
        var east = match.SpawnUnit(1, Point("12.5", "10.5"), QuarterTile);
        var center = match.SpawnUnit(0, Point("10.5", "10.5"), QuarterTile);
        var justOutside = match.SpawnUnit(0, new FixedVector2(Point("7.5", "0").X - Fixed.Epsilon, Point("0", "10.5").Y), QuarterTile);
        var onTheEdge = match.SpawnUnit(1, Point("10.5", "7.5"), QuarterTile);
        match.RefreshDerivedState();
        var found = new List<EntityId> { far };

        match.SpatialIndex.QueryCircle(Point("10.5", "10.5"), Fixed.FromInt(3), found);

        // The edge of the circle counts, and the list that was passed in is emptied first.
        Assert.Equal([east, center, onTheEdge], found);
        Assert.Equal(5, match.SpatialIndex.Count);

        match.SpatialIndex.QueryCircle(Point("10.5", "10.5"), Fixed.Zero, found);
        Assert.Equal([center], found);

        match.SpatialIndex.QueryCircle(Point("3.5", "3.5"), Fixed.One, found);
        Assert.Empty(found);

        match.SpatialIndex.QueryCircle(Point("10.5", "10.5"), Fixed.FromInt(100), found);
        Assert.Equal([far, east, center, justOutside, onTheEdge], found);

        Assert.Throws<ArgumentOutOfRangeException>(() => match.SpatialIndex.QueryCircle(Point("1", "1"), -Fixed.Epsilon, found));
        Assert.Throws<ArgumentNullException>(() => match.SpatialIndex.QueryCircle(Point("1", "1"), Fixed.One, null!));
    }

    [Fact]
    public void ABoxFindsTheEntitiesInsideItEdgesIncluded()
    {
        var match = TestMatches.Create(TestMaps.Open(20, 20));
        var inside = match.SpawnUnit(0, Point("5.5", "5.5"), QuarterTile);
        var onTheCorner = match.SpawnUnit(0, Point("8", "8"), QuarterTile);
        var outside = match.SpawnUnit(0, Point("8.01", "8"), QuarterTile);
        var onTheLowEdge = match.SpawnUnit(0, Point("4", "6.5"), QuarterTile);
        match.RefreshDerivedState();
        var found = new List<EntityId>();

        match.SpatialIndex.QueryBox(Point("4", "4"), Point("8", "8"), found);
        Assert.Equal([inside, onTheCorner, onTheLowEdge], found);

        // A box may reach beyond the map, and one whose corners are the wrong way round is empty.
        match.SpatialIndex.QueryBox(Point("-50", "-50"), Point("50", "50"), found);
        Assert.Equal([inside, onTheCorner, outside, onTheLowEdge], found);
        match.SpatialIndex.QueryBox(Point("8", "8"), Point("4", "4"), found);
        Assert.Empty(found);

        // A box that is a single point.
        match.SpatialIndex.QueryBox(Point("8", "8"), Point("8", "8"), found);
        Assert.Equal([onTheCorner], found);
    }

    /// <summary>
    /// The index holds the positions as they were when it was last rebuilt, which the tick does
    /// right after movement. An entity destroyed since is left out at once.
    /// </summary>
    [Fact]
    public void TheIndexFollowsTheWorldTickByTick()
    {
        var match = TestMatches.Create(TestMaps.Open(20, 20));
        var walker = match.SpawnUnit(0, Point("2.5", "2.5"), Fixed.One);
        var stander = match.SpawnUnit(0, Point("3.5", "2.5"), Fixed.One);
        var found = new List<EntityId>();

        // Placed from outside a tick: seen once the index is refreshed or a tick has run.
        match.SpatialIndex.QueryCircle(Point("2.5", "2.5"), Fixed.FromInt(2), found);
        Assert.Empty(found);
        match.RefreshDerivedState();
        match.SpatialIndex.QueryCircle(Point("2.5", "2.5"), Fixed.FromInt(2), found);
        Assert.Equal([walker, stander], found);

        match.OrderMove(0, Point("12.5", "2.5"), walker);
        match.Simulation.Run(6);
        match.SpatialIndex.QueryCircle(Point("2.5", "2.5"), Fixed.FromInt(2), found);
        Assert.Equal([stander], found);
        match.SpatialIndex.QueryCircle(Point("8.5", "2.5"), Fixed.Half, found);
        Assert.Equal([walker], found);

        match.World.Entities.Destroy(stander);
        match.SpatialIndex.QueryCircle(Point("2.5", "2.5"), Fixed.FromInt(2), found);
        Assert.Empty(found);
        Assert.Equal(2, match.SpatialIndex.Count);

        match.Simulation.Tick();
        Assert.Equal(1, match.SpatialIndex.Count);
        match.SpatialIndex.QueryBox(Point("0", "0"), Point("20", "20"), found);
        Assert.Equal([walker], found);
    }

    [Fact]
    public void AMatchStartsWithItsStartingWorkersInTheIndex()
    {
        var match = MatchSetup.Create(TestMatches.Data, TestMatches.Data.Maps[0], 1, [new(0), new(1)]);
        var found = new List<EntityId>();

        match.SpatialIndex.QueryBox(Point("0", "0"), Point("1000", "1000"), found);

        Assert.Equal(match.Bodies.Count, match.SpatialIndex.Count);
        Assert.Equal(Enumerable.Range(0, match.Bodies.Count).Select(match.Bodies.EntityAt), found);
        Assert.NotEmpty(found);
    }

    /// <summary>
    /// Checked against a walk over every body, on maps whose sides are not whole squares of the
    /// index, with many entities sharing a point and others on the very edges of the map.
    /// </summary>
    [Theory]
    [InlineData(1UL, 37, 23)]
    [InlineData(2UL, 3, 50)]
    [InlineData(3UL, 64, 64)]
    public void QueriesAgreeWithAWalkOverEveryBody(ulong seed, int width, int height)
    {
        var random = new DeterministicRandom(seed);
        var match = TestMatches.Create(TestMaps.Open(width, height));
        FixedVector2 RandomPoint() => random.NextInt(8) switch
        {
            0 => new FixedVector2(Fixed.Zero, Fixed.FromRaw(random.NextInt(height * Fixed.RawOne))),
            1 => new FixedVector2(Fixed.FromInt(width) - Fixed.Epsilon, Fixed.FromRaw(random.NextInt(height * Fixed.RawOne))),
            2 => MapGrid.TileCenter(random.NextInt(width), random.NextInt(height)),
            3 => FixedVector2.FromInts(random.NextInt(width), random.NextInt(height)),
            _ => new FixedVector2(Fixed.FromRaw(random.NextInt(width * Fixed.RawOne)), Fixed.FromRaw(random.NextInt(height * Fixed.RawOne))),
        };

        var units = new List<EntityId>();
        for (var i = 0; i < 300; i++)
        {
            units.Add(match.SpawnUnit(i % 2, RandomPoint(), QuarterTile));
        }

        // Some are destroyed before the index is built and some after.
        for (var i = 0; i < 20; i++)
        {
            match.World.Entities.Destroy(units[random.NextInt(units.Count)]);
        }

        match.RefreshDerivedState();
        for (var i = 0; i < 20; i++)
        {
            match.World.Entities.Destroy(units[random.NextInt(units.Count)]);
        }

        var found = new List<EntityId>();
        for (var i = 0; i < 400; i++)
        {
            var center = RandomPoint();
            var radius = Fixed.FromRaw(random.NextInt(12 * Fixed.RawOne));
            match.SpatialIndex.QueryCircle(center, radius, found);
            Assert.Equal(
                units.Where(unit => match.World.Entities.IsAlive(unit)
                    && FixedVector2.DistanceSquaredRaw(match.PositionOf(unit), center) <= FixedVector2.SquareRaw(radius)),
                found);

            var corner = RandomPoint();
            var min = new FixedVector2(Fixed.Min(center.X, corner.X), Fixed.Min(center.Y, corner.Y));
            var max = new FixedVector2(Fixed.Max(center.X, corner.X), Fixed.Max(center.Y, corner.Y));
            match.SpatialIndex.QueryBox(min, max, found);
            Assert.Equal(
                units.Where(unit =>
                {
                    var position = match.PositionOf(unit);
                    return match.World.Entities.IsAlive(unit)
                        && position.X >= min.X && position.X <= max.X && position.Y >= min.Y && position.Y <= max.Y;
                }),
                found);
        }
    }

    [Fact]
    public void RebuildingFromTheSameBodiesGivesTheSameAnswers()
    {
        var random = new DeterministicRandom(4);
        var match = TestMatches.Create(TestMaps.Open(30, 30));
        for (var i = 0; i < 100; i++)
        {
            match.SpawnUnit(0, MapGrid.TileCenter(random.NextInt(30), random.NextInt(30)), QuarterTile);
        }

        // A second index over the same bodies, built once, against the match's own after many rebuilds.
        match.Simulation.Run(5);
        var other = new SpatialIndex(match.Grid, match.World.Entities, match.Bodies);
        other.Rebuild();

        var found = new List<EntityId>();
        var expected = new List<EntityId>();
        for (var i = 0; i < 100; i++)
        {
            var center = MapGrid.TileCenter(random.NextInt(30), random.NextInt(30));
            match.SpatialIndex.QueryCircle(center, Fixed.FromInt(5), found);
            other.QueryCircle(center, Fixed.FromInt(5), expected);
            Assert.Equal(expected, found);
        }
    }
}
