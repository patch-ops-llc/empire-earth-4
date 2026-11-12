using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

/// <summary>Design 4.1: where the places of a deposit or Generator are, who takes which, who waits and who leaves.</summary>
public class PlacesTests
{
    private static readonly (int X, int Y)[] PlacesOfTheDeposit = [(10, 9), (11, 9), (9, 10), (12, 10), (9, 11), (12, 11), (10, 12), (11, 12)];

    private static EntityId[] Workers(Match match, int player, int count, int row) =>
        [.. Enumerable.Range(0, count).Select(i => match.WorkerAt(player, 2 + i, row))];

    [Fact]
    public void TheEightPlacesAreTheTilesThatShareASideWithTheDeposit()
    {
        var match = WithOneDeposit(out var deposit);
        var workers = Workers(match, 0, 8, 2);

        match.OrderGather(0, deposit, workers);
        match.RunUntilWorking(workers);

        Assert.Equal(PlacesOfTheDeposit, workers.Select(match.TileOf).OrderBy(tile => tile.Y).ThenBy(tile => tile.X));
        Assert.All(workers, worker => Assert.Equal(MapGrid.TileCenter(match.TileOf(worker).X, match.TileOf(worker).Y), match.PositionOf(worker)));
        Assert.Equal(8, match.Work.HoldersOf(match.World, deposit));

        var before = match.Stockpiles.Materials(0);
        match.Simulation.Run(20);
        Assert.Equal(before + (8 * MaterialsPayment), match.Stockpiles.Materials(0));
    }

    /// <summary>
    /// The nearest free place at the moment the order runs, ties to the lower y and then the lower
    /// x. Distances are from where the worker stands to the center of the place.
    /// </summary>
    [Theory]
    [InlineData("4.5", "10.5", 9, 10)]
    [InlineData("4.5", "11.5", 9, 11)]
    [InlineData("11", "7.5", 10, 9)]
    [InlineData("7.5", "11", 9, 10)]
    [InlineData("9", "9", 10, 9)]
    [InlineData("13", "13", 12, 11)]
    public void AWorkerTakesTheNearestFreePlaceWithTiesToTheLowerYThenTheLowerX(string x, string y, int placeX, int placeY)
    {
        var match = Create(OneDeposit);
        var site = match.SiteAt(10, 10);
        var worker = match.SpawnWorker(0, TestMatches.Point(x, y));

        match.OrderGather(0, site, worker);
        match.Simulation.Tick();

        Assert.Equal((placeX, placeY), match.TileOf(worker));
        Assert.Equal(WorkerState.Walking, match.StateOf(worker));
    }

    [Fact]
    public void APlaceIsHeldFromTheMomentTheOrderRunsAndTheNextWorkerTakesTheNextNearest()
    {
        var match = WithOneDeposit(out var deposit);
        var first = match.WorkerAt(0, 2, 10);
        var second = match.WorkerAt(0, 8, 10);

        // The second stands right beside the place the first is still walking to.
        match.OrderGather(0, deposit, first);
        match.Simulation.Tick();
        match.OrderGather(0, deposit, second);
        match.Simulation.Tick();

        Assert.Equal((9, 10), match.TileOf(first));
        Assert.Equal(WorkerState.Walking, match.StateOf(first));
        Assert.Equal((9, 11), match.TileOf(second));
    }

    [Fact]
    public void AWorkerSentToAFullDepositWaitsBesideItAndEarnsNothing()
    {
        var match = WithOneDeposit(out var deposit);
        var workers = Workers(match, 0, 8, 2);
        var ninth = match.WorkerAt(0, 2, 10);

        match.OrderGather(0, deposit, workers);
        match.Simulation.Run(5);
        match.OrderGather(0, deposit, ninth);
        match.Simulation.Tick();

        // All eight places are held, though nobody has reached one yet.
        Assert.Equal(WorkerState.Waiting, match.StateOf(ninth));
        Assert.Equal(5, match.WorkerOf(ninth).WaitingSince);
        Assert.Equal((8, 10), match.TileOf(ninth));

        match.RunUntilWorking(workers);
        match.Simulation.Run(200);
        Assert.Equal(WorkerState.Waiting, match.StateOf(ninth));
        Assert.Equal((8, 10), match.StandsOn(ninth));
        Assert.Equal(MoveState.Idle, match.Movers.Get(ninth).State);
        Assert.Equal(0, match.WorkerOf(ninth).Timer);
        Assert.Equal(8, match.Work.HoldersOf(match.World, deposit));
    }

    [Fact]
    public void FreedPlacesGoToWaitingWorkersInTheOrderTheyBeganToWaitWithTiesToTheLowestId()
    {
        var match = WithOneDeposit(out var deposit);
        var workers = Workers(match, 0, 8, 2);
        var older = match.WorkerAt(0, 2, 20);
        var younger = match.WorkerAt(0, 3, 20);
        var youngest = match.WorkerAt(0, 4, 20);

        match.OrderGather(0, deposit, workers);
        match.RunUntilWorking(workers);

        // The youngest begins to wait first. The other two begin in the same tick, listed youngest first.
        match.OrderGather(0, deposit, youngest);
        match.Simulation.Run(3);
        match.OrderGather(0, deposit, younger, older);
        match.Simulation.Run(200);
        Assert.All((EntityId[])[older, younger, youngest], worker => Assert.Equal(WorkerState.Waiting, match.StateOf(worker)));
        Assert.Equal(3, new[] { older, younger, youngest }.Select(match.TileOf).Distinct().Count());

        // One place is freed: the worker that began to wait first walks to it and works.
        var freed = match.TileOf(workers[0]);
        match.OrderMove(0, TestMatches.Point("2.5", "2.5"), workers[0]);
        match.Simulation.Tick();
        Assert.Equal(WorkerState.Idle, match.StateOf(workers[0]));
        Assert.Equal(WorkerState.Walking, match.StateOf(youngest));
        Assert.Equal(freed, match.TileOf(youngest));
        Assert.Equal(WorkerState.Waiting, match.StateOf(older));
        Assert.Equal(WorkerState.Waiting, match.StateOf(younger));

        var arrival = match.RunUntilWorking(youngest);
        var before = match.Stockpiles.Materials(0);
        match.RunThrough(arrival + 20);
        Assert.Equal(before + (8 * MaterialsPayment), match.Stockpiles.Materials(0));

        // Two are freed in one tick: both of the others get one, the lower ID choosing first.
        match.OrderMove(0, TestMatches.Point("2.5", "2.5"), workers[1], workers[2]);
        match.Simulation.Tick();
        Assert.Equal(WorkerState.Walking, match.StateOf(older));
        Assert.Equal(WorkerState.Walking, match.StateOf(younger));

        // One more: nobody is waiting, and the place stays free.
        match.OrderMove(0, TestMatches.Point("2.5", "2.5"), workers[3]);
        match.Simulation.Run(100);
        Assert.Equal(7, match.Work.HoldersOf(match.World, deposit));
    }

    [Fact]
    public void WhenOnePlaceIsFreedTheLowestIdAmongThoseWhoBeganToWaitTogetherGetsIt()
    {
        var match = WithOneDeposit(out var deposit);
        var workers = Workers(match, 0, 8, 2);
        var older = match.WorkerAt(0, 2, 20);
        var younger = match.WorkerAt(0, 3, 20);

        match.OrderGather(0, deposit, workers);
        match.RunUntilWorking(workers);
        match.OrderGather(0, deposit, younger, older);
        match.Simulation.Run(100);

        match.OrderMove(0, TestMatches.Point("2.5", "2.5"), workers[5]);
        match.Simulation.Tick();

        Assert.Equal(WorkerState.Walking, match.StateOf(older));
        Assert.Equal(WorkerState.Waiting, match.StateOf(younger));
    }

    /// <summary>A place freed by an order goes to the one who waits, not to a worker assigned later in the same tick.</summary>
    [Fact]
    public void AWorkerAssignedWhileOthersWaitDoesNotPassThem()
    {
        var match = WithOneDeposit(out var deposit);
        var workers = Workers(match, 0, 8, 2);
        var waiting = match.WorkerAt(0, 2, 20);
        var newcomer = match.WorkerAt(0, 8, 10);

        match.OrderGather(0, deposit, workers);
        match.RunUntilWorking(workers);
        match.OrderGather(0, deposit, waiting);
        match.Simulation.Run(100);

        // The newcomer stands right beside the place that is freed.
        var holder = workers.Single(worker => match.TileOf(worker) == (9, 10));
        match.OrderMove(0, TestMatches.Point("2.5", "2.5"), holder);
        match.OrderGather(0, deposit, newcomer);
        match.Simulation.Tick();

        Assert.Equal(WorkerState.Walking, match.StateOf(waiting));
        Assert.Equal((9, 10), match.TileOf(waiting));
        Assert.Equal(WorkerState.Waiting, match.StateOf(newcomer));
    }

    /// <summary>
    /// The room left for the build order of a later milestone: a worker that steps away keeps its
    /// assignment, gives up its place and its count, and takes a place again when it returns.
    /// </summary>
    [Fact]
    public void AWorkerThatStepsAwayKeepsItsAssignmentAndTakesAPlaceAgainWhenItReturns()
    {
        var match = WithOneDeposit(out var deposit);
        var workers = Workers(match, 0, 8, 2);
        var ninth = match.WorkerAt(0, 2, 20);

        match.OrderGather(0, deposit, workers);
        var arrival = match.RunUntilWorking(workers);
        match.OrderGather(0, deposit, ninth);
        match.RunThrough(arrival + 110);

        var builder = workers[3];
        match.Work.StepAway(builder);
        Assert.Equal(new Worker { State = WorkerState.Away, Site = deposit }, match.WorkerOf(builder));
        Assert.Equal(7, match.Work.HoldersOf(match.World, deposit));

        // Its place goes to the one who waited, and it earns nothing while it is away.
        match.Simulation.Tick();
        Assert.Equal(WorkerState.Walking, match.StateOf(ninth));
        match.RunUntilWorking(ninth);
        match.Simulation.Run(60);
        Assert.Equal(WorkerState.Away, match.StateOf(builder));
        Assert.Equal(deposit, match.WorkerOf(builder).Site);

        // Back, it finds the deposit full and waits like anyone else.
        Assert.True(match.Work.Return(match.World, builder));
        Assert.Equal(WorkerState.Waiting, match.StateOf(builder));
        Assert.False(match.Work.Return(match.World, builder));

        // Another order while away ends the assignment, and so does the loss of the site.
        match.Work.StepAway(workers[0]);
        match.OrderMove(0, TestMatches.Point("2.5", "2.5"), workers[0]);
        match.Simulation.Tick();
        Assert.Equal(default, match.WorkerOf(workers[0]));
        Assert.False(match.Work.Return(match.World, workers[0]));

        match.Work.StepAway(workers[1]);
        match.World.Entities.Destroy(deposit);
        match.Simulation.Tick();
        Assert.Equal(default, match.WorkerOf(workers[1]));

        // A worker with no assignment has none to keep.
        match.Work.StepAway(workers[0]);
        Assert.Equal(default, match.WorkerOf(workers[0]));
    }

    [Fact]
    public void AMoveOrderFreesThePlaceAndTheCountOfTicks()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 8, 10);

        match.OrderGather(0, deposit, worker);
        var arrival = match.RunUntilWorking(worker);
        match.RunThrough(arrival + 15);
        Assert.Equal(15, match.WorkerOf(worker).Timer);

        match.OrderMove(0, TestMatches.Point("6.5", "10.5"), worker);
        match.Simulation.Tick();
        Assert.Equal(default, match.WorkerOf(worker));
        Assert.Equal(0, match.Work.HoldersOf(match.World, deposit));

        match.Simulation.Run(100);
        Assert.Equal(0, match.Stockpiles.Materials(0));
        Assert.Equal((6, 10), match.StandsOn(worker));

        // Sent back, it counts from its new arrival.
        match.OrderGather(0, deposit, worker);
        arrival = match.RunUntilWorking(worker);
        match.RunThrough(arrival + 19);
        Assert.Equal(0, match.Stockpiles.Materials(0));
        match.Simulation.Tick();
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
    }

    /// <summary>A move order to the very spot it works on is another order too.</summary>
    [Fact]
    public void AMoveOrderToItsOwnPlaceStillEndsTheWork()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 8, 10);

        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);
        match.OrderMove(0, MapGrid.TileCenter(9, 10), worker);
        match.Simulation.Run(40);

        Assert.Equal(WorkerState.Idle, match.StateOf(worker));
        Assert.Equal(0, match.Stockpiles.Materials(0));
    }

    [Fact]
    public void AMoveOrderThatIsDroppedOrIsNotThePlayersChangesNothing()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 8, 10);

        match.OrderGather(0, deposit, worker);
        var arrival = match.RunUntilWorking(worker);

        // Off the map, from the other player, and from nobody in the match.
        match.OrderMove(0, TestMatches.Point("-3", "5"), worker);
        match.OrderMove(1, TestMatches.Point("6.5", "10.5"), worker);
        match.OrderMove(7, TestMatches.Point("6.5", "10.5"), worker);
        match.RunThrough(arrival + 20);

        Assert.Equal(3, match.Simulation.DroppedCommandCount);
        Assert.Equal(WorkerState.Working, match.StateOf(worker));
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
    }

    [Fact]
    public void AGatherOrderForAWorkerAlreadyThereIsAnotherOrderAndStartsItsCountAgain()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 8, 10);

        match.OrderGather(0, deposit, worker);
        var arrival = match.RunUntilWorking(worker);
        match.RunThrough(arrival + 12);

        match.OrderGather(0, deposit, worker);
        match.Simulation.Tick();

        // It takes the nearest free place, which is the one it stands on, and has arrived at once.
        Assert.Equal(WorkerState.Working, match.StateOf(worker));
        Assert.Equal((9, 10), match.TileOf(worker));
        Assert.Equal(0, match.WorkerOf(worker).Timer);
        match.RunThrough(arrival + 13 + 19);
        Assert.Equal(0, match.Stockpiles.Materials(0));
        match.Simulation.Tick();
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
    }

    [Fact]
    public void AWorkerThatDiesFreesItsPlaceForTheNextInLine()
    {
        var match = WithOneDeposit(out var deposit);
        var workers = Workers(match, 0, 8, 2);
        var ninth = match.WorkerAt(0, 2, 20);

        match.OrderGather(0, deposit, workers);
        match.RunUntilWorking(workers);
        match.OrderGather(0, deposit, ninth);
        match.Simulation.Run(150);
        Assert.Equal(WorkerState.Waiting, match.StateOf(ninth));

        var freed = match.TileOf(workers[4]);
        match.World.Entities.Destroy(workers[4]);
        match.Simulation.Tick();

        Assert.False(match.Workers.Contains(workers[4]));
        Assert.Equal(WorkerState.Walking, match.StateOf(ninth));
        Assert.Equal(freed, match.TileOf(ninth));
        match.RunUntilWorking(ninth);
        Assert.Equal(8, match.Work.HoldersOf(match.World, deposit));
    }

    [Fact]
    public void AGeneratorHasTheSameEightTilesAndLetsFourWorkersUseThem()
    {
        var match = Create(TestMaps.Open(24, 24));
        var generator = match.PlaceGenerator(0, 10, 10);
        var workers = Workers(match, 0, 6, 2);

        match.OrderGather(0, generator, workers);
        match.Simulation.Tick();

        Assert.All(workers[..4], worker => Assert.Equal(WorkerState.Walking, match.StateOf(worker)));
        Assert.All(workers[..4], worker => Assert.Contains(match.TileOf(worker), PlacesOfTheDeposit));
        Assert.All(workers[4..], worker => Assert.Equal(WorkerState.Waiting, match.StateOf(worker)));
        Assert.All(workers[4..], worker => Assert.DoesNotContain(match.TileOf(worker), PlacesOfTheDeposit));

        match.RunUntilWorking(workers[..4]);
        match.Simulation.Run(40);
        Assert.Equal(4, match.Work.HoldersOf(match.World, generator));

        // A freed place goes to the first in line, who may take any free tile of the eight.
        match.OrderMove(0, TestMatches.Point("2.5", "2.5"), workers[0]);
        match.Simulation.Tick();
        Assert.Equal(WorkerState.Walking, match.StateOf(workers[4]));
        Assert.Equal(WorkerState.Waiting, match.StateOf(workers[5]));
        Assert.Contains(match.TileOf(workers[4]), PlacesOfTheDeposit);
    }

    /// <summary>
    /// A place whose tile is blocked cannot be used. Of the eight tiles beside a deposit in the
    /// corner of the map, four are off the map, one is a cliff and a Generator covers one.
    /// </summary>
    [Fact]
    public void APlaceWhoseTileIsBlockedCannotBeUsed()
    {
        var rows = Enumerable.Repeat(new string('.', 12), 12).ToArray();
        rows[0] = "..#.........";
        var match = Create(TestMaps.FromRows(rows).WithDeposits((0, 0)));
        match.PlaceTerritory(0, 0, 0, 2, 20);
        var deposit = match.SiteAt(0, 0);
        match.PlaceGenerator(1, 2, 1);
        var workers = Enumerable.Range(0, 4).Select(i => match.WorkerAt(0, 6 + i, 8)).ToArray();

        match.OrderGather(0, deposit, workers);
        match.Simulation.Tick();

        Assert.Equal([(0, 2), (1, 2)], workers[..2].Select(match.TileOf).OrderBy(tile => tile.X));
        Assert.All(workers[..2], worker => Assert.Equal(WorkerState.Walking, match.StateOf(worker)));

        // The other two wait on the only tiles near the deposit that are no place of either site.
        Assert.All(workers[2..], worker => Assert.Equal(WorkerState.Waiting, match.StateOf(worker)));
        Assert.Equal([(0, 3), (1, 3)], workers[2..].Select(match.TileOf).OrderBy(tile => tile.X));
    }

    /// <summary>Only a place the worker can walk to counts: a deposit in a gap of a wall has places on both sides.</summary>
    [Fact]
    public void AWorkerTakesOnlyAPlaceItCanWalkTo()
    {
        var rows = Enumerable.Repeat("..........##..........", 24).ToArray();
        rows[10] = new string('.', 22);
        rows[11] = new string('.', 22);
        var match = Create(TestMaps.FromRows(rows).WithDeposits((10, 10)));
        match.PlaceTerritory(0, 10, 10, 2, 40);
        var deposit = match.SiteAt(10, 10);
        Assert.NotEqual(match.Grid.RegionOf(9, 10), match.Grid.RegionOf(12, 10));

        var left = Enumerable.Range(0, 3).Select(i => match.WorkerAt(0, 2, 8 + i)).ToArray();
        var right = match.WorkerAt(0, 18, 10);
        match.OrderGather(0, deposit, [.. left, right]);
        match.Simulation.Tick();

        Assert.Equal([(9, 10), (9, 11)], left[..2].Select(match.TileOf).OrderBy(tile => tile.Y));
        Assert.Equal(WorkerState.Waiting, match.StateOf(left[2]));
        Assert.Equal(match.Grid.RegionOf(9, 10), match.Grid.RegionOf(match.TileOf(left[2]).X, match.TileOf(left[2]).Y));
        Assert.Equal((12, 10), match.TileOf(right));
    }

    [Fact]
    public void WorkersOfAnyPlayerMayHoldPlacesAtADepositAndOnlyThoseWhoseOwnerHoldsItEarn()
    {
        var match = WithOneDeposit(out var deposit);
        var own = Workers(match, 0, 4, 2);
        var others = Workers(match, 1, 5, 20);

        match.OrderGather(1, deposit, others[..4]);
        match.OrderGather(0, deposit, own);
        match.Simulation.Tick();
        match.OrderGather(1, deposit, others[4]);
        match.RunUntilWorking([.. own, .. others[..4]]);
        match.Simulation.Run(40);

        Assert.Equal(WorkerState.Waiting, match.StateOf(others[4]));
        Assert.True(match.Stockpiles.Materials(0) >= 4 * MaterialsPayment);
        Assert.Equal(0, match.Stockpiles.Materials(1));
    }

    [Fact]
    public void WorkersLoseTheirAssignmentWhenTheirSiteIsGone()
    {
        var match = Create(TestMaps.Open(24, 24));
        var generator = match.PlaceGenerator(0, 10, 10);
        var workers = Workers(match, 0, 5, 2);

        match.OrderGather(0, generator, workers);
        match.RunUntilWorking(workers[..4]);
        match.World.Entities.Destroy(generator);
        match.Simulation.Tick();

        Assert.All(workers, worker => Assert.Equal(default, match.WorkerOf(worker)));
        Assert.Equal(0, match.WorkSites.Count);
    }
}
