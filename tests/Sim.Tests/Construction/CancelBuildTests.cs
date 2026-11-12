using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Movement;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Structures;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Construction;

/// <summary>
/// How a build order ends before its foundation, and how a foundation is cancelled (design 2.5;
/// design 8.4, row 12): the whole price back before the foundation, 75% of it after, each
/// resource rounded half up.
/// </summary>
public class CancelBuildTests
{
    [Fact]
    public void ACancelledOrderGivesTheWholePriceBackAndTheWorkerGoesBackToItsWork()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 200, 100);
        var deposit = match.SiteAt(2, 10);
        var worker = match.WorkerAt(0, 4, 9);
        match.OrderGather(0, deposit, worker);
        match.RunUntilWorking(worker);
        var before = match.Stockpiles.Materials(0);
        match.OrderBuild(0, "academy", 10, 10, worker);
        match.Simulation.Run(10);
        Assert.Equal((before - 200_000, 0), (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0)));

        match.OrderCancelBuild(0, worker);
        match.Simulation.Tick();

        Assert.Equal((before, 100_000), (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0)));
        Assert.Equal(0, match.BuildOrders.Count);
        Assert.Equal([(worker, BuildProblem.Cancelled)], match.Problems(SimEventKind.BuildOrderEnded));
        Assert.Equal(new TileSquare(10, 10, 2).Center, match.EventsOf(SimEventKind.BuildOrderEnded)[0].Position);
        Assert.Equal(deposit, match.WorkerOf(worker).Site);
        Assert.NotEqual(WorkerState.Away, match.StateOf(worker));
        match.RunUntilWorking(worker);
        Assert.Equal(EntityId.None, match.StructureAt(10, 10));
    }

    [Fact]
    public void ACancelledOrderOfAWorkerWithoutWorkLeavesItStanding()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 4);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Run(10);

        match.OrderCancelBuild(0, worker);
        match.Simulation.Tick();
        var stands = match.PositionOf(worker);
        match.Simulation.Run(20);

        Assert.Equal((1000, 1000), match.StockOf(0));
        Assert.Equal(MoveState.Idle, match.MoveStateOf(worker));
        Assert.Equal(stands, match.PositionOf(worker));
        Assert.Equal(default, match.WorkerOf(worker));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(5, 4)]
    [InlineData(6, 5)]
    [InlineData(10, 8)]
    [InlineData(51, 38)]
    [InlineData(100, 75)]
    [InlineData(205, 154)]
    [InlineData(250, 188)]
    [InlineData(400, 300)]
    public void ThreeQuartersOfAPriceRoundHalfUp(int paid, int back) =>
        Assert.Equal(back, ConstructionRules.RefundOf(paid));

    /// <summary>Design 8.4, row 12: 75% of 205 Materials is 153.75 and of 51 Energy is 38.25.</summary>
    [Fact]
    public void ACancelledFoundationGivesThreeQuartersOfEachResourceBackRoundedHalfUp()
    {
        var match = WithACapitol(DataStartingAtTier(2));
        var worker = match.WorkerAt(0, 9, 8);
        match.OrderBuild(0, "works", 9, 9, worker);
        match.Simulation.Run(100);
        var works = match.StructureAt(9, 9);
        Assert.Equal((795, 949), match.StockOf(0));

        match.OrderCancelBuild(0, works);
        match.Simulation.Tick();

        // Its tiles are free in the tick it is cancelled in, and it is told as a removal, not a death.
        Assert.Equal((949, 987), match.StockOf(0));
        Assert.Equal(EntityId.None, match.StructureAt(9, 9));
        Assert.True(match.Grid.IsPassable(10, 10));
        var removed = Assert.Single(match.EventsOf(SimEventKind.StructureRemoved));
        Assert.Equal((works, 0), (removed.Entity, removed.Player));
        Assert.Empty(match.EventsOf(SimEventKind.Death));
        match.Simulation.Tick();
        Assert.Equal((0, 1), (match.Foundations.Count, match.Structures.Count));

        // The time it had been building changes nothing: one cancelled in its last tick gives the same.
        match.OrderBuild(0, "works", 9, 9, worker);
        match.Simulation.Run(900);
        Assert.Equal(899, match.Foundations.Get(match.StructureAt(9, 9)).Built);
        match.OrderCancelBuild(0, match.StructureAt(9, 9));
        match.Simulation.Tick();
        Assert.Equal((949 - 205 + 154, 987 - 51 + 38), match.StockOf(0));
        Assert.Empty(match.EventsOf(SimEventKind.StructureFinished));
    }

    /// <summary>Design 2.5: there is no demolition, so a finished structure cannot be cancelled.</summary>
    [Fact]
    public void AFinishedStructureCannotBeCancelled()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Run(501);
        var generator = match.StructureAt(10, 10);
        Assert.True(match.Construction.IsFinished(generator));

        match.OrderCancelBuild(0, generator, match.StructureAt(6, 10), worker);
        match.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(2, match.StructuresOf(0).Length);
        Assert.Equal((900, 1000), match.StockOf(0));
    }

    [Fact]
    public void OnlyTheOwnerCancels()
    {
        var match = WithACapitol();
        var builder = match.WorkerAt(0, 10, 9);
        var walker = match.WorkerAt(0, 2, 2);
        match.OrderBuild(0, "generator", 10, 10, builder);
        match.OrderBuild(0, "generator", 10, 12, walker);
        match.Simulation.Tick();
        var foundation = match.StructureAt(10, 10);

        match.OrderCancelBuild(1, foundation, walker);
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.CancelBuild, 2, [foundation, walker]));
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.CancelBuild, 0, [new EntityId(999)]));
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.CancelBuild, 0));
        match.Simulation.Tick();

        Assert.Equal(4, match.Simulation.DroppedCommandCount);
        Assert.Equal((1, 1), (match.Foundations.Count, match.BuildOrders.Count));
        Assert.Equal((800, 1000), match.StockOf(0));

        // One command cancels both, and skips what cannot be cancelled.
        match.OrderCancelBuild(0, match.StructureAt(6, 10), walker, foundation, builder);
        match.Simulation.Tick();

        Assert.Equal(4, match.Simulation.DroppedCommandCount);
        Assert.Equal((0, 0), (match.Foundations.Count, match.BuildOrders.Count));
        Assert.Equal((975, 1000), match.StockOf(0));
    }

    /// <summary>Design 8.4, row 12.</summary>
    [Fact]
    public void AWorkerThatDiesOnTheWayHasItsOrderRefundedInTheTickItDies()
    {
        var match = WithACapitol();
        var worker = match.WorkerAt(0, 10, 4);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Run(10);
        Assert.Equal((900, 1000), match.StockOf(0));

        match.Raze(worker);
        match.Simulation.Tick();

        Assert.Equal((1000, 1000), match.StockOf(0));
        Assert.Equal([(worker, BuildProblem.WorkerDied)], match.Problems(SimEventKind.BuildOrderEnded));
        Assert.Equal(0, match.BuildOrders.Count);
        match.Simulation.Run(100);
        Assert.Equal(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal((1000, 1000), match.StockOf(0));
    }

    public static TheoryData<CommandType> OtherOrders() => [CommandType.Move, CommandType.AttackMove, CommandType.Stop, CommandType.Gather];

    /// <summary>Design 8.4, row 12: a worker given another order gives the build order up, with the whole price back.</summary>
    [Theory]
    [MemberData(nameof(OtherOrders))]
    public void AnotherOrderForTheWorkerEndsItsBuildOrderWithAFullRefund(CommandType type)
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10), (2, 14)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 100, 0);
        var worker = match.WorkerAt(0, 4, 9);
        var other = match.WorkerAt(0, 10, 4);
        match.OrderGather(0, match.SiteAt(2, 10), worker);
        match.RunUntilWorking(worker);
        var before = match.Stockpiles.Materials(0);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Run(10);
        var point = MapGrid.TileCenter(4, 4);

        // The other worker is listed too. It has no build order, and nothing is told of it.
        match.Simulation.Commands.Enqueue(match.World.Tick, type == CommandType.Gather
            ? new Command(type, 0, [other, worker], targetEntity: match.SiteAt(2, 14))
            : new Command(type, 0, [other, worker], point.X, point.Y));
        match.Simulation.Tick();

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(before, match.Stockpiles.Materials(0));
        Assert.Equal([(worker, BuildProblem.OtherOrder)], match.Problems(SimEventKind.BuildOrderEnded));
        Assert.Equal(0, match.BuildOrders.Count);

        // It does what the other order says, and does not go back to the work it had.
        Assert.Equal(type == CommandType.Gather ? match.SiteAt(2, 14) : EntityId.None, match.WorkerOf(worker).Site);
        Assert.NotEqual(WorkerState.Away, match.StateOf(worker));
        match.Simulation.Run(200);
        Assert.Equal(EntityId.None, match.StructureAt(10, 10));
        if (type is CommandType.Move or CommandType.AttackMove)
        {
            Assert.Equal(point, match.PositionOf(worker));
        }
    }

    [Fact]
    public void AnOrderThatDoesNotReachTheWorkerLeavesItsBuildOrder()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((2, 10)));
        match.PlaceStructure(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 100, 0);
        var worker = match.WorkerAt(0, 10, 4);
        var other = match.WorkerAt(0, 4, 4);
        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();
        var point = MapGrid.TileCenter(4, 4);

        // Another player's, one for another unit, and a gather order that is refused: its site is a structure that takes no workers.
        match.OrderMove(1, point, worker);
        match.OrderMove(0, point, other);
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.Stop, 1, [worker]));
        match.OrderGather(0, match.StructureAt(6, 10), worker);
        match.Simulation.Tick();

        Assert.True(match.BuildOrders.Contains(worker));
        Assert.Empty(match.EventsOf(SimEventKind.BuildOrderEnded));
        Assert.Equal((0, 0), match.StockOf(0));
        match.RunUntilOrderEnds(worker);
        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
    }
}
