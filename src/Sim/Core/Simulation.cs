namespace Sim.Core;

/// <summary>A step of the tick: a function over the world. Systems run in the order they were added.</summary>
public interface ISystem
{
    void Run(World world);
}

/// <summary>Executes one type of command.</summary>
public interface ICommandHandler
{
    /// <summary>
    /// Checks the command against the world as it is now (ownership, affordability, placement) and
    /// carries it out. Returns false, having changed nothing, if the command is not valid. The
    /// check must depend only on the world and the command, so that every machine drops the same
    /// commands.
    /// </summary>
    bool Execute(World world, in Command command);
}

/// <summary>
/// Decides, before any handler sees it, whether a command is run at all: the one rule that holds
/// for every type of command, such as that a player who is out of the match gives no orders.
/// </summary>
public interface ICommandGate
{
    /// <summary>
    /// False drops the command. Like a handler's check, the answer must depend only on the world
    /// and the command, so that every machine drops the same commands, and it must change nothing.
    /// </summary>
    bool Admits(World world, in Command command);
}

/// <summary>
/// The tick loop. It owns the order in which everything happens, which is the same on every
/// machine: the event list is emptied, then the commands scheduled for the tick in the order they
/// were queued, then each system in the order it was added, then the removal of destroyed
/// entities, then the tick number goes up.
/// </summary>
/// <remarks>
/// Single-player, network play, replays and tests all drive the simulation through this class and
/// nothing else. It reads no clock: the caller decides when a tick runs.
/// </remarks>
public sealed class Simulation
{
    private readonly List<ISystem> _systems = [];
    private readonly List<Command> _due = [];
    private ICommandHandler?[] _handlers = [];
    private ICommandGate? _gate;
    private bool _started;

    public Simulation(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        World = world;
        Commands = new CommandQueue(world.Tick);
        Events = new SimEventList(world);
    }

    public World World { get; }

    /// <summary>The commands waiting for their tick.</summary>
    public CommandQueue Commands { get; }

    /// <summary>
    /// What happened in the last tick that ran, for whatever shows the match. Command handlers and
    /// systems are handed this list when the match is set up and add to it; nothing in the
    /// simulation reads it. It is not part of the state hash or of a snapshot.
    /// </summary>
    public SimEventList Events { get; }

    /// <summary>
    /// How many commands have been dropped, because the gate turned them away, their type had no
    /// handler or their handler found them invalid. For diagnostics and tests; it is not part of
    /// the state hash.
    /// </summary>
    public long DroppedCommandCount { get; private set; }

    /// <summary>Adds a system after those already added. Only before the first tick.</summary>
    public void AddSystem(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        ThrowIfStarted();
        _systems.Add(system);
    }

    /// <summary>Sets the handler for a command type. One handler per type, and only before the first tick.</summary>
    public void SetCommandHandler(CommandType type, ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual((int)type, (int)CommandType.None, nameof(type));
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)type, ushort.MaxValue, nameof(type));
        ThrowIfStarted();

        var index = (int)type;
        if (index >= _handlers.Length)
        {
            Array.Resize(ref _handlers, index + 1);
        }

        if (_handlers[index] is not null)
        {
            throw new ArgumentException($"Command type {type} already has a handler.", nameof(type));
        }

        _handlers[index] = handler;
    }

    /// <summary>
    /// Sets the gate every command passes before its handler is asked. One gate, and only before
    /// the first tick. Without one every command reaches its handler.
    /// </summary>
    public void SetCommandGate(ICommandGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ThrowIfStarted();
        if (_gate is not null)
        {
            throw new InvalidOperationException("The simulation already has a command gate.");
        }

        _gate = gate;
    }

    /// <summary>Runs one tick.</summary>
    public void Tick()
    {
        _started = true;
        var world = World;

        Events.Clear();
        _due.Clear();
        Commands.TakeDue(world.Tick, _due);
        foreach (var command in _due)
        {
            var index = (int)command.Type;
            var handler = index >= 0 && index < _handlers.Length ? _handlers[index] : null;
            if ((_gate is { } gate && !gate.Admits(world, in command)) || handler is null || !handler.Execute(world, in command))
            {
                DroppedCommandCount++;
            }
        }

        foreach (var system in _systems)
        {
            system.Run(world);
        }

        world.FlushDestroyedEntities();
        world.AdvanceTick();
    }

    /// <summary>Runs a number of ticks one after another.</summary>
    public void Run(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (var i = 0; i < ticks; i++)
        {
            Tick();
        }
    }

    private void ThrowIfStarted()
    {
        if (_started)
        {
            throw new InvalidOperationException("Systems and command handlers can only be added before the first tick.");
        }
    }
}
