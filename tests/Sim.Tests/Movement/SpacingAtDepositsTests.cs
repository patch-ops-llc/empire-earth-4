using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Economy;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Movement;

/// <summary>
/// The spacing rules of design 2.4 with the economy's workers and deposits (design 4.1): a worker
/// at its place stays on it among its own side, an enemy blocks a worker like any ground unit,
/// and a deposit's four tiles are ground no group is placed on.
/// </summary>
public class SpacingAtDepositsTests
{
    private static readonly Fixed Speed = Fixed.One / 8;

    /// <summary>
    /// A worker must stand on its place to work, so it holds its ground there: a unit of its own
    /// side that stops on top of it steps away alone, and the worker goes on earning.
    /// </summary>
    [Fact]
    public void AWorkerAtItsPlaceIsNotSteppedAsideByAUnitThatStopsOnIt()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 4, 10);
        var unit = match.SpawnUnit(0, TestMatches.Point("3.5", "3.5"), Speed, Fixed.One);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);
        var place = match.PositionOf(worker);
        Assert.Equal(MapGrid.TileCenter(9, 10), place);

        match.OrderMove(0, place, unit);
        var before = match.Stockpiles.Materials(0);
        for (var tick = 0; tick < 200; tick++)
        {
            match.Simulation.Tick();
            Assert.Equal(place, match.PositionOf(worker));
            Assert.Equal(WorkerState.Working, match.StateOf(worker));
        }

        Assert.Equal(before + (10 * MaterialsPayment), match.Stockpiles.Materials(0));
        Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
        match.AssertKeepTheirSpacing(worker, unit);
        match.AssertStandsClear(unit);
    }

    /// <summary>A worker that arrives at a place where a unit of its own side stands takes it, and that unit makes room.</summary>
    [Fact]
    public void AWorkerTakesAPlaceThatAUnitOfItsOwnSideStandsOn()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 4, 10);
        var unit = match.SpawnUnit(0, MapGrid.TileCenter(9, 10), Speed, Fixed.One);

        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);
        match.Simulation.Run(40);

        Assert.Equal(MapGrid.TileCenter(9, 10), match.PositionOf(worker));
        Assert.Equal(WorkerState.Working, match.StateOf(worker));
        match.AssertKeepTheirSpacing(worker, unit);
        match.AssertStandsClear(unit);
    }

    /// <summary>
    /// Only a worker at its place holds its ground. Two workers without work that stand on one
    /// point step apart like any two units of one side, each by its half.
    /// </summary>
    [Fact]
    public void WorkersWithoutAPlaceStepApartLikeAnyUnits()
    {
        var match = WithOneDeposit(out _);
        var first = match.WorkerAt(0, 4, 10);
        var second = match.WorkerAt(0, 4, 10);
        var start = match.PositionOf(first);
        Assert.Equal(start, match.PositionOf(second));

        Assert.True(match.RunUntilAtRest(100, first, second) > 1);

        Assert.NotEqual(start, match.PositionOf(first));
        Assert.NotEqual(start, match.PositionOf(second));
        match.AssertKeepTheirSpacing(first, second);
    }

    /// <summary>
    /// Design 2.4: workers block and are blocked like any ground unit. An enemy unit on a
    /// worker's place keeps it off: the worker waits beside it with its place still held, never
    /// closer than their spacing, and goes to work when the enemy has left.
    /// </summary>
    [Fact]
    public void AnEnemyStandingOnAPlaceKeepsTheWorkerOffItUntilItLeaves()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 4, 10);
        var enemy = match.SpawnUnit(1, MapGrid.TileCenter(9, 10), Speed, Fixed.One);
        var watch = new EnemyPairs(match, [worker], [enemy]);

        match.OrderGather(0, deposit, worker);
        for (var tick = 0; tick < 200; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            Assert.Equal(WorkerState.Walking, match.StateOf(worker));
        }

        Assert.True(match.Movers.Get(worker).Waiting);
        Assert.Equal((9, 10), match.TileOf(worker));
        Assert.Equal(0, match.Stockpiles.Materials(0));

        match.OrderMove(1, TestMatches.Point("3.5", "20.5"), enemy);
        match.RunUntilWorking(worker);
        Assert.Equal(MapGrid.TileCenter(9, 10), match.PositionOf(worker));
    }

    /// <summary>
    /// Eight workers at work and three that wait, all of one side: those at work stand on the
    /// centers of their places, a tile apart, and nobody stands closer than the spacing.
    /// </summary>
    [Fact]
    public void WorkersAtADepositAndThoseWhoWaitKeepTheirSpacing()
    {
        var match = WithOneDeposit(out var deposit);
        EntityId[] workers = [.. Enumerable.Range(0, 11).Select(i => match.WorkerAt(0, 2 + i, 2))];

        match.OrderGather(0, deposit, workers);
        match.RunUntilWorking(workers[..8]);
        match.RunUntilAtRest(600, workers);
        match.Simulation.Run(100);

        Assert.All(workers[..8], worker => Assert.Equal(WorkerState.Working, match.StateOf(worker)));
        Assert.All(workers[8..], worker => Assert.Equal(WorkerState.Waiting, match.StateOf(worker)));
        Assert.All(workers[..8], worker => Assert.Equal(MapGrid.TileCenter(match.TileOf(worker).X, match.TileOf(worker).Y), match.PositionOf(worker)));
        match.AssertKeepTheirSpacing(workers);
        Assert.All(workers, match.AssertStandsClear);
    }

    /// <summary>
    /// Design 4.1: a deposit blocks its four tiles for movement. A group ordered onto it is placed
    /// on the ground beside it, comes to rest clear of it and keeps its spacing.
    /// </summary>
    [Fact]
    public void AGroupOrderedOntoADepositComesToRestBesideItWithItsSpacingKept()
    {
        var match = WithOneDeposit(out _);
        var units = match.SpawnBlock(0, TestMatches.Point("3.5", "17.5"), 3, 6, Speed, Fixed.One);

        match.OrderMove(0, TestMatches.Point("11", "11"), units);
        match.RunUntilAtRest(900, units);

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.All(units, match.AssertStandsClear);
        match.AssertKeepTheirSpacing(units);
        Assert.All(units, unit => Assert.InRange(
            FixedVector2.DistanceSquaredRaw(match.PositionOf(unit), TestMatches.Point("11", "11")),
            0,
            FixedVector2.SquareRaw(Fixed.FromInt(5))));
    }

    /// <summary>
    /// The same group with two workers at work on neighboring places. The workers stay on their
    /// places and go on working, and every unit comes to rest, also the one whose place is between
    /// the two workers and the deposit: it has nowhere to step that does not bring it nearer one
    /// of them, so it stays closer to them than the spacing (design 2.4 settles units only where
    /// there is room), and it stands still there.
    /// </summary>
    [Fact]
    public void AGroupThatStopsAmongWorkersAtTheirPlacesLeavesThemThereAndComesToRest()
    {
        var match = WithOneDeposit(out var deposit);
        EntityId[] workers = [match.WorkerAt(0, 4, 10), match.WorkerAt(0, 4, 11)];
        match.OrderGather(0, deposit, workers);
        match.RunUntilWorking(workers);
        var places = workers.Select(match.PositionOf).ToArray();
        var units = match.SpawnBlock(0, TestMatches.Point("3.5", "17.5"), 3, 6, Speed, Fixed.One);

        match.OrderMove(0, TestMatches.Point("11", "11"), units);
        match.RunUntilAtRest(900, units);
        var rest = units.Select(match.PositionOf).ToArray();
        match.Simulation.Run(100);

        Assert.Equal(rest, units.Select(match.PositionOf));
        Assert.Equal(places, workers.Select(match.PositionOf));
        Assert.All(workers, worker => Assert.Equal(WorkerState.Working, match.StateOf(worker)));
        Assert.All(units, match.AssertStandsClear);
        match.AssertKeepTheirSpacing(units);

        // The one between the two places is the only one closer to a worker than the spacing.
        var pressed = units.Where(unit => workers.Any(worker =>
            FixedVector2.DistanceSquaredRaw(match.PositionOf(unit), match.PositionOf(worker)) < FixedVector2.SquareRaw(Fixed.One))).ToArray();
        var between = Assert.Single(pressed);
        Assert.Equal(Fixed.FromInt(11), match.PositionOf(between).Y);
    }
}
