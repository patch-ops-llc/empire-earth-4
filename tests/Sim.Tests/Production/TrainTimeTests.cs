using Sim.Core;
using Sim.Production;
using Sim.Setup;
using Sim.Tests.Map;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// How long an order takes (design 2.3 and 4.1), the pair of the Batch perk (design 8.2, point 7),
/// and what is fixed when an order is placed and what when its production begins.
/// </summary>
public class TrainTimeTests
{
    private static readonly int Hoplite = Chassis("hoplite");
    private static readonly int Peltast = Chassis("peltast");
    private static readonly int Crossbowman = Chassis("crossbowman");

    /// <summary>The mix of design 8.2, example B: FP 2, PR 2, MO 3, MF 5, LG 5, EX 3.</summary>
    private static readonly int[] SwarmMix = [2, 2, 3, 5, 5, 3];

    private static Match WithBarracks(out EntityId barracks, int[]? tiers = null)
    {
        var match = Create(TestMaps.Open(24, 24));
        if (tiers is not null)
        {
            match.Research.SetTiers(0, tiers);
        }

        barracks = match.Place(0, "barracks", 10, 10);
        return match;
    }

    [Fact]
    public void AWorkerTakesTwelveSecondsAndTheNextOneTwelveMore()
    {
        var match = Create(TestMaps.Open(24, 24));
        var capitol = match.Place(0, "capitol", 4, 4);
        match.Stockpiles.Add(0, 100, 0);

        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, capitol, AWorker);
        match.Simulation.Run(WorkerTicks - 1);

        Assert.Equal((0, 0), match.Stock(0));
        Assert.Equal(0, match.Workers.Count);
        Assert.Equal((WorkerTicks, WorkerTicks - 1), (match.ProducerOf(capitol).BuildTicks, match.ProducerOf(capitol).Progress));

        match.Simulation.Tick();

        var first = Assert.Single(match.Appeared());
        Assert.True(match.Workers.Contains(first));
        Assert.Equal(WorkerTicks - 1, match.World.Tick - 1);
        Assert.Equal(
            new TrainEvent(SimEventKind.UnitTrained, WorkerTicks - 1, 0, capitol, AWorker, 1, TrainCheck.Allowed, first, match.Bodies.Get(first).Position),
            Assert.Single(match.TrainEvents()));
        Assert.Single(match.OrdersAt(capitol));
        Assert.Equal((0, 0), (match.ProducerOf(capitol).BuildTicks, match.ProducerOf(capitol).Progress));

        Assert.Equal(WorkerTicks, match.RunUntilAppeared());
        Assert.Equal(2, match.Workers.Count);
        Assert.Empty(match.OrdersAt(capitol));
        Assert.Equal(2, match.Supply.Used(0));
    }

    [Fact]
    public void TheProgressOfTheFirstOrderRisesByOneEachTick()
    {
        var match = WithBarracks(out var barracks);
        match.MakeRich(0);
        Assert.Equal((0, 0, 0), (match.ProducerOf(barracks).Count, match.ProducerOf(barracks).BuildTicks, match.ProducerOf(barracks).Progress));

        match.OrderTrain(0, barracks, Hoplite);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        Assert.Equal((360, 1), (match.ProducerOf(barracks).BuildTicks, match.ProducerOf(barracks).Progress));

        match.Simulation.Run(99);
        Assert.Equal((360, 100), (match.ProducerOf(barracks).BuildTicks, match.ProducerOf(barracks).Progress));

        // The Hoplite's 18 seconds, then the Peltast's 12.
        Assert.Equal(260, match.RunUntilAppeared());
        Assert.Equal(Hoplite, match.Combatants.Get(match.Appeared()[0]).Chassis);
        Assert.Equal(240, match.RunUntilAppeared());
        Assert.Equal(Peltast, match.Combatants.Get(match.Appeared()[0]).Chassis);
    }

    [Fact]
    public void ABatchTrainsItsPairInThePairsTime()
    {
        var match = WithBarracks(out var barracks, SwarmMix);
        match.Stockpiles.Add(0, 64, 0);
        Assert.Equal((32, 0, 1), match.Production.PriceOf(0, Crossbowman));
        Assert.Equal(2, match.Production.CountOf(0, Crossbowman));

        match.OrderTrain(0, barracks, Crossbowman);
        match.Simulation.Tick();

        Assert.Equal(
            new TrainOrder { Unit = Crossbowman, Count = 2, Materials = 64, Energy = 0, Supply = 2 },
            Assert.Single(match.OrdersAt(barracks)));
        Assert.Equal((0, 0), match.Stock(0));
        Assert.Equal(2, match.Supply.Used(0));
        Assert.Equal(2, match.TrainEvents()[0].Count);
        Assert.Equal(230, match.ProducerOf(barracks).BuildTicks);

        // Design 8.2, example B: 1.5 times the 7.68 seconds of one, 11.52 seconds, 230 ticks.
        Assert.Equal(229, match.RunUntilAppeared());
        var pair = match.Appeared();
        Assert.Equal(2, pair.Length);
        Assert.All(pair, unit => Assert.Equal(Crossbowman, match.Combatants.Get(unit).Chassis));
        Assert.All(match.TrainEvents(), train => Assert.Equal((SimEventKind.UnitTrained, 1, Crossbowman, barracks), (train.Kind, train.Count, train.Unit, train.Structure)));
        Assert.Empty(match.OrdersAt(barracks));
        Assert.Equal(2, match.Supply.Used(0));
    }

    [Theory]
    [InlineData(63, 0, 31)] // The stockpile covers one.
    [InlineData(32, 0, 0)]
    [InlineData(1000, 114, 968)] // The supply has room for one: 114 of 115 are used.
    public void ABatchFallsBackToASingleUnitWhenOnlyOneCanBeAfforded(int materials, int supplyUsed, int materialsLeft)
    {
        var match = WithBarracks(out var barracks, SwarmMix);
        match.Stockpiles.Add(0, materials, 0);
        match.SpawnMany(0, Chassis("hippeus"), supplyUsed / 2);

        Assert.Equal((115, supplyUsed), (match.Supply.Cap(0), match.Supply.Used(0)));
        Assert.Equal(1, match.Production.CountOf(0, Crossbowman));

        match.OrderTrain(0, barracks, Crossbowman);
        match.Simulation.Tick();

        Assert.Equal(
            new TrainOrder { Unit = Crossbowman, Count = 1, Materials = 32, Energy = 0, Supply = 1 },
            Assert.Single(match.OrdersAt(barracks)));
        Assert.Equal((materialsLeft, 0), match.Stock(0));
        Assert.Equal(154, match.ProducerOf(barracks).BuildTicks);

        // One Crossbowman of example B: 7.68 seconds, 154 ticks.
        Assert.Equal(153, match.RunUntilAppeared());
        Assert.Single(match.Appeared());
    }

    [Fact]
    public void AnOrderIsFixedWhenItIsPlaced()
    {
        var match = WithBarracks(out var barracks);
        match.Stockpiles.Add(0, 200, 0);

        // At tier 1 a Peltast is 40 Materials and 2 supply, and comes alone.
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        var placed = match.OrdersAt(barracks);
        Assert.All(placed, order => Assert.Equal(new TrainOrder { Unit = Peltast, Count = 1, Materials = 40, Energy = 0, Supply = 2 }, order));

        // Under the swarm mix it would be 57 Materials and 1 supply, in pairs.
        match.Research.SetTiers(0, SwarmMix);
        Assert.Equal((57, 0, 1), match.Production.PriceOf(0, Peltast));
        Assert.Equal(2, match.Production.CountOf(0, Peltast));
        match.Simulation.Tick();

        Assert.Equal(placed, match.OrdersAt(barracks));
        Assert.Equal((120, 0), match.Stock(0));
        Assert.Equal(4, match.Supply.Used(0));

        // Cancelling gives back the price that was paid, not today's.
        match.OrderCancel(0, barracks, 1);
        match.Simulation.Tick();
        Assert.Equal((160, 0), match.Stock(0));

        // The order in production keeps the build time it began with, and one unit comes out.
        Assert.Equal(240, match.ProducerOf(barracks).BuildTicks);
        Assert.Equal(237, match.RunUntilAppeared());
        Assert.Single(match.Appeared());
    }

    [Fact]
    public void TheBuildTimeIsSetWhenProductionOfTheOrderBegins()
    {
        var match = WithBarracks(out var barracks);
        match.MakeRich(0);

        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        match.Research.SetTiers(0, SwarmMix);

        // The first began at tier 1, with 240 ticks. The second begins under the swarm mix, with 154.
        Assert.Equal(239, match.RunUntilAppeared());
        var first = Assert.Single(match.Appeared());
        Assert.Equal(154, match.RunUntilAppeared());
        var second = Assert.Single(match.Appeared());

        // Each came out with the stats of its owner's tech as it was then.
        var blueprint = match.Research.Tech(0).Blueprints[Peltast];
        Assert.Equal(blueprint.HitPoints, match.Health.Get(first).HitPoints);
        Assert.Equal(blueprint.HitPoints, match.Health.Get(second).HitPoints);
    }

    [Fact]
    public void APairWhoseChassisLostTheBatchPerkTakesTheTimeOfTwo()
    {
        var match = WithBarracks(out var barracks, SwarmMix);
        match.MakeRich(0);

        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        Assert.All(match.OrdersAt(barracks), order => Assert.Equal(2, order.Count));
        Assert.Equal(230, match.ProducerOf(barracks).BuildTicks);

        // An even mix has no Batch perk.
        match.Research.SetTiers(0, [5, 5, 5, 5, 5, 5]);
        var blueprint = match.Research.Tech(0).Blueprints[Peltast];
        Assert.Equal(1, blueprint.BatchSize);

        Assert.Equal(229, match.RunUntilAppeared());
        Assert.Equal(2, match.Appeared().Length);
        match.Simulation.Tick();
        Assert.Equal(2 * blueprint.BuildTicks, match.ProducerOf(barracks).BuildTicks);
        Assert.Equal((2 * blueprint.BuildTicks) - 1, match.RunUntilAppeared());
        Assert.Equal(2, match.Appeared().Length);
    }

    [Fact]
    public void TwoStructuresWorkAtTheSameTime()
    {
        var match = WithBarracks(out var barracks);
        var second = match.Place(0, "barracks", 2, 2);
        match.MakeRich(0);

        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, second, Peltast);

        Assert.Equal(240, match.RunUntilAppeared());
        Assert.Equal([barracks, second], match.TrainEvents().Select(train => train.Structure));
    }
}
