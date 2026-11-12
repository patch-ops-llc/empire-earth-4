using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Core;

public class SimulationTests
{
    private const CommandType Note = (CommandType)1;
    private const CommandType Refuse = (CommandType)2;
    private const CommandType Unhandled = (CommandType)3;

    /// <summary>Writes what happened, and at which tick, to a shared log.</summary>
    private sealed class Recorder(List<string> log, string name, Action<World>? action = null) : ISystem, ICommandHandler
    {
        public void Run(World world)
        {
            log.Add($"{world.Tick}:{name}");
            action?.Invoke(world);
        }

        public bool Execute(World world, in Command command)
        {
            log.Add($"{world.Tick}:{name}({command.ContentId})");
            return name != "refuse";
        }
    }

    private static (Simulation Simulation, List<string> Log) Create()
    {
        var log = new List<string>();
        var simulation = new Simulation(new World(0));
        simulation.SetCommandHandler(Note, new Recorder(log, "note"));
        simulation.SetCommandHandler(Refuse, new Recorder(log, "refuse"));
        simulation.AddSystem(new Recorder(log, "first"));
        simulation.AddSystem(new Recorder(log, "second"));
        return (simulation, log);
    }

    [Fact]
    public void ATickRunsCommandsThenSystemsInOrderThenAdvances()
    {
        var (simulation, log) = Create();
        simulation.Commands.Enqueue(1, new Command(Note, 0, contentId: 3));
        simulation.Commands.Enqueue(0, new Command(Note, 0, contentId: 1));
        simulation.Commands.Enqueue(0, new Command(Note, 0, contentId: 2));

        simulation.Tick();
        Assert.Equal(1, simulation.World.Tick);
        simulation.Tick();
        Assert.Equal(2, simulation.World.Tick);

        Assert.Equal(
            ["0:note(1)", "0:note(2)", "0:first", "0:second", "1:note(3)", "1:first", "1:second"],
            log);
        Assert.Equal(0, simulation.DroppedCommandCount);
    }

    [Fact]
    public void InvalidAndUnknownCommandsAreDroppedAndTheTickGoesOn()
    {
        var (simulation, log) = Create();
        simulation.Commands.Enqueue(0, new Command(Refuse, 0, contentId: 1));
        simulation.Commands.Enqueue(0, new Command(Unhandled, 0, contentId: 2));
        simulation.Commands.Enqueue(0, new Command(CommandType.None, 0, contentId: 3));
        simulation.Commands.Enqueue(0, new Command((CommandType)(-5), 0, contentId: 4));
        simulation.Commands.Enqueue(0, new Command((CommandType)1_000_000, 0, contentId: 5));
        simulation.Commands.Enqueue(0, new Command(Note, 0, contentId: 6));

        simulation.Tick();

        Assert.Equal(["0:refuse(1)", "0:note(6)", "0:first", "0:second"], log);
        Assert.Equal(5, simulation.DroppedCommandCount);
        Assert.Equal(1, simulation.World.Tick);
    }

    [Fact]
    public void DestroyedEntitiesAreRemovedAtTheEndOfTheTick()
    {
        var log = new List<string>();
        var world = new World(0);
        var simulation = new Simulation(world);
        var entity = world.Entities.Create();
        simulation.AddSystem(new Recorder(log, "destroy", w => w.Entities.Destroy(entity)));
        simulation.AddSystem(new Recorder(log, "observe", w => log.Add($"alive={w.Entities.IsAlive(entity)} listed={w.Entities.Count}")));

        simulation.Tick();

        Assert.Equal(["0:destroy", "0:observe", "alive=False listed=1"], log);
        Assert.Equal(0, world.Entities.Count);
    }

    [Fact]
    public void ACommandHandlerSeesEntitiesCreatedByEarlierCommandsOfTheSameTick()
    {
        var match = new ToyMatch(seed: 1);
        match.Simulation.Commands.Enqueue(0, new Command(ToyMatch.Spawn, 1, targetX: Fixed.FromInt(5), targetY: Fixed.FromInt(5)));
        match.Simulation.Commands.Enqueue(0, new Command(ToyMatch.Turn, 1, [new EntityId(1)], contentId: 100));
        match.Simulation.Commands.Enqueue(0, new Command(ToyMatch.Turn, 0, [new EntityId(1)], contentId: 100));

        match.Simulation.Tick();

        // The second command turned the mover the first one placed. The third came from the other player.
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(1, match.Movers.Get(new EntityId(1)).Owner);
    }

    /// <summary>Lets through the commands of every player but one.</summary>
    private sealed class Barring(int player) : ICommandGate
    {
        public bool Admits(World world, in Command command) => command.Player != player;
    }

    [Fact]
    public void TheGateDropsACommandBeforeItsHandlerSeesIt()
    {
        var (simulation, log) = Create();
        simulation.SetCommandGate(new Barring(1));
        simulation.Commands.Enqueue(0, new Command(Note, 1, contentId: 1));
        simulation.Commands.Enqueue(0, new Command(Note, 0, contentId: 2));
        simulation.Commands.Enqueue(0, new Command(Unhandled, 1, contentId: 3));
        simulation.Commands.Enqueue(1, new Command(Note, 1, contentId: 4));

        simulation.Run(2);

        Assert.Equal(["0:note(2)", "0:first", "0:second", "1:first", "1:second"], log);
        Assert.Equal(3, simulation.DroppedCommandCount);
    }

    [Fact]
    public void ThereIsOneGateAndItIsSetBeforeTheFirstTick()
    {
        var (simulation, _) = Create();
        Assert.Throws<ArgumentNullException>(() => simulation.SetCommandGate(null!));
        simulation.SetCommandGate(new Barring(1));
        Assert.Throws<InvalidOperationException>(() => simulation.SetCommandGate(new Barring(0)));

        var (started, _) = Create();
        started.Tick();
        Assert.Throws<InvalidOperationException>(() => started.SetCommandGate(new Barring(1)));
    }

    [Fact]
    public void SystemsAndHandlersAreFixedOnceTheFirstTickHasRun()
    {
        var (simulation, log) = Create();
        simulation.Tick();

        Assert.Throws<InvalidOperationException>(() => simulation.AddSystem(new Recorder(log, "late")));
        Assert.Throws<InvalidOperationException>(() => simulation.SetCommandHandler(Unhandled, new Recorder(log, "late")));
    }

    [Fact]
    public void RegistrationRejectsWhatCannotWork()
    {
        var (simulation, log) = Create();

        Assert.Throws<ArgumentException>(() => simulation.SetCommandHandler(Note, new Recorder(log, "again")));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetCommandHandler(CommandType.None, new Recorder(log, "none")));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetCommandHandler((CommandType)(-1), new Recorder(log, "negative")));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetCommandHandler((CommandType)65_536, new Recorder(log, "too large")));
        Assert.Throws<ArgumentNullException>(() => simulation.SetCommandHandler(Unhandled, null!));
        Assert.Throws<ArgumentNullException>(() => simulation.AddSystem(null!));
        Assert.Throws<ArgumentNullException>(() => new Simulation(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Run(-1));
    }

    [Fact]
    public void ASimulationCanBeBuiltWithNothingRegistered()
    {
        var simulation = new Simulation(new World(5));
        simulation.Commands.Enqueue(3, new Command(Note, 0));

        simulation.Run(10);

        Assert.Equal(10, simulation.World.Tick);
        Assert.Equal(1, simulation.DroppedCommandCount);
        Assert.Equal(10, simulation.Commands.NextTick);
    }
}
