using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Production;
using Sim.Setup;
using Sim.Tests.Map;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// Design 2.3: "cancelling refunds the price paid". And design 8.4, row 25, with design 2.5: a
/// destroyed structure refunds every order of its queue in full and releases its supply.
/// </summary>
public class CancelAndRefundTests
{
    private static readonly int Hoplite = Chassis("hoplite");
    private static readonly int Peltast = Chassis("peltast");

    /// <summary>A Barracks with a Peltast, a Hoplite and a Peltast ordered, 100 ticks into the first.</summary>
    private static Match WithThreeOrders(out EntityId barracks)
    {
        var match = Create(TestMaps.Open(24, 24));
        barracks = match.Place(0, "barracks", 10, 10);
        match.Stockpiles.Add(0, 200, 20);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Hoplite);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(100);
        Assert.Equal((50, 10), match.Stock(0));
        Assert.Equal(6, match.Supply.Used(0));
        return match;
    }

    [Fact]
    public void CancellingAnOrderThatWaitsGivesBackItsPriceAndItsSupply()
    {
        var match = WithThreeOrders(out var barracks);

        match.OrderCancel(0, barracks, 1);
        match.Simulation.Tick();

        Assert.Equal((120, 20), match.Stock(0));
        Assert.Equal(4, match.Supply.Used(0));
        Assert.Equal([Peltast, Peltast], match.OrdersAt(barracks).Select(order => order.Unit));
        Assert.Equal((240, 101), (match.ProducerOf(barracks).BuildTicks, match.ProducerOf(barracks).Progress));
        Assert.Equal(
            new TrainEvent(SimEventKind.TrainCancelled, 100, 0, barracks, Hoplite, 1, TrainCheck.Allowed, EntityId.None, match.Bodies.Get(barracks).Position),
            Assert.Single(match.TrainEvents()));

        // The first order was not disturbed, and the Peltast behind it follows at once.
        Assert.Equal(139, match.RunUntilAppeared());
        Assert.Equal(240, match.RunUntilAppeared());
        Assert.Empty(match.OrdersAt(barracks));
    }

    [Fact]
    public void CancellingTheOrderInProductionGivesBackAllOfItAndTheNextBeginsAnew()
    {
        var match = WithThreeOrders(out var barracks);

        match.OrderCancel(0, barracks, 0);
        match.Simulation.Tick();

        Assert.Equal((90, 10), match.Stock(0));
        Assert.Equal(4, match.Supply.Used(0));
        Assert.Equal([Hoplite, Peltast], match.OrdersAt(barracks).Select(order => order.Unit));

        // The Hoplite began in the tick of the cancelling and takes its own 360 ticks from there.
        Assert.Equal((360, 1), (match.ProducerOf(barracks).BuildTicks, match.ProducerOf(barracks).Progress));
        Assert.Equal(359, match.RunUntilAppeared());
        Assert.Equal(Hoplite, match.Combatants.Get(match.Appeared()[0]).Chassis);
    }

    [Fact]
    public void CancellingEveryOrderGivesBackEverything()
    {
        var match = WithThreeOrders(out var barracks);

        // Each cancel names the last order that is left when it runs.
        match.OrderCancel(0, barracks, 2);
        match.OrderCancel(0, barracks, 1);
        match.OrderCancel(0, barracks, 0);
        match.Simulation.Tick();

        Assert.Equal((200, 20), match.Stock(0));
        Assert.Equal(0, match.Supply.Used(0));
        Assert.Empty(match.OrdersAt(barracks));
        Assert.Equal((0, 0), (match.ProducerOf(barracks).BuildTicks, match.ProducerOf(barracks).Progress));
        Assert.Equal([Peltast, Hoplite, Peltast], match.TrainEvents().Select(train => train.Unit));

        match.Simulation.Run(400);
        Assert.Equal(0, match.Combatants.Count);
    }

    [Fact]
    public void CancellingAPairGivesBackThePriceOfTwo()
    {
        var match = Create(TestMaps.Open(24, 24));
        match.Research.SetTiers(0, [2, 2, 3, 5, 5, 3]);
        var barracks = match.Place(0, "barracks", 10, 10);
        match.Stockpiles.Add(0, 114, 0);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(50);
        Assert.Equal((0, 0), match.Stock(0));
        Assert.Equal(2, match.Supply.Used(0));

        match.OrderCancel(0, barracks, 0);
        match.Simulation.Tick();

        Assert.Equal((114, 0), match.Stock(0));
        Assert.Equal(0, match.Supply.Used(0));
        Assert.Equal(2, Assert.Single(match.TrainEvents()).Count);
    }

    [Fact]
    public void ACancelThatNamesNoOrderIsRefused()
    {
        var match = WithThreeOrders(out var barracks);
        var theirs = match.Place(1, "barracks", 2, 2);
        var unit = match.SpawnWorker(0, MapGrid.TileCenter(20, 20));
        Command[] refused =
        [
            CancelCommand(0, barracks, 3),
            CancelCommand(0, barracks, -1),
            CancelCommand(0, barracks, int.MaxValue),
            CancelCommand(1, barracks, 0),
            CancelCommand(0, theirs, 0),
            CancelCommand(0, unit, 0),
            CancelCommand(0, EntityId.None, 0),
            CancelCommand(9, barracks, 0),
            CancelCommand(-1, barracks, 0),
            new Command(CommandType.CancelTrain, 0, [], contentId: 0),
            new Command(CommandType.CancelTrain, 0, [barracks, barracks], contentId: 0),
        ];
        foreach (var command in refused)
        {
            match.Simulation.Commands.Enqueue(match.World.Tick, command);
        }

        match.Simulation.Tick();

        Assert.Equal((50, 10), match.Stock(0));
        Assert.Equal(3, match.OrdersAt(barracks).Length);
        Assert.Empty(match.TrainEvents());
        Assert.Equal(
            refused.Select(command => (SimEventKind.OrderRefused, command.Player)),
            match.Simulation.Events.Select(simEvent => (simEvent.Kind, simEvent.Player)));
    }

    [Fact]
    public void ADestroyedStructureRefundsItsWholeQueueAndReleasesItsSupply()
    {
        var match = WithThreeOrders(out var barracks);
        var other = match.Place(0, "barracks", 2, 2);
        match.MakeRich(1);
        var theirs = match.Place(1, "barracks", 16, 16);
        match.OrderTrain(1, theirs, Hoplite);
        match.Stockpiles.Add(0, 40, 0);
        match.OrderTrain(0, other, Peltast);
        match.Simulation.Tick();
        Assert.Equal((50, 10), match.Stock(0));
        Assert.Equal(8, match.Supply.Used(0));
        var center = match.Bodies.Get(barracks).Position;

        match.Destroy(barracks);
        match.Simulation.Tick();

        // Everything paid for the three orders is back in the tick the structure falls, with nothing kept for the progress.
        Assert.False(match.World.Entities.IsAlive(barracks));
        Assert.Equal((200, 20), match.Stock(0));
        Assert.Equal(2, match.Supply.Used(0));
        Assert.Equal(
            [
                new TrainEvent(SimEventKind.TrainRefunded, 101, 0, barracks, Peltast, 1, TrainCheck.Allowed, EntityId.None, center),
                new TrainEvent(SimEventKind.TrainRefunded, 101, 0, barracks, Hoplite, 1, TrainCheck.Allowed, EntityId.None, center),
                new TrainEvent(SimEventKind.TrainRefunded, 101, 0, barracks, Peltast, 1, TrainCheck.Allowed, EntityId.None, center),
            ],
            match.TrainEvents());
        Assert.False(match.World.Entities.IsAlive(barracks));

        // It is given back once, and the other structures work on.
        match.Simulation.Tick();
        Assert.Empty(match.TrainEvents());
        Assert.Equal((200, 20), match.Stock(0));
        Assert.False(match.Production.Producers.Contains(barracks));
        Assert.Single(match.OrdersAt(other));
        Assert.Single(match.OrdersAt(theirs));
        Assert.Equal((99_930, 99_990), match.Stock(1));

        // Nothing comes out of the fallen structure: one Peltast from the other, one Hoplite from the other player's.
        match.Simulation.Run(500);
        Assert.Equal(2, match.Combatants.Count);
    }

    [Fact]
    public void ADestroyedStructureWithAPairInItsQueueRefundsThePriceOfTwo()
    {
        var match = Create(TestMaps.Open(24, 24));
        match.Research.SetTiers(0, [2, 2, 3, 5, 5, 3]);
        var barracks = match.Place(0, "barracks", 10, 10);
        match.Stockpiles.Add(0, 114 + 57, 0);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(229);
        Assert.Equal([2, 1], match.OrdersAt(barracks).Select(order => order.Count));
        Assert.Equal((0, 0), match.Stock(0));

        // The structure falls in the tick its pair comes out: production runs before the deaths, so the pair is out and the single is refunded.
        match.Destroy(barracks);
        match.Simulation.Tick();

        Assert.Equal(
            [SimEventKind.UnitTrained, SimEventKind.UnitTrained, SimEventKind.TrainRefunded],
            match.TrainEvents().Select(train => train.Kind));
        Assert.Equal((57, 0), match.Stock(0));
        Assert.Equal(2, match.Supply.Used(0));
        Assert.Equal(2, match.Combatants.Count);
    }

    [Fact]
    public void AnEmptyQueueRefundsNothing()
    {
        var match = Create(TestMaps.Open(24, 24));
        var barracks = match.Place(0, "barracks", 10, 10);
        var capitol = match.Place(0, "capitol", 2, 2);

        match.Destroy(barracks);
        match.Destroy(capitol);
        match.Simulation.Tick();

        Assert.Empty(match.TrainEvents());
        Assert.Equal((0, 0), match.Stock(0));
    }
}
