using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Tests.Construction;
using Sim.Tests.Data;
using Sim.Tests.Economy;
using Sim.Tests.Structures;
using Sim.Walls;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// The wall order when it is given (design 2.5; design 8.4, row 41): a straight run of tiles,
/// paid in full, for one worker, which starts at the end nearer to it.
/// </summary>
/// <remarks>
/// The match is that of <see cref="ConstructionMatches.WithACapitol"/>: player 0 holds the tiles
/// whose centers are within 6 tiles of the point (7, 11), which in the rows 13 and 14 are the
/// tiles 2 to 11, and has 1,000 of each resource. A wall tile costs 20 Materials.
/// </remarks>
public class WallOrderTests
{
    [Fact]
    public void TheRunIsPaidInFullWhenItIsOrderedAndOneWorkerWalksToIt()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        match.Simulation.Tick();

        Assert.Equal((880, 1000), match.StockOf(0));
        Assert.Equal([(4, 13), (5, 13), (6, 13), (7, 13), (8, 13), (9, 13)], match.PlansOf(worker));
        Assert.All(
            Enumerable.Range(0, match.WallPlans.Count),
            i => Assert.Equal(
                match.WallPlans.ValueAt(i) with { X = 0, Y = 0 },
                new WallPlan { Worker = worker, Owner = 0, Materials = 20, Energy = 0 }));
        Assert.True(match.Walls.Has(worker));
        Assert.True(match.Construction.HasOrder(worker));
        Assert.NotEqual(MoveState.Idle, match.MoveStateOf(worker));
        Assert.Empty(match.WallOf(0));
        Assert.True(match.Grid.IsPassable(4, 13));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Empty(match.EventsOf(SimEventKind.BuildRefused));
    }

    /// <summary>The first tile is the tile of the command's point, wherever in the tile the point is; the last is named by its number on the map.</summary>
    [Fact]
    public void TheFirstTileHoldsThePointAndTheLastIsNamedByItsNumber()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 2, 15);
        var point = Setup.TestMatches.Point("4.99", "13.01");
        match.Simulation.Commands.Enqueue(0, new Command(CommandType.BuildWall, 0, [worker], point.X, point.Y, contentId: (15 * 40) + 7));

        match.Simulation.Tick();

        Assert.Equal([(4, 13), (5, 14), (6, 14), (7, 15)], match.PlansOf(worker));
    }

    /// <summary>A worker starts at the end of the run that is nearer to it, and at the first tile when both are equally near.</summary>
    [Theory]
    [InlineData(2, 15, 4)]
    [InlineData(11, 15, 9)]
    [InlineData(6, 16, 4)]
    [InlineData(7, 16, 9)]
    public void TheWorkerStartsAtTheEndNearerToIt(int workerX, int workerY, int startsAt)
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, workerX, workerY);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        match.Simulation.Tick();

        var plans = match.PlansOf(worker);
        Assert.Equal(6, plans.Length);
        Assert.Equal((startsAt, 13), plans[0]);
        Assert.Equal((13 - startsAt, 13), plans[^1]);
    }

    /// <summary>
    /// A tile that cannot be laid when the order is given is skipped then and never paid for:
    /// the run costs what it adds.
    /// </summary>
    [Theory]
    // Across the Capitol, which stands on the tiles 6 and 7 of the row.
    [InlineData(4, 10, 9, 10, new[] { 4, 5, 8, 9 })]
    // From outside the territory to outside it: the row 13 is the player's from 2 to 11.
    [InlineData(0, 13, 14, 13, new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })]
    public void TilesThatCannotBeLaidWhenTheOrderIsGivenAreNotPaidFor(int firstX, int y, int lastX, int lastY, int[] laid)
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 0, y + 2);
        match.OrderWall(0, (firstX, y), (lastX, lastY), worker);

        match.Simulation.Tick();

        Assert.Equal(laid.Select(x => (x, y)), match.PlansOf(worker));
        Assert.Equal((1000 - (20 * laid.Length), 1000), match.StockOf(0));
    }

    /// <summary>The interface asks the same rule for the run it shows and its price.</summary>
    [Fact]
    public void TheTilesToLayAreThoseTheOrderPlans()
    {
        var match = WithACapitol();
        var tiles = new List<(int X, int Y)>();

        Assert.Equal(BuildProblem.None, match.Walls.TilesToLay(0, 4, 10, 9, 10, tiles));
        Assert.Equal([(4, 10), (5, 10), (8, 10), (9, 10)], tiles);

        tiles.Clear();
        Assert.Equal(BuildProblem.OutsideTerritory, match.Walls.TilesToLay(0, 20, 5, 24, 5, tiles));
        Assert.Equal(BuildProblem.OutsideTerritory, match.Walls.TilesToLay(1, 4, 13, 9, 13, tiles));
        Assert.Equal(BuildProblem.SiteNotFree, match.Walls.TilesToLay(0, 6, 10, 7, 11, tiles));
        Assert.Equal(BuildProblem.SiteNotFree, match.Walls.TilesToLay(0, 4, 13, 40, 13, tiles));
        Assert.Equal(BuildProblem.NoStructure, match.Walls.TilesToLay(2, 4, 13, 9, 13, tiles));
        Assert.Empty(tiles);
    }

    public static TheoryData<int, int, int, int, int, BuildProblem> OrdersThatAreRefused() => new()
    {
        // No tile of the run can be laid: on the Capitol, and outside the territory.
        { 6, 10, 7, 11, 0, BuildProblem.SiteNotFree },
        { 20, 5, 24, 5, 0, BuildProblem.OutsideTerritory },
        // The first tile is off the map, and the last is.
        { -1, 13, 5, 13, 0, BuildProblem.SiteNotFree },
        { 4, 13, 40, 13, 0, BuildProblem.SiteNotFree },
        { 4, 13, 4, -1, 0, BuildProblem.SiteNotFree },
        // Player 1 has no territory.
        { 4, 13, 9, 13, 1, BuildProblem.OutsideTerritory },
    };

    [Theory]
    [MemberData(nameof(OrdersThatAreRefused))]
    public void AnOrderThatBreaksARuleIsRefusedAndItsPlayerIsToldWhy(int firstX, int firstY, int lastX, int lastY, int player, BuildProblem why)
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(player, 2, 15);
        match.Stockpiles.Add(1, 1000, 1000);
        var point = MapGrid.TileCenter(firstX, firstY);
        var last = lastX is < 0 or >= 40 || lastY < 0 ? -7 : (lastY * 40) + lastX;
        match.Simulation.Commands.Enqueue(0, new Command(CommandType.BuildWall, player, [worker], point.X, point.Y, contentId: last));

        match.Simulation.Tick();

        var refused = Assert.Single(match.EventsOf(SimEventKind.BuildRefused));
        Assert.Equal((player, worker, point, (long)why), (refused.Player, refused.Entity, refused.Position, refused.Amount));
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(0, match.WallPlans.Count);
        Assert.Equal((1000, 1000), match.StockOf(player));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
    }

    [Fact]
    public void AnOrderThatListsNoWorkerOfThePlayerIsRefused()
    {
        var match = WithACapitol();
        var theirs = match.WorkerAt(1, 2, 15);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(3, 15), StructureMatches.QuarterTile);
        match.OrderWall(0, (4, 13), (9, 13), theirs, unit, new EntityId(99));

        match.Simulation.Tick();

        Assert.Equal([(theirs, BuildProblem.NoWorker)], match.Reasons(SimEventKind.BuildRefused));
        Assert.Equal((1000, 1000), match.StockOf(0));
    }

    [Fact]
    public void ARunThePlayerCannotPayInFullIsRefused()
    {
        var match = WithACapitol();
        match.Stockpiles.TryPay(0, 890, 0);
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        match.Simulation.Tick();

        Assert.Equal([(worker, BuildProblem.CannotAfford)], match.Reasons(SimEventKind.BuildRefused));
        Assert.Equal((110, 1000), match.StockOf(0));

        // One tile less is covered to the last Material.
        match.Stockpiles.TryPay(0, 10, 0);
        match.OrderWall(0, (4, 13), (8, 13), worker);
        match.Simulation.Tick();

        Assert.Equal((0, 1000), match.StockOf(0));
        Assert.Equal(5, match.PlansOf(worker).Length);
    }

    public static TheoryData<GameData, BuildProblem> DataWithoutAWallToBuild() => new()
    {
        // The data has no wall.
        { EconomyMatches.Data, BuildProblem.NoStructure },
        // The wall needs a tier of Protection the player has not reached.
        { DataAssert.Valid(Files().Edit("structures/wall.jsonc", "\"footprint\": 1,", "\"footprint\": 1, \"unlock_tier\": 2,")), BuildProblem.Locked },
    };

    [Theory]
    [MemberData(nameof(DataWithoutAWallToBuild))]
    public void AWallThePlayerMayNotBuildIsRefused(GameData data, BuildProblem why)
    {
        var match = WithACapitol(data);
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);

        match.Simulation.Tick();

        Assert.Equal([(worker, why)], match.Reasons(SimEventKind.BuildRefused));
        Assert.Equal((1000, 1000), match.StockOf(0));
    }

    /// <summary>As for a build order: a worker without an order goes before a nearer one that has one.</summary>
    [Fact]
    public void AWorkerWithoutAnOrderTakesItBeforeANearerOneThatHasOne()
    {
        var match = WithACapitol();
        var near = match.WorkerAt(0, 4, 15);
        var far = match.WorkerAt(0, 11, 20);
        match.OrderWall(0, (9, 8), (4, 8), near);
        match.Simulation.Tick();

        match.OrderWall(0, (4, 13), (9, 13), near, far);
        match.Simulation.Tick();

        Assert.Equal(6, match.PlansOf(near).Count(tile => tile.Y == 8));
        Assert.Equal([(9, 13), (8, 13), (7, 13), (6, 13), (5, 13), (4, 13)], match.PlansOf(far));
        Assert.Equal((760, 1000), match.StockOf(0));
        Assert.Empty(match.EventsOf(SimEventKind.BuildOrderEnded));
    }

    /// <summary>A worker has one order: a new one replaces the old, which gives back what it holds, and that counts toward the price.</summary>
    [Fact]
    public void ANewWallOrderReplacesTheOldOneWhichIsGivenBack()
    {
        var match = WithACapitol();
        match.Stockpiles.TryPay(0, 880, 0);
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        match.Simulation.Tick();
        Assert.Equal((0, 1000), match.StockOf(0));

        match.OrderWall(0, (4, 14), (8, 14), worker);
        match.Simulation.Tick();

        Assert.Equal([(4, 14), (5, 14), (6, 14), (7, 14), (8, 14)], match.PlansOf(worker));
        Assert.Equal((20, 1000), match.StockOf(0));
        var ended = Assert.Single(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal(
            (0, worker, MapGrid.TileCenter(4, 13), (long)BuildProblem.OtherOrder),
            (ended.Player, ended.Entity, ended.Position, ended.Amount));
    }

    [Fact]
    public void AWallOrderAndABuildOrderReplaceEachOther()
    {
        var match = WithACapitol();
        match.Stockpiles.TryPay(0, 880, 0);
        var worker = match.WorkerAt(0, 2, 15);
        match.OrderWall(0, (4, 13), (9, 13), worker);
        match.Simulation.Tick();

        // The Generator costs 100, and the wall gives 120 back.
        match.OrderBuild(0, "generator", 9, 7, worker);
        match.Simulation.Tick();

        Assert.False(match.Walls.Has(worker));
        Assert.True(match.BuildOrders.Contains(worker));
        Assert.Equal((20, 1000), match.StockOf(0));
        Assert.Equal([(worker, BuildProblem.OtherOrder)], match.Reasons(SimEventKind.BuildOrderEnded));

        // And back: the build order gives its 100 back, and five tiles cost 100.
        match.OrderWall(0, (4, 14), (8, 14), worker);
        match.Simulation.Tick();

        Assert.False(match.BuildOrders.Contains(worker));
        Assert.Equal(5, match.PlansOf(worker).Length);
        Assert.Equal((20, 1000), match.StockOf(0));
        Assert.Equal([(worker, BuildProblem.OtherOrder)], match.Reasons(SimEventKind.BuildOrderEnded));
    }

    /// <summary>A build order goes to a worker without a wall order before a nearer one that has one.</summary>
    [Fact]
    public void ABuildOrderPassesOverAWorkerThatHasAWallOrder()
    {
        var match = WithACapitol();
        var near = match.WorkerAt(0, 8, 9);
        var far = match.WorkerAt(0, 2, 20);
        match.OrderWall(0, (4, 14), (8, 14), near);
        match.Simulation.Tick();

        match.OrderBuild(0, "generator", 9, 7, near, far);
        match.Simulation.Tick();

        Assert.True(match.Walls.Has(near));
        Assert.True(match.BuildOrders.Contains(far));
    }
}
