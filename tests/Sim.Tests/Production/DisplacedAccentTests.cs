using Sim.Core;
using Sim.Data;
using Sim.Production;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Tech;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// Design 2.3, "Accents can be displaced", and decision D2: on a chassis older than the player's
/// epoch a research can lift a third track above one of the two accents the unit carries, and
/// the unit swaps perks. Two of the three intended consequences are production's and are here:
/// used supply above the cap, and a pair already in a queue. The third, the Reserves refund,
/// comes with that perk.
/// </summary>
/// <remarks>
/// Every swap here is made by a research that is ordered, paid and run to its end in the match,
/// on a Peltast, the chassis of epoch 1.
/// </remarks>
public class DisplacedAccentTests
{
    private static readonly int Firepower = DesignData.Track("firepower");
    private static readonly int Protection = DesignData.Track("protection");
    private static readonly int Mobility = DesignData.Track("mobility");
    private static readonly int Manufacturing = DesignData.Track("manufacturing");
    private static readonly int Logistics = DesignData.Track("logistics");

    private static readonly int Peltast = Chassis("peltast");

    /// <summary>
    /// A Peltast is Standardized II and Supplied II, with Mobility one ahead and out of slots.
    /// Mobility 3 ties all three at two ahead and at tier 3, and the fixed order of the tracks
    /// gives the slots to Mobility and Manufacturing: Light footprint goes.
    /// </summary>
    private static readonly int[] BeforeLightFootprintGoes = [1, 1, 2, 3, 3, 1];

    /// <summary>
    /// A Peltast is Overgunned II and Standardized II, with Protection one ahead and out of
    /// slots. Protection 3 ties all three, and the slots go to Firepower and Protection: Batch goes.
    /// </summary>
    private static readonly int[] BeforeBatchGoes = [3, 2, 1, 3, 1, 1];

    /// <summary>A Capitol, a Barracks and a research under way for player 0, who has more than the test spends.</summary>
    private static Match Researching(int[] tiers, string track, out EntityId capitol, out EntityId barracks, GameData? data = null)
    {
        var match = Create(TestMaps.Open(24, 24), data: data);
        match.Research.SetTiers(0, tiers);
        capitol = match.Place(0, "capitol", 2, 2);
        barracks = match.Place(0, "barracks", 10, 10);
        match.MakeRich(0);
        match.OrderResearch(0, track);
        match.Simulation.Tick();
        Assert.True(match.Research.State.Slot(0, 0).IsBusy);
        return match;
    }

    /// <summary>
    /// "The research panel names every accent a research would add or remove before the player
    /// pays, so a swap is a choice and never a surprise": what the preview says is what the
    /// research then does.
    /// </summary>
    [Fact]
    public void ThePreviewNamesTheSwapThatTheResearchThenMakes()
    {
        var match = Researching(BeforeLightFootprintGoes, "mobility", out _, out _);
        var preview = TechResolver.Preview(match.Data, BeforeLightFootprintGoes, Mobility)!;
        Assert.Equal([new Accent(Manufacturing, 2), new Accent(Logistics, 2)], match.BlueprintOf(0, Peltast).Accents);
        Assert.Contains(new AccentChange(Peltast, Logistics, 2, 0), preview.AccentChanges);
        Assert.Contains(new AccentChange(Peltast, Mobility, 0, 2), preview.AccentChanges);
        Assert.Equal((1, 2), (preview.Before.Blueprints[Peltast].Supply, preview.After.Blueprints[Peltast].Supply));

        match.RunUntilResearchHas(0, ticksLeft: 0);

        var after = match.BlueprintOf(0, Peltast);
        Assert.Equal([new Accent(Mobility, 2), new Accent(Manufacturing, 2)], after.Accents);
        Assert.Equal(preview.After.Blueprints[Peltast].Accents, after.Accents);
        Assert.Equal((2, 2), (after.Supply, after.BatchSize));

        // Only a chassis older than the player's epoch can have three tracks ahead of it.
        Assert.Equal(3, match.Research.Tech(0).Epoch);
        Assert.Equal(1, after.Chassis.Epoch);
    }

    /// <summary>
    /// "A unit that loses Light footprint costs its full supply again. Used supply may then
    /// exceed the cap. Nothing is lost; the player cannot order new units until used supply is
    /// back under the cap."
    /// </summary>
    [Fact]
    public void AUnitThatLosesLightFootprintCostsItsFullSupplyAgainAndNothingIsLost()
    {
        var match = Researching(BeforeLightFootprintGoes, "mobility", out var capitol, out var barracks);
        match.RunUntilResearchHas(0, ticksLeft: 2);
        var army = match.SpawnMany(0, Peltast, 80);
        match.OrderTrain(0, capitol, AWorker);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        // 80 Peltasts at 1 each, a worker ordered and a pair ordered: 83 of 85.
        var worker = new TrainOrder { Unit = AWorker, Count = 1, Materials = 50, Energy = 0, Supply = 1 };
        var pair = Assert.Single(match.OrdersAt(barracks));
        Assert.Equal((2, 2), (pair.Count, pair.Supply));
        Assert.Equal((85, 83), (match.Supply.Cap(0), match.Supply.Used(0)));
        var stock = match.Stock(0);
        var hitPoints = army.Select(unit => match.Health.Get(unit).HitPoints).ToArray();

        match.Simulation.Tick();

        // The research finished: every Peltast on the map uses 2, and the orders hold what they held.
        Assert.Equal(3, match.Research.State.Tier(0, Mobility));
        Assert.Equal((85, 163, 0), (match.Supply.Cap(0), match.Supply.Used(0), match.Supply.Free(0)));

        // Nothing is lost: no unit, no order, nothing from the stockpile.
        Assert.Equal(80, army.Count(match.World.Entities.IsAlive));
        Assert.Equal(hitPoints, army.Select(unit => match.Health.Get(unit).HitPoints));
        Assert.Equal([worker], match.OrdersAt(capitol));
        Assert.Equal([pair], match.OrdersAt(barracks));
        Assert.Equal(stock, match.Stock(0));
        Assert.DoesNotContain(match.Simulation.Events, simEvent => TrainEvent.IsProduction(simEvent.Kind));

        // No new unit can be ordered, a worker no more than a Peltast.
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, barracks, Peltast));
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, capitol, AWorker));
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, capitol, AWorker);
        match.Simulation.Tick();
        Assert.Equal([TrainCheck.NoSupply, TrainCheck.NoSupply], match.TrainEvents().Select(train => train.Reason));
        Assert.All(match.TrainEvents(), train => Assert.Equal(SimEventKind.TrainRefused, train.Kind));
        Assert.Equal(stock, match.Stock(0));

        // What was ordered is still trained, above the cap: the worker, and the pair, whose
        // Peltasts use 2 each where their order held 1 each.
        match.RunUntilAppeared();
        Assert.Equal(2, match.Appeared().Length);
        Assert.Equal(82, match.Combatants.Count);
        Assert.Equal(165, match.Supply.Used(0));
        match.RunUntilAppeared();
        Assert.Equal(1, match.Workers.Count);
        Assert.Equal(165, match.Supply.Used(0));
    }

    /// <summary>
    /// "Until used supply is back under the cap": orders are taken again as the army shrinks,
    /// each as soon as it fits under the cap of 85.
    /// </summary>
    [Fact]
    public void OrdersAreTakenAgainWhenUsedSupplyIsBackUnderTheCap()
    {
        var match = Researching(BeforeLightFootprintGoes, "mobility", out var capitol, out var barracks);
        match.RunUntilResearchHas(0, ticksLeft: 1);
        var army = match.SpawnMany(0, Peltast, 80);
        Assert.Equal((85, 80), (match.Supply.Cap(0), match.Supply.Used(0)));
        match.Simulation.Tick();
        Assert.Equal((85, 160), (match.Supply.Cap(0), match.Supply.Used(0)));

        // 43 Peltasts use 86: still over.
        match.Kill(army.Take(37));
        match.Simulation.Tick();
        Assert.Equal(86, match.Supply.Used(0));
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, capitol, AWorker));

        // 42 use 84: a worker fits, a Peltast does not.
        match.Kill(army.Skip(37).Take(1));
        match.Simulation.Tick();
        Assert.Equal(84, match.Supply.Used(0));
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, barracks, Peltast));
        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, capitol, AWorker));
        match.OrderTrain(0, capitol, AWorker);
        match.Simulation.Tick();

        // At the cap nothing more fits.
        Assert.Equal(85, match.Supply.Used(0));
        Assert.Equal(TrainCheck.NoSupply, match.Production.CheckTrain(0, capitol, AWorker));

        // 41 and the worker's order use 83: one Peltast fits, and the Batch it still has does not make it two.
        match.Kill(army.Skip(38).Take(1));
        match.Simulation.Tick();
        Assert.Equal(83, match.Supply.Used(0));
        Assert.Equal(2, match.BlueprintOf(0, Peltast).BatchSize);
        Assert.Equal(1, match.Production.CountOf(0, Peltast));
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        Assert.Equal((1, 2), (match.OrdersAt(barracks)[0].Count, match.OrdersAt(barracks)[0].Supply));
        Assert.Equal(85, match.Supply.Used(0));
    }

    /// <summary>An order can be cancelled above the cap, and gives back its price and the supply it held.</summary>
    [Fact]
    public void AnOrderCanBeCancelledAboveTheCap()
    {
        var match = Researching(BeforeLightFootprintGoes, "mobility", out _, out var barracks);
        match.RunUntilResearchHas(0, ticksLeft: 2);
        match.SpawnMany(0, Peltast, 80);
        var stock = match.Stock(0);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(2);
        var price = match.OrdersAt(barracks)[0].Materials;
        Assert.Equal((stock.Materials - price, 162), (match.Stock(0).Materials, match.Supply.Used(0)));

        match.OrderCancel(0, barracks, 0);
        match.Simulation.Tick();

        Assert.Empty(match.OrdersAt(barracks));
        Assert.Equal(stock, match.Stock(0));
        Assert.Equal(160, match.Supply.Used(0));
    }

    /// <summary>
    /// A pair already in a queue when its chassis loses Batch (design 2.3: an order's "price,
    /// supply and single-or-pair are all fixed when it is placed", so "a queue is never charged
    /// a second time"): it stays a pair, costs nothing more and gives back what was paid.
    /// </summary>
    [Fact]
    public void APairAlreadyInAQueueStaysAPairWhenItsChassisLosesBatch()
    {
        var match = Researching(BeforeBatchGoes, "protection", out _, out var barracks);
        var preview = TechResolver.Preview(match.Data, BeforeBatchGoes, Protection)!;
        Assert.Equal([new Accent(Firepower, 2), new Accent(Manufacturing, 2)], match.BlueprintOf(0, Peltast).Accents);
        Assert.Contains(new AccentChange(Peltast, Manufacturing, 2, 0), preview.AccentChanges);
        Assert.Contains(new AccentChange(Peltast, Protection, 0, 2), preview.AccentChanges);

        match.RunUntilResearchHas(0, ticksLeft: 101);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();

        // Overgunned II and Standardized II on a Peltast: 49 Materials, 2 supply, 154 ticks, a pair in 230.
        var pair = new TrainOrder { Unit = Peltast, Count = 2, Materials = 98, Energy = 0, Supply = 4 };
        Assert.Equal([pair, pair, pair], match.OrdersAt(barracks));
        Assert.Equal(230, match.ProducerOf(barracks).BuildTicks);
        var stock = match.Stock(0);

        match.RunUntilResearchHas(0, ticksLeft: 0);

        // Batch is gone, and a Peltast with Hardened II costs 57.
        var after = match.BlueprintOf(0, Peltast);
        Assert.Equal([new Accent(Firepower, 2), new Accent(Protection, 2)], after.Accents);
        Assert.Equal((1, 57, 154), (after.BatchSize, after.Materials, after.BuildTicks));
        Assert.Equal(1, match.Production.CountOf(0, Peltast));

        // The queue is as it was, and nothing was paid or given back.
        Assert.Equal([pair, pair, pair], match.OrdersAt(barracks));
        Assert.Equal((230, 101), (match.ProducerOf(barracks).BuildTicks, match.ProducerOf(barracks).Progress));
        Assert.Equal(stock, match.Stock(0));
        Assert.Equal(12, match.Supply.Used(0));
        Assert.DoesNotContain(match.Simulation.Events, simEvent => TrainEvent.IsProduction(simEvent.Kind));

        // Cancelling gives back the price paid for two, not today's price of one or of two.
        match.OrderCancel(0, barracks, 2);
        match.Simulation.Tick();
        Assert.Equal((stock.Materials + 98, stock.Energy), match.Stock(0));
        Assert.Equal(8, match.Supply.Used(0));

        // An order placed now is one unit at today's price.
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        Assert.Equal(
            [pair, pair, new TrainOrder { Unit = Peltast, Count = 1, Materials = 57, Energy = 0, Supply = 2 }],
            match.OrdersAt(barracks));

        // The pair in production keeps the time it began with and comes out as two, with the
        // stats and accents of the moment it appears.
        Assert.Equal(230 - 103, match.RunUntilAppeared());
        Assert.Equal(2, match.Appeared().Length);
        Assert.All(match.Appeared(), unit => Assert.Equal(after.HitPoints, match.Health.Get(unit).HitPoints));

        // The pair that waited begins after the swap, with no pair's time to take: two units, one after the other.
        match.Simulation.Tick();
        Assert.Equal(308, match.ProducerOf(barracks).BuildTicks);
        Assert.Equal(307, match.RunUntilAppeared());
        Assert.Equal(2, match.Appeared().Length);

        Assert.Equal(154, match.RunUntilAppeared());
        Assert.Single(match.Appeared());
        Assert.Equal(5, match.Combatants.Count);
        Assert.Equal(10, match.Supply.Used(0));
    }

    /// <summary>The data of these matches with a tier 3 that is researched in 5 seconds, so that the snapshot test is short.</summary>
    private static readonly Lazy<GameData> QuickResearch =
        new(() => DataAssert.Valid(Files().Edit(DesignData.ResearchPath, "\"seconds\": 60", "\"seconds\": 5")));

    public static TheoryData<int> SnapshotTicks => [0, 99, 100, 150];

    /// <summary>
    /// A match whose used supply is above the cap is saved and restored like any other: nothing
    /// in a snapshot holds the supply, and nothing refuses a state that is over the cap.
    /// </summary>
    [Theory]
    [MemberData(nameof(SnapshotTicks))]
    public void AMatchAboveTheCapRestoresAndContinues(int snapshotTick)
    {
        var capitol = new EntityId(1);
        var barracks = new EntityId(2);
        (int Tick, Command Command)[] script =
        [
            (0, new Command(CommandType.StartResearch, 0, contentId: Mobility)),
            (60, TrainCommand(0, barracks, Peltast)),
            (60, TrainCommand(0, barracks, Peltast)),
            (90, TrainCommand(0, capitol, AWorker)),
            (105, TrainCommand(0, barracks, Peltast)),
            (110, CancelCommand(0, barracks, 1)),
        ];

        SnapshotAssert.RestoresAndContinues(Build, script, snapshotTick, lastTick: 340);

        static Match Build()
        {
            var match = Create(TestMaps.Open(24, 24), data: QuickResearch.Value);
            match.Research.SetTiers(0, BeforeLightFootprintGoes);
            Assert.Equal(new EntityId(1), match.Place(0, "capitol", 2, 2));
            Assert.Equal(new EntityId(2), match.Place(0, "barracks", 10, 10));
            match.MakeRich(0);
            match.SpawnMany(0, Peltast, 80);
            match.RefreshDerivedState();
            return match;
        }
    }

    /// <summary>The script of the snapshot test does take the match above the cap, with orders in both queues.</summary>
    [Fact]
    public void TheScriptOfTheSnapshotTestGoesAboveTheCap()
    {
        var match = Researching(BeforeLightFootprintGoes, "mobility", out var capitol, out var barracks, QuickResearch.Value);
        match.SpawnMany(0, Peltast, 80);
        match.Simulation.Run(59);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(30);
        match.OrderTrain(0, capitol, AWorker);
        match.Simulation.Run(9);
        Assert.Equal((99, 2), (match.World.Tick, match.Research.State.Tier(0, Mobility)));
        Assert.Equal(85, match.Supply.Used(0));

        match.Simulation.Tick();

        Assert.Equal(3, match.Research.State.Tier(0, Mobility));
        Assert.Equal((85, 165), (match.Supply.Cap(0), match.Supply.Used(0)));
        Assert.Equal((2, 1), (match.OrdersAt(barracks).Length, match.OrdersAt(capitol).Length));

        // The order of tick 105 is refused, and the cancel of tick 110 is carried out.
        match.Simulation.Run(5);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        Assert.Equal(TrainCheck.NoSupply, Assert.Single(match.TrainEvents()).Reason);
        match.Simulation.Run(4);
        match.OrderCancel(0, barracks, 1);
        match.Simulation.Tick();
        Assert.Equal(SimEventKind.TrainCancelled, Assert.Single(match.TrainEvents()).Kind);
        Assert.Equal(163, match.Supply.Used(0));
    }
}
