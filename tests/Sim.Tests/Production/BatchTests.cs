using Sim.Core;
using Sim.Data;
using Sim.Production;
using Sim.Setup;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Tech;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// The Batch perk in a real queue (design 2.3, Standardized I, and decision D8): "each queue slot
/// produces two units in 1.5x the build time of one", a pair costs the price and the supply of
/// two, and "if the stockpile or the free supply covers only one unit, a single unit is ordered
/// instead, at the build time of one".
/// </summary>
/// <remarks>
/// <see cref="TrainTimeTests"/> has the pair and its fallback for the Crossbowman of example B.
/// These are the cases that test leaves: the design's own pair of Auxiliaries, a whole queue of
/// pairs, a price in two resources, a perk that a research brings, and the numbers that are data.
/// </remarks>
public class BatchTests
{
    /// <summary>Design 8.2, example B at first-playable tiers: FP 1, PR 1, MO 2, MF 4, LG 4, EX 2.</summary>
    private static readonly int[] FirstPlayableSwarm = [1, 1, 2, 4, 4, 2];

    private static readonly int Auxiliary = Chassis("auxiliary");
    private static readonly int Peltast = Chassis("peltast");
    private static readonly int Hippeus = Chassis("hippeus");

    private static Match With(string structure, int[] tiers, out EntityId producer, GameData? data = null)
    {
        var match = Create(TestMaps.Open(24, 24), data: data);
        match.Research.SetTiers(0, tiers);
        producer = match.Place(0, structure, 10, 10);
        return match;
    }

    /// <summary>
    /// "A pair of Auxiliaries in example B takes 230 ticks, not 1.5 x 154 = 231": the pair's time
    /// comes from the unrounded time of one, 7.68 seconds.
    /// </summary>
    [Fact]
    public void APairOfAuxiliariesCostsThePriceAndSupplyOfTwoAndTakes230Ticks()
    {
        var match = With("barracks", FirstPlayableSwarm, out var barracks);
        match.Stockpiles.Add(0, 56, 0);
        var blueprint = match.BlueprintOf(0, Auxiliary);
        Assert.Equal((28, 0, 1), match.Production.PriceOf(0, Auxiliary));
        Assert.Equal((154, 2, 230), (blueprint.BuildTicks, blueprint.BatchSize, blueprint.BatchBuildTicks));
        Assert.Equal(100, match.Supply.Cap(0));

        match.OrderTrain(0, barracks, Auxiliary);
        match.Simulation.Tick();

        Assert.Equal(
            new TrainOrder { Unit = Auxiliary, Count = 2, Materials = 56, Energy = 0, Supply = 2 },
            Assert.Single(match.OrdersAt(barracks)));
        Assert.Equal((0, 0), match.Stock(0));
        Assert.Equal(2, match.Supply.Used(0));

        match.Simulation.Run(228);
        Assert.Equal(0, match.Combatants.Count);
        match.Simulation.Tick();

        Assert.Equal(2, match.Appeared().Length);
        Assert.Equal(2, match.Combatants.Count);
        Assert.Equal(2, match.Supply.Used(0));
    }

    /// <summary>"Each queue slot produces two units": five orders are ten units, two every 230 ticks.</summary>
    [Fact]
    public void EachSlotOfAQueueProducesTwoUnits()
    {
        var match = With("barracks", FirstPlayableSwarm, out var barracks);
        match.Stockpiles.Add(0, 1000, 0);

        for (var order = 0; order < 6; order++)
        {
            match.OrderTrain(0, barracks, Auxiliary);
        }

        match.Simulation.Tick();

        Assert.Equal(5, match.OrdersAt(barracks).Length);
        Assert.All(match.OrdersAt(barracks), order => Assert.Equal((2, 56, 2), (order.Count, order.Materials, order.Supply)));
        Assert.Equal(TrainCheck.QueueFull, match.TrainEvents()[^1].Reason);
        Assert.Equal((1000 - 280, 0), match.Stock(0));
        Assert.Equal(10, match.Supply.Used(0));

        for (var pair = 1; pair <= 5; pair++)
        {
            Assert.Equal(pair == 1 ? 229 : 230, match.RunUntilAppeared());
            Assert.Equal(2, match.Appeared().Length);
            Assert.Equal(2 * pair, match.Combatants.Count);
        }

        Assert.Empty(match.OrdersAt(barracks));
        Assert.Equal(10, match.Supply.Used(0));
        Assert.Equal((720, 0), match.Stock(0));
    }

    /// <summary>
    /// A pair is paid in both resources, and either one can be what covers only one unit. Under
    /// Manufacturing 2 a Hippeus is 81 Materials and 27 Energy and takes 384 ticks; a pair takes 576.
    /// </summary>
    [Theory]
    [InlineData(162, 54, 2)]
    [InlineData(161, 54, 1)] // The Materials cover one.
    [InlineData(162, 53, 1)] // The Energy covers one.
    [InlineData(81, 27, 1)]
    public void APairIsPaidInBothResourcesAndEitherCanBeTheOneThatCoversOnlyOne(int materials, int energy, int count)
    {
        var match = With("works", [1, 1, 1, 2, 1, 1], out var works);
        match.Stockpiles.Add(0, materials, energy);
        Assert.Equal((81, 27, 3), match.Production.PriceOf(0, Hippeus));
        Assert.Equal(count, match.Production.CountOf(0, Hippeus));

        match.OrderTrain(0, works, Hippeus);
        match.Simulation.Tick();

        Assert.Equal(
            new TrainOrder { Unit = Hippeus, Count = count, Materials = count * 81, Energy = count * 27, Supply = count * 3 },
            Assert.Single(match.OrdersAt(works)));
        Assert.Equal((materials - (count * 81), energy - (count * 27)), match.Stock(0));
        Assert.Equal(count == 2 ? 576 : 384, match.ProducerOf(works).BuildTicks);
        Assert.Equal(count == 2 ? 575 : 383, match.RunUntilAppeared());
        Assert.Equal(count, match.Appeared().Length);
    }

    /// <summary>
    /// A research that brings Batch changes the orders placed after it and none placed before
    /// (design 2.3: "an order is fixed when it is placed"). Research is the last thing of a tick,
    /// so an order given in the tick a research finishes is still placed without it.
    /// </summary>
    [Fact]
    public void AResearchThatBringsBatchMakesPairsOfTheOrdersPlacedAfterIt()
    {
        var match = With("barracks", [1, 1, 1, 1, 1, 1], out var barracks);
        match.Research.OpenPlayerSlot(0);
        match.MakeRich(0);
        match.OrderResearch(0, "manufacturing");
        match.Simulation.Tick();

        match.RunUntilResearchHas(0, ticksLeft: 2);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        // The research finished in the tick that placed the second order.
        Assert.Equal(2, match.Research.State.Tier(0, DesignData.Track("manufacturing")));
        var single = new TrainOrder { Unit = Peltast, Count = 1, Materials = 40, Energy = 0, Supply = 2 };
        Assert.Equal([single, single], match.OrdersAt(barracks));
        Assert.Equal(2, match.Production.CountOf(0, Peltast));

        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        // Standardized I on a Peltast: 36 Materials, 192 ticks, a pair in 288.
        var pair = new TrainOrder { Unit = Peltast, Count = 2, Materials = 72, Energy = 0, Supply = 4 };
        Assert.Equal([single, single, pair], match.OrdersAt(barracks));

        // The first began before the research with 240 ticks; the second begins after it and
        // takes the 192 of one unit; the third takes the pair's 288.
        Assert.Equal(240, match.ProducerOf(barracks).BuildTicks);
        Assert.Equal(237, match.RunUntilAppeared());
        Assert.Single(match.Appeared());
        Assert.Equal(192, match.RunUntilAppeared());
        Assert.Single(match.Appeared());
        Assert.Equal(288, match.RunUntilAppeared());
        Assert.Equal(2, match.Appeared().Length);
        Assert.Equal(8, match.Supply.Used(0));
    }

    /// <summary>
    /// "A batch is always a pair; only the time multiplier is a tuning value": with 1.25 in the
    /// data a pair of Peltasts under Manufacturing 2 takes 240 ticks, and is still two.
    /// </summary>
    [Fact]
    public void TheTimeOfAPairIsDataAndAPairIsAlwaysTwo()
    {
        var data = DataAssert.Valid(Files().Edit(DesignData.AccentsPath, "\"time_multiplier\": \"1.5\"", "\"time_multiplier\": \"1.25\""));
        var match = With("barracks", [1, 1, 1, 2, 1, 1], out var barracks, data);
        match.MakeRich(0);

        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        Assert.Equal(2, Assert.Single(match.OrdersAt(barracks)).Count);
        Assert.Equal(240, match.ProducerOf(barracks).BuildTicks);
        Assert.Equal(239, match.RunUntilAppeared());
        Assert.Equal(2, match.Appeared().Length);
    }

    /// <summary>The worker is no chassis and carries no accent: a Capitol trains one at a time whatever the mix.</summary>
    [Fact]
    public void AWorkerIsNeverTrainedInPairs()
    {
        var match = With("capitol", FirstPlayableSwarm, out var capitol);
        match.MakeRich(0);
        Assert.Equal(1, match.Production.CountOf(0, AWorker));
        Assert.Equal((50, 0, 1), match.Production.PriceOf(0, AWorker));

        match.OrderTrain(0, capitol, AWorker);
        match.Simulation.Tick();

        Assert.Equal(
            new TrainOrder { Unit = AWorker, Count = 1, Materials = 50, Energy = 0, Supply = 1 },
            Assert.Single(match.OrdersAt(capitol)));
        Assert.Equal(WorkerTicks - 1, match.RunUntilAppeared());
        Assert.Single(match.Appeared());
        Assert.Equal(1, match.Workers.Count);
    }
}
