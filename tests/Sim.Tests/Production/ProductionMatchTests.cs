using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Production;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Research;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// Production as part of a match: its store, its place in the state hash and in a snapshot, and
/// a match that begins as design 5.2 says and trains from there.
/// </summary>
public class ProductionMatchTests
{
    private static readonly int Hoplite = Chassis("hoplite");
    private static readonly int Peltast = Chassis("peltast");

    private static Match Started() => MatchSetup.Create(ProductionMatches.Data, ProductionMatches.Data.Maps[0], 1, [new(0), new(1)]);

    [Fact]
    public void TheMatchRegistersTheStoreOfProduction()
    {
        var match = Create(TestMaps.Open(24, 24));

        Assert.Equal("producers", Producer.StoreName);
        Assert.Same(match.Production.Producers, Assert.Single(match.World.Stores, store => store.Name == Producer.StoreName));
    }

    [Fact]
    public void AStructureThatTrainsHasAQueueFromTheMomentItIsFinished()
    {
        var match = Create(TestMaps.Open(24, 24));
        var data = match.Data;

        var laid = match.LayStructure(0, data.StructureOf("barracks"), 10, 10);
        var generator = match.Place(0, "generator", 2, 2);
        Assert.Equal(0, match.Production.Producers.Count);

        match.FinishStructure(laid);
        var capitol = match.Place(1, "capitol", 16, 16);
        var works = match.Place(1, "works", 2, 16);

        Assert.Equal([laid, capitol, works], Enumerable.Range(0, match.Production.Producers.Count).Select(match.Production.Producers.EntityAt));
        Assert.False(match.Production.Producers.Contains(generator));
        Assert.Equal(default, match.ProducerOf(laid));
    }

    [Fact]
    public void AMatchThatBeginsWithWorkersAloneTrainsMore()
    {
        var match = Started();
        var capitols = Enumerable.Range(0, match.Production.Producers.Count).Select(match.Production.Producers.EntityAt).ToArray();
        Assert.Equal(2, capitols.Length);
        Assert.Equal((12, 0), (match.Workers.Count, match.Combatants.Count));
        Assert.Equal((6, 6), (match.Supply.Used(0), match.Supply.Used(1)));

        // 400 Materials at the start: 8 workers' worth, and a queue holds 5.
        for (var order = 0; order < 6; order++)
        {
            match.OrderTrain(0, capitols[0], AWorker);
        }

        match.OrderTrain(1, capitols[1], AWorker);
        match.Simulation.Tick();

        Assert.Equal((5, 1), (match.OrdersAt(capitols[0]).Length, match.OrdersAt(capitols[1]).Length));
        Assert.Equal((11, 7), (match.Supply.Used(0), match.Supply.Used(1)));
        Assert.Equal(150_000, match.Stockpiles.Materials(0) - (match.Stockpiles.Materials(0) % 1000));

        match.Simulation.Run((5 * WorkerTicks) - 1);

        Assert.Equal(18, match.Workers.Count);
        Assert.Equal((11, 7), (match.Supply.Used(0), match.Supply.Used(1)));
        Assert.Empty(match.OrdersAt(capitols[0]));
    }

    [Fact]
    public void TheQueuesAreInTheStateHash()
    {
        ulong HashAfter(Action<Match, EntityId> change)
        {
            var match = Create(TestMaps.Open(24, 24));
            var barracks = match.Place(0, "barracks", 10, 10);
            match.MakeRich(0);
            match.OrderTrain(0, barracks, Hoplite);
            match.OrderTrain(0, barracks, Peltast);
            match.Simulation.Run(10);
            change(match, barracks);
            return match.World.ComputeHash();
        }

        var plain = HashAfter((_, _) => { });

        Assert.Equal(plain, HashAfter((_, _) => { }));
        Assert.NotEqual(plain, HashAfter((match, barracks) => match.Production.Producers.Get(barracks).Progress++));
        Assert.NotEqual(plain, HashAfter((match, barracks) => match.Production.Producers.Get(barracks).BuildTicks++));
        Assert.NotEqual(plain, HashAfter((match, barracks) => match.Production.Producers.Get(barracks).RallySet = true));
        Assert.NotEqual(plain, HashAfter((match, barracks) => match.Production.Producers.Get(barracks).RallyPoint = MapGrid.TileCenter(0, 1)));
        Assert.NotEqual(plain, HashAfter((match, barracks) => match.Production.Producers.Get(barracks).RallyPoint = MapGrid.TileCenter(1, 0)));
    }

    public static TheoryData<int> SnapshotTicks => [0, 1, 2, 60, 239, 240, 241, 300, 469, 470, 471, 600];

    [Theory]
    [MemberData(nameof(SnapshotTicks))]
    public void AMatchWithOrdersInItsQueuesRestoresAndContinues(int snapshotTick)
    {
        // The deposit is the first entity of the match.
        var capitol = new EntityId(2);
        var barracks = new EntityId(3);
        (int Tick, Command Command)[] script =
        [
            (0, TrainCommand(0, capitol, AWorker)),
            (0, TrainCommand(0, capitol, AWorker)),
            (0, TrainCommand(0, barracks, Peltast)),
            (1, RallyCommand(0, TestMatches.Point("12.5", "5.5"), capitol)),
            (1, TrainCommand(0, barracks, Peltast)),
            (1, TrainCommand(0, barracks, Hoplite)),
            (1, TrainCommand(0, barracks, Peltast)),
            (50, RallyCommand(0, TestMatches.Point("20.5", "20.5"), barracks)),
            (60, CancelCommand(0, barracks, 2)),
            (250, CancelCommand(0, barracks, 0)),
            (260, TrainCommand(0, capitol, AWorker)),
            (480, TrainCommand(0, barracks, Hoplite)),
        ];

        SnapshotAssert.RestoresAndContinues(Build, script, snapshotTick, lastTick: 760);

        static Match Build()
        {
            var match = Create(TestMaps.Open(24, 24).WithDeposits((12, 5)));
            match.Research.SetTiers(0, [2, 2, 3, 5, 5, 3]);
            Assert.Equal(new EntityId(2), match.Place(0, "capitol", 4, 4));
            Assert.Equal(new EntityId(3), match.Place(0, "barracks", 10, 12));
            match.Stockpiles.Add(0, 2000, 500);
            match.RefreshDerivedState();
            return match;
        }
    }

    [Fact]
    public void TheScriptOfTheSnapshotTestTrainsCancelsAndAssigns()
    {
        var match = Create(TestMaps.Open(24, 24).WithDeposits((12, 5)));
        match.Research.SetTiers(0, [2, 2, 3, 5, 5, 3]);
        var capitol = match.Place(0, "capitol", 4, 4);
        var barracks = match.Place(0, "barracks", 10, 12);
        match.Stockpiles.Add(0, 2000, 500);
        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        match.OrderRally(0, TestMatches.Point("12.5", "5.5"), capitol);
        match.Simulation.Run(600);

        // A deposit, whichever entity it is, has the worker; the pair of Peltasts stands beside the Barracks.
        Assert.Equal(1, match.Workers.Count);
        Assert.Equal(WorkerState.Working, match.Workers.ValueAt(0).State);
        Assert.Equal(2, match.Combatants.Count);
    }

    [Theory]
    [InlineData("Count", 0, 6)] // The queue's own count, then the count of each of its five orders.
    [InlineData("Count", 0, -1)]
    [InlineData("Count", 1, 3)]
    [InlineData("Count", 5, -1)]
    [InlineData("Materials", 0, -1)]
    [InlineData("Energy", 2, -1)]
    [InlineData("Supply", 4, -1)]
    public void ARestoreRefusesAQueueThatCannotBe(string field, int occurrence, int replacement)
    {
        var producer = default(Producer);
        var change = new ChangingWalker(field, occurrence, replacement);

        Assert.Throws<InvalidDataException>(() => producer.Walk(change));
        Assert.True(change.Changed);
    }

    [Fact]
    public void ARestoreTakesEveryFieldOfAQueue()
    {
        var producer = default(Producer);

        producer.Walk(new ChangingWalker("Count", 0, 5));
        producer.Walk(new ChangingWalker("Unit", 4, 7));
        producer.Walk(new ChangingWalker("Supply", 4, 3));

        Assert.Equal(5, producer.Count);
        Assert.Equal(new TrainOrder { Unit = 7, Supply = 3 }, producer[4]);
        Assert.Equal(3, producer.ReservedSupply);
        Assert.Throws<ArgumentOutOfRangeException>(() => producer[5]);
        Assert.Throws<ArgumentOutOfRangeException>(() => producer[-1]);
    }
}
