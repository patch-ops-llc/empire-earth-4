using Sim.Combat;
using Sim.Core;
using Sim.Map;
using Sim.Production;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// Design 8.4, row 31: a queue holds at most 5 orders, and an order the stockpile or the free
/// supply does not cover is refused. And design 2.3: an order is paid for, and holds its supply,
/// from the moment it is placed.
/// </summary>
public class TrainOrderTests
{
    private static readonly int Hoplite = Chassis("hoplite");
    private static readonly int Peltast = Chassis("peltast");
    private static readonly int Hippeus = Chassis("hippeus");
    private static readonly int Legionary = Chassis("legionary");

    private static Match WithBarracks(out EntityId barracks)
    {
        var match = Create(TestMaps.Open(24, 24));
        barracks = match.Place(0, "barracks", 10, 10);
        return match;
    }

    [Fact]
    public void AnOrderIsPaidForAndHoldsItsSupplyFromTheMomentItIsPlaced()
    {
        var match = WithBarracks(out var barracks);
        match.Stockpiles.Add(0, 100, 15);

        match.OrderTrain(0, barracks, Hoplite);
        match.Simulation.Tick();

        Assert.Equal((30, 5), match.Stock(0));
        Assert.Equal(2, match.Supply.Used(0));
        Assert.Equal(2, match.Production.SupplyUsedBy(0));
        Assert.Equal(0, match.Supply.Used(1));
        Assert.Equal(
            new TrainOrder { Unit = Hoplite, Count = 1, Materials = 70, Energy = 10, Supply = 2 },
            Assert.Single(match.OrdersAt(barracks)));
        Assert.Equal(
            new TrainEvent(SimEventKind.TrainOrdered, 0, 0, barracks, Hoplite, 1, TrainCheck.Allowed, EntityId.None, match.Bodies.Get(barracks).Position),
            Assert.Single(match.TrainEvents()));
    }

    [Fact]
    public void AQueueHoldsFiveOrdersAndRefusesTheSixth()
    {
        var match = WithBarracks(out var barracks);
        match.Stockpiles.Add(0, 1000, 0);

        for (var order = 0; order < 6; order++)
        {
            match.OrderTrain(0, barracks, Peltast);
        }

        match.Simulation.Tick();

        Assert.Equal(5, Producer.MaxOrders);
        Assert.Equal(5, match.OrdersAt(barracks).Length);
        Assert.Equal((800, 0), match.Stock(0));
        Assert.Equal(10, match.Supply.Used(0));
        var events = match.TrainEvents();
        Assert.Equal(Enumerable.Repeat(SimEventKind.TrainOrdered, 5).Append(SimEventKind.TrainRefused), events.Select(train => train.Kind));
        Assert.Equal(
            new TrainEvent(SimEventKind.TrainRefused, 0, 0, barracks, Peltast, 0, TrainCheck.QueueFull, EntityId.None, default),
            events[^1]);
        Assert.Equal(TrainCheck.QueueFull, match.Production.CheckTrain(0, barracks, Peltast));

        // A place comes free when the first order is done.
        match.Simulation.Run(239);
        Assert.Equal(4, match.OrdersAt(barracks).Length);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        Assert.Equal(5, match.OrdersAt(barracks).Length);
    }

    [Theory]
    [InlineData(69, 10)]
    [InlineData(70, 9)]
    [InlineData(0, 0)]
    public void AnOrderTheStockpileDoesNotCoverIsRefused(int materials, int energy)
    {
        var match = WithBarracks(out var barracks);
        match.Stockpiles.Add(0, materials, energy);

        match.OrderTrain(0, barracks, Hoplite);
        match.Simulation.Tick();

        Assert.Empty(match.OrdersAt(barracks));
        Assert.Equal((materials, energy), match.Stock(0));
        Assert.Equal(0, match.Supply.Used(0));
        var refused = Assert.Single(match.TrainEvents());
        Assert.Equal((SimEventKind.TrainRefused, TrainCheck.TooExpensive, Hoplite), (refused.Kind, refused.Reason, refused.Unit));
    }

    [Fact]
    public void AnOrderTheFreeSupplyDoesNotCoverIsRefused()
    {
        var match = WithBarracks(out var barracks);
        var capitol = match.Place(0, "capitol", 2, 2);
        match.MakeRich(0);
        for (var unit = 0; unit < 17; unit++)
        {
            match.SpawnChassis(0, Hippeus, MapGrid.TileCenter(20, 2 + unit));
        }

        Assert.Equal((55, 51), (match.Supply.Cap(0), match.Supply.Used(0)));

        // The supply of an order in a queue counts: one Peltast is 2, and 53 of 55 leaves room for a worker or two, not for a Peltast.
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        Assert.Equal(2, match.OrdersAt(barracks).Length);
        Assert.Equal(55, match.Supply.Used(0));
        Assert.Equal(TrainCheck.NoSupply, match.TrainEvents()[^1].Reason);
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, capitol, AWorker));

        // Cancelling gives the supply back, and then a worker and one more fit, and no third.
        match.OrderCancel(0, barracks, 1);
        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, capitol, AWorker);
        match.Simulation.Tick();

        Assert.Equal(2, match.OrdersAt(capitol).Length);
        Assert.Equal(55, match.Supply.Used(0));
        Assert.Equal(
            [SimEventKind.TrainCancelled, SimEventKind.TrainOrdered, SimEventKind.TrainOrdered, SimEventKind.TrainRefused],
            match.TrainEvents().Select(train => train.Kind));
        Assert.Equal((AWorker, TrainCheck.NoSupply), (match.TrainEvents()[^1].Unit, match.TrainEvents()[^1].Reason));
    }

    [Fact]
    public void TheSupplyOfAnOrderBecomesTheSupplyOfItsUnit()
    {
        var match = WithBarracks(out var barracks);
        match.MakeRich(0);

        match.OrderTrain(0, barracks, Hoplite);
        match.Simulation.Tick();
        Assert.Equal((2, 2), (match.Supply.Used(0), match.Production.SupplyUsedBy(0)));

        match.RunUntilAppeared();

        Assert.Equal((2, 0), (match.Supply.Used(0), match.Production.SupplyUsedBy(0)));
    }

    [Fact]
    public void AStructureTakesOnlyTheOrdersForWhatItTrains()
    {
        var match = WithBarracks(out var barracks);
        var capitol = match.Place(0, "capitol", 2, 2);
        var works = match.Place(0, "works", 16, 16);
        match.MakeRich(0);

        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, barracks, Peltast));
        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, barracks, Hoplite));
        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, works, Hippeus));
        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, capitol, AWorker));
        Assert.Equal(TrainCheck.NotTrainedHere, match.Production.CheckTrain(0, barracks, AWorker));
        Assert.Equal(TrainCheck.NotTrainedHere, match.Production.CheckTrain(0, barracks, Hippeus));
        Assert.Equal(TrainCheck.NotTrainedHere, match.Production.CheckTrain(0, works, Peltast));
        Assert.Equal(TrainCheck.NotTrainedHere, match.Production.CheckTrain(0, capitol, Peltast));
        Assert.Equal(TrainCheck.NotTrainedHere, match.Production.CheckTrain(0, barracks, 9999));
        Assert.Equal(TrainCheck.NotTrainedHere, match.Production.CheckTrain(0, barracks, -2));
        Assert.Equal(TrainCheck.NotTrainedHere, match.Production.CheckTrain(0, barracks, int.MinValue));

        // The Legionary is the Guard of epoch 2, and the player is in epoch 1.
        Assert.Equal(TrainCheck.Locked, match.Production.CheckTrain(0, barracks, Legionary));
        match.Research.SetTiers(0, [2, 2, 2, 1, 1, 1]);
        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, barracks, Legionary));
    }

    [Fact]
    public void WhatAStructureTrainsIsListedInTheOrderItIsOffered()
    {
        var match = WithBarracks(out var barracks);
        var data = match.Data;

        Assert.Equal([AWorker], match.Production.TrainedAt(data.StructureOf("capitol").Index));
        Assert.Empty(match.Production.TrainedAt(data.StructureOf("generator").Index));
        Assert.Empty(match.Production.TrainedAt(-1));
        Assert.Empty(match.Production.TrainedAt(9999));

        var offered = match.Production.TrainedAt(match.Structures.Get(barracks).Type);
        var lines = offered.Select(unit => data.Chassis[unit].Line).ToArray();
        Assert.Equal(18, offered.Count);
        Assert.Equal(Peltast, offered[0]);
        Assert.Equal(Hoplite, offered[9]);
        Assert.Equal(
            Enumerable.Repeat(data.Chassis[Peltast].Line, 9).Concat(Enumerable.Repeat(data.Chassis[Hoplite].Line, 9)),
            lines);
        Assert.Equal(
            Enumerable.Range(1, 9).Concat(Enumerable.Range(1, 9)),
            offered.Select(unit => data.Chassis[unit].Epoch));
    }

    [Fact]
    public void OnlyTheOwnersFinishedLivingProductionStructureTakesAnOrder()
    {
        var match = WithBarracks(out var barracks);
        var generator = match.Place(0, "generator", 2, 2);
        var laid = match.LayStructure(0, match.Data.StructureOf("barracks"), 16, 16);
        var theirs = match.Place(1, "barracks", 2, 16);
        var unit = match.SpawnChassis(0, Peltast, MapGrid.TileCenter(20, 20));
        var razed = match.Place(0, "barracks", 16, 2);
        match.MakeRich(0);
        match.MakeRich(1);
        match.Destroy(razed);
        match.Simulation.Tick();

        Command[] refused =
        [
            TrainCommand(0, generator, Peltast),
            TrainCommand(0, laid, Peltast),
            TrainCommand(0, theirs, Peltast),
            TrainCommand(0, unit, Peltast),
            TrainCommand(0, razed, Peltast),
            TrainCommand(0, new EntityId(9999), Peltast),
            TrainCommand(0, EntityId.None, Peltast),
            TrainCommand(7, barracks, Peltast),
            TrainCommand(-1, barracks, Peltast),
            new Command(CommandType.Train, 0, [], contentId: Peltast),
            new Command(CommandType.Train, 0, [barracks, barracks], contentId: Peltast),
        ];
        foreach (var command in refused)
        {
            match.Simulation.Commands.Enqueue(match.World.Tick, command);
        }

        match.Simulation.Tick();

        Assert.Equal((100_000, 100_000), match.Stock(0));
        Assert.Equal(0, match.Production.SupplyUsedBy(0));
        Assert.Empty(match.OrdersAt(barracks));
        Assert.Empty(match.OrdersAt(theirs));
        Assert.False(match.Production.Producers.Contains(laid));
        Assert.False(match.Production.Producers.Contains(generator));
        var events = match.TrainEvents();
        Assert.Equal(refused.Length, events.Length);
        Assert.All(events, train => Assert.Equal((SimEventKind.TrainRefused, TrainCheck.NoProducer, Peltast, 0), (train.Kind, train.Reason, train.Unit, train.Count)));
        Assert.Equal(
            refused.Select(command => (command.Player, command.Entities.Length == 0 ? EntityId.None : command.Entities[0])),
            events.Select(train => (train.Player, train.Structure)));

        // The structure takes orders once it is finished.
        match.FinishStructure(laid);
        match.OrderTrain(0, laid, Peltast);
        match.Simulation.Tick();
        Assert.Single(match.OrdersAt(laid));
    }

    [Fact]
    public void TheReasonsAreCheckedInAFixedOrder()
    {
        var match = WithBarracks(out var barracks);
        for (var unit = 0; unit < 18; unit++)
        {
            match.SpawnChassis(0, Hippeus, MapGrid.TileCenter(20, 2 + unit));
        }

        // Locked comes before a full queue, a full queue before the price, and the price before the supply.
        Assert.Equal(54, match.Supply.Used(0));
        Assert.Equal(TrainCheck.Locked, match.Production.CheckTrain(0, barracks, Legionary));
        Assert.Equal(TrainCheck.TooExpensive, match.Production.CheckTrain(0, barracks, Peltast));
        match.Stockpiles.Add(0, 40, 0);
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, barracks, Peltast));

        var other = Create(TestMaps.Open(24, 24));
        var full = other.Place(0, "barracks", 10, 10);
        other.Stockpiles.Add(0, 200, 0);
        for (var order = 0; order < 5; order++)
        {
            other.OrderTrain(0, full, Peltast);
        }

        other.Simulation.Tick();
        Assert.Equal((0, 0), other.Stock(0));
        Assert.Equal(TrainCheck.QueueFull, other.Production.CheckTrain(0, full, Peltast));
        Assert.Equal(TrainCheck.Locked, other.Production.CheckTrain(0, full, Legionary));
    }

    [Fact]
    public void AskingWhetherAnOrderCanBePlacedChangesNothing()
    {
        var match = WithBarracks(out var barracks);
        match.MakeRich(0);
        var before = match.World.ComputeHash();

        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, barracks, Peltast));
        Assert.Equal(1, match.Production.CountOf(0, Peltast));
        Assert.Equal((40, 0, 2), match.Production.PriceOf(0, Peltast));
        Assert.Equal((50, 0, 1), match.Production.PriceOf(0, AWorker));

        Assert.Equal(before, match.World.ComputeHash());
    }

    [Fact]
    public void TheOrdersOfTwoPlayersAndTwoStructuresAreKeptApart()
    {
        var match = WithBarracks(out var mine);
        var second = match.Place(0, "barracks", 2, 2);
        var theirs = match.Place(1, "barracks", 16, 16);
        match.Stockpiles.Add(0, 500, 500);
        match.Stockpiles.Add(1, 500, 500);

        match.OrderTrain(0, mine, Peltast);
        match.OrderTrain(1, theirs, Hoplite);
        match.OrderTrain(0, second, Hoplite);
        match.OrderTrain(0, mine, Hoplite);
        match.Simulation.Tick();

        Assert.Equal([Peltast, Hoplite], match.OrdersAt(mine).Select(order => order.Unit));
        Assert.Equal([Hoplite], match.OrdersAt(second).Select(order => order.Unit));
        Assert.Equal([Hoplite], match.OrdersAt(theirs).Select(order => order.Unit));
        Assert.Equal((320, 480), match.Stock(0));
        Assert.Equal((430, 490), match.Stock(1));
        Assert.Equal((6, 2), (match.Supply.Used(0), match.Supply.Used(1)));
    }

    [Fact]
    public void AnEventOfAnotherKindIsNoEventOfProduction()
    {
        Assert.False(TrainEvent.TryRead(new SimEvent(SimEventKind.OrderRefused, 3, 0, default, default, default, 0), out var train));
        Assert.Equal(default, train);
        Assert.Equal(
            [SimEventKind.RallyPointSet, SimEventKind.TrainCancelled, SimEventKind.TrainOrdered, SimEventKind.TrainRefunded, SimEventKind.TrainRefused, SimEventKind.UnitTrained],
            Enum.GetValues<SimEventKind>().Where(TrainEvent.IsProduction));
    }
}
