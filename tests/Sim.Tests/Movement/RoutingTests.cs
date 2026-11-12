using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>Scenarios: units ordered to places they cannot walk straight to.</summary>
public class RoutingTests
{
    private static readonly Fixed QuarterTile = Fixed.One / 4;

    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    [Fact]
    public void AUnitWalksAroundWhatIsInItsWay()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "............",
            "......#.....",
            "......#.....",
            "......#.....",
            "......F.....",
            "......F.....",
            "............"));
        var unit = match.SpawnUnit(0, Point("2.5", "3.5"), QuarterTile);
        var target = Point("10.5", "3.5");

        match.OrderMove(0, target, unit);
        var ticks = RunUntilIdle(match, 200, unit);

        Assert.Equal(target, match.PositionOf(unit));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(1, match.Routes.SearchCount);

        // Longer than the straight line of 8 tiles, which is blocked, and no longer than the walk
        // from tile to tile that the search found: the unit cuts across wherever it can.
        Assert.InRange(ticks, 33, TicksAlong(match, (2, 3), (10, 3), QuarterTile));
    }

    /// <summary>
    /// A unit leaves its route for a straight line as soon as the line is clear, so it turns only
    /// where the obstacle makes it: here around the end of the wall. Its walk is close to the
    /// shortest there is, and well short of the walk from tile to tile.
    /// </summary>
    [Fact]
    public void AUnitTurnsOnlyWhereTheObstacleMakesIt()
    {
        // A wall at x = 16 from row 3 down, open in the three rows above it.
        var rows = Enumerable.Range(0, 12).Select(y => y < 3 ? new string('.', 32) : new string('.', 16) + "#" + new string('.', 15));
        var match = TestMatches.Create(TestMaps.FromRows([.. rows]));
        var unit = match.SpawnUnit(0, Point("1.5", "10.5"), QuarterTile);
        var target = Point("30.5", "10.5");

        match.OrderMove(0, target, unit);
        var facings = new List<Angle>();
        var ticks = 0;
        while (match.Movers.Get(unit).State != MoveState.Idle || ticks == 0)
        {
            match.Simulation.Tick();
            ticks++;
            AssertStandsClear(match, unit);
            if (facings.Count == 0 || !Near(facings[^1], match.Movers.Get(unit).Facing))
            {
                facings.Add(match.Movers.Get(unit).Facing);
            }

            Assert.InRange(ticks, 1, 500);
        }

        Assert.Equal(target, match.PositionOf(unit));

        // Up and to the right toward the end of the wall, around it, then down and to the right.
        // Looking ahead only every few ticks can add a bend on the way round.
        Assert.InRange(facings.Count, 2, 5);
        Assert.InRange(facings[0].Units, 3072 + 1, 4096 - 1);
        Assert.InRange(facings[^1].Units, 1, 1024 - 1);

        // Straight to the tile above the wall's end and straight on is 17 tiles and then a little
        // over 16: 133 ticks. From tile to tile it is 16 diagonal and 13 straight steps: 142 ticks.
        Assert.Equal(142, TicksAlong(match, (1, 10), (30, 10), QuarterTile) - 2);
        Assert.InRange(ticks, 133, 135);

        // Two facings a few units apart are the same stretch, aimed again after a look ahead.
        static bool Near(Angle a, Angle b) => Math.Abs((a - b).Units) <= 64 || Math.Abs((a - b).Units) >= 4096 - 64;
    }

    /// <summary>
    /// A unit that reaches a turn of its route with some of the tick's distance left walks the rest
    /// of it past the turn: 2 tiles down and 4 across at three quarters of a tile per tick take 8
    /// ticks, not 3 and then 6.
    /// </summary>
    [Fact]
    public void WhatIsLeftOfATicksDistanceAtATurnIsWalkedPastIt()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            ".####",
            ".####",
            "....."));
        var unit = match.SpawnUnit(0, Point("0.5", "0.5"), Fixed.One * 3 / 4);

        match.OrderMove(0, Point("4.5", "2.5"), unit);
        match.Simulation.Run(3);
        Assert.Equal(Point("0.75", "2.5"), match.PositionOf(unit));
        Assert.Equal(Angle.Zero, match.Movers.Get(unit).Facing);

        match.Simulation.Run(4);
        Assert.Equal(Point("3.75", "2.5"), match.PositionOf(unit));
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(unit).State);

        match.Simulation.Tick();
        Assert.Equal(Point("4.5", "2.5"), match.PositionOf(unit));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
    }

    [Fact]
    public void AUnitWalksThroughAPassageOneTileWide()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "....#.......",
            "....#.####..",
            "....#.#..#..",
            "......#.##..",
            ".######.#...",
            "........#..."));
        var unit = match.SpawnUnit(0, Point("0.5", "0.5"), Mover.SpeedPerTick(Fraction.Parse("4.5")));
        var target = Point("7.5", "2.5");

        match.OrderMove(0, target, unit);
        var ticks = RunUntilIdle(match, 400, unit);

        Assert.Equal(target, match.PositionOf(unit));
        Assert.InRange(ticks, 1, TicksAlong(match, (0, 0), (7, 2), Mover.SpeedPerTick(Fraction.Parse("4.5"))));
    }

    /// <summary>
    /// The target of an order may be a place the unit cannot stand on or cannot get to. The unit
    /// goes as near as it can: to the center of the nearest tile it can reach.
    /// </summary>
    [Fact]
    public void AUnitOrderedToWhereItCannotGoWalksToTheNearestTileItCanReach()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "..........",
            "..FFF.....",
            "..FFF.###.",
            "..FFF.#.#.",
            "......###.",
            ".........."));
        var unit = match.SpawnUnit(0, Point("0.5", "5.5"), QuarterTile);

        // Into the forest, nearer its lower edge.
        match.OrderMove(0, Point("3.5", "3.25"), unit);
        RunUntilIdle(match, 200, unit);
        Assert.Equal(MapGrid.TileCenter(3, 4), match.PositionOf(unit));

        // Onto the tile walled in by cliffs: open ground, but in another region. The tiles above,
        // below, left and right of the cliffs are equally near; the one nearest the unit is taken.
        match.OrderMove(0, Point("7.5", "3.5"), unit);
        RunUntilIdle(match, 200, unit);
        Assert.Equal(MapGrid.TileCenter(5, 3), match.PositionOf(unit));

        // A unit inside the walls stays inside: its nearest tile is its own.
        var inside = match.SpawnUnit(0, Point("7.25", "3.75"), QuarterTile);
        match.OrderMove(0, Point("0.5", "0.5"), inside);
        RunUntilIdle(match, 200, inside);
        Assert.Equal(MapGrid.TileCenter(7, 3), match.PositionOf(inside));

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>
    /// One order gives its group one destination per region, so units on both sides of a wall can
    /// be ordered together and each goes where it can.
    /// </summary>
    [Fact]
    public void UnitsInDifferentRegionsEachGoWhereTheyCan()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "....#....",
            "....#....",
            "....#...."));
        var left = match.SpawnUnit(0, Point("2.5", "0.5"), QuarterTile);
        var right = match.SpawnUnit(0, Point("6.5", "2.5"), QuarterTile);

        // The middle of the two is (4.5, 1.5), so their places are (4.75, 0.25), which is in the
        // wall, and (8.75, 2.25), which is too close to the edge of the map.
        match.OrderMove(0, Point("6.75", "1.25"), left, right);
        RunUntilIdle(match, 200, left, right);

        Assert.Equal(MapGrid.TileCenter(3, 0), match.PositionOf(left));
        Assert.Equal(new FixedVector2(Fixed.FromInt(9) - Mover.StandOff, Point("0", "2.25").Y), match.PositionOf(right));
    }

    /// <summary>
    /// A destination keeps off blocked ground by <see cref="Mover.StandOff"/>, so that the unit
    /// standing there can walk away in any direction without first backing off.
    /// </summary>
    [Fact]
    public void ADestinationBesideBlockedGroundIsMovedOffIt()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "........",
            "...##...",
            "........"));
        var unit = match.SpawnUnit(0, Point("0.5", "0.5"), QuarterTile);

        // Just above the cliff's upper edge, and then tight in the corner of the map.
        match.OrderMove(0, Point("3.5", "0.99"), unit);
        RunUntilIdle(match, 100, unit);
        Assert.Equal(new FixedVector2(Point("3.5", "0").X, Fixed.One - Mover.StandOff), match.PositionOf(unit));

        match.OrderMove(0, Point("7.99", "2.99"), unit);
        RunUntilIdle(match, 100, unit);
        Assert.Equal(new FixedVector2(Fixed.FromInt(8) - Mover.StandOff, Fixed.FromInt(3) - Mover.StandOff), match.PositionOf(unit));

        // From there every way out that is open is a straight walk: no route is needed.
        var searches = match.Routes.SearchCount;
        match.OrderMove(0, Point("5.5", "2.5"), unit);
        match.Simulation.Tick();
        Assert.Equal(MoveState.Moving, match.Movers.Get(unit).State);
        Assert.Equal(searches, match.Routes.SearchCount);
    }

    /// <summary>The plan's rule: one path per order, shared by the group.</summary>
    [Fact]
    public void AGroupOrderedTogetherSharesOneRoute()
    {
        var match = TestMatches.Create(WalledField());
        var units = new List<EntityId>();
        for (var y = 2; y <= 4; y++)
        {
            for (var x = 2; x <= 5; x++)
            {
                units.Add(match.SpawnUnit(0, MapGrid.TileCenter(x, y), QuarterTile));
            }
        }

        var target = Point("15.5", "3.5");
        var places = match.PlacesAround(target, [.. units]);
        match.OrderMove(0, target, [.. units]);
        match.Simulation.Tick();

        // The first unit's tile starts the route, and the others walk to it.
        Assert.Equal(1, match.Routes.SearchCount);
        Assert.All(units, unit =>
        {
            var mover = match.Movers.Get(unit);
            Assert.Equal(MoveState.Following, mover.State);
            Assert.Equal((2 * 20) + 2, mover.RouteStart);
            Assert.Equal((3 * 20) + 15, mover.RouteGoal);
        });

        RunUntilIdle(match, 400, [.. units]);

        // Each ends on its own place around the target, as far from the others as it began.
        Assert.Equal(places, units.Select(match.PositionOf));
        Assert.Equal(1, match.Routes.SearchCount);
    }

    /// <summary>
    /// A unit that cannot walk straight to the start of the group's route starts a route of its
    /// own, from the tile it stands on.
    /// </summary>
    [Fact]
    public void AUnitThatCannotReachTheGroupsRouteGetsItsOwn()
    {
        var match = TestMatches.Create(TestMaps.FromRows(
            "...#.......#...",
            "...#...#...#...",
            "...#...#...#...",
            ".......#......."));
        var first = match.SpawnUnit(0, Point("1.5", "0.5"), QuarterTile);
        var beside = match.SpawnUnit(0, Point("0.5", "2.5"), QuarterTile);
        var behindTheWall = match.SpawnUnit(0, Point("5.5", "0.5"), QuarterTile);
        var target = Point("13.5", "0.5");

        match.OrderMove(0, target, first, beside, behindTheWall);
        match.Simulation.Tick();

        Assert.Equal(1, match.Movers.Get(first).RouteStart);
        Assert.Equal(1, match.Movers.Get(beside).RouteStart);
        Assert.Equal(5, match.Movers.Get(behindTheWall).RouteStart);

        // The middle of the three is (3, 1.5). The first unit's place and the third's are off the
        // top of the map, so they end beside its edge, one of them beside the wall too. Both can
        // be walked to from the end of the group's route.
        Assert.Equal(13, match.Movers.Get(first).RouteGoal);
        Assert.Equal(13, match.Movers.Get(behindTheWall).RouteGoal);

        // The second unit's place, (11, 1.5), is in the wall at x = 11. The nearest tile to it is
        // on the near side of that wall, out of sight from the end of the group's route, so this
        // unit gets a route to that tile: the third search.
        Assert.Equal((1 * 15) + 10, match.Movers.Get(beside).RouteGoal);
        Assert.Equal(3, match.Routes.SearchCount);

        RunUntilIdle(match, 400, first, beside, behindTheWall);
        Assert.Equal(new FixedVector2(Fixed.FromInt(12) + Mover.StandOff, Mover.StandOff), match.PositionOf(first));
        Assert.Equal(MapGrid.TileCenter(10, 1), match.PositionOf(beside));
        Assert.Equal(new FixedVector2(Fixed.FromInt(15) - Mover.StandOff, Mover.StandOff), match.PositionOf(behindTheWall));
    }

    /// <summary>
    /// The units of a group that have a clear line walk it. Only those that have not take the
    /// route, so a group in the open costs no search at all.
    /// </summary>
    [Fact]
    public void UnitsWithAClearLineNeedNoRoute()
    {
        var match = TestMatches.Create(WalledField());
        var blocked = match.SpawnUnit(0, Point("7.5", "3.5"), QuarterTile);
        var clear = match.SpawnUnit(0, Point("12.5", "5.5"), QuarterTile);
        var target = Point("15.5", "3.5");

        match.OrderMove(0, target, blocked, clear);
        match.Simulation.Tick();

        Assert.Equal(MoveState.Following, match.Movers.Get(blocked).State);
        Assert.Equal(MoveState.Moving, match.Movers.Get(clear).State);
        Assert.Equal(1, match.Routes.SearchCount);

        var open = TestMatches.Create(TestMaps.Open(30, 30));
        var group = Enumerable.Range(0, 40).Select(i => open.SpawnUnit(0, MapGrid.TileCenter(2 + (i % 8), 2 + (i / 8)), QuarterTile)).ToArray();
        var places = open.PlacesAround(Point("25.25", "20.75"), group);
        open.OrderMove(0, Point("25.25", "20.75"), group);
        RunUntilIdle(open, 200, group);

        Assert.Equal(0, open.Routes.SearchCount);
        Assert.Equal(places, group.Select(open.PositionOf));
    }

    /// <summary>A unit takes its first step in the tick that carries out its order, route or no route.</summary>
    [Fact]
    public void AnOrderedUnitStartsWalkingInTheTickOfItsOrder()
    {
        var match = TestMatches.Create(WalledField());
        var unit = match.SpawnUnit(0, Point("3.5", "3.5"), QuarterTile);

        match.OrderMove(0, Point("15.5", "3.5"), unit);
        match.Simulation.Tick();

        Assert.Equal(MoveState.Following, match.Movers.Get(unit).State);
        Assert.InRange(FixedVector2.Distance(Point("3.5", "3.5"), match.PositionOf(unit)).Raw, QuarterTile.Raw - 2, QuarterTile.Raw);
    }

    /// <summary>
    /// The searches of one tick stop when they have expanded <see cref="RoutePlanningSystem.ExpansionsPerTick"/>
    /// tiles. The units still without a route wait where they stand and are served in later ticks,
    /// oldest first. The budget is counted in work done, never in time, so every machine makes the
    /// same units wait. A route that happens to be known already costs the same, because what is
    /// known is not part of the state.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RouteSearchesBeyondTheTicksBudgetWaitForLaterTicks(bool routesKnownAlready)
    {
        const int Size = 48;
        var match = TestMatches.Create(Switchback(Size));
        var goal = ((Size - 1) * Size) + 24;
        var units = new EntityId[20];
        var cost = new int[units.Length];
        var reference = new RouteCache(match.Grid);
        for (var i = 0; i < units.Length; i++)
        {
            units[i] = match.SpawnUnit(0, MapGrid.TileCenter(2 * i, 0), QuarterTile);
            cost[i] = reference.Get(2 * i, goal).Expansions;
            if (routesKnownAlready)
            {
                match.Routes.Get(2 * i, goal);
            }

            // One order each, so that each unit gets a route of its own.
            match.OrderMove(0, MapGrid.TileCenter(24, Size - 1), units[i]);
        }

        Assert.All(cost, expansions => Assert.InRange(expansions, 1000, Size * Size));

        // The rule, worked out here from the costs: a search starts while any budget is left.
        var expectedStart = new int[units.Length];
        var tick = 0;
        for (var next = 0; next < units.Length; tick++)
        {
            for (var spent = 0; next < units.Length && spent < RoutePlanningSystem.ExpansionsPerTick; next++)
            {
                spent += cost[next];
                expectedStart[next] = tick;
            }
        }

        Assert.InRange(tick, 4, units.Length);

        var started = new int[units.Length];
        Array.Fill(started, -1);
        for (var now = 0; now < tick; now++)
        {
            match.Simulation.Tick();
            for (var i = 0; i < units.Length; i++)
            {
                var state = match.Movers.Get(units[i]).State;
                if (started[i] >= 0)
                {
                    Assert.NotEqual(MoveState.Planning, state);
                }
                else if (state == MoveState.Planning)
                {
                    Assert.Equal(MapGrid.TileCenter(2 * i, 0), match.PositionOf(units[i]));
                    Assert.Equal(0, match.Movers.Get(units[i]).RouteNode);
                }
                else
                {
                    started[i] = now;
                    Assert.NotEqual(MapGrid.TileCenter(2 * i, 0), match.PositionOf(units[i]));
                }
            }
        }

        Assert.Equal(expectedStart, started);
        Assert.Equal(units.Length, match.Routes.SearchCount);

        // All were sent to the one point, each by its own order. They arrive in the last row,
        // which is one tile wide, and step apart along it.
        RunUntilIdle(match, 4000, units);
        match.RunUntilAtRest(4000, units);
        Assert.All(units, unit => Assert.Equal(Size - 1, match.PositionOf(unit).Y.FloorToInt()));
        match.AssertKeepTheirSpacing(units);
    }

    /// <summary>
    /// The budget and the number of routes one order makes for others to share decide in which tick
    /// a unit starts and which way it goes. Changing either changes every recorded match.
    /// </summary>
    [Fact]
    public void TheNumbersThatDecideWhoWaitsAreRulesOfTheGame()
    {
        Assert.Equal(4096, RoutePlanningSystem.ExpansionsPerTick);
        Assert.Equal(8, MoveCommandHandler.MaxSharedRoutes);
        Assert.Equal(4, MovementSystem.LookInterval);
        Assert.Equal(Fixed.One * 3 / 8, Mover.Clearance);
        Assert.Equal(Fixed.One * 7 / 16, Mover.StandOff);
        Assert.Equal((70, 99), (RouteFinder.StraightCost, RouteFinder.DiagonalCost));
    }

    /// <summary>
    /// One order makes at most <see cref="MoveCommandHandler.MaxSharedRoutes"/> routes that later
    /// units of the order can take, which bounds the work of the order. Units beyond that each
    /// start from their own tile.
    /// </summary>
    [Fact]
    public void OneOrderMakesAtMostEightRoutesForOthersToShare()
    {
        // Ten stalls one tile wide, open at the bottom onto a lane that leaves at the far right.
        var match = TestMatches.Create(TestMaps.FromRows(
            ".#.#.#.#.#.#.#.#.#.#.",
            ".#.#.#.#.#.#.#.#.#.#.",
            ".#.#.#.#.#.#.#.#.#.#.",
            ".....................",
            "####################.",
            "....................."));
        var back = Enumerable.Range(0, 10).Select(stall => match.SpawnUnit(0, MapGrid.TileCenter(2 * stall, 0), QuarterTile)).ToArray();
        var front = Enumerable.Range(0, 10).Select(stall => match.SpawnUnit(0, MapGrid.TileCenter(2 * stall, 1), QuarterTile)).ToArray();

        // The ten at the back are listed first. None can walk straight to another's tile, so each
        // starts a route, and the first eight of those are open to the units listed after them.
        match.OrderMove(0, Point("0.5", "5.5"), [.. back, .. front]);
        match.Simulation.Tick();

        for (var stall = 0; stall < 10; stall++)
        {
            Assert.Equal(2 * stall, match.Movers.Get(back[stall]).RouteStart);
            Assert.Equal(stall < 8 ? 2 * stall : 21 + (2 * stall), match.Movers.Get(front[stall]).RouteStart);
        }

        Assert.Equal(12, match.Routes.SearchCount);

        // The stalls are 18 tiles from end to end: too long a group to keep a shape, so all go to
        // the point itself, in the bottom lane, and step apart along it.
        EntityId[] all = [.. back, .. front];
        Assert.All(all, unit => Assert.Equal(Point("0.5", "5.5"), match.Movers.Get(unit).Destination));
        RunUntilIdle(match, 400, all);
        match.RunUntilAtRest(4000, all);
        Assert.All(all, unit => Assert.Equal(5, match.PositionOf(unit).Y.FloorToInt()));
        match.AssertKeepTheirSpacing(all);
    }

    /// <summary>A route costs its search once in a tick, however many units of the group take it.</summary>
    [Fact]
    public void AGroupSharingOneLongRouteStartsTogether()
    {
        const int Size = 48;
        var match = TestMatches.Create(Switchback(Size));
        var units = Enumerable.Range(0, 30).Select(i => match.SpawnUnit(0, MapGrid.TileCenter(i, i % 2), QuarterTile)).ToArray();

        match.OrderMove(0, MapGrid.TileCenter(24, Size - 1), units);
        match.Simulation.Tick();

        Assert.Equal(1, match.Routes.SearchCount);
        Assert.InRange(30 * match.Routes.Get(0, ((Size - 1) * Size) + 24).Expansions, 5 * RoutePlanningSystem.ExpansionsPerTick, int.MaxValue);
        Assert.All(units, unit => Assert.Equal(MoveState.Following, match.Movers.Get(unit).State));
    }

    /// <summary>
    /// The routes that have been found are kept outside the state, and a route costs its budget
    /// whether it was kept or not. So a match runs the same when they are thrown away, as they are
    /// when a world is restored from a snapshot.
    /// </summary>
    [Fact]
    public void ThrowingAwayTheRoutesFoundChangesNothing()
    {
        var kept = PlayRandomOrders(seed: 5, ticks: 600, forgetRoutesEvery: 0, out var searchesKept);
        var forgotten = PlayRandomOrders(seed: 5, ticks: 600, forgetRoutesEvery: 1, out var searchesForgotten);
        var sometimes = PlayRandomOrders(seed: 5, ticks: 600, forgetRoutesEvery: 7, out _);

        Assert.Equal(kept, forgotten);
        Assert.Equal(kept, sometimes);
        Assert.InRange(searchesKept, 20, 2_000);
        Assert.InRange(searchesForgotten, 10 * searchesKept, long.MaxValue);

        Assert.NotEqual(kept[^1], PlayRandomOrders(seed: 6, ticks: 600, forgetRoutesEvery: 0, out _)[^1]);
    }

    /// <summary>
    /// Many units, many orders, some given while a unit is waiting for a route or is half way along
    /// one. Whatever happens, no unit ever stands on or too close to blocked ground, and every unit
    /// ends its order near where it sent it: units that arrive on top of each other step apart.
    /// </summary>
    [Theory]
    [InlineData(1UL, 2)]
    [InlineData(2UL, 3)]
    [InlineData(3UL, 1)]
    public void UnitsUnderRandomOrdersStayOnOpenGroundAndArrive(ulong seed, int blockedInTen)
    {
        var random = new DeterministicRandom(seed);
        const int Size = 28;
        var match = TestMatches.Create(MapGridReachTests.RandomMap(random, Size, Size, blockedInTen));
        var units = SpawnOnRandomTiles(match, random, 24);

        for (var tick = 0; tick < 1500; tick++)
        {
            if (tick % 9 == 0)
            {
                var target = new FixedVector2(
                    Fixed.FromRaw(random.NextInt(Size * Fixed.RawOne)),
                    Fixed.FromRaw(random.NextInt(Size * Fixed.RawOne)));
                var first = random.NextInt(units.Length);

                // The first order goes to everyone, so that every unit has a destination.
                var count = tick == 0 ? units.Length : 1 + random.NextInt(6);
                match.OrderMove(0, target, [.. Enumerable.Range(first, count).Select(i => units[i % units.Length])]);
            }

            match.Simulation.Tick();
            foreach (var unit in units)
            {
                AssertStandsClear(match, unit);
            }
        }

        RunUntilIdle(match, 2000, units);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        match.RunUntilAtRest(2000, units);
        Assert.All(units, unit =>
        {
            var mover = match.Movers.Get(unit);
            AssertStandsClear(match, unit);
            Assert.Equal(match.Grid.RegionOf(mover.Destination), match.Grid.RegionOf(match.PositionOf(unit)));
            Assert.InRange(FixedVector2.Distance(mover.Destination, match.PositionOf(unit)).Raw, 0, 6 * Fixed.RawOne);
            Assert.Equal((0, 0, 0), (mover.RouteStart, mover.RouteGoal, mover.RouteNode));
            Assert.Equal(FixedVector2.Zero, mover.Spread);
        });
    }

    [Fact]
    public void ANewOrderReplacesARouteHalfWalked()
    {
        var match = TestMatches.Create(WalledField());
        var unit = match.SpawnUnit(0, Point("3.5", "3.5"), QuarterTile);

        match.OrderMove(0, Point("15.5", "3.5"), unit);
        match.Simulation.Run(20);
        Assert.Equal(MoveState.Following, match.Movers.Get(unit).State);

        // Back to where it came from: a straight walk again, with nothing left of the route.
        match.OrderMove(0, Point("3.5", "3.5"), unit);
        match.Simulation.Tick();
        var mover = match.Movers.Get(unit);
        Assert.Equal(MoveState.Moving, mover.State);
        Assert.Equal((0, 0, 0), (mover.RouteStart, mover.RouteGoal, mover.RouteNode));

        RunUntilIdle(match, 100, unit);
        Assert.Equal(Point("3.5", "3.5"), match.PositionOf(unit));
    }

    [Fact]
    public void AUnitDestroyedOnItsRouteLeavesTheOthersToWalkIt()
    {
        var match = TestMatches.Create(WalledField());
        var first = match.SpawnUnit(0, Point("3.5", "3.5"), QuarterTile);
        var second = match.SpawnUnit(0, Point("4.5", "4.5"), QuarterTile);
        var target = Point("15.5", "3.5");

        // The route starts on the first unit's tile. It does not need the unit.
        match.OrderMove(0, target, first, second);
        match.Simulation.Tick();
        match.World.Entities.Destroy(first);
        match.Simulation.Run(10);

        Assert.False(match.Movers.Contains(first));
        Assert.Equal((3 * 20) + 3, match.Movers.Get(second).RouteStart);

        // The second unit's place is half a tile right of and below the target, as it stood.
        RunUntilIdle(match, 400, second);
        Assert.Equal(Point("16", "4"), match.PositionOf(second));
    }

    /// <summary>
    /// On the real map <c>ridge</c> the cliffs down the middle are open at the north pass and at
    /// the open field. A unit ordered across from beside the cliffs goes through the pass.
    /// </summary>
    [Fact]
    public void OnRidgeAUnitCrossesTheCliffsThroughTheNorthPass()
    {
        var loaded = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Errors));
        var ridge = loaded.Data.Maps.Single(map => map.Id == "ridge");
        var match = MatchSetup.Create(loaded.Data, ridge, 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);
        var speed = Mover.SpeedPerTick(3);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(40, 8), speed);
        var target = MapGrid.TileCenter(55, 8);

        match.OrderMove(0, target, unit);
        var inThePass = false;
        var ticks = 0;
        while (match.Movers.Get(unit).State != MoveState.Idle || ticks == 0)
        {
            match.Simulation.Tick();
            ticks++;
            AssertStandsClear(match, unit);
            var position = match.PositionOf(unit);
            inThePass |= position.X.FloorToInt() is >= 46 and < 50 && position.Y.FloorToInt() is >= 20 and < 26;
            Assert.InRange(ticks, 1, 2000);
        }

        Assert.Equal(target, match.PositionOf(unit));
        Assert.True(inThePass);

        // Down to the pass and up again: about 30 tiles at 0.15 of a tile per tick, and less than
        // the walk from tile to tile.
        Assert.InRange(ticks, 190, TicksAlong(match, (40, 8), (55, 8), speed));
        Assert.Equal(1, match.Routes.SearchCount);
        Assert.InRange(match.Routes.Get((8 * 96) + 40, (8 * 96) + 55).Expansions, 1, RoutePlanningSystem.ExpansionsPerTick);
    }

    /// <summary>Design 5.2: on <c>ridge</c> the six starting workers can walk to the enemy base, as one group with one route.</summary>
    [Fact]
    public void OnRidgeTheStartingWorkersWalkToTheEnemyBase()
    {
        var loaded = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Errors));
        var ridge = loaded.Data.Maps.Single(map => map.Id == "ridge");
        var match = MatchSetup.Create(loaded.Data, ridge, 1, [new(0), new(1)]);
        var workers = Enumerable.Range(0, 6).Select(match.Bodies.EntityAt).ToArray();
        var target = MapGrid.TileCenter(84, 44);
        var places = match.PlacesAround(target, workers);

        match.OrderMove(0, target, workers);
        var ticks = RunUntilIdle(match, 2000, workers);

        // They arrive as they started, in a row around the point.
        Assert.Equal(places, workers.Select(match.PositionOf));
        match.AssertKeepTheirSpacing(workers);
        Assert.InRange(match.Routes.SearchCount, 0, 1);

        // 75 tiles at 0.15 of a tile per tick.
        Assert.InRange(ticks, 480, 560);
    }

    /// <summary>A field 20 by 12 with a wall down from the top at x = 10, open in its last three rows.</summary>
    private static MapDef WalledField()
    {
        var rows = new string[12];
        for (var y = 0; y < rows.Length; y++)
        {
            rows[y] = y < 9 ? "..........#........." : "....................";
        }

        return TestMaps.FromRows(rows);
    }

    /// <summary>
    /// A square map crossed by walls with a gap at alternating ends, so that a search from the top
    /// row to the bottom row has to look at most of the map.
    /// </summary>
    private static MapDef Switchback(int size)
    {
        var rows = new string[size];
        for (var y = 0; y < size; y++)
        {
            rows[y] = y % 4 != 2 ? new string('.', size)
                : y % 8 == 2 ? new string('#', size - 2) + ".."
                : ".." + new string('#', size - 2);
        }

        return TestMaps.FromRows(rows);
    }

    private static EntityId[] SpawnOnRandomTiles(Match match, DeterministicRandom random, int count)
    {
        var units = new List<EntityId>();
        while (units.Count < count)
        {
            var (x, y) = (random.NextInt(match.Grid.Width), random.NextInt(match.Grid.Height));
            if (match.Grid.IsPassable(x, y))
            {
                units.Add(match.SpawnUnit(0, MapGrid.TileCenter(x, y), Fixed.FromRaw(random.NextInt(3000, 30_000))));
            }
        }

        return [.. units];
    }

    /// <summary>The state hash after every tick of a match with random units and random orders.</summary>
    private static ulong[] PlayRandomOrders(ulong seed, int ticks, int forgetRoutesEvery, out long searches)
    {
        var random = new DeterministicRandom(seed);
        const int Size = 28;
        var match = TestMatches.Create(MapGridReachTests.RandomMap(random, Size, Size, blockedInTen: 2));
        var units = SpawnOnRandomTiles(match, random, 30);

        var hashes = new ulong[ticks];
        for (var tick = 0; tick < ticks; tick++)
        {
            if (tick % 12 == 0)
            {
                var target = new FixedVector2(
                    Fixed.FromRaw(random.NextInt(Size * Fixed.RawOne)),
                    Fixed.FromRaw(random.NextInt(Size * Fixed.RawOne)));
                var first = random.NextInt(units.Length);
                match.OrderMove(0, target, [.. Enumerable.Range(first, 8).Select(i => units[i % units.Length])]);
            }

            if (forgetRoutesEvery > 0 && tick % forgetRoutesEvery == 0)
            {
                match.Routes.Clear();
            }

            match.Simulation.Tick();
            hashes[tick] = match.World.ComputeHash();
        }

        searches = match.Routes.SearchCount;
        return hashes;
    }

    /// <summary>Runs ticks until every unit listed has no order left, checking each on every tick. Returns the number of ticks.</summary>
    private static int RunUntilIdle(Match match, int mostTicks, params EntityId[] units)
    {
        for (var ticks = 1; ticks <= mostTicks; ticks++)
        {
            match.Simulation.Tick();
            foreach (var unit in units)
            {
                AssertStandsClear(match, unit);
            }

            if (units.All(unit => match.Movers.Get(unit).State == MoveState.Idle))
            {
                return ticks;
            }
        }

        Assert.Fail($"After {mostTicks} ticks a unit is still on its way.");
        return 0;
    }

    private static void AssertStandsClear(Match match, EntityId unit) => match.AssertStandsClear(unit);

    /// <summary>
    /// The ticks a unit needs to walk from the center of one tile to the center of another along
    /// the tiles of the route between them, turning at every tile. Rounded up, plus one.
    /// </summary>
    private static int TicksAlong(Match match, (int X, int Y) from, (int X, int Y) to, Fixed speed)
    {
        var width = match.Grid.Width;
        var tiles = new RouteFinder(match.Grid).Find(from.X, from.Y, to.X, to.Y, out _);
        long cost = 0;
        for (var i = 1; i < tiles.Length; i++)
        {
            var diagonal = tiles[i] % width != tiles[i - 1] % width && tiles[i] / width != tiles[i - 1] / width;
            cost += diagonal ? RouteFinder.DiagonalCost : RouteFinder.StraightCost;
        }

        // A straight step is one tile and costs 70, so the length in distance units is cost / 70 * 65,536.
        return (int)((cost * Fixed.RawOne / (RouteFinder.StraightCost * (long)speed.Raw)) + 2);
    }
}
