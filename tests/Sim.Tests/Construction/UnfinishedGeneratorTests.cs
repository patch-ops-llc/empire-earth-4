using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Tests.Structures;
using static Sim.Tests.Construction.ConstructionMatches;

namespace Sim.Tests.Construction;

/// <summary>
/// Design 4.1, "Standing without earning": "A worker may be sent to a deposit that is outside its
/// owner's territory, or to a Generator that is not finished. It stands at its place and earns
/// nothing. At each payment moment the conditions are tested, and a payment whose conditions fail
/// is skipped; the count of ticks runs on."
/// </summary>
/// <remarks>
/// The Generator of these tests is laid on the tiles (10, 10) to (11, 11) by a worker that stands
/// beside it, in the first tick, and builds itself in 25 seconds, 500 ticks.
/// </remarks>
public class UnfinishedGeneratorTests
{
    private const int BuildTicks = 500;

    private static Match WithAFoundation(out EntityId generator, out EntityId builder)
    {
        var match = WithACapitol();
        builder = match.WorkerAt(0, 10, 9);
        match.OrderBuild(0, "generator", 10, 10, builder);
        match.Simulation.Tick();
        generator = match.StructureAt(10, 10);
        Assert.False(match.Construction.IsFinished(generator));
        return match;
    }

    [Fact]
    public void AWorkerSentToAnUnfinishedGeneratorStandsAtItsPlaceAndEarnsNothing()
    {
        var match = WithAFoundation(out var generator, out _);
        var worker = match.WorkerAt(0, 4, 10);
        var energy = match.Stockpiles.Energy(0);

        match.OrderGather(0, generator, worker);
        match.Simulation.Tick();

        // The order is taken: the worker holds the place nearest to it and walks there.
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal((WorkerState.Walking, generator), (match.StateOf(worker), match.WorkerOf(worker).Site));
        Assert.Equal((9, 10), EconomyMatches.TileOf(match, worker));

        match.RunUntilWorking(worker);
        Assert.Equal((9, 10), match.StandsOn(worker));

        // It stands there for as long as the Generator builds itself, and no payment is made.
        while (!match.Construction.IsFinished(generator))
        {
            Assert.True(match.World.Tick <= BuildTicks + 1, "The Generator is still unfinished.");
            Assert.Equal(energy, match.Stockpiles.Energy(0));
            Assert.Equal((WorkerState.Working, (9, 10)), (match.StateOf(worker), match.StandsOn(worker)));
            match.Simulation.Tick();
        }
    }

    [Fact]
    public void TheCountOfTicksRunsOnSoTheFirstPaymentComesAtTheWorkersOwnMomentAfterCompletion()
    {
        var match = WithAFoundation(out var generator, out _);
        var worker = match.WorkerAt(0, 4, 10);
        var energy = match.Stockpiles.Energy(0);
        match.OrderGather(0, generator, worker);
        var arrived = match.RunUntilWorking(worker);

        var finished = -1;
        var paid = -1;
        while (paid < 0)
        {
            Assert.True(match.World.Tick < BuildTicks + (2 * SimInfo.TicksPerSecond), "No payment came.");
            match.Simulation.Tick();
            var tick = match.World.Tick - 1;
            if (finished < 0 && match.Construction.IsFinished(generator))
            {
                finished = tick;
            }

            if (match.Stockpiles.Energy(0) != energy)
            {
                paid = tick;
            }
        }

        // The worker's moments are every second from its arrival. The Generator is finished
        // between two of them, and the payment comes at the next one, not a second after completion.
        Assert.NotEqual(0, (finished - arrived) % SimInfo.TicksPerSecond);
        Assert.Equal(0, (paid - arrived) % SimInfo.TicksPerSecond);
        Assert.InRange(paid - finished, 1, SimInfo.TicksPerSecond - 1);
        Assert.Equal(energy + EconomyMatches.EnergyPayment, match.Stockpiles.Energy(0));

        // And one more each second from then on.
        match.Simulation.Run(3 * SimInfo.TicksPerSecond);
        Assert.Equal(energy + (4 * EconomyMatches.EnergyPayment), match.Stockpiles.Energy(0));
    }

    [Fact]
    public void AnUnfinishedGeneratorHasTheFourPlacesOfAFinishedOne()
    {
        var match = WithAFoundation(out var generator, out _);
        EntityId[] workers = [.. Enumerable.Range(0, 5).Select(index => match.WorkerAt(0, 4 + index, 14))];

        match.OrderGather(0, generator, workers);
        match.Simulation.Run(200);

        Assert.Equal(4, match.Data.Resources.Energy.WorkersPerGenerator);
        Assert.Equal(4, match.Work.HoldersOf(match.World, generator));
        Assert.Equal(4, workers.Count(worker => match.StateOf(worker) == WorkerState.Working));
        Assert.Equal(WorkerState.Waiting, match.StateOf(workers[4]));

        // When it is finished the four are paid, each at its own moment, and the fifth still waits.
        var energy = match.Stockpiles.Energy(0);
        match.RunThrough(BuildTicks + SimInfo.TicksPerSecond);
        Assert.True(match.Construction.IsFinished(generator));
        Assert.Equal(energy + (4 * EconomyMatches.EnergyPayment), match.Stockpiles.Energy(0));
        Assert.Equal(WorkerState.Waiting, match.StateOf(workers[4]));
    }

    [Fact]
    public void TheWorkerThatLaidItCanBeSentToItAtOnce()
    {
        var match = WithAFoundation(out var generator, out var builder);
        var energy = match.Stockpiles.Energy(0);

        match.OrderGather(0, generator, builder);
        match.RunUntilWorking(builder);
        Assert.Equal(energy, match.Stockpiles.Energy(0));

        match.RunThrough(BuildTicks + SimInfo.TicksPerSecond);
        Assert.Equal(energy + EconomyMatches.EnergyPayment, match.Stockpiles.Energy(0));
    }

    [Fact]
    public void OnlyItsOwnerMaySendWorkersToIt()
    {
        var match = WithAFoundation(out var generator, out _);
        var theirs = match.WorkerAt(1, 14, 10);

        match.OrderGather(1, generator, theirs);
        match.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(WorkerState.Idle, match.StateOf(theirs));
    }

    /// <summary>Design 8.4, row 27: a foundation is cancelled with a part of its price back. Its workers have nowhere to work then.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenTheFoundationIsCancelledOrDestroyedItsWorkersAreLeftWithoutWork(bool cancelled)
    {
        var match = WithAFoundation(out var generator, out _);
        var worker = match.WorkerAt(0, 8, 10);
        match.OrderGather(0, generator, worker);
        match.RunUntilWorking(worker);

        if (cancelled)
        {
            match.OrderCancelBuild(0, generator);
        }
        else
        {
            match.Raze(generator);
        }

        match.Simulation.Run(2);

        Assert.Equal(WorkerState.Idle, match.StateOf(worker));
        Assert.Equal((0, 0), (match.WorkSites.Count, match.Foundations.Count));
        Assert.Equal((9, 10), match.StandsOn(worker));
        var energy = match.Stockpiles.Energy(0);
        match.Simulation.Run(3 * SimInfo.TicksPerSecond);
        Assert.Equal(energy, match.Stockpiles.Energy(0));
    }

    /// <summary>The worker's place and its count of ticks are state, at an unfinished Generator as at any site.</summary>
    [Fact]
    public void AMatchWithAWorkerAtAnUnfinishedGeneratorRestoresAndContinues()
    {
        static Match Start()
        {
            var match = Create(TestMaps.Open(40, 24));
            match.PlaceStructure(0, "capitol", 6, 10);
            match.Stockpiles.Add(0, 1000, 1000);
            match.WorkerAt(0, 10, 9);
            match.WorkerAt(0, 4, 10);
            match.RefreshDerivedState();
            return match;
        }

        // The Capitol is the first thing in the match, the workers the second and third, and the Generator the fourth.
        var (builder, worker, generator) = (new EntityId(2), new EntityId(3), new EntityId(4));
        var probe = Start();
        (int Tick, Command Command)[] script =
        [
            (0, probe.Build(0, "generator", 10, 10, builder)),
            (1, EconomyMatches.Gather(0, generator, worker)),
        ];
        foreach (var (tick, command) in script)
        {
            probe.Simulation.Commands.Enqueue(tick, command);
        }

        probe.Simulation.Run(BuildTicks + (3 * SimInfo.TicksPerSecond));
        Assert.Equal(0, probe.Simulation.DroppedCommandCount);
        Assert.Equal((WorkerState.Working, generator), (probe.StateOf(worker), probe.WorkerOf(worker).Site));
        Assert.True(probe.Stockpiles.Energy(0) > 900_000);

        foreach (var snapshotTick in new[] { 1, 2, 60, 300, BuildTicks, BuildTicks + 1, BuildTicks + 15 })
        {
            SnapshotAssert.RestoresAndContinues(Start, script, snapshotTick, lastTick: BuildTicks + (3 * SimInfo.TicksPerSecond));
        }
    }
}
