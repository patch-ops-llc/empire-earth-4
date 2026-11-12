using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>
/// Scenarios for the hard half of the spacing rule (design 2.4 and 8.4, rows 7 to 9): no movement
/// brings a unit closer to an enemy unit than their spacing, so units cannot walk through an
/// enemy line, enemies never push each other, and a blocked unit keeps its order.
/// </summary>
public class EnemySpacingTests
{
    private static readonly Fixed QuarterTile = Fixed.One / 4;
    private static readonly Fixed OneAndAHalf = Fixed.One * 3 / 2;

    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    /// <summary>
    /// A unit with one enemy in its way steers round it and arrives. It is never closer to the
    /// enemy than their spacing, and the enemy does not move. An enemy straight ahead is passed
    /// on the side the unit's ID picks.
    /// </summary>
    [Theory]
    [InlineData("10", "1", "1")]
    [InlineData("10.2", "1", "1")]
    [InlineData("9.7", "1", "1")]
    [InlineData("10", "1.5", "1")]
    [InlineData("10.4", "1", "1.5")]
    public void AUnitSteersRoundAnEnemyInItsWay(string enemyY, string unitSpacing, string enemySpacing)
    {
        var match = TestMatches.Create(TestMaps.Open(30, 20));
        var unit = match.SpawnUnit(0, Point("4.5", "10"), QuarterTile, Fraction.Parse(unitSpacing).ToFixed());
        var enemy = match.SpawnUnit(1, Point("15", enemyY), QuarterTile, Fraction.Parse(enemySpacing).ToFixed());
        var spacing = SpacingRule.Between(match.Movers.Get(unit), match.Movers.Get(enemy));
        var target = Point("25.5", "10");

        match.OrderMove(0, target, unit);
        var ticks = 0;
        var steered = false;
        do
        {
            match.Simulation.Tick();
            ticks++;
            Assert.InRange(ticks, 1, 200);
            Assert.True(match.ClosestSquared([unit], [enemy]) >= FixedVector2.SquareRaw(spacing), $"Too close at tick {ticks}.");
            Assert.Equal(Point("15", enemyY), match.PositionOf(enemy));
            Assert.False(match.Movers.Get(unit).Waiting);
            steered |= match.PositionOf(unit).Y != target.Y;
        }
        while (match.Movers.Get(unit).State != MoveState.Idle);

        Assert.Equal(target, match.PositionOf(unit));
        Assert.True(steered);

        // The straight walk takes 84 ticks. Round the enemy is a little longer.
        Assert.InRange(ticks, 85, 100);
    }

    /// <summary>
    /// Design 2.4: units keep 3/8 of a tile from blocked ground, so three units can close a pass
    /// 6 tiles wide to everything that walks. A group ordered through is stopped, keeps its order
    /// and stands still. It goes on when the way opens.
    /// </summary>
    [Fact]
    public void ThreeUnitsCloseAPassSixTilesWide()
    {
        var match = TestMatches.Create(PassMap());
        var units = match.SpawnBlock(0, MapGrid.TileCenter(3, 4), 3, 12, QuarterTile, Fixed.One);

        // 1.7 tiles apart, and 1.3 from the sides of the pass, which are at y = 3 and y = 9.
        EntityId[] guards =
        [
            match.SpawnUnit(1, Point("15", "4.3"), QuarterTile),
            match.SpawnUnit(1, Point("15", "6"), QuarterTile),
            match.SpawnUnit(1, Point("15", "7.7"), QuarterTile),
        ];
        var guardPositions = guards.Select(match.PositionOf).ToArray();
        var target = Point("25", "6");
        var places = match.PlacesAround(target, units);

        match.OrderMove(0, target, units);
        var watch = new EnemyPairs(match, units, guards);
        for (var tick = 0; tick < 300; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            foreach (var unit in units)
            {
                match.AssertStandsClear(unit);
            }
        }

        // Nobody is through, everybody still has the order, and the guards have not been pushed.
        Assert.All(units, unit => Assert.True(match.PositionOf(unit).X < Fixed.FromInt(15)));
        Assert.All(units, unit => Assert.True(match.Movers.Get(unit).Waiting));
        Assert.All(units, unit => Assert.Equal(MoveState.Moving, match.Movers.Get(unit).State));
        Assert.Equal(places, units.Select(unit => match.Movers.Get(unit).Destination));
        Assert.Equal(guardPositions, guards.Select(match.PositionOf));

        // Waiting units stand still.
        var waiting = units.Select(match.PositionOf).ToArray();
        match.Simulation.Run(40);
        Assert.Equal(waiting, units.Select(match.PositionOf));

        // Without the guard in the middle the other two are 3.4 tiles apart, which leaves a way.
        // The units that stand before it go on. A unit does not walk back to go round, so those
        // held beside the other two guards wait on.
        match.World.Entities.Destroy(guards[1]);
        for (var tick = 0; tick < 300; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
        }

        var through = units.Where(unit => match.Movers.Get(unit).State == MoveState.Idle).ToArray();
        Assert.InRange(through.Length, 1, units.Length);
        Assert.All(through, unit => Assert.Equal(places[Array.IndexOf(units, unit)], match.PositionOf(unit)));
        Assert.All(units.Except(through), unit => Assert.True(match.Movers.Get(unit).Waiting));

        // With the pass empty, everyone ends on its place in the group.
        match.World.Entities.Destroy(guards[0]);
        match.World.Entities.Destroy(guards[2]);
        match.RunUntilAtRest(300, units);
        Assert.Equal(places, units.Select(match.PositionOf));
    }

    /// <summary>
    /// Design 2.4: two enemy units leave a way between them only when they stand at least twice
    /// the spacing apart: 2 tiles for infantry, 3 when the blocker or the unit passing is
    /// Cavalry/Armor. The corridors here are closed beside the two enemies, so the only way is
    /// between them.
    /// </summary>
    [Theory]
    [InlineData(3, "1", "1", "1.5", "3.5", true)]
    [InlineData(3, "1", "1", "1.5", "3.499", false)]
    [InlineData(3, "1", "1", "1.501", "3.5", false)]
    [InlineData(4, "1.5", "1", "1.5", "4.5", true)]
    [InlineData(4, "1.5", "1", "1.5", "4.499", false)]
    [InlineData(4, "1", "1.5", "1.5", "4.5", true)]
    [InlineData(4, "1", "1.5", "1.501", "4.5", false)]
    [InlineData(4, "1", "1", "1.5", "4.499", true)]
    public void TwoEnemiesLeaveAWayOnlyWhenTwiceTheSpacingApart(int rows, string unitSpacing, string enemySpacing, string upperY, string lowerY, bool passes)
    {
        var match = TestMatches.Create(TestMaps.FromRows(
        [
            new string('#', 30),
            .. Enumerable.Repeat(new string('.', 30), rows),
            new string('#', 30),
        ]));
        var middle = rows == 3 ? "2.5" : "3";
        var unit = match.SpawnUnit(0, Point("4.5", middle), QuarterTile, Fraction.Parse(unitSpacing).ToFixed());
        EntityId[] enemies =
        [
            match.SpawnUnit(1, Point("15", upperY), QuarterTile, Fraction.Parse(enemySpacing).ToFixed()),
            match.SpawnUnit(1, Point("15", lowerY), QuarterTile, Fraction.Parse(enemySpacing).ToFixed()),
        ];
        var target = Point("25.5", middle);

        match.OrderMove(0, target, unit);
        var watch = new EnemyPairs(match, [unit], enemies);
        for (var tick = 0; tick < 300; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            match.AssertStandsClear(unit);
        }

        if (passes)
        {
            Assert.Equal(target, match.PositionOf(unit));
            Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
        }
        else
        {
            Assert.True(match.PositionOf(unit).X < Fixed.FromInt(15));
            Assert.True(match.Movers.Get(unit).Waiting);
            Assert.Equal((MoveState.Moving, target), (match.Movers.Get(unit).State, match.Movers.Get(unit).Destination));
        }

        Assert.Equal([Point("15", upperY), Point("15", lowerY)], enemies.Select(match.PositionOf));
    }

    /// <summary>
    /// A unit ordered onto the point an enemy stands on walks up to the spacing and waits there
    /// with its order. When the enemy walks away, it goes on and arrives.
    /// </summary>
    [Fact]
    public void ABlockedUnitKeepsItsOrderAndGoesOnWhenTheWayOpens()
    {
        var match = TestMatches.Create(TestMaps.Open(30, 20));
        var unit = match.SpawnUnit(0, Point("4.5", "10"), QuarterTile);
        var enemy = match.SpawnUnit(1, Point("15", "10"), QuarterTile);
        var target = Point("15", "10");

        match.OrderMove(0, target, unit);
        match.Simulation.Run(100);

        // The last whole step that ends no nearer than one tile: 38 steps of a quarter tile.
        Assert.Equal(Point("14", "10"), match.PositionOf(unit));
        Assert.True(match.Movers.Get(unit).Waiting);
        Assert.Equal((MoveState.Moving, target), (match.Movers.Get(unit).State, match.Movers.Get(unit).Destination));
        Assert.Equal(target, match.PositionOf(enemy));

        match.OrderMove(1, Point("15", "16"), enemy);
        match.RunUntilAtRest(200, unit, enemy);

        Assert.Equal(target, match.PositionOf(unit));
        Assert.False(match.Movers.Get(unit).Waiting);
        Assert.Equal(Point("15", "16"), match.PositionOf(enemy));
    }

    /// <summary>A new order frees a waiting unit: it is not waiting for anything on its new way.</summary>
    [Fact]
    public void ANewOrderEndsTheWait()
    {
        var match = TestMatches.Create(TestMaps.Open(30, 20));
        var unit = match.SpawnUnit(0, Point("12.5", "10"), QuarterTile);
        match.SpawnUnit(1, Point("15", "10"), QuarterTile);

        match.OrderMove(0, Point("15", "10"), unit);
        match.Simulation.Run(20);
        Assert.True(match.Movers.Get(unit).Waiting);

        match.OrderMove(0, Point("5.5", "10"), unit);
        match.Simulation.Tick();
        Assert.False(match.Movers.Get(unit).Waiting);

        match.RunUntilAtRest(100, unit);
        Assert.Equal(Point("5.5", "10"), match.PositionOf(unit));
    }

    /// <summary>
    /// Design 2.4: enemy units never push each other. Two that stand closer than their spacing,
    /// as after one was produced beside the other, stay where they are. Either may walk away at
    /// once, and neither may come nearer: one ordered to the far side of the other first leaves
    /// to the spacing and then goes round.
    /// </summary>
    [Theory]
    [InlineData("5", "10")]
    [InlineData("10", "4")]
    [InlineData("15", "10")]
    [InlineData("12", "10.5")]
    public void EnemiesCloserThanTheirSpacingStayPutAndMayOnlyMoveApart(string targetX, string targetY)
    {
        var match = TestMatches.Create(TestMaps.Open(30, 20));
        var unit = match.SpawnUnit(0, Point("10", "10"), QuarterTile);
        var enemy = match.SpawnUnit(1, Point("10.5", "10"), QuarterTile);

        match.Simulation.Run(50);
        Assert.Equal(Point("10", "10"), match.PositionOf(unit));
        Assert.Equal(Point("10.5", "10"), match.PositionOf(enemy));

        var target = Point(targetX, targetY);
        match.OrderMove(0, target, unit);
        var spacing = FixedVector2.SquareRaw(Fixed.One);
        var apart = match.ClosestSquared([unit], [enemy]);
        for (var tick = 1; tick <= 100; tick++)
        {
            match.Simulation.Tick();
            var now = match.ClosestSquared([unit], [enemy]);
            Assert.True(now >= spacing || now >= apart, $"The unit came nearer at tick {tick}.");
            Assert.False(match.Movers.Get(unit).Waiting);
            apart = now;
        }

        Assert.Equal(target, match.PositionOf(unit));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
        Assert.Equal(Point("10.5", "10"), match.PositionOf(enemy));
    }

    /// <summary>
    /// A unit closer to an enemy than their spacing that is ordered to a point nearer still does
    /// not take one step, and keeps the order.
    /// </summary>
    [Fact]
    public void AUnitInsideTheSpacingDoesNotStepNearer()
    {
        var match = TestMatches.Create(TestMaps.Open(30, 20));
        var unit = match.SpawnUnit(0, Point("10", "10"), QuarterTile);
        match.SpawnUnit(1, Point("10.5", "10"), QuarterTile);

        match.OrderMove(0, Point("10.25", "10"), unit);
        match.Simulation.Run(50);

        Assert.Equal(Point("10", "10"), match.PositionOf(unit));
        Assert.True(match.Movers.Get(unit).Waiting);
        Assert.Equal(MoveState.Moving, match.Movers.Get(unit).State);
    }

    /// <summary>
    /// Stepping apart is movement too: units that part from their own side do not come nearer an
    /// enemy than their spacing, and the enemy is not moved. A pile beside an enemy unit spreads
    /// away from it.
    /// </summary>
    [Fact]
    public void UnitsSteppingApartDoNotComeNearerAnEnemy()
    {
        var match = TestMatches.Create(TestMaps.Open(30, 20));
        var pile = Enumerable.Range(0, 16).Select(_ => match.SpawnUnit(0, Point("14", "10"), QuarterTile)).ToArray();
        EntityId[] enemies =
        [
            match.SpawnUnit(1, Point("15", "10"), QuarterTile),
            match.SpawnUnit(1, Point("15", "8.5"), QuarterTile, OneAndAHalf),
            match.SpawnUnit(1, Point("15", "11.5"), QuarterTile, OneAndAHalf),
        ];
        var enemyPositions = enemies.Select(match.PositionOf).ToArray();

        var watch = new EnemyPairs(match, pile, enemies);
        for (var tick = 0; tick < 400; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
        }

        match.RunUntilAtRest(400, pile);
        match.AssertKeepTheirSpacing(pile);
        Assert.Equal(enemyPositions, enemies.Select(match.PositionOf));
        foreach (var enemy in enemies)
        {
            var spacing = match.Movers.Get(enemy).Spacing;
            Assert.True(match.ClosestSquared(pile, [enemy]) >= FixedVector2.SquareRaw(spacing));
        }
    }

    /// <summary>
    /// The design's check for the end of any tick: every pair of enemy units is at least its
    /// spacing apart, or no closer than it was at the start of the tick. Here two players' units,
    /// of both spacings, are sent across each other on maps with obstacles for a minute and a
    /// half. No unit ever stands on or too close to blocked ground either.
    /// </summary>
    [Theory]
    [InlineData(1UL, 0)]
    [InlineData(2UL, 1)]
    [InlineData(3UL, 2)]
    [InlineData(4UL, 3)]
    public void AtTheEndOfEveryTickNoEnemyPairHasComeTooClose(ulong seed, int blockedInTen)
    {
        var random = new DeterministicRandom(seed);
        const int Size = 24;
        var match = TestMatches.Create(MapGridReachTests.RandomMap(random, Size, Size, blockedInTen));
        var first = SpawnOnRandomTiles(match, random, 0, 20);
        var second = SpawnOnRandomTiles(match, random, 1, 20);
        var watch = new EnemyPairs(match, first, second);
        var waited = 0;

        for (var tick = 0; tick < 1800; tick++)
        {
            if (tick % 7 == 0)
            {
                // Orders to groups of one side, small and large, to anywhere on the map.
                var player = random.NextInt(2);
                var units = player == 0 ? first : second;
                var from = random.NextInt(units.Length);
                var count = 1 + random.NextInt(tick % 5 == 0 ? units.Length : 5);
                var target = new FixedVector2(
                    Fixed.FromRaw(random.NextInt(Size * Fixed.RawOne)),
                    Fixed.FromRaw(random.NextInt(Size * Fixed.RawOne)));
                match.OrderMove(player, target, [.. Enumerable.Range(from, count).Select(i => units[i % units.Length])]);
            }

            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            foreach (var unit in first.Concat(second))
            {
                match.AssertStandsClear(unit);
                waited += match.Movers.Get(unit).Waiting ? 1 : 0;
            }
        }

        // The two sides did get in each other's way.
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.InRange(waited, 100, int.MaxValue);
    }

    /// <summary>A field 30 by 12 with a wall two tiles thick at x = 14 and 15, open in rows 3 to 8.</summary>
    private static Sim.Data.MapDef PassMap()
    {
        var rows = new string[12];
        for (var y = 0; y < rows.Length; y++)
        {
            rows[y] = y is >= 3 and <= 8 ? new string('.', 30) : new string('.', 14) + "##" + new string('.', 14);
        }

        return TestMaps.FromRows(rows);
    }

    private static EntityId[] SpawnOnRandomTiles(Match match, DeterministicRandom random, int player, int count)
    {
        var units = new List<EntityId>();
        while (units.Count < count)
        {
            var (x, y) = (random.NextInt(match.Grid.Width), random.NextInt(match.Grid.Height));
            if (match.Grid.IsPassable(x, y))
            {
                var spacing = units.Count % 4 == 0 ? OneAndAHalf : Fixed.One;
                units.Add(match.SpawnUnit(player, MapGrid.TileCenter(x, y), Fixed.FromRaw(random.NextInt(3000, 30_000)), spacing));
            }
        }

        return [.. units];
    }

    /// <summary>
    /// The end-of-tick check of design 2.4 for the units of two sides: call
    /// <see cref="AssertNoPairCameTooClose"/> after every tick.
    /// </summary>
    private sealed class EnemyPairs
    {
        private readonly Match _match;
        private readonly EntityId[] _first;
        private readonly EntityId[] _second;
        private readonly long[] _before;

        public EnemyPairs(Match match, EntityId[] first, EntityId[] second)
        {
            _match = match;
            _first = first;
            _second = second;
            _before = new long[first.Length * second.Length];
            Measure((_, _, pair, distance) => _before[pair] = distance);
        }

        public void AssertNoPairCameTooClose()
        {
            Measure((one, other, pair, distance) =>
            {
                var spacing = SpacingRule.Between(_match.Movers.Get(one), _match.Movers.Get(other));
                Assert.True(
                    distance >= FixedVector2.SquareRaw(spacing) || distance >= _before[pair],
                    $"Tick {_match.World.Tick}: enemies {one} at {_match.PositionOf(one)} and {other} at {_match.PositionOf(other)} came closer than {spacing}.");
                _before[pair] = distance;
            });
        }

        private void Measure(Action<EntityId, EntityId, int, long> each)
        {
            for (var i = 0; i < _first.Length; i++)
            {
                for (var j = 0; j < _second.Length; j++)
                {
                    if (_match.Bodies.Contains(_first[i]) && _match.Bodies.Contains(_second[j]))
                    {
                        each(_first[i], _second[j], (i * _second.Length) + j, FixedVector2.DistanceSquaredRaw(_match.PositionOf(_first[i]), _match.PositionOf(_second[j])));
                    }
                }
            }
        }
    }
}
