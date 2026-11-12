using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Tests.Serialization;

namespace Sim.Tests.Core;

/// <summary>
/// The event list: what a tick reports to whatever shows the match. It is output, so the tests
/// here are of two kinds: the list holds what was added in the last tick and nothing else, and a
/// match runs to the same state whether or not anything is reported.
/// </summary>
public class SimEventTests
{
    // No part of the game has a kind yet. The numbers of kinds are recorded nowhere, so any will do.
    private const SimEventKind Rang = (SimEventKind)1;
    private const SimEventKind Knocked = (SimEventKind)2;
    private const SimEventKind Moved = (SimEventKind)3;

    private const CommandType Knock = (CommandType)1;
    private const CommandType Unheard = (CommandType)99;

    /// <summary>Reports once in every tick it runs in, and once for every command it is given.</summary>
    private sealed class Bell(SimEventList events, int number, Func<int, bool>? rings = null) : ISystem, ICommandHandler
    {
        public void Run(World world)
        {
            if (rings is null || rings(world.Tick))
            {
                events.Add(Rang, amount: number);
            }
        }

        public bool Execute(World world, in Command command)
        {
            events.Add(Knocked, command.Player, amount: command.ContentId);
            return command.ContentId >= 0;
        }
    }

    private static Simulation WithBells(Func<int, bool>? rings = null)
    {
        var simulation = new Simulation(new World(0));
        simulation.SetCommandHandler(Knock, new Bell(simulation.Events, 0));
        simulation.AddSystem(new Bell(simulation.Events, 1, rings));
        simulation.AddSystem(new Bell(simulation.Events, 2, rings));
        return simulation;
    }

    [Fact]
    public void TheListIsEmptyBeforeTheFirstTick()
    {
        var simulation = WithBells();

        Assert.Empty(simulation.Events);
    }

    [Fact]
    public void TheEventsOfATickAreThereAfterItAndGoneAfterTheNext()
    {
        var simulation = WithBells(rings: tick => tick == 0);

        simulation.Tick();
        Assert.Equal(
            [new SimEvent(Rang, 0, SimEvent.NoPlayer, EntityId.None, EntityId.None, FixedVector2.Zero, 1),
             new SimEvent(Rang, 0, SimEvent.NoPlayer, EntityId.None, EntityId.None, FixedVector2.Zero, 2)],
            simulation.Events);

        simulation.Tick();
        Assert.Empty(simulation.Events);
    }

    [Fact]
    public void TheListHoldsTheEventsOfTheLastTickOnly()
    {
        var simulation = WithBells();

        for (var tick = 0; tick < 5; tick++)
        {
            simulation.Tick();

            Assert.Equal(2, simulation.Events.Count);
            Assert.All(simulation.Events, e => Assert.Equal(tick, e.Tick));
        }
    }

    [Fact]
    public void EventsComeInTheOrderTheyWereAddedCommandsFirstThenEachSystem()
    {
        var simulation = WithBells();
        simulation.Commands.Enqueue(1, new Command(Knock, 1, contentId: 30));
        simulation.Commands.Enqueue(1, new Command(Knock, 0, contentId: 10));
        simulation.Tick();

        simulation.Tick();

        Assert.Equal(
            [(Knocked, 1, 30L), (Knocked, 0, 10L), (Rang, SimEvent.NoPlayer, 1L), (Rang, SimEvent.NoPlayer, 2L)],
            simulation.Events.Select(e => (e.Kind, e.Player, e.Amount)));
        Assert.Equal(4, simulation.Events.Count);
        Assert.Equal(simulation.Events.ToArray()[3], simulation.Events[3]);
    }

    /// <summary>A handler that finds its command invalid changes no state, but it may say why.</summary>
    [Fact]
    public void AHandlerThatRefusesItsCommandCanStillReport()
    {
        var simulation = WithBells(rings: _ => false);
        simulation.Commands.Enqueue(0, new Command(Knock, 1, contentId: -7));

        simulation.Tick();

        Assert.Equal(1, simulation.DroppedCommandCount);
        Assert.Equal([(Knocked, 1, -7L)], simulation.Events.Select(e => (e.Kind, e.Player, e.Amount)));
    }

    [Fact]
    public void AnEventCarriesItsKindTheTickAPlayerTwoEntitiesAPointAndANumber()
    {
        var simulation = new Simulation(new World(0));
        simulation.Run(7);
        var point = new FixedVector2(Fixed.FromInt(12) + Fixed.Half, Fixed.FromInt(-3));

        simulation.Events.Add(Moved, player: 2, entity: new EntityId(40), other: new EntityId(41), position: point, amount: long.MinValue);
        simulation.Events.Add(Rang);

        Assert.Equal(new SimEvent(Moved, 7, 2, new EntityId(40), new EntityId(41), point, long.MinValue), simulation.Events[0]);
        Assert.Equal(new SimEvent(Rang, 7, SimEvent.NoPlayer, EntityId.None, EntityId.None, FixedVector2.Zero, 0), simulation.Events[1]);
    }

    [Fact]
    public void AnEventWithoutAKindIsNotTaken()
    {
        var simulation = new Simulation(new World(0));

        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Events.Add(SimEventKind.None));
        Assert.Empty(simulation.Events);
    }

    /// <summary>
    /// The events of a tick are read after it, when the entities destroyed in it have left the
    /// stores. The event is still there, with the place it was given.
    /// </summary>
    [Fact]
    public void AnEventOutlivesTheEntityItIsAboutUntilTheNextTick()
    {
        var match = new ToyMatch(DeterminismTests.Seed);
        var events = match.Simulation.Events;
        match.Simulation.AddSystem(new Reporter(match, events));

        var reportedAfterRemoval = 0;
        for (var tick = 0; tick < 300; tick++)
        {
            match.Simulation.Tick();
            foreach (var e in events)
            {
                if (!match.Movers.Contains(e.Entity))
                {
                    reportedAfterRemoval++;
                    Assert.False(match.World.Entities.IsAlive(e.Entity));
                    Assert.NotEqual(FixedVector2.Zero, e.Position);
                }
            }
        }

        Assert.True(reportedAfterRemoval > 20, "The match destroyed entities in ticks that reported them.");
    }

    /// <summary>
    /// The list is output. A match with a system and a handler that report in every tick has, tick
    /// for tick, the state hash of the same match without them, and ends on the recorded hash.
    /// </summary>
    [Fact]
    public void TheStateHashIsTheSameWithAndWithoutEvents()
    {
        var script = ToyMatch.Script(DeterminismTests.Ticks);
        var silent = new ToyMatch(DeterminismTests.Seed);
        var reporting = Reporting();

        var eventCount = 0;
        foreach (var (tick, command) in script)
        {
            silent.Simulation.Commands.Enqueue(tick, command);
            reporting.Simulation.Commands.Enqueue(tick, command);
        }

        for (var tick = 0; tick < DeterminismTests.Ticks; tick++)
        {
            silent.Simulation.Tick();
            reporting.Simulation.Tick();
            eventCount += reporting.Simulation.Events.Count;

            Assert.Empty(silent.Simulation.Events);
            Assert.Equal(silent.World.ComputeHash(), reporting.World.ComputeHash());
        }

        Assert.Equal(DeterminismTests.ExpectedFinalHash, reporting.World.ComputeHash());
        Assert.Equal(silent.Simulation.DroppedCommandCount, reporting.Simulation.DroppedCommandCount);
        Assert.True(eventCount > 10_000, "The reporting match reported throughout.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(600)]
    public void ASnapshotIsTheSameWithAndWithoutEvents(int snapshotTick)
    {
        var script = ToyMatch.Script(DeterminismTests.Ticks);
        var silent = new ToyMatch(DeterminismTests.Seed);
        var reporting = Reporting();
        silent.Play(snapshotTick, script);
        reporting.Play(snapshotTick, script);

        Assert.Equal(snapshotTick == 0, reporting.Simulation.Events.Count == 0);
        Assert.Equal(SnapshotOf(silent.World), SnapshotOf(reporting.World));
    }

    /// <summary>
    /// A snapshot holds no events, so a restored simulation has none until its next tick: the
    /// ones it held were of a tick that the restored state did not come from.
    /// </summary>
    [Fact]
    public void RestoringASnapshotEmptiesTheList()
    {
        var script = ToyMatch.Script(DeterminismTests.Ticks);
        var source = Reporting();
        source.Play(100, script);
        var snapshot = SnapshotOf(source.World);
        var eventsAtTheSnapshot = source.Simulation.Events.ToArray();

        var target = Reporting();
        target.Play(40, script);
        Assert.NotEmpty(target.Simulation.Events);

        Snapshot.Restore(target.Simulation, new ByteReader(snapshot));
        Assert.Empty(target.Simulation.Events);

        // From there both report the same.
        foreach (var (tick, command) in script.Where(entry => entry.Tick >= 100))
        {
            target.Simulation.Commands.Enqueue(tick, command);
        }

        source.Simulation.Tick();
        target.Simulation.Tick();
        Assert.NotEmpty(target.Simulation.Events);
        Assert.Equal(source.Simulation.Events, target.Simulation.Events);
        Assert.NotEqual(eventsAtTheSnapshot, target.Simulation.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(57)]
    [InlineData(300)]
    public void AMatchThatReportsRestoresAndContinues(int snapshotTick)
    {
        SnapshotAssert.RestoresAndContinues(
            () => Reporting().Simulation, ToyMatch.Script(DeterminismTests.Ticks), snapshotTick, DeterminismTests.Ticks);
    }

    /// <summary>
    /// The toy match with a system that reports every mover in every tick, and a handler that
    /// reports the command the toy match has no handler for and drops it, as before.
    /// </summary>
    private static ToyMatch Reporting()
    {
        var match = new ToyMatch(DeterminismTests.Seed);
        var reporter = new Reporter(match, match.Simulation.Events);
        match.Simulation.SetCommandHandler(Unheard, reporter);
        match.Simulation.AddSystem(reporter);
        return match;
    }

    private static byte[] SnapshotOf(World world)
    {
        var writer = new ByteWriter();
        Snapshot.Write(world, writer);
        return writer.ToArray();
    }

    private sealed class Reporter(ToyMatch match, SimEventList events) : ISystem, ICommandHandler
    {
        public void Run(World world)
        {
            var movers = match.Movers;
            for (var i = 0; i < movers.Count; i++)
            {
                var mover = movers.ValueAt(i);
                events.Add(Moved, mover.Owner, movers.EntityAt(i), position: new FixedVector2(mover.X, mover.Y), amount: mover.Age);
            }
        }

        public bool Execute(World world, in Command command)
        {
            events.Add(Knocked, command.Player);
            return false;
        }
    }
}
