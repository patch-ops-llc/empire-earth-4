using Sim.Core;

namespace Net.Tests;

/// <summary>
/// A small made-up simulation for the session tests. It has no rule of the real game in it. Its
/// whole state is the entity list and the random generator, both of which the state hash covers,
/// so it needs no store, and every command changes that state in a way that depends on the player,
/// on the command and on what ran before it. Two worlds that ran the same commands in a different
/// order, or in different ticks, have different hashes.
/// </summary>
internal sealed class ToyGame
{
    // Numbers far from the ones the real game gives out, which start at 1.
    public const CommandType Spawn = (CommandType)65001;
    public const CommandType Cull = (CommandType)65002;
    public const ulong DefaultSeed = 20261110;

    public ToyGame(ulong seed = DefaultSeed)
    {
        World = new World(seed);
        Simulation = new Simulation(World);
        Simulation.SetCommandHandler(Spawn, new SpawnHandler(this));
        Simulation.SetCommandHandler(Cull, new CullHandler(this));
        Simulation.AddSystem(new ChurnSystem());
    }

    public World World { get; }

    public Simulation Simulation { get; }

    /// <summary>Every command that ran, with the tick it ran in. A record for the tests, not state.</summary>
    public List<Executed> Log { get; } = [];

    /// <summary>
    /// Runs a recorded match with no session: each bundle queued for its tick, then that many
    /// ticks. What a replay does, and what every peer of the session must have ended up with.
    /// </summary>
    public static ToyGame Replay(IEnumerable<TickBundle> bundles, int ticks, ulong seed = DefaultSeed)
    {
        var game = new ToyGame(seed);
        foreach (var bundle in bundles)
        {
            foreach (var command in bundle.Commands)
            {
                game.Simulation.Commands.Enqueue(bundle.Tick, command);
            }
        }

        game.Simulation.Run(ticks);
        return game;
    }

    public readonly record struct Executed(int Tick, int Player, CommandType Type, int ContentId);

    /// <summary>Creates entities: one, plus the player's number, plus the content ID modulo 3.</summary>
    private sealed class SpawnHandler(ToyGame game) : ICommandHandler
    {
        public bool Execute(World world, in Command command)
        {
            if (command.Player < 0 || command.ContentId < 0)
            {
                return false;
            }

            var count = 1 + command.Player + (command.ContentId % 3);
            for (var i = 0; i < count; i++)
            {
                world.Entities.Create();
            }

            game.Log.Add(new Executed(world.Tick, command.Player, command.Type, command.ContentId));
            return true;
        }
    }

    /// <summary>Destroys entities chosen at random: one, plus the player's number.</summary>
    private sealed class CullHandler(ToyGame game) : ICommandHandler
    {
        public bool Execute(World world, in Command command)
        {
            if (command.Player < 0 || world.Entities.Count == 0)
            {
                return false;
            }

            for (var i = 0; i <= command.Player; i++)
            {
                world.Entities.Destroy(world.Entities[world.Random.NextInt(world.Entities.Count)]);
            }

            game.Log.Add(new Executed(world.Tick, command.Player, command.Type, command.ContentId));
            return true;
        }
    }

    /// <summary>Keeps the state moving without commands: entities come and go at random.</summary>
    private sealed class ChurnSystem : ISystem
    {
        public void Run(World world)
        {
            if (world.Random.NextInt(4) == 0)
            {
                world.Entities.Create();
            }

            if (world.Entities.Count > 60)
            {
                world.Entities.Destroy(world.Entities[world.Random.NextInt(world.Entities.Count)]);
            }
        }
    }
}
