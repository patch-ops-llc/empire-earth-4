using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Economy.EconomyMatches;

namespace Sim.Tests.Economy;

/// <summary>Which gather orders are carried out, which listed entities take them, and which are dropped.</summary>
public class GatherCommandTests
{
    [Fact]
    public void EntitiesThatCannotTakeTheOrderAreSkippedAndTheRestGo()
    {
        var match = WithOneDeposit(out var deposit);
        var worker = match.WorkerAt(0, 4, 10);
        var soldier = match.SpawnUnit(0, MapGrid.TileCenter(4, 11), Fixed.Half);
        var enemy = match.WorkerAt(1, 4, 12);
        var dead = match.WorkerAt(0, 4, 13);
        match.World.Entities.Destroy(dead);

        match.OrderGather(0, deposit, soldier, enemy, dead, new EntityId(999), worker);
        match.Simulation.Tick();

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(WorkerState.Walking, match.StateOf(worker));
        Assert.Equal(deposit, match.WorkerOf(worker).Site);
        Assert.Equal(WorkerState.Idle, match.StateOf(enemy));
        Assert.Equal(Sim.Movement.MoveState.Idle, match.Movers.Get(soldier).State);
        Assert.Equal(Sim.Movement.MoveState.Idle, match.Movers.Get(enemy).State);
    }

    [Fact]
    public void ACommandThatAssignsNobodyIsDropped()
    {
        var match = WithOneDeposit(out var deposit);
        var generator = match.PlaceGenerator(1, 16, 16);
        var worker = match.WorkerAt(0, 4, 10);
        var soldier = match.SpawnUnit(0, MapGrid.TileCenter(4, 11), Fixed.Half);
        var enemy = match.WorkerAt(1, 4, 12);

        var dropped = new[]
        {
            Gather(0, deposit),
            Gather(0, deposit, soldier, enemy),
            Gather(2, deposit, worker),
            Gather(-1, deposit, worker),
            Gather(0, default, worker),
            Gather(0, new EntityId(999), worker),

            // Not a work site, and another player's Generator.
            Gather(0, enemy, worker),
            Gather(0, generator, worker),
        };
        foreach (var command in dropped)
        {
            match.Simulation.Commands.Enqueue(match.World.Tick, command);
        }

        match.Simulation.Tick();

        Assert.Equal(dropped.Length, match.Simulation.DroppedCommandCount);
        Assert.Equal(default, match.WorkerOf(worker));
        Assert.Equal(Sim.Movement.MoveState.Idle, match.Movers.Get(worker).State);

        match.OrderGather(1, generator, enemy);
        match.Simulation.Tick();
        Assert.Equal(dropped.Length, match.Simulation.DroppedCommandCount);
        Assert.Equal(generator, match.WorkerOf(enemy).Site);
    }

    [Fact]
    public void AWorkerThatCannotWalkToAnyPlaceOfTheSiteIsSkipped()
    {
        var rows = Enumerable.Repeat("......#.................", 24).ToArray();
        var match = Create(TestMaps.FromRows(rows).WithDeposits((10, 10)));
        var deposit = match.SiteAt(10, 10);
        var cutOff = match.WorkerAt(0, 2, 10);
        var near = match.WorkerAt(0, 8, 10);

        match.OrderGather(0, deposit, cutOff);
        match.Simulation.Tick();
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(default, match.WorkerOf(cutOff));

        match.OrderGather(0, deposit, cutOff, near);
        match.Simulation.Tick();
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(default, match.WorkerOf(cutOff));
        Assert.Equal(WorkerState.Walking, match.StateOf(near));
    }

    [Fact]
    public void AGatherOrderTakesAWorkerFromOneSiteToAnother()
    {
        var match = WithOneDeposit(out var deposit);
        var generator = match.PlaceGenerator(0, 16, 10);
        var worker = match.WorkerAt(0, 8, 10);

        match.OrderGather(0, deposit, worker);
        var arrival = match.RunUntilWorking(worker);
        match.RunThrough(arrival + 20);
        Assert.Equal(1, match.Work.HoldersOf(match.World, deposit));

        match.OrderGather(0, generator, worker);
        match.Simulation.Tick();
        Assert.Equal(0, match.Work.HoldersOf(match.World, deposit));
        Assert.Equal(1, match.Work.HoldersOf(match.World, generator));

        arrival = match.RunUntilWorking(worker);
        match.RunThrough(arrival + 20);
        Assert.Equal(MaterialsPayment, match.Stockpiles.Materials(0));
        Assert.Equal(EnergyPayment, match.Stockpiles.Energy(0));
    }

    /// <summary>Workers never act on their own (design 2.4): one that was never ordered stays idle beside a deposit.</summary>
    [Fact]
    public void AnIdleWorkerBesideADepositDoesNotStartToWork()
    {
        var match = WithOneDeposit(out _);
        var worker = match.WorkerAt(0, 9, 10);

        match.Simulation.Run(100);

        Assert.Equal(default, match.WorkerOf(worker));
        Assert.Equal(0, match.Stockpiles.Materials(0));
    }

    /// <summary>
    /// The number is in replays and on the wire. 2 and 3 are the research orders' and 4 to 6 the
    /// combat orders', in pull requests that are open beside this one.
    /// </summary>
    [Fact]
    public void TheNumberOfTheGatherOrderIsSeven()
    {
        Assert.Equal(7, (int)CommandType.Gather);
    }
}
