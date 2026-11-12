using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>
/// Scenarios for a group of one player's units: where one order sends each of them (design 5.3),
/// and how units of one side step apart when they stand too close (design 2.4).
/// </summary>
public class GroupMovementTests
{
    private static readonly Fixed QuarterTile = Fixed.One / 4;
    private static readonly Fixed OneAndAHalf = Fixed.One * 3 / 2;

    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    /// <summary>
    /// Design 5.3: each unit walks to the ordered point plus its own offset from the middle of the
    /// group, at its own speed. The middle is the center of the box that holds the units, not
    /// their average: three of these four stand in one corner.
    /// </summary>
    [Fact]
    public void AGroupKeepsItsShape()
    {
        var match = TestMatches.Create(TestMaps.Open(40, 30));
        EntityId[] units =
        [
            match.SpawnUnit(0, Point("3.5", "4.5"), QuarterTile),
            match.SpawnUnit(0, Point("4.75", "4.5"), QuarterTile / 2),
            match.SpawnUnit(0, Point("3.5", "6"), QuarterTile * 2),
            match.SpawnUnit(0, Point("11.5", "12.5"), QuarterTile),
        ];
        var target = Point("27.25", "15.75");

        // The box runs from (3.5, 4.5) to (11.5, 12.5), so its center is (7.5, 8.5).
        FixedVector2[] places = [Point("23.25", "11.75"), Point("24.5", "11.75"), Point("23.25", "13.25"), Point("31.25", "19.75")];
        Assert.Equal(places, match.PlacesAround(target, units));

        match.OrderMove(0, target, units);
        match.Simulation.Tick();
        Assert.Equal(places, units.Select(unit => match.Movers.Get(unit).Destination));

        // Each at its own speed: the fast one is there first and the slow one last.
        var arrived = new int[units.Length];
        for (var tick = 2; arrived.Contains(0); tick++)
        {
            match.Simulation.Tick();
            for (var i = 0; i < units.Length; i++)
            {
                if (arrived[i] == 0 && match.Movers.Get(units[i]).State == MoveState.Idle)
                {
                    arrived[i] = tick;
                }
            }

            Assert.InRange(tick, 2, 400);
        }

        Assert.Equal(places, units.Select(match.PositionOf));
        Assert.True(arrived[2] < arrived[0] && arrived[0] < arrived[1], string.Join(", ", arrived));
        Assert.Equal(0, match.Routes.SearchCount);
    }

    /// <summary>
    /// Design 5.3: a group whose box is more than 16 tiles long or wide has no shape to keep. All
    /// its units go to the ordered point and settle around it. At exactly 16 tiles it keeps its shape.
    /// </summary>
    [Theory]
    [InlineData("16", false, true)]
    [InlineData("16", true, true)]
    [InlineData("16.001", false, false)]
    [InlineData("16.001", true, false)]
    [InlineData("30", false, false)]
    public void AGroupLongerOrWiderThanSixteenTilesGoesToThePoint(string length, bool tall, bool keepsShape)
    {
        var match = TestMatches.Create(TestMaps.Open(48, 48));
        var far = Fraction.Parse(length).ToFixed();
        var corner = Point("4.5", "4.5");
        EntityId[] units =
        [
            match.SpawnUnit(0, corner, QuarterTile),
            match.SpawnUnit(0, corner + new FixedVector2(Fixed.One, Fixed.One), QuarterTile),
            match.SpawnUnit(0, corner + (tall ? new FixedVector2(Fixed.One * 2, far) : new FixedVector2(far, Fixed.One * 2)), QuarterTile),
        ];
        var target = Point("24.5", "24.5");
        var places = match.PlacesAround(target, units);

        match.OrderMove(0, target, units);
        match.Simulation.Tick();

        Assert.Equal(
            keepsShape ? places : [target, target, target],
            units.Select(unit => match.Movers.Get(unit).Destination));

        match.RunUntilAtRest(400, units);
        match.AssertKeepTheirSpacing(units);
        if (keepsShape)
        {
            Assert.Equal(places, units.Select(match.PositionOf));
        }
        else
        {
            // Around the point: none further from it than three units need to stand apart.
            Assert.All(units, unit => Assert.InRange(FixedVector2.Distance(target, match.PositionOf(unit)).Raw, 0, 2 * Fixed.RawOne));
        }
    }

    /// <summary>
    /// The plan's test for M12: a group that has stopped has no two units closer than their
    /// spacing. These groups arrive in the worst way, every unit on the same point.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(7, false)]
    [InlineData(40, false)]
    [InlineData(40, true)]
    [InlineData(150, true)]
    public void AGroupThatHasStoppedHasNoTwoUnitsCloserThanTheirSpacing(int count, bool mixedLines)
    {
        var match = TestMatches.Create(TestMaps.Open(60, 60));
        var units = new EntityId[count];
        for (var i = 0; i < count; i++)
        {
            // From all over the map, so the group is too long to keep a shape. Every third unit
            // is Cavalry/Armor when the lines are mixed.
            var spacing = mixedLines && i % 3 == 0 ? OneAndAHalf : Fixed.One;
            units[i] = match.SpawnUnit(0, MapGrid.TileCenter(5 + ((i * 23) % 50), 5 + ((i * 11) % 50)), QuarterTile + (QuarterTile * (i % 3) / 4), spacing);
        }

        var target = Point("30.25", "29.5");
        match.OrderMove(0, target, units);
        var ticks = match.RunUntilAtRest(3000, units);

        match.AssertKeepTheirSpacing(units);
        Assert.All(units, unit => Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State));

        // Around the point and no further than it takes: a disc that holds the group at its
        // spacing has a radius of about half the square root of the unit count, times the spacing.
        var widest = mixedLines ? 3 : 2;
        var radius = (((long)Math.Sqrt(count) * widest / 2) + 2) * Fixed.RawOne;
        Assert.All(units, unit => Assert.InRange(FixedVector2.Distance(target, match.PositionOf(unit)).Raw, 0, radius));

        // And they stay there.
        var positions = units.Select(match.PositionOf).ToArray();
        match.Simulation.Run(50);
        Assert.Equal(positions, units.Select(match.PositionOf));
        Assert.InRange(ticks, 1, 3000);
    }

    /// <summary>
    /// The plan's test for M12: a block that starts in a square grid at its spacing, as the
    /// acceptance battles do (design 5.1), stays where it is until ordered. Infantry stand a tile
    /// apart and Cavalry/Armor a tile and a half; a mixed block stands at the larger value.
    /// </summary>
    [Theory]
    [InlineData("1", "1", "1")]
    [InlineData("1.5", "1.5", "1.5")]
    [InlineData("1", "1.5", "1.5")]
    public void ABlockInASquareGridAtItsSpacingStaysWhereItIsUntilOrdered(string firstSpacing, string secondSpacing, string apart)
    {
        var match = TestMatches.Create(TestMaps.Open(64, 40));
        var step = Fraction.Parse(apart).ToFixed();
        var units = new EntityId[60];
        for (var i = 0; i < units.Length; i++)
        {
            // 10 wide and 6 deep, not on tile centers, the two lines in alternate rows.
            var position = Point("7.3", "9.85") + new FixedVector2(step * (i % 10), step * (i / 10));
            var spacing = Fraction.Parse((i / 10) % 2 == 0 ? firstSpacing : secondSpacing).ToFixed();
            units[i] = match.SpawnUnit(0, position, QuarterTile, spacing);
        }

        // An enemy block 15 tiles away, as in the acceptance battles.
        var enemies = match.SpawnBlock(1, Point("37.3", "9.85"), 10, 60, QuarterTile, Fixed.One);
        match.RefreshDerivedState();
        var before = units.Concat(enemies).Select(match.PositionOf).ToArray();

        match.Simulation.Run(200);

        Assert.Equal(before, units.Concat(enemies).Select(match.PositionOf));
        Assert.All(units, unit => Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State));

        // Ordered, it goes, in the same grid: a group this size keeps its shape.
        var target = Point("14.5", "30.5");
        var places = match.PlacesAround(target, units);
        match.OrderMove(0, target, units);
        match.RunUntilAtRest(400, units);

        Assert.Equal(places, units.Select(match.PositionOf));
        Assert.Equal(before.Skip(units.Length), enemies.Select(match.PositionOf));
    }

    /// <summary>
    /// Units a hair closer than their spacing step apart, each by half of what is missing, and
    /// units a hair further apart are left alone.
    /// </summary>
    [Fact]
    public void TwoUnitsTooCloseEachStepHalfTheWayApart()
    {
        var match = TestMatches.Create(TestMaps.Open(20, 20));
        var left = match.SpawnUnit(0, Point("9.75", "10"), Fixed.Half);
        var right = match.SpawnUnit(0, Point("10.25", "10"), Fixed.Half);
        var apart = match.SpawnUnit(0, Point("10", "15"), QuarterTile);
        var alsoApart = match.SpawnUnit(0, Point("10", "16") + new FixedVector2(Fixed.Zero, Fixed.Epsilon), QuarterTile);

        match.Simulation.Tick();

        // Half a tile short of their spacing, and the 1/256 of a tile they aim beyond it.
        var each = (Fixed.Half + SeparationSystem.Slack) / 2;
        Assert.Equal(Point("9.75", "10") - new FixedVector2(each, Fixed.Zero), match.PositionOf(left));
        Assert.Equal(Point("10.25", "10") + new FixedVector2(each, Fixed.Zero), match.PositionOf(right));

        match.RunUntilAtRest(20, left, right, apart, alsoApart);
        match.AssertKeepTheirSpacing(left, right, apart, alsoApart);
        Assert.Equal(Point("10", "15"), match.PositionOf(apart));
        Assert.Equal(Point("10", "16") + new FixedVector2(Fixed.Zero, Fixed.Epsilon), match.PositionOf(alsoApart));

        // Standing units take no order and do not turn when they step apart.
        Assert.Equal((MoveState.Idle, Angle.Zero), (match.Movers.Get(left).State, match.Movers.Get(left).Facing));
    }

    /// <summary>A step apart is no longer than the unit's speed, so a slow unit takes several ticks.</summary>
    [Fact]
    public void AUnitStepsApartNoFasterThanItWalks()
    {
        var match = TestMatches.Create(TestMaps.Open(20, 20));
        var slow = Fixed.One / 16;
        var left = match.SpawnUnit(0, Point("9.75", "10"), slow);
        var right = match.SpawnUnit(0, Point("10.25", "10"), slow);

        match.Simulation.Tick();
        Assert.Equal(Point("9.6875", "10"), match.PositionOf(left));
        Assert.Equal(Point("10.3125", "10"), match.PositionOf(right));

        Assert.Equal(4, match.RunUntilAtRest(20, left, right));
        match.AssertKeepTheirSpacing(left, right);
    }

    /// <summary>
    /// A mover without speed cannot step aside, so the other unit steps all the way. Two movers
    /// without speed stay as they are.
    /// </summary>
    [Fact]
    public void AUnitThatCannotMoveLeavesAllOfTheStepToTheOther()
    {
        var match = TestMatches.Create(TestMaps.Open(20, 20));
        var stuck = match.SpawnUnit(0, Point("10", "10"), Fixed.Zero);
        var free = match.SpawnUnit(0, Point("10.5", "10"), QuarterTile);
        var alsoStuck = match.SpawnUnit(0, Point("10", "15"), Fixed.Zero);
        var stuckBeside = match.SpawnUnit(0, Point("10.25", "15"), Fixed.Zero);

        match.RunUntilAtRest(20, stuck, free, alsoStuck, stuckBeside);

        Assert.Equal(Point("10", "10"), match.PositionOf(stuck));
        Assert.Equal(Point("11", "10"), match.PositionOf(free));
        Assert.Equal(Point("10", "15"), match.PositionOf(alsoStuck));
        Assert.Equal(Point("10.25", "15"), match.PositionOf(stuckBeside));
    }

    /// <summary>
    /// Units on the very same point have no direction to part in, so each pair takes one from its
    /// two IDs. A pile parts all ways and ends as any stopped group does.
    /// </summary>
    [Fact]
    public void UnitsOnOnePointPartAllWays()
    {
        var match = TestMatches.Create(TestMaps.Open(30, 30));
        var center = Point("15", "15");
        var units = Enumerable.Range(0, 24).Select(_ => match.SpawnUnit(0, center, QuarterTile)).ToArray();

        match.Simulation.Tick();
        Assert.Equal(units.Length, units.Select(match.PositionOf).Distinct().Count());

        match.RunUntilAtRest(600, units);
        match.AssertKeepTheirSpacing(units);
        var positions = units.Select(match.PositionOf).ToArray();
        Assert.Contains(positions, position => position.X < center.X && position.Y < center.Y);
        Assert.Contains(positions, position => position.X > center.X && position.Y < center.Y);
        Assert.Contains(positions, position => position.X < center.X && position.Y > center.Y);
        Assert.Contains(positions, position => position.X > center.X && position.Y > center.Y);
        Assert.All(positions, position => Assert.InRange(FixedVector2.Distance(center, position).Raw, 0, 4 * Fixed.RawOne));
    }

    /// <summary>
    /// Design 2.4: a player's own units never block each other. A unit walks straight through a
    /// standing block of its own side in the time the walk takes, and the block does not stir.
    /// </summary>
    [Fact]
    public void AWalkingUnitPassesThroughItsOwnSide()
    {
        var match = TestMatches.Create(TestMaps.Open(40, 20));
        var block = match.SpawnBlock(0, Point("15.5", "5.5"), 6, 54, QuarterTile, Fixed.One);
        var walker = match.SpawnUnit(0, Point("4.5", "10.25"), QuarterTile);
        var standing = block.Select(match.PositionOf).ToArray();

        match.OrderMove(0, Point("34.5", "10.25"), walker);
        for (var tick = 1; tick <= 120; tick++)
        {
            match.Simulation.Tick();
            Assert.Equal(Point("4.5", "10.25") + new FixedVector2(QuarterTile * tick, Fixed.Zero), match.PositionOf(walker));
            Assert.Equal(standing, block.Select(match.PositionOf));
        }

        Assert.Equal(MoveState.Idle, match.Movers.Get(walker).State);
    }

    /// <summary>
    /// A unit that stops among others makes room for itself: it and its neighbors step apart.
    /// </summary>
    [Fact]
    public void AUnitThatStopsInsideABlockMakesRoomForItself()
    {
        var match = TestMatches.Create(TestMaps.Open(40, 20));
        var block = match.SpawnBlock(0, Point("15.5", "5.5"), 6, 36, QuarterTile, Fixed.One);
        var walker = match.SpawnUnit(0, Point("4.5", "8"), QuarterTile);
        var standing = block.Select(match.PositionOf).ToArray();

        // Between four units of the block.
        match.OrderMove(0, Point("18", "8"), walker);
        match.Simulation.Run(53);
        Assert.Equal(Point("17.75", "8"), match.PositionOf(walker));
        Assert.Equal(standing, block.Select(match.PositionOf));

        EntityId[] all = [.. block, walker];
        match.RunUntilAtRest(400, all);
        match.AssertKeepTheirSpacing(all);
        Assert.NotEqual(standing, block.Select(match.PositionOf));
    }

    /// <summary>
    /// Units that walk the same way keep half their spacing from each other where they have the
    /// room, so a column that walks onto one line does not become one point. They still arrive.
    /// </summary>
    [Fact]
    public void WalkingUnitsGiveEachOtherHalfTheirSpacing()
    {
        var match = TestMatches.Create(TestMaps.Open(60, 20));
        var first = match.SpawnUnit(0, Point("4.5", "10"), QuarterTile);
        var second = match.SpawnUnit(0, Point("4.5", "10.125"), QuarterTile);

        // Two orders, so that each walks to the point itself, an eighth of a tile apart.
        match.OrderMove(0, Point("54.5", "10"), first);
        match.OrderMove(0, Point("54.5", "10.125"), second);
        match.Simulation.Run(100);

        var apart = FixedVector2.Distance(match.PositionOf(first), match.PositionOf(second));
        Assert.InRange(apart.Raw, Fixed.RawOne * 7 / 16, Fixed.RawOne / 2);
        Assert.Equal(MoveState.Moving, match.Movers.Get(first).State);

        // They arrive all the same, each on its own point, and then stand a tile apart.
        match.RunUntilAtRest(400, first, second);
        match.AssertKeepTheirSpacing(first, second);
    }

    /// <summary>
    /// Units that step apart beside blocked ground end no closer to it than a unit that has
    /// arrived, and never cross it: a pile on each side of a wall one tile thick stays on its side.
    /// </summary>
    [Fact]
    public void UnitsSteppingApartKeepOffBlockedGround()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "..............",
            "..............",
            "......#.......",
            "......#.......",
            "......#.......",
            "..............",
            ".............."));
        var left = Enumerable.Range(0, 12).Select(_ => match.SpawnUnit(0, Point("5.5", "3.5"), QuarterTile)).ToArray();
        var right = Enumerable.Range(0, 12).Select(_ => match.SpawnUnit(0, Point("7.5", "3.5"), QuarterTile)).ToArray();
        EntityId[] all = [.. left, .. right];

        for (var tick = 0; tick < 600; tick++)
        {
            match.Simulation.Tick();
            foreach (var unit in all)
            {
                match.AssertStandsClear(unit);
            }
        }

        match.RunUntilAtRest(600, all);
        match.AssertKeepTheirSpacing(all);
        Assert.All(all, unit =>
        {
            var position = match.PositionOf(unit);
            Assert.Equal(match.Grid.KeepClear(position, Mover.StandOff), position);
        });

        // Nobody went through the wall: whoever is level with it is on the side it began on.
        Assert.All(left, unit => Assert.False(LevelWithTheWall(unit) && match.PositionOf(unit).X > Fixed.FromInt(6)));
        Assert.All(right, unit => Assert.False(LevelWithTheWall(unit) && match.PositionOf(unit).X < Fixed.FromInt(7)));

        bool LevelWithTheWall(EntityId unit) => match.PositionOf(unit).Y.FloorToInt() is >= 2 and <= 4;
    }

    /// <summary>
    /// Units pressed into a place too small for them stay closer than their spacing. They stay in
    /// the place and off its walls, whatever they are pushed with, and come to rest there: a step
    /// that the walls turn aside is not taken when it leads nearer to another unit.
    /// </summary>
    [Fact]
    public void UnitsInAPlaceTooSmallForThemStayInIt()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "#####....",
            "#.###....",
            "#####....",
            "........."));
        var walledIn = Enumerable.Range(0, 3).Select(_ => match.SpawnUnit(0, Point("1.5", "1.5"), QuarterTile)).ToArray();

        for (var tick = 0; tick < 200; tick++)
        {
            match.Simulation.Tick();
            Assert.All(walledIn, unit => Assert.Equal(match.Grid.KeepClear(match.PositionOf(unit), Mover.StandOff), match.PositionOf(unit)));
            Assert.All(walledIn, unit => Assert.Equal((1, 1), (match.PositionOf(unit).X.FloorToInt(), match.PositionOf(unit).Y.FloorToInt())));
        }

        Assert.Equal(3, walledIn.Select(match.PositionOf).Distinct().Count());
        Assert.Equal(1, match.RunUntilAtRest(10, walledIn));
    }

    /// <summary>
    /// In a lane one tile wide units can part only along it. Two that stand across the lane from
    /// each other have no room to step the way they are pushed, and step along the lane instead.
    /// </summary>
    [Fact]
    public void UnitsAcrossANarrowLaneFromEachOtherPartAlongIt()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "############",
            "............",
            "############"));
        var upper = match.SpawnUnit(0, Point("0.4375", "1.4375"), QuarterTile);
        var lower = match.SpawnUnit(0, Point("0.4375", "1.5625"), QuarterTile);
        var others = Enumerable.Range(0, 6).Select(i => match.SpawnUnit(0, Point("6.5", "1.5"), QuarterTile)).ToArray();
        EntityId[] all = [upper, lower, .. others];

        match.RunUntilAtRest(600, all);

        match.AssertKeepTheirSpacing(all);
        Assert.All(all, unit => match.AssertStandsClear(unit));
    }

    /// <summary>
    /// A group that needs a route walks beside it as it stood, closes up where the ground is too
    /// narrow for it, and still ends in its shape on the far side. Here a block six wide goes
    /// through a gap two tiles wide, from where none of its units can see its place.
    /// </summary>
    [Fact]
    public void AGroupClosesUpThroughAGapAndEndsInItsShape()
    {
        var match = TestMatches.Create(GapMap());
        var units = match.SpawnBlock(0, MapGrid.TileCenter(3, 0), 4, 24, QuarterTile, Fixed.One);
        var target = Point("30", "10");
        var places = match.PlacesAround(target, units);
        var offsets = match.PlacesAround(FixedVector2.Zero, units);

        match.OrderMove(0, target, units);
        match.Simulation.Tick();

        // One route for all, each beside it by its offset in the group.
        Assert.Equal(1, match.Routes.SearchCount);
        Assert.Equal(offsets, units.Select(unit => match.Movers.Get(unit).Spread));
        Assert.All(units, unit => Assert.Equal(MoveState.Following, match.Movers.Get(unit).State));

        var halved = new bool[units.Length];
        var widestInTheGap = Fixed.Zero;
        var widestBefore = Fixed.Zero;
        for (var tick = 1; tick <= 600 && units.Any(unit => match.Movers.Get(unit).State != MoveState.Idle); tick++)
        {
            match.Simulation.Tick();
            for (var i = 0; i < units.Length; i++)
            {
                match.AssertStandsClear(units[i]);
                var spread = match.Movers.Get(units[i]).Spread;
                halved[i] |= offsets[i] != FixedVector2.Zero && spread == offsets[i] / 2;
            }

            var inTheGap = units.Select(match.PositionOf).Where(position => position.X.FloorToInt() == 16).ToArray();
            if (inTheGap.Length > 1)
            {
                widestInTheGap = Fixed.Max(widestInTheGap, inTheGap.Max(position => position.Y) - inTheGap.Min(position => position.Y));
            }

            if (tick == 20)
            {
                // Still in the open: the block is about as deep as it stood, five tiles.
                var positions = units.Select(match.PositionOf).ToArray();
                widestBefore = positions.Max(position => position.Y) - positions.Min(position => position.Y);
            }
        }

        // The gap is two tiles wide and units keep 3/8 of a tile from its sides.
        Assert.InRange(widestBefore.Raw, 4 * Fixed.RawOne, 5 * Fixed.RawOne);
        Assert.InRange(widestInTheGap.Raw, 1, (2 * Fixed.RawOne) - (2 * Mover.Clearance.Raw));
        Assert.Contains(true, halved);

        Assert.Equal(places, units.Select(match.PositionOf));
        Assert.All(units, unit => Assert.Equal(FixedVector2.Zero, match.Movers.Get(unit).Spread));
        Assert.Equal(1, match.Routes.SearchCount);
        match.AssertKeepTheirSpacing(units);
    }

    /// <summary>
    /// A unit whose place in the group cannot be walked to from the end of the group's route gets
    /// a route of its own to that place, without a spread. The others share one.
    /// </summary>
    [Fact]
    public void AUnitWhosePlaceIsOutOfSightOfTheGroupsRouteGetsItsOwn()
    {
        // The target is beside the end of a wall. The place of the lower unit is behind it.
        var match = TestMatches.Create(TestMaps.FromRows(
            "....#...........",
            "....#...........",
            "....#....#####..",
            "....#...........",
            "................",
            "................"));
        var upper = match.SpawnUnit(0, Point("1.5", "0.5"), QuarterTile);
        var lower = match.SpawnUnit(0, Point("1.5", "2.5"), QuarterTile);

        // The middle is (1.5, 1.5): the places are (11.5, 0.75) and (11.5, 2.75), which is in the wall.
        match.OrderMove(0, Point("11.5", "1.75"), upper, lower);
        match.Simulation.Tick();

        Assert.Equal((1 * 16) + 11, match.Movers.Get(upper).RouteGoal);
        Assert.Equal(new FixedVector2(Fixed.Zero, -Fixed.One), match.Movers.Get(upper).Spread);
        Assert.Equal(Point("11.5", "0.75"), match.Movers.Get(upper).Destination);

        // The nearest tile to the lower unit's place is the one below the wall.
        Assert.Equal((3 * 16) + 11, match.Movers.Get(lower).RouteGoal);
        Assert.Equal(FixedVector2.Zero, match.Movers.Get(lower).Spread);
        Assert.Equal(MapGrid.TileCenter(11, 3), match.Movers.Get(lower).Destination);
        Assert.Equal(2, match.Routes.SearchCount);

        match.RunUntilAtRest(400, upper, lower);
        Assert.Equal(Point("11.5", "0.75"), match.PositionOf(upper));
        Assert.Equal(MapGrid.TileCenter(11, 3), match.PositionOf(lower));
    }

    /// <summary>
    /// A unit's place that falls outside the map is taken to the nearest point of the map and then
    /// kept off its edge like any destination.
    /// </summary>
    [Fact]
    public void APlaceThatFallsOffTheMapIsTakenToItsEdge()
    {
        var match = TestMatches.Create(TestMaps.Open(20, 12));
        var units = match.SpawnBlock(0, MapGrid.TileCenter(2, 2), 5, 25, QuarterTile, Fixed.One);

        // The corner of the map: of the block of five by five, only the first unit's place is on it.
        match.OrderMove(0, Point("19.5", "11.5"), units);
        match.Simulation.Tick();

        var edge = new FixedVector2(Fixed.FromInt(20) - Mover.StandOff, Fixed.FromInt(12) - Mover.StandOff);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(Point("17.5", "9.5"), match.Movers.Get(units[0]).Destination);
        Assert.Equal(new FixedVector2(edge.X, Point("0", "9.5").Y), match.Movers.Get(units[4]).Destination);
        Assert.Equal(new FixedVector2(Point("18.5", "0").X, edge.Y), match.Movers.Get(units[21]).Destination);
        Assert.Equal(edge, match.Movers.Get(units[24]).Destination);

        // Those sent to the same point of the edge step apart again when they get there.
        match.RunUntilAtRest(2000, units);
        match.AssertKeepTheirSpacing(units);
        Assert.All(units, unit => match.AssertStandsClear(unit));
    }

    /// <summary>
    /// Stepping apart is worked out for all units from where they stand, before any of them
    /// steps, so the order of the units does not decide it: the same pile, mirrored and with its
    /// units created in the opposite order, parts into the mirror image.
    /// </summary>
    [Fact]
    public void SteppingApartDoesNotDependOnTheOrderOfTheUnits()
    {
        FixedVector2[] pile = [Point("10", "10"), Point("10.25", "10.5"), Point("9.5", "10.125"), Point("10.75", "9.75"), Point("10.125", "9.25"), Point("9.25", "9.5")];

        var match = TestMatches.Create(TestMaps.Open(20, 20));
        var units = pile.Select(position => match.SpawnUnit(0, position, QuarterTile)).ToArray();

        var mirrored = TestMatches.Create(TestMaps.Open(20, 20));
        var mirroredUnits = pile.Reverse().Select(position => mirrored.SpawnUnit(0, Mirror(position), QuarterTile)).Reverse().ToArray();

        for (var tick = 0; tick < 60; tick++)
        {
            match.Simulation.Tick();
            mirrored.Simulation.Tick();
            Assert.Equal(units.Select(unit => Mirror(match.PositionOf(unit))), mirroredUnits.Select(mirrored.PositionOf));
        }

        match.AssertKeepTheirSpacing(units);

        static FixedVector2 Mirror(FixedVector2 point) => new(Fixed.FromInt(20) - point.X, point.Y);
    }

    /// <summary>
    /// A group's walk through a gap and its stepping apart afterwards, cut at a snapshot and
    /// continued: with the spreads whole, with some halved before the gap, with some units arrived,
    /// with six more on their way to one point, while those step apart, and at rest.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(45)]
    [InlineData(110)]
    [InlineData(160)]
    [InlineData(216)]
    [InlineData(260)]
    [InlineData(SnapshotLastTick)]
    public void AGroupOnItsWayRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(BuildGapGroup, GapGroupScript(), snapshotTick, SnapshotLastTick);

    /// <summary>The run above is in the states its snapshots are meant to catch.</summary>
    [Fact]
    public void TheSnapshotRunHasSpreadsHalvedAndUnitsSteppingApart()
    {
        var match = BuildGapGroup();
        foreach (var (tick, command) in GapGroupScript())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        var units = UnitsOf(match);
        var whole = match.PlacesAround(FixedVector2.Zero, units[..24]);

        match.Simulation.Run(20);
        Assert.Equal(whole, units[..24].Select(unit => match.Movers.Get(unit).Spread));

        match.Simulation.Run(25);
        var spreads = units[..24].Select(unit => match.Movers.Get(unit).Spread).ToArray();
        Assert.Contains(Enumerable.Range(0, 24), i => spreads[i] != whole[i] && spreads[i] != FixedVector2.Zero);
        Assert.Contains(Enumerable.Range(0, 24), i => spreads[i] == whole[i]);

        match.Simulation.Run(65);
        Assert.InRange(units[..24].Count(unit => match.Movers.Get(unit).State == MoveState.Idle), 1, 23);

        // The six sent to one point have arrived on it and are stepping apart.
        match.Simulation.Run(106);
        Assert.All(units[24..], unit => Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State));
        var before = units[24..].Select(match.PositionOf).ToArray();
        match.Simulation.Tick();
        Assert.NotEqual(before, units[24..].Select(match.PositionOf));

        match.Simulation.Run(SnapshotLastTick - 218);
        before = units.Select(match.PositionOf).ToArray();
        match.Simulation.Tick();
        Assert.Equal(before, units.Select(match.PositionOf));
        match.AssertKeepTheirSpacing(units);
    }

    private const int SnapshotLastTick = 420;

    /// <summary>A field 36 by 20 with a wall at x = 16, open for two tiles at rows 9 and 10.</summary>
    private static MapDef GapMap()
    {
        var rows = new string[20];
        for (var y = 0; y < rows.Length; y++)
        {
            rows[y] = y is 9 or 10 ? new string('.', 36) : new string('.', 16) + "#" + new string('.', 19);
        }

        return TestMaps.FromRows(rows);
    }

    /// <summary>A block of 24 left of the gap, and six units right of it that will be sent to one point.</summary>
    private static Match BuildGapGroup()
    {
        var match = TestMatches.Create(GapMap());
        match.SpawnBlock(0, MapGrid.TileCenter(3, 0), 4, 24, QuarterTile, Fixed.One);
        for (var i = 0; i < 6; i++)
        {
            match.SpawnUnit(0, MapGrid.TileCenter(20 + (3 * i), 2 + (3 * (i % 2))), QuarterTile, i % 2 == 0 ? Fixed.One : OneAndAHalf);
        }

        match.RefreshDerivedState();
        return match;
    }

    private static (int Tick, Command Command)[] GapGroupScript()
    {
        var units = UnitsOf(BuildGapGroup());
        var beyond = Point("30", "10");
        var onePoint = Point("24.5", "16.5");
        var script = new List<(int Tick, Command Command)>
        {
            (0, new Command(CommandType.Move, 0, units.AsSpan(0, 24), beyond.X, beyond.Y)),
        };

        // One order each, so that all six go to the point itself.
        for (var i = 24; i < 30; i++)
        {
            script.Add((150, new Command(CommandType.Move, 0, units.AsSpan(i, 1), onePoint.X, onePoint.Y)));
        }

        return [.. script];
    }

    private static EntityId[] UnitsOf(Match match) =>
        [.. Enumerable.Range(0, match.Movers.Count).Select(match.Movers.EntityAt)];
}
