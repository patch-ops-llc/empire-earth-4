using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Core;

/// <summary>
/// A small made-up game for the kernel tests. It has no rule of the real game in it, but it uses
/// every part of the kernel the way a real system will: two stores, three systems, two command
/// types, the random generator, fixed-point movement by angle, exact fractions rounded once, and
/// entities created and destroyed while systems walk the stores.
/// </summary>
internal sealed class ToyMatch
{
    public const CommandType Turn = (CommandType)1;
    public const CommandType Spawn = (CommandType)2;
    public const int Players = 2;
    public const int MapSize = 96;

    public ToyMatch(ulong seed, bool registerStoresInReverse = false)
    {
        World = new World(seed);
        if (registerStoresInReverse)
        {
            Scores = World.AddStore(new ScoreStore());
            Movers = World.AddStore(new ComponentStore<Mover>("movers"));
        }
        else
        {
            Movers = World.AddStore(new ComponentStore<Mover>("movers"));
            Scores = World.AddStore(new ScoreStore());
        }

        Simulation = new Simulation(World);
        Simulation.SetCommandHandler(Turn, new TurnHandler(this));
        Simulation.SetCommandHandler(Spawn, new SpawnHandler(this));
        Simulation.AddSystem(new ReinforcementSystem(this));
        Simulation.AddSystem(new MovementSystem(this));
        Simulation.AddSystem(new RetirementSystem(this));
    }

    public World World { get; }

    public Simulation Simulation { get; }

    public ComponentStore<Mover> Movers { get; }

    public ScoreStore Scores { get; }

    /// <summary>
    /// A fixed script of commands for the first <paramref name="ticks"/> ticks: some valid, some
    /// aimed at entities that do not exist or belong to the other player, and one of a type that
    /// has no handler.
    /// </summary>
    public static List<(int Tick, Command Command)> Script(int ticks)
    {
        var script = new List<(int Tick, Command Command)>();
        for (var tick = 0; tick < ticks; tick++)
        {
            var player = tick / 7 % Players;
            if (tick % 7 == 3)
            {
                EntityId[] entities = [new(1 + (tick / 5)), new(2 + (tick / 5)), new(4 + (tick / 5))];
                script.Add((tick, new Command(Turn, player, entities, contentId: 300 + tick)));
            }

            if (tick % 13 == 5)
            {
                script.Add((tick, new Command(
                    Spawn,
                    player,
                    targetX: Fixed.FromInt(tick * 7 % 110),
                    targetY: Fixed.FromInt(tick * 3 % MapSize) + Fixed.Half)));
            }

            if (tick % 97 == 50)
            {
                script.Add((tick, new Command((CommandType)99, player)));
            }
        }

        return script;
    }

    /// <summary>Queues a script and runs the match, returning the state hash after every tick.</summary>
    public ulong[] Play(int ticks, IEnumerable<(int Tick, Command Command)> script)
    {
        foreach (var (tick, command) in script)
        {
            Simulation.Commands.Enqueue(tick, command);
        }

        var hashes = new ulong[ticks];
        for (var i = 0; i < ticks; i++)
        {
            Simulation.Tick();
            hashes[i] = World.ComputeHash();
        }

        return hashes;
    }

    private void SpawnMover(int owner, Fixed x, Fixed y, Angle heading, Fixed speed)
    {
        var entity = World.Entities.Create();
        Movers.Add(entity, new Mover { Owner = owner, X = x, Y = y, Heading = heading, Speed = speed });
    }

    public struct Mover : IStateHashable
    {
        public int Owner;
        public Fixed X;
        public Fixed Y;
        public Angle Heading;
        public Fixed Speed;
        public int Age;

        public readonly void AddToHash(ref StateHasher hasher)
        {
            hasher.Add(Owner);
            hasher.Add(X);
            hasher.Add(Y);
            hasher.Add(Heading);
            hasher.Add(Speed);
            hasher.Add(Age);
        }
    }

    /// <summary>State that does not belong to an entity: each player's score in thousandths.</summary>
    public sealed class ScoreStore : IStateStore
    {
        private readonly long[] _thousandths = new long[Players];

        public string Name => "scores";

        public long this[int player] => _thousandths[player];

        public void Add(int player, long thousandths) => _thousandths[player] = checked(_thousandths[player] + thousandths);

        public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
        {
        }

        public void AddToHash(ref StateHasher hasher)
        {
            foreach (var score in _thousandths)
            {
                hasher.Add(score);
            }
        }
    }

    /// <summary>Turns the listed movers by <see cref="Command.ContentId"/> units. All of them must be alive and the player's own.</summary>
    private sealed class TurnHandler(ToyMatch match) : ICommandHandler
    {
        public bool Execute(World world, in Command command)
        {
            foreach (var entity in command.Entities)
            {
                if (!world.Entities.IsAlive(entity)
                    || !match.Movers.TryGet(entity, out var mover)
                    || mover.Owner != command.Player)
                {
                    return false;
                }
            }

            foreach (var entity in command.Entities)
            {
                ref var mover = ref match.Movers.Get(entity);
                mover.Heading += Angle.FromUnits(command.ContentId);
            }

            return true;
        }
    }

    /// <summary>Places a mover for the player at the target position, which must be on the map.</summary>
    private sealed class SpawnHandler(ToyMatch match) : ICommandHandler
    {
        public bool Execute(World world, in Command command)
        {
            var size = Fixed.FromInt(MapSize);
            if (command.Player is < 0 or >= Players
                || command.TargetX < Fixed.Zero || command.TargetX >= size
                || command.TargetY < Fixed.Zero || command.TargetY >= size)
            {
                return false;
            }

            var heading = Angle.FromUnits(world.Random.NextInt(Angle.UnitsPerTurn));
            match.SpawnMover(command.Player, command.TargetX, command.TargetY, heading, Fraction.Parse("0.15").ToFixed());
            return true;
        }
    }

    /// <summary>Every fourth tick, places a mover at a random position for a random player.</summary>
    private sealed class ReinforcementSystem(ToyMatch match) : ISystem
    {
        public void Run(World world)
        {
            if (world.Tick % 4 != 0)
            {
                return;
            }

            var random = world.Random;
            var owner = random.NextInt(Players);
            var x = Fixed.FromRaw(random.NextInt(MapSize * Fixed.RawOne));
            var y = Fixed.FromRaw(random.NextInt(MapSize * Fixed.RawOne));
            var heading = Angle.FromUnits(random.NextInt(Angle.UnitsPerTurn));
            var speed = new Fraction(random.NextInt(5, 40), 100).ToFixed();
            match.SpawnMover(owner, x, y, heading, speed);
        }
    }

    /// <summary>Moves every mover along its heading. One that leaves the map is destroyed and scores for its owner.</summary>
    private sealed class MovementSystem(ToyMatch match) : ISystem
    {
        public void Run(World world)
        {
            var movers = match.Movers;
            var size = Fixed.FromInt(MapSize);
            for (var i = 0; i < movers.Count; i++)
            {
                ref var mover = ref movers.ValueAt(i);
                mover.X += mover.Heading.Cos() * mover.Speed;
                mover.Y += mover.Heading.Sin() * mover.Speed;
                mover.Age++;

                if (mover.X < Fixed.Zero || mover.X >= size || mover.Y < Fixed.Zero || mover.Y >= size)
                {
                    world.Entities.Destroy(movers.EntityAt(i));

                    // 1.25 points per tick survived, rounded once to thousandths.
                    match.Scores.Add(mover.Owner, (mover.Age * Fraction.Parse("1.25")).RoundHalfUp(1000));
                }
            }
        }
    }

    /// <summary>Destroys movers older than 300 ticks, and every tenth tick one more chosen at random.</summary>
    private sealed class RetirementSystem(ToyMatch match) : ISystem
    {
        public void Run(World world)
        {
            var movers = match.Movers;
            for (var i = 0; i < movers.Count; i++)
            {
                if (movers.ValueAt(i).Age > 300)
                {
                    world.Entities.Destroy(movers.EntityAt(i));
                }
            }

            if (world.Tick % 10 == 9 && movers.Count > 0)
            {
                world.Entities.Destroy(movers.EntityAt(world.Random.NextInt(movers.Count)));
            }
        }
    }
}
