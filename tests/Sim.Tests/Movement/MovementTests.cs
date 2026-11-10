using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>How a unit with a move order walks.</summary>
public class MovementTests
{
    private static readonly Fixed QuarterTile = Fixed.One / 4;

    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    [Fact]
    public void AUnitWalksAtItsSpeedAndArrivesExactly()
    {
        var match = TestMatches.Create(TestMaps.Open(16, 6));
        var unit = match.SpawnUnit(0, Point("2.5", "2.5"), QuarterTile);

        // 8 tiles at a quarter of a tile per tick is 32 ticks, and the first step is taken in the
        // tick the order runs.
        match.OrderMove(0, Point("10.5", "2.5"), unit);
        match.Simulation.Tick();
        Assert.Equal(Point("2.75", "2.5"), match.PositionOf(unit));

        match.Simulation.Run(30);
        Assert.Equal(Point("10.25", "2.5"), match.PositionOf(unit));
        Assert.Equal(MoveState.Moving, match.Movers.Get(unit).State);

        match.Simulation.Tick();
        Assert.Equal(Point("10.5", "2.5"), match.PositionOf(unit));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);

        match.Simulation.Run(10);
        Assert.Equal(Point("10.5", "2.5"), match.PositionOf(unit));
    }

    /// <summary>
    /// The Guard's 2.6 tiles per second is 8,520 units per tick (design 2.3 rounds it half up from
    /// 8,519.68), so 13 tiles take 100 ticks and 5 seconds.
    /// </summary>
    [Fact]
    public void ASpeedInTilesPerSecondBecomesADistancePerTickRoundedOnce()
    {
        var speed = Mover.SpeedPerTick(Fraction.Parse("2.6"));
        Assert.Equal(8520, speed.Raw);
        Assert.Equal(9830, Mover.SpeedPerTick(3).Raw);

        var match = TestMatches.Create(TestMaps.Open(16, 6));
        var unit = match.SpawnUnit(0, Point("1.5", "2.5"), speed);

        match.OrderMove(0, Point("14.5", "2.5"), unit);
        match.Simulation.Run(99);
        Assert.Equal(MoveState.Moving, match.Movers.Get(unit).State);

        match.Simulation.Tick();
        Assert.Equal(Point("14.5", "2.5"), match.PositionOf(unit));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
    }

    [Fact]
    public void AUnitWalksInAStraightLineAtAnyAngle()
    {
        var match = TestMatches.Create(TestMaps.Open(24, 12));
        var start = Point("1.5", "1.5");
        var target = Point("20.25", "9.75");
        var speed = Mover.SpeedPerTick(3);
        var unit = match.SpawnUnit(0, start, speed);
        var whole = target - start;

        match.OrderMove(0, target, unit);
        var ticks = 0;
        var previous = start;
        while (match.Movers.Get(unit).State == MoveState.Moving || ticks == 0)
        {
            match.Simulation.Tick();
            ticks++;
            var position = match.PositionOf(unit);

            // The distance from the straight line, in units, is the cross product over the length.
            // Rounding each step to the unit lets it drift a little: under 1/1,024 of a tile.
            Assert.True(Math.Abs(FixedVector2.CrossRaw(whole, position - start)) < 64L * whole.Length().Raw);
            Assert.True(FixedVector2.DistanceSquaredRaw(position, previous) <= FixedVector2.SquareRaw(speed + Fixed.Epsilon));
            previous = position;
        }

        Assert.Equal(target, match.PositionOf(unit));
        Assert.Equal((whole.Length().Raw / speed.Raw) + 1, ticks);
    }

    [Fact]
    public void AUnitFacesTheWayItWalks()
    {
        var match = TestMatches.Create(TestMaps.Open(12, 12));
        var unit = match.SpawnUnit(0, Point("6.5", "6.5"), QuarterTile);
        Assert.Equal(Angle.Zero, match.Movers.Get(unit).Facing);

        // y grows downward, so a quarter turn faces the bottom of the map.
        (string X, string Y, int Units)[] legs =
        [
            ("9.5", "6.5", 0),
            ("9.5", "9.5", 1024),
            ("6.5", "9.5", 2048),
            ("6.5", "6.5", 3072),
            ("9.5", "9.5", 512),
            ("8.5", "8.5", 2560),
        ];

        foreach (var (x, y, units) in legs)
        {
            match.OrderMove(0, Point(x, y), unit);
            match.Simulation.Run(20);

            Assert.Equal(Point(x, y), match.PositionOf(unit));
            Assert.Equal(units, match.Movers.Get(unit).Facing.Units);
        }
    }

    [Fact]
    public void ANewOrderReplacesTheOldOne()
    {
        var match = TestMatches.Create(TestMaps.Open(16, 6));
        var unit = match.SpawnUnit(0, Point("2.5", "2.5"), QuarterTile);

        match.OrderMove(0, Point("10.5", "2.5"), unit);
        match.Simulation.Run(8);
        Assert.Equal(Point("4.5", "2.5"), match.PositionOf(unit));

        match.OrderMove(0, Point("4.5", "4.5"), unit);
        match.Simulation.Run(8);

        Assert.Equal(Point("4.5", "4.5"), match.PositionOf(unit));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
    }

    [Fact]
    public void AnOrderToWhereTheUnitStandsEndsAtOnce()
    {
        var match = TestMatches.Create(TestMaps.Open(16, 6));
        var unit = match.SpawnUnit(0, Point("2.5", "2.5"), QuarterTile);

        match.OrderMove(0, Point("2.5", "2.5"), unit);
        match.Simulation.Tick();

        Assert.Equal(Point("2.5", "2.5"), match.PositionOf(unit));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>There is no route finding yet: an obstacle in the way ends the order.</summary>
    [Fact]
    public void AUnitStopsBeforeGroundItCannotStandOn()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "........",
            "....#...",
            "....F...",
            "........"));
        var blockedByCliff = match.SpawnUnit(0, Point("1.5", "1.5"), QuarterTile);
        var blockedByForest = match.SpawnUnit(0, Point("1.5", "2.5"), QuarterTile);
        var free = match.SpawnUnit(0, Point("1.5", "3.5"), QuarterTile);

        match.OrderMove(0, Point("6.5", "1.5"), blockedByCliff);
        match.OrderMove(0, Point("6.5", "2.5"), blockedByForest);
        match.OrderMove(0, Point("6.5", "3.5"), free);
        match.Simulation.Run(40);

        // The last step that stayed on open ground, a quarter of a tile short of the obstacle.
        Assert.Equal(Point("3.75", "1.5"), match.PositionOf(blockedByCliff));
        Assert.Equal(Point("3.75", "2.5"), match.PositionOf(blockedByForest));
        Assert.Equal(Point("6.5", "3.5"), match.PositionOf(free));
        Assert.All(
            (EntityId[])[blockedByCliff, blockedByForest, free],
            unit => Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State));
    }

    [Fact]
    public void AUnitNeverLeavesOpenGround()
    {
        var map = TestMaps.FromRows(
            "..........",
            "..FF..##..",
            "..FF..##..",
            "..........",
            "....##....",
            "..........");
        var match = TestMatches.Create(map);
        var unit = match.SpawnUnit(0, Point("0.5", "0.5"), Mover.SpeedPerTick(Fraction.Parse("4.5")));
        var random = new DeterministicRandom(11);

        for (var order = 0; order < 200; order++)
        {
            var target = new FixedVector2(
                Fixed.FromRaw(random.NextInt(10 * Fixed.RawOne)),
                Fixed.FromRaw(random.NextInt(6 * Fixed.RawOne)));
            match.OrderMove(0, target, unit);
            for (var tick = 0; tick < 15; tick++)
            {
                match.Simulation.Tick();
                Assert.True(match.Grid.IsPassable(match.PositionOf(unit)));
            }
        }
    }

    [Fact]
    public void AMoverWithoutSpeedStaysWhereItIs()
    {
        var match = TestMatches.Create(TestMaps.Open(16, 6));
        var unit = match.SpawnUnit(0, Point("2.5", "2.5"), Fixed.Zero);

        match.OrderMove(0, Point("10.5", "2.5"), unit);
        match.Simulation.Run(20);

        Assert.Equal(Point("2.5", "2.5"), match.PositionOf(unit));
        Assert.Equal(MoveState.Moving, match.Movers.Get(unit).State);
    }

    [Fact]
    public void ADestroyedUnitStopsAndLeavesTheStores()
    {
        var match = TestMatches.Create(TestMaps.Open(16, 6));
        var unit = match.SpawnUnit(0, Point("2.5", "2.5"), QuarterTile);
        var other = match.SpawnUnit(0, Point("2.5", "4.5"), QuarterTile);

        match.OrderMove(0, Point("10.5", "2.5"), unit, other);
        match.Simulation.Run(4);
        match.World.Entities.Destroy(unit);
        match.Simulation.Tick();

        Assert.False(match.Bodies.Contains(unit));
        Assert.False(match.Movers.Contains(unit));
        Assert.Equal(1, match.Bodies.Count);

        match.Simulation.Run(60);
        Assert.Equal(Point("10.5", "2.5"), match.PositionOf(other));
    }

    /// <summary>
    /// Design 5.2 and 5.3: both players start alike on a mirrored map. Movement keeps it so: mirrored
    /// units given mirrored orders stay mirror images of each other on every tick.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MirroredUnitsWithMirroredOrdersStayMirrored(bool mirrorX)
    {
        const int Size = 40;
        var match = TestMatches.Create(TestMaps.Open(Size, Size));
        var random = new DeterministicRandom(7);
        FixedVector2 Mirror(FixedVector2 point) => mirrorX
            ? new FixedVector2(Fixed.FromInt(Size) - point.X, point.Y)
            : new FixedVector2(point.X, Fixed.FromInt(Size) - point.Y);
        FixedVector2 RandomPoint() => new(
            Fixed.FromRaw(random.NextInt(1, (Size / 2) * Fixed.RawOne)),
            Fixed.FromRaw(random.NextInt(1, (Size / 2) * Fixed.RawOne)));

        var pairs = new List<(EntityId Left, EntityId Right)>();
        for (var i = 0; i < 12; i++)
        {
            var position = RandomPoint();
            var speed = Fixed.FromRaw(random.NextInt(3000, 30_000));
            pairs.Add((match.SpawnUnit(0, position, speed), match.SpawnUnit(1, Mirror(position), speed)));
        }

        for (var tick = 0; tick < 300; tick++)
        {
            if (tick % 25 == 0)
            {
                foreach (var (left, right) in pairs)
                {
                    var target = RandomPoint();
                    match.OrderMove(0, target, left);
                    match.OrderMove(1, Mirror(target), right);
                }
            }

            match.Simulation.Tick();
            foreach (var (left, right) in pairs)
            {
                Assert.Equal(Mirror(match.PositionOf(left)), match.PositionOf(right));
                var facing = match.Movers.Get(left).Facing;
                Assert.Equal(mirrorX ? Angle.HalfTurn - facing : -facing, match.Movers.Get(right).Facing);
            }
        }
    }

    [Fact]
    public void TheSameOrdersGiveTheSameHashOnEveryTick()
    {
        ulong[] Play(int orderShift)
        {
            var match = TestMatches.Create(TestMaps.Open(40, 40), seed: 99);
            var random = new DeterministicRandom(3);
            var units = new List<EntityId>();
            for (var i = 0; i < 30; i++)
            {
                var position = MapGrid.TileCenter(random.NextInt(40), random.NextInt(40));
                units.Add(match.SpawnUnit(i % 2, position, Fixed.FromRaw(random.NextInt(3000, 30_000))));
            }

            var hashes = new ulong[200];
            for (var tick = 0; tick < hashes.Length; tick++)
            {
                if (tick % 10 == 0)
                {
                    var target = MapGrid.TileCenter(random.NextInt(40), (random.NextInt(40) + orderShift) % 40);
                    var player = tick / 10 % 2;
                    match.OrderMove(player, target, [.. units.Where((_, index) => index % 3 != 0)]);
                }

                match.Simulation.Tick();
                hashes[tick] = match.World.ComputeHash();
            }

            return hashes;
        }

        var first = Play(0);

        Assert.Equal(first, Play(0));
        Assert.Equal(first.Length, first.Distinct().Count());
        Assert.NotEqual(first[^1], Play(1)[^1]);
    }
}
