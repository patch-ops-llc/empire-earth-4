using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Construction;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Structures;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// A wall order being carried out (design 2.5; design 8.4, rows 22, 30 and 41): the worker lays
/// the tiles one after another, each a structure of its own that builds itself in 5 seconds; a
/// tile that cannot be laid is skipped and given back; an order that ends early gives back every
/// tile not yet laid.
/// </summary>
public class WallLayingTests
{
    private const int TileSeconds = 5;

    [Fact]
    public void TheWorkerLaysTheTilesOneAfterAnotherFromItsEnd()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        var placed = new List<(int Tick, EntityId Tile)>();
        var finished = new List<(int Tick, EntityId Tile)>();
        for (var tick = 0; tick < 400; tick++)
        {
            match.Simulation.Tick();
            placed.AddRange(match.EventsOf(SimEventKind.StructurePlaced).Select(laid => (tick, laid.Entity)));
            finished.AddRange(match.EventsOf(SimEventKind.StructureFinished).Select(done => (tick, done.Entity)));
            Assert.Empty(match.EventsOf(SimEventKind.WallTileSkipped));
            Assert.Empty(match.EventsOf(SimEventKind.BuildOrderEnded));
        }

        // Six structures of one tile, laid in the order of the run, never two in a tick.
        Assert.Equal([(4, 13), (5, 13), (6, 13), (7, 13), (8, 13), (9, 13)], match.WallOf(0));
        Assert.Equal(6, placed.Select(laid => laid.Tick).Distinct().Count());
        Assert.Equal(placed.Select(laid => laid.Tile), placed.Select(laid => laid.Tile).Order());
        Assert.All(placed, laid => Assert.Equal(1, match.Structures.Get(laid.Tile).Size));

        // Each builds itself in 5 seconds from the tick it was laid in, with no worker beside it.
        Assert.Equal(placed.Select(laid => (laid.Tick + (TileSeconds * SimInfo.TicksPerSecond), laid.Tile)), finished);

        // Nothing was given back, and the order is over.
        Assert.Equal((880, 1000), match.StockOf(0));
        Assert.Equal(0, match.WallPlans.Count);
        Assert.False(match.Construction.HasOrder(worker));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
    }

    /// <summary>A tile blocks everyone from the tick it is laid in, while it still builds itself, and its price is the tile's.</summary>
    [Fact]
    public void ATileBlocksAsSoonAsItIsLaidAndIsAFoundationOfItsOwn()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        while (match.WallOf(0).Length == 0)
        {
            match.Simulation.Tick();
        }

        var tile = match.StructureAt(4, 13);
        Assert.False(match.Grid.IsPassable(4, 13));
        Assert.True(match.Grid.IsPassable(5, 13));
        Assert.Equal(new Foundation { Built = 0, Materials = 20, Energy = 0 }, match.Foundations.Get(tile));
        Assert.Equal(400_000, match.Health.Get(tile).HitPoints);
        Assert.Equal([(5, 13), (6, 13), (7, 13), (8, 13), (9, 13)], match.PlansOf(worker));

        // The worker stands beside the tile it laid, within reach of it, and walks on.
        Assert.NotEqual((4, 13), match.StandsOn(worker));
        Assert.InRange(
            new TileSquare(4, 13, 1).DistanceSquaredRawTo(match.Bodies.Get(worker).Position),
            1,
            FixedVector2.SquareRaw(ConstructionRules.ArrivalReach));
        Assert.NotEqual(MoveState.Idle, match.MoveStateOf(worker));
    }

    /// <summary>The tiles of a diagonal run touch at their corners only; the worker gets round each to lay the next.</summary>
    [Fact]
    public void ADiagonalRunIsLaidToItsLastTile()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 6);
        match.OrderWall(0, (4, 6), (9, 11), worker);

        match.RunUntilWallOrderEnds(worker);

        Assert.Equal([(4, 6), (5, 7), (6, 8), (7, 9), (8, 10), (9, 11)], match.WallOf(0));
        Assert.Equal((880, 1000), match.StockOf(0));
        Assert.NotEqual(0, match.Grid.RegionOf(match.Bodies.Get(worker).Position));
    }

    /// <summary>Design 8.4, row 30: an enemy ground unit on the tile stops the tile, which is given back; the run goes on.</summary>
    [Fact]
    public void ATileAnEnemyStandsOnIsSkippedAndGivenBack()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        var enemy = match.SpawnUnit(1, MapGrid.TileCenter(6, 13), StructureMatches.QuarterTile);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        var skipped = new List<SimEvent>();
        for (var tick = 0; tick < 400; tick++)
        {
            match.Simulation.Tick();
            skipped.AddRange(match.EventsOf(SimEventKind.WallTileSkipped));
        }

        Assert.Equal([(4, 13), (5, 13), (7, 13), (8, 13), (9, 13)], match.WallOf(0));
        Assert.Equal((900, 1000), match.StockOf(0));
        var skip = Assert.Single(skipped);
        Assert.Equal(
            (0, worker, MapGrid.TileCenter(6, 13), (long)BuildProblem.EnemyOnSite),
            (skip.Player, skip.Entity, skip.Position, skip.Amount));
        Assert.Equal((6, 13), match.StandsOn(enemy));
        Assert.False(match.Construction.HasOrder(worker));
    }

    /// <summary>Design 8.4, row 30: the owner's own units on the tile are moved off it.</summary>
    [Fact]
    public void TheOwnersUnitsOnATileAreMovedOffIt()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        var own = match.SpawnUnit(0, MapGrid.TileCenter(6, 13), StructureMatches.QuarterTile);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        match.RunUntilWallOrderEnds(worker);

        Assert.Equal(6, match.WallOf(0).Length);
        Assert.NotEqual((6, 13), match.StandsOn(own));
        Assert.True(match.Grid.IsPassable(match.Bodies.Get(own).Position));
    }

    /// <summary>The placement is judged again when the worker comes to each tile, with the state as it is then.</summary>
    [Fact]
    public void TilesThatCanNoLongerBeLaidWhenTheWorkerComesAreSkippedAndGivenBack()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        match.Simulation.Tick();

        // A Generator comes to stand on two tiles of the run.
        match.PlaceStructure(0, "generator", 6, 13);
        var skipped = new List<(EntityId Entity, BuildProblem Why)>();
        while (match.Walls.Has(worker))
        {
            match.Simulation.Tick();
            skipped.AddRange(match.Reasons(SimEventKind.WallTileSkipped));
        }

        Assert.Equal([(4, 13), (5, 13), (8, 13), (9, 13)], match.WallOf(0));
        Assert.Equal([(worker, BuildProblem.SiteNotFree), (worker, BuildProblem.SiteNotFree)], skipped);
        Assert.Equal((920, 1000), match.StockOf(0));
    }

    [Fact]
    public void TilesWhoseTerritoryIsLostAreSkippedAndGivenBack()
    {
        var match = Create(TestMaps.Open(40, 24));
        var territory = match.PlaceTerritory(0, 6, 10, 2, 6);
        match.Stockpiles.Add(0, 1000, 1000);
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        while (match.WallOf(0).Length < 2)
        {
            match.Simulation.Tick();
        }

        match.World.Entities.Destroy(territory);
        var skipped = new List<(EntityId Entity, BuildProblem Why)>();
        while (match.Walls.Has(worker))
        {
            match.Simulation.Tick();
            skipped.AddRange(match.Reasons(SimEventKind.WallTileSkipped));
        }

        Assert.Equal([(4, 13), (5, 13)], match.WallOf(0));
        Assert.Equal(Enumerable.Repeat((worker, BuildProblem.OutsideTerritory), 4), skipped);
        Assert.Equal((960, 1000), match.StockOf(0));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
    }

    /// <summary>A tile in ground the worker cannot get to: it stops at the nearest tile it can reach, and the tile is skipped.</summary>
    [Fact]
    public void ATileTheWorkerCannotReachIsSkippedAndGivenBack()
    {
        var match = Create(TestMaps.FromRows(
            "..............",
            "..............",
            "......###.....",
            "......#.#.....",
            "......###.....",
            "..............",
            ".............."));
        match.PlaceTerritory(0, 0, 0, 1, 40);
        match.Stockpiles.Add(0, 100, 0);
        var worker = match.WorkerAt(0, 2, 5);
        match.OrderWall(0, (4, 3), (10, 3), worker);
        match.Simulation.Tick();
        Assert.Equal([(4, 3), (5, 3), (7, 3), (9, 3), (10, 3)], match.PlansOf(worker));

        var skipped = new List<SimEvent>();
        while (match.Walls.Has(worker))
        {
            match.Simulation.Tick();
            skipped.AddRange(match.EventsOf(SimEventKind.WallTileSkipped));
        }

        Assert.Equal([(4, 3), (5, 3), (9, 3), (10, 3)], match.WallOf(0));
        var skip = Assert.Single(skipped);
        Assert.Equal((MapGrid.TileCenter(7, 3), (long)BuildProblem.CannotReach), (skip.Position, skip.Amount));
        Assert.Equal((20, 0), match.StockOf(0));
    }

    /// <summary>Design 8.4, row 41: cancelling gives back every tile not yet laid. What is laid stays.</summary>
    [Fact]
    public void CancellingTheOrderGivesBackEveryTileNotYetLaid()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        while (match.WallOf(0).Length < 2)
        {
            match.Simulation.Tick();
        }

        match.OrderCancelBuild(0, worker);
        match.Simulation.Tick();

        Assert.Equal((960, 1000), match.StockOf(0));
        Assert.Equal(0, match.WallPlans.Count);
        Assert.False(match.Construction.HasOrder(worker));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
        var ended = Assert.Single(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal(
            (0, worker, MapGrid.TileCenter(6, 13), (long)BuildProblem.Cancelled),
            (ended.Player, ended.Entity, ended.Position, ended.Amount));

        match.Simulation.Run(200);
        Assert.Equal([(4, 13), (5, 13)], match.WallOf(0));
        Assert.Equal(0, match.Foundations.Count);
        Assert.Equal((960, 1000), match.StockOf(0));
    }

    /// <summary>A tile that is laid is a structure that builds itself: cancelling it gives back 75% of its price, as for any.</summary>
    [Fact]
    public void ALaidTileIsCancelledAsAnyStructureThatBuildsItself()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        while (match.WallOf(0).Length < 1)
        {
            match.Simulation.Tick();
        }

        match.OrderCancelBuild(0, match.StructureAt(4, 13));
        match.Simulation.Tick();

        Assert.Equal((895, 1000), match.StockOf(0));
        Assert.True(match.Grid.IsPassable(4, 13));
        Assert.True(match.Walls.Has(worker));
    }

    public static TheoryData<CommandType> OtherOrders() => [CommandType.Move, CommandType.AttackMove, CommandType.Stop];

    /// <summary>As for a build order (design 8.4, row 12): any other order for the worker ends the wall order.</summary>
    [Theory]
    [MemberData(nameof(OtherOrders))]
    public void AnotherOrderForTheWorkerEndsTheWallOrder(CommandType order)
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        while (match.WallOf(0).Length < 1)
        {
            match.Simulation.Tick();
        }

        var point = MapGrid.TileCenter(3, 20);
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(order, 0, [worker], point.X, point.Y));
        match.Simulation.Tick();

        Assert.Equal((980, 1000), match.StockOf(0));
        Assert.Equal(0, match.WallPlans.Count);
        Assert.Equal([(worker, BuildProblem.OtherOrder)], match.Reasons(SimEventKind.BuildOrderEnded));

        match.Simulation.Run(200);
        Assert.Equal([(4, 13)], match.WallOf(0));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
    }

    /// <summary>Design 8.4, row 41: the death of the worker gives back every tile not yet laid, in the tick it dies.</summary>
    [Fact]
    public void TheDeathOfTheWorkerGivesBackEveryTileNotYetLaid()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        while (match.WallOf(0).Length < 3)
        {
            match.Simulation.Tick();
        }

        match.Raze(worker);
        match.Simulation.Tick();

        Assert.False(match.World.Entities.IsAlive(worker));
        Assert.Equal((940, 1000), match.StockOf(0));
        Assert.Equal(0, match.WallPlans.Count);
        var ended = Assert.Single(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal(
            (0, worker, MapGrid.TileCenter(7, 13), (long)BuildProblem.WorkerDied),
            (ended.Player, ended.Entity, ended.Position, ended.Amount));
        Assert.Equal(3, match.WallOf(0).Length);
    }

    /// <summary>As with a build order (design 4.1): the worker keeps its work site and goes back to it when the run is done.</summary>
    [Fact]
    public void TheWorkerGoesBackToItsWorkWhenTheRunIsDone()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 100, 0);
        var deposit = match.SiteAt(2, 10);
        var worker = match.WorkerAt(0, 4, 9);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);

        match.OrderWall(0, (4, 14), (6, 14), worker);
        match.Simulation.Tick();
        Assert.Equal(WorkerState.Away, match.StateOf(worker));
        var paid = match.Stockpiles.Materials(0);
        match.RunUntilWallOrderEnds(worker);

        Assert.Equal(paid, match.Stockpiles.Materials(0));
        Assert.Equal(3, match.WallOf(0).Length);
        Assert.Equal(deposit, match.WorkerOf(worker).Site);
        Assert.NotEqual(WorkerState.Away, match.StateOf(worker));
        match.RunUntilWorking(worker);
        var before = match.Stockpiles.Materials(0);
        match.Simulation.Run(SimInfo.TicksPerSecond);
        Assert.Equal(before + EconomyMatches.MaterialsPayment, match.Stockpiles.Materials(0));
    }

    /// <summary>A worker whose order is cancelled goes back to its work as well.</summary>
    [Fact]
    public void AWorkerWhoseWallOrderIsCancelledGoesBackToItsWork()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 100, 0);
        var deposit = match.SiteAt(2, 10);
        var worker = match.WorkerAt(0, 4, 9);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);
        match.OrderWall(0, (4, 14), (6, 14), worker);
        match.Simulation.Run(3);

        match.OrderCancelBuild(0, worker);
        match.Simulation.Tick();

        Assert.Equal(deposit, match.WorkerOf(worker).Site);
        Assert.NotEqual(WorkerState.Away, match.StateOf(worker));
        match.RunUntilWorking(worker);
    }
}
