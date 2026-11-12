using Sim.Core;
using Sim.Map;
using Sim.Production;
using Sim.Setup;
using Sim.Tests.Construction;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// Design 2.5: a structure gives what it gives once it is finished. A structure that a worker
/// builds has its queue from the tick it is finished and not before, and a structure that is
/// gone has none.
/// </summary>
public class BuiltStructureTests
{
    private const int BarracksTicks = 35 * SimInfo.TicksPerSecond;

    private static readonly int Peltast = Chassis("peltast");

    /// <summary>
    /// Player 0 with a Capitol, 1,000 of each resource and a worker that has just laid the
    /// foundation of a Barracks.
    /// </summary>
    private static Match WithAFoundation(out EntityId barracks)
    {
        var match = Create(TestMaps.Open(40, 24));
        match.Place(0, "capitol", 6, 10);
        match.Stockpiles.Add(0, 1000, 1000);
        var worker = match.WorkerAt(0, 8, 11);
        match.OrderBuild(0, "barracks", 8, 12, worker);
        match.Simulation.Tick();
        barracks = match.StructureAt(8, 12);
        Assert.True(match.Foundations.Contains(barracks));
        return match;
    }

    [Fact]
    public void AStructureUnderConstructionTakesNoOrder()
    {
        var match = WithAFoundation(out var barracks);
        var stock = match.Stock(0);

        match.OrderTrain(0, barracks, Peltast);
        match.OrderRally(0, MapGrid.TileCenter(20, 20), barracks);
        match.OrderCancel(0, barracks, 0);
        match.Simulation.Tick();

        Assert.Equal(TrainCheck.NoProducer, match.Production.CheckTrain(0, barracks, Peltast));
        Assert.False(match.Production.Producers.Contains(barracks));
        var refused = Assert.Single(match.TrainEvents());
        Assert.Equal((SimEventKind.TrainRefused, TrainCheck.NoProducer, barracks), (refused.Kind, refused.Reason, refused.Structure));
        Assert.Equal(2, match.Simulation.Events.Count(simEvent => simEvent.Kind == SimEventKind.OrderRefused));
        Assert.Equal(stock, match.Stock(0));
        Assert.Equal(1, match.Supply.Used(0));
    }

    [Fact]
    public void AStructureAWorkerBuiltTrainsFromTheTickItIsFinished()
    {
        var match = WithAFoundation(out var barracks);

        // The foundation was laid in the first tick, so the last tick of its build time is this one.
        match.Simulation.Run(BarracksTicks - 1);
        Assert.False(match.Construction.IsFinished(barracks));
        match.Simulation.Tick();
        Assert.True(match.Construction.IsFinished(barracks));
        Assert.Equal(0, match.ProducerOf(barracks).Count);

        match.OrderTrain(0, barracks, Peltast);
        var ticks = match.RunUntilAppeared();

        Assert.Equal(240, ticks);
        var peltast = Assert.Single(match.Appeared());
        Assert.Equal(Peltast, match.Combatants.Get(peltast).Chassis);
        Assert.Equal((1000 - 150 - 40, 1000), match.Stock(0));
        Assert.Equal(3, match.Supply.Used(0));
    }

    /// <summary>A foundation that falls has no queue to give back, and falls like any structure.</summary>
    [Fact]
    public void AFoundationThatIsDestroyedGivesNothingBackFromAQueue()
    {
        var match = WithAFoundation(out var barracks);
        var stock = match.Stock(0);

        match.Destroy(barracks);
        match.Simulation.Tick();

        Assert.Empty(match.TrainEvents());
        Assert.Equal(stock, match.Stock(0));
    }

    /// <summary>
    /// A structure that was destroyed is no structure that trains, even in the same tick, while
    /// it is still among the structures.
    /// </summary>
    [Fact]
    public void ADestroyedStructureTakesNoOrderEvenBeforeItIsTakenOffTheMap()
    {
        var match = Create(TestMaps.Open(24, 24));
        var barracks = match.Place(0, "barracks", 10, 10);
        match.MakeRich(0);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Tick();
        Assert.Equal(2, match.Production.SupplyUsedBy(0));
        Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(0, barracks, Peltast));

        match.World.Entities.Destroy(barracks);

        Assert.True(match.Production.Producers.Contains(barracks));
        Assert.Equal(0, match.Production.SupplyUsedBy(0));
        Assert.Equal(TrainCheck.NoProducer, match.Production.CheckTrain(0, barracks, Peltast));
        Assert.Equal(TrainCheck.NoProducer, match.Production.Train(0, barracks, Peltast));
        Assert.False(match.Production.SetRallyPoint(0, barracks, MapGrid.TileCenter(2, 2)));
        Assert.False(match.Production.Cancel(0, barracks, 0));
    }

    /// <summary>
    /// A structure that is gone when production runs trains nothing more, even an order that
    /// was due in that tick, and its orders are given back in the same tick.
    /// </summary>
    [Fact]
    public void AStructureThatIsGoneWhenItsOrderIsDueTrainsNothingAndRefundsIt()
    {
        var match = Create(TestMaps.Open(24, 24));
        var barracks = match.Place(0, "barracks", 10, 10);
        match.Stockpiles.Add(0, 40, 0);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(239);
        Assert.Equal(239, match.ProducerOf(barracks).Progress);

        match.World.Entities.Destroy(barracks);
        match.Simulation.Tick();

        var refunded = Assert.Single(match.TrainEvents());
        Assert.Equal((SimEventKind.TrainRefunded, Peltast), (refunded.Kind, refunded.Unit));
        Assert.Equal(0, match.Combatants.Count);
        Assert.Equal((40, 0), match.Stock(0));
        Assert.Equal(0, match.Supply.Used(0));
    }
}
