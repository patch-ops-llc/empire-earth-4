using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Structures;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Construction;

/// <summary>
/// The worker's arrival and the foundation (design 2.5 and 5.4; design 8.4, rows 22 to 24 and 28
/// to 30): the foundation is laid in the tick of arrival if every rule still holds, blocks its
/// tiles at once, and builds itself.
/// </summary>
public class FoundationTests
{
    private static readonly long Reach = FixedVector2.SquareRaw(ConstructionRules.ArrivalReach);

    [Fact]
    public void TheWorkerArrivesThreeQuartersOfATileFromTheNearestEdge() =>
        Assert.Equal(Fixed.One * 3 / 4, ConstructionRules.ArrivalReach);

    /// <summary>Design 8.4, row 28.</summary>
    [Fact]
    public void TheFoundationIsLaidInTheTickTheWorkerComesWithinReachOfTheFootprint()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 4);
        var site = new TileSquare(10, 10, 2);
        match.OrderBuild(0, "generator", 10, 10, worker);

        // Until the worker is within 3/4 of a tile of the footprint's nearest edge, nothing is there.
        match.Simulation.Tick();
        while (site.DistanceSquaredRawTo(match.PositionOf(worker)) > Reach)
        {
            Assert.True(match.BuildOrders.Contains(worker));
            Assert.Equal(EntityId.None, match.StructureAt(10, 10));
            Assert.True(match.Grid.IsPassable(10, 10));
            match.Simulation.Tick();
        }

        // In the tick in which it is, the foundation is: its tiles are blocked and it has all its hit points.
        var structure = match.StructureAt(10, 10);
        Assert.NotEqual(EntityId.None, structure);
        Assert.False(match.BuildOrders.Contains(worker));
        Assert.Equal(new Foundation { Built = 0, Materials = 100, Energy = 0 }, match.Foundations.Get(structure));
        Assert.Equal((500_000, 500_000), (match.Health.Get(structure).HitPoints, match.Health.Get(structure).Maximum));
        for (var i = 0; i < 4; i++)
        {
            Assert.False(match.Grid.IsPassable(10 + (i % 2), 10 + (i / 2)));
        }

        var placed = Assert.Single(match.EventsOf(SimEventKind.StructurePlaced));
        Assert.Equal((structure, 0, Generator), (placed.Entity, placed.Player, placed.Amount));
        Assert.Empty(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal((900, 1000), match.StockOf(0));

        // The worker stopped short of the edge by less than its step, and stands.
        var distance = site.DistanceSquaredRawTo(match.PositionOf(worker));
        Assert.InRange(distance, FixedVector2.SquareRaw(Fixed.One / 2), Reach);
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
        Assert.Equal(default, match.WorkerOf(worker));
        match.Simulation.Run(40);
        Assert.Equal(distance, site.DistanceSquaredRawTo(match.PositionOf(worker)));
    }

    /// <summary>
    /// A worker that is within reach when the order is given lays the foundation in that tick.
    /// One that is not walks a step of 0.15 tiles in it, and has not arrived.
    /// </summary>
    [Theory]
    [InlineData("10.5", "9.25", true)]
    [InlineData("12.5", "12.5", true)]
    [InlineData("10.5", "9", false)]
    [InlineData("12.75", "12.75", false)]
    public void AWorkerWithinReachLaysTheFoundationAtOnce(string x, string y, bool within)
    {
        var match = WithACapitol();
        var worker = match.SpawnWorker(0, TestMatches.Point(x, y));
        match.OrderBuild(0, "generator", 10, 10, worker);

        match.Simulation.Tick();

        Assert.Equal(within, match.StructureAt(10, 10) != EntityId.None);
        Assert.Equal(within, !match.BuildOrders.Contains(worker));
    }

    /// <summary>Design 8.4, rows 22 and 23: the build time is fixed, and the structure builds itself whatever the worker does.</summary>
    [Fact]
    public void AStructureBuildsItselfInItsBuildTimeAndGivesWhatItGivesFromThen()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();
        var generator = match.StructureAt(10, 10);

        // The worker leaves, and the structure goes on by itself: 25 seconds are 500 ticks.
        match.OrderMove(0, MapGrid.TileCenter(2, 2), worker);
        match.Simulation.Run(499);
        Assert.Equal(499, match.Foundations.Get(generator).Built);
        Assert.False(match.Construction.IsFinished(generator));
        Assert.Equal(0, match.WorkSites.Count);
        Assert.Empty(match.EventsOf(SimEventKind.StructureFinished));

        match.Simulation.Tick();

        Assert.True(match.Construction.IsFinished(generator));
        Assert.Equal(0, match.Foundations.Count);
        Assert.Equal(
            new WorkSite { Kind = WorkSiteKind.Generator, Owner = 0, X = 10, Y = 10, Size = 2 },
            match.WorkSites.Get(generator));
        var finished = Assert.Single(match.EventsOf(SimEventKind.StructureFinished));
        Assert.Equal((generator, 0, Generator), (finished.Entity, finished.Player, finished.Amount));
        Assert.Equal(500_000, match.Health.Get(generator).HitPoints);
        Assert.Equal((900, 1000), match.StockOf(0));

        // A worker earns Energy beside it from now on.
        match.OrderGather(0, generator, worker);
        match.RunUntilWorking(worker);
        var before = match.Stockpiles.Energy(0);
        match.Simulation.Run(SimInfo.TicksPerSecond);
        Assert.Equal(before + EconomyMatches.EnergyPayment, match.Stockpiles.Energy(0));
    }

    /// <summary>Design 5.4: an Outpost needs no territory and gives one, once it is finished.</summary>
    [Fact]
    public void AnOutpostGivesItsTerritoryWhenItIsFinished()
    {
        var match = WithACapitol();
        var builder = match.WorkerAt(0, 22, 9);
        var other = match.WorkerAt(0, 24, 9);
        match.OrderBuild(0, "outpost", 22, 10, builder);
        match.Simulation.Tick();
        var outpost = match.StructureAt(22, 10);

        // 45 seconds are 900 ticks. Until then the ground beside it is nobody's.
        match.Simulation.Run(899);
        match.OrderBuild(0, "generator", 25, 10, other);
        match.Simulation.Tick();

        Assert.True(match.Construction.IsFinished(outpost));
        Assert.Equal(Territory.Around(0, 22, 10, 3, 5), match.Territories.Get(outpost));
        Assert.Equal([(other, BuildProblem.OutsideTerritory)], match.Problems(SimEventKind.BuildRefused));

        match.OrderBuild(0, "generator", 25, 10, other);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(25, 10));
    }

    /// <summary>Design 2.5: an Academy's research slot is there when the Academy is finished.</summary>
    [Fact]
    public void AnAcademyOpensItsResearchSlotWhenItIsFinished()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "academy", 10, 10, worker);
        match.Simulation.Run(900);
        var academy = match.StructureAt(10, 10);

        Assert.False(match.Research.State.Slot(0, 1).IsOpen);
        match.Simulation.Tick();

        Assert.True(match.Research.State.Slot(0, 1).IsOpen);
        Assert.Equal(academy, match.Research.State.Slot(0, 1).Holder);
    }

    /// <summary>
    /// A Capitol that is not finished is not yet a Capitol to the rule of design 1.3: it does not
    /// keep the player's research slot open, and its fall closes nothing.
    /// </summary>
    [Fact]
    public void ACapitolUnderConstructionNeitherHoldsThePlayersResearchSlotNorClosesIt()
    {
        var match = WithACapitol();
        var capitol = match.StructureAt(6, 10);
        var worker = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "capitol", 10, 10, worker);
        match.Simulation.Tick();
        var second = match.StructureAt(10, 10);

        match.Raze(second);
        match.Simulation.Tick();
        Assert.True(match.Research.State.Slot(0, 0).IsOpen);

        match.OrderBuild(0, "capitol", 10, 10, worker);
        match.Simulation.Tick();
        var third = match.StructureAt(10, 10);
        match.Raze(capitol);
        match.Simulation.Tick();
        Assert.False(match.Research.State.Slot(0, 0).IsOpen);
        Assert.Equal(0, match.Territories.Count);

        // 90 seconds after it was laid the new one is finished, and the slot is open again.
        match.Simulation.Run(1798);
        Assert.False(match.Research.State.Slot(0, 0).IsOpen);
        match.Simulation.Tick();
        Assert.True(match.Research.State.Slot(0, 0).IsOpen);
        Assert.Equal(Territory.Around(0, 10, 10, 2, 6), match.Territories.Get(third));
    }

    /// <summary>Design 8.4, row 23.</summary>
    [Fact]
    public void TheWorkerGoesBackToItsWorkWhenTheFoundationIsLaid()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 100, 0);
        var deposit = match.SiteAt(2, 10);
        var worker = match.WorkerAt(0, 4, 9);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);

        match.OrderBuild(0, "generator", 10, 10, worker);
        match.RunUntilOrderEnds(worker);

        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(deposit, match.WorkerOf(worker).Site);
        Assert.NotEqual(WorkerState.Away, match.StateOf(worker));
        match.RunUntilWorking(worker);
        var before = match.Stockpiles.Materials(0);
        match.Simulation.Run(SimInfo.TicksPerSecond);
        Assert.Equal(before + EconomyMatches.MaterialsPayment, match.Stockpiles.Materials(0));
    }

    /// <summary>Design 8.4, row 29: the site is judged again when the worker arrives, with the state as it is then.</summary>
    [Fact]
    public void ASiteWhoseTerritoryIsLostOnTheWayIsRefusedOnArrivalWithAFullRefund()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 4);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Run(5);

        match.Raze(match.StructureAt(6, 10));
        var tick = match.RunUntilOrderEnds(worker);

        AssertEndedOnArrival(match, worker, BuildProblem.OutsideTerritory, new TileSquare(10, 10, 2));
        Assert.True(tick > 20);
        Assert.Equal((1000, 1000), match.StockOf(0));
    }

    [Fact]
    public void ASiteThatIsTakenOnTheWayIsRefusedOnArrivalWithAFullRefund()
    {
        var match = WithACapitol();
        var near = match.WorkerAt(0, 11, 8);
        var far = match.WorkerAt(0, 10, 4);

        // The two footprints share the tiles (11, 10) and (11, 11). Both are charged.
        match.OrderBuild(0, "generator", 11, 10, near);
        match.OrderBuild(0, "generator", 10, 10, far);
        match.Simulation.Tick();
        Assert.Equal((800, 1000), match.StockOf(0));
        match.RunUntilOrderEnds(near);
        Assert.NotEqual(EntityId.None, match.StructureAt(11, 10));

        match.RunUntilOrderEnds(far);

        AssertEndedOnArrival(match, far, BuildProblem.SiteNotFree, new TileSquare(10, 10, 2));
        Assert.Equal((900, 1000), match.StockOf(0));
        Assert.Equal(2, match.Structures.Count);
    }

    [Fact]
    public void AStructureWhoseLimitIsReachedOnTheWayIsRefusedOnArrivalWithAFullRefund()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 4);
        match.OrderBuild(0, "academy", 10, 10, worker);
        match.Simulation.Run(5);

        match.PlaceStructure(0, "academy", 3, 9);
        match.PlaceStructure(0, "academy", 3, 12);
        match.RunUntilOrderEnds(worker);

        AssertEndedOnArrival(match, worker, BuildProblem.LimitReached, new TileSquare(10, 10, 2));
        Assert.Equal((1000, 1000), match.StockOf(0));
    }

    /// <summary>
    /// Design 5.4 and 8.4, row 29: territories of different players may not overlap, which is
    /// judged once, on arrival, and the enemy's unfinished structures count.
    /// </summary>
    [Theory]
    [InlineData(31, true)]
    [InlineData(30, false)]
    public void AnOutpostWhoseTerritoryWouldOverlapAnEnemysIsRefusedOnArrival(int x, bool laid)
    {
        var match = WithACapitol();
        match.Stockpiles.Add(1, 250, 0);
        var theirs = match.WorkerAt(0, 22, 3);
        var worker = match.WorkerAt(1, x, 3);

        // Player 0's Outpost is a foundation with its center at (22.5, 5.5), and both radii are 5.
        match.OrderBuild(0, "outpost", 21, 4, theirs);
        match.Simulation.Tick();
        Assert.False(match.Construction.IsFinished(match.StructureAt(21, 4)));

        // A player's own territories may overlap.
        Assert.False(match.Construction.IsTooCloseToEnemyTerritory(0, match.Def("outpost"), 24, 4));

        match.OrderBuild(1, "outpost", x, 4, worker);
        match.Simulation.Tick();

        Assert.Equal(!laid, match.Construction.IsTooCloseToEnemyTerritory(1, match.Def("outpost"), x, 4));
        Assert.Equal(laid, match.StructureAt(x, 4) != EntityId.None);
        Assert.Equal(laid ? 0 : 250, match.StockOf(1).Materials);
        if (!laid)
        {
            AssertEndedOnArrival(match, worker, BuildProblem.TooCloseToEnemyTerritory, new TileSquare(x, 4, 3));
        }
    }

    /// <summary>A structure that gives no territory may stand anywhere in its owner's, however near the enemy's.</summary>
    [Fact]
    public void AStructureWithoutTerritoryIsNeverTooCloseToTheEnemys()
    {
        var match = WithACapitol();
        match.PlaceStructure(1, "outpost", 15, 10);

        Assert.False(match.Construction.IsTooCloseToEnemyTerritory(0, match.Def("generator"), 11, 10));
        Assert.True(match.Construction.IsTooCloseToEnemyTerritory(0, match.Def("outpost"), 11, 14));
    }

    /// <summary>Design 8.4, row 30.</summary>
    [Fact]
    public void AnEnemyGroundUnitOnTheSiteStopsTheFoundationWithAFullRefund()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        var enemy = match.SpawnUnit(1, MapGrid.TileCenter(11, 11), StructureMatches.QuarterTile);

        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();

        AssertEndedOnArrival(match, worker, BuildProblem.EnemyOnSite, new TileSquare(10, 10, 2));
        Assert.Equal((1000, 1000), match.StockOf(0));
        Assert.Equal(MapGrid.TileCenter(11, 11), match.PositionOf(enemy));

        // One step off the footprint and it is in nobody's way.
        match.Bodies.Get(enemy).Position = TestMatches.Point("12.01", "11.5");
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(TestMatches.Point("12.01", "11.5"), match.PositionOf(enemy));
    }

    /// <summary>Design 8.4, row 30.</summary>
    [Fact]
    public void TheOwnersUnitsOnTheSiteAreMovedToTheNearestTileOutsideAndKeepTheirOrders()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        var standing = match.SpawnChassis(0, 0, TestMatches.Point("10.4", "10.6"));
        var walking = match.SpawnUnit(0, MapGrid.TileCenter(11, 11), StructureMatches.QuarterTile);
        var destination = MapGrid.TileCenter(11, 15);

        match.OrderBuild(0, "generator", 10, 10, worker);
        match.OrderMove(0, destination, walking);
        match.Simulation.Tick();

        // Each went to the tile outside whose center was nearest: to the left, and below.
        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(MapGrid.TileCenter(9, 10), match.PositionOf(standing));
        Assert.Equal(MapGrid.TileCenter(11, 12), match.PositionOf(walking));

        // The one that stood has its post where it stands now, and the one on its way goes on.
        Assert.Equal(MapGrid.TileCenter(9, 10), match.Combatants.Get(standing).Anchor);
        Assert.NotEqual(MoveState.Idle, match.MoveStateOf(walking));
        match.RunUntilIdle(walking);
        Assert.Equal(destination, match.PositionOf(walking));
        Assert.Equal(MapGrid.TileCenter(9, 10), match.PositionOf(standing));
    }

    /// <summary>Of tiles equally near, the one with the lower y takes the unit, and then the one with the lower x.</summary>
    [Theory]
    [InlineData(10, 10, 10, 9)]
    [InlineData(11, 10, 11, 9)]
    [InlineData(10, 11, 9, 11)]
    [InlineData(11, 11, 12, 11)]
    public void AUnitInTheMiddleOfATileOfTheSiteGoesToTheTileWithTheLowerY(int x, int y, int toX, int toY)
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 13, 13);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(x, y), StructureMatches.QuarterTile);
        match.Bodies.Get(worker).Position = TestMatches.Point("12.5", "12.5");

        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(MapGrid.TileCenter(toX, toY), match.PositionOf(unit));
    }

    /// <summary>The worker that builds is one of the owner's units: on the site, it is moved off it too.</summary>
    [Fact]
    public void AWorkerThatStandsOnItsOwnSiteIsMovedOffIt()
    {
        var match = WithACapitol();
        var worker = match.SpawnWorker(0, TestMatches.Point("11.5", "11.75"));
        match.OrderBuild(0, "generator", 10, 10, worker);

        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(MapGrid.TileCenter(11, 12), match.PositionOf(worker));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
    }

    /// <summary>A unit on a site with no ground beside it that it can stand on goes to the nearest tile that there is.</summary>
    [Fact]
    public void AUnitOnASiteWalledInIsMovedToTheNearestGroundThereIs()
    {
        var match = Create(TestMaps.FromRows(
            "..........",
            "..####....",
            "..#..#....",
            "..#..#....",
            "..####....",
            ".........."));
        match.PlaceTerritory(0, 0, 0, 1, 40);
        match.Stockpiles.Add(0, 100, 0);
        var worker = match.WorkerAt(0, 3, 2);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(4, 3), StructureMatches.QuarterTile);

        match.OrderBuild(0, "generator", 3, 2, worker);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(3, 2));
        Assert.Equal(MapGrid.TileCenter(3, 0), match.PositionOf(worker));
        Assert.Equal(MapGrid.TileCenter(6, 3), match.PositionOf(unit));
    }

    /// <summary>A site the worker cannot get to: it walks as near as it can, and the order ends there.</summary>
    [Fact]
    public void AnOrderForASiteTheWorkerCannotReachEndsWithAFullRefund()
    {
        var match = Create(TestMaps.FromRows(
            "............",
            ".......####.",
            ".......#..#.",
            ".......#..#.",
            ".......####.",
            "............"));
        match.PlaceTerritory(0, 0, 0, 1, 40);
        match.Stockpiles.Add(0, 100, 0);
        var worker = match.WorkerAt(0, 1, 2);

        match.OrderBuild(0, "generator", 8, 2, worker);
        match.Simulation.Tick();
        Assert.Equal((0, 0), match.StockOf(0));
        match.RunUntilOrderEnds(worker);

        AssertEndedOnArrival(match, worker, BuildProblem.CannotReach, new TileSquare(8, 2, 2));
        Assert.Equal((100, 0), match.StockOf(0));
        Assert.Equal(0, match.Structures.Count);
    }

    /// <summary>
    /// A worker of a work site stands on a tile beside it. A foundation laid on that tile takes
    /// the place, and the worker takes another.
    /// </summary>
    [Fact]
    public void AWorkerWhosePlaceIsUnderAFoundationTakesAnotherPlace()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((10, 12)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 100, 0);
        var deposit = match.SiteAt(10, 12);
        var gatherer = match.WorkerAt(0, 10, 9);
        match.OrderGather(0, deposit, gatherer);
        match.RunUntilWorking(gatherer);
        var (x, y) = EconomyMatches.TileOf(match, gatherer);
        Assert.Equal((10, 11), (x, y));
        var builder = match.WorkerAt(0, 9, 9);

        match.OrderBuild(0, "generator", 10, 10, builder);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(deposit, match.WorkerOf(gatherer).Site);
        Assert.False(new TileSquare(10, 10, 2).Covers(EconomyMatches.TileOf(match, gatherer).X, EconomyMatches.TileOf(match, gatherer).Y));
        match.RunUntilWorking(gatherer);
        Assert.Equal(EconomyMatches.TileOf(match, gatherer), match.StandsOn(gatherer));
        Assert.True(match.Grid.IsPassable(match.PositionOf(gatherer)));
    }

    /// <summary>Design 8.4, rows 24 and 25: the foundation is a structure like any other to those who shoot at it, and its price is lost with it.</summary>
    [Fact]
    public void AFoundationThatIsDestroyedGivesNothingBack()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Run(10);
        var generator = match.StructureAt(10, 10);

        match.Raze(generator);
        match.Simulation.Tick();

        Assert.Equal((900, 1000), match.StockOf(0));
        Assert.Equal(EntityId.None, match.StructureAt(10, 10));
        Assert.True(match.Grid.IsPassable(10, 10));
        Assert.Single(match.EventsOf(SimEventKind.Death));
        Assert.Single(match.EventsOf(SimEventKind.StructureRemoved));
        match.Simulation.Tick();
        Assert.Equal((0, 0, 1), (match.Foundations.Count, match.BuildOrders.Count, match.Structures.Count));
    }

    /// <summary>Design 8.4, rows 24 and 26: a foundation has all its hit points at once, and nothing gives back the ones it loses.</summary>
    [Fact]
    public void ADamagedFoundationIsNotRepairedByBeingBuilt()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();
        var generator = match.StructureAt(10, 10);
        match.Health.Get(generator).HitPoints = 120_000;

        match.Simulation.Run(500);

        Assert.True(match.Construction.IsFinished(generator));
        Assert.Equal((120_000, 500_000), (match.Health.Get(generator).HitPoints, match.Health.Get(generator).Maximum));
        match.Simulation.Run(200);
        Assert.Equal(120_000, match.Health.Get(generator).HitPoints);
    }

    /// <summary>The order ended as the worker arrived: the player is told why and where, and the worker stands without work.</summary>
    private static void AssertEndedOnArrival(Match match, EntityId worker, BuildProblem why, TileSquare site)
    {
        var ended = Assert.Single(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal((worker, why, site.Center), (ended.Entity, (BuildProblem)ended.Amount, ended.Position));
        Assert.Equal(match.Bodies.Get(worker).Owner, ended.Player);
        Assert.False(match.BuildOrders.Contains(worker));
        Assert.Equal(EntityId.None, match.StructureAt(site.X, site.Y));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
        Assert.Equal(default, match.WorkerOf(worker));
    }
}
