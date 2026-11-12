using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using Godot;
using Net;
using Sim;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// What the game does when it is started with <see cref="Main.SmokeTestArgument"/>: it builds the
/// same match on every machine, plays the same short script of move orders in it, prints one line
/// that describes how the match stands at the end and quits. CI and the export workflow look for
/// that line, because Godot exits with 0 even when the game assembly or a script fails to load, and
/// a game that failed never prints it.
/// </summary>
/// <remarks>
/// <para>
/// The line carries two hashes for the workflows to compare. <c>content_hash</c> is the hash of the
/// data files as the game read them, written the way the <c>datacheck</c> tool prints it for
/// <c>game/data</c>, and the two must be equal. <c>state_hash</c> is the hash of the match's state
/// after the script, which must be the same on every operating system and processor.
/// </para>
/// <para>
/// The script goes the way a player's orders go: through the session, which runs the ticks. Time
/// is not read from a clock. Each step hands the session exactly one tick's worth of it, so the
/// test takes the same course on a fast machine and a slow one.
/// </para>
/// </remarks>
public sealed class SmokeTest
{
    public const string Marker = "SMOKE_TEST_OK";

    /// <summary>The seed of the smoke test's match. Every machine uses this one.</summary>
    public const ulong Seed = 20261111;

    /// <summary>The start of the local player in the smoke test's match.</summary>
    public const int LocalStart = 0;

    /// <summary>The number of ticks the smoke test's match runs for: 30 seconds of play.</summary>
    public const int Ticks = 600;

    // The orders, each given once the match has run the number of ticks named. Units are the local
    // player's, counted from 0 in ID order. Targets are in sixteenths of a tile, so that they are
    // whole numbers here. On ridge the local player starts on the left, and the cliff runs down
    // the middle of the map with gaps in it.
    private static readonly Order[] Script =
    [
        // Everyone to the far side of the cliff, which takes a route through a gap.
        new(AtTick: 0, FirstUnit: 0, UnitCount: 6, X16: 936, Y16: 484),

        // Three of them onto the cliff itself, where nobody can stand: they go to the nearest tile they can reach.
        new(AtTick: 100, FirstUnit: 0, UnitCount: 3, X16: 760, Y16: 8),

        // The others to a point right beside a forest, which is moved off the trees.
        new(AtTick: 220, FirstUnit: 3, UnitCount: 3, X16: 543, Y16: 72),

        // One to the last point of the map, in its far corner.
        new(AtTick: 400, FirstUnit: 0, UnitCount: 1, X16: 1536, Y16: 1536),
    ];

    private readonly Match _match;
    private readonly LocalSession _session;
    private readonly EntityId[] _units;
    private readonly int _firstTick;
    private int _nextOrder;

    public SmokeTest(Match match, LocalSession session)
    {
        _match = match;
        _session = session;
        _firstTick = match.World.Tick;

        var seat = session.Client.Player;
        var units = new List<EntityId>();
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            if (match.Bodies.ValueAt(i).Owner == seat)
            {
                units.Add(match.Bodies.EntityAt(i));
            }
        }

        _units = [.. units];
    }

    /// <summary>True once the match has run all its ticks.</summary>
    public bool IsDone => _match.World.Tick - _firstTick >= Ticks;

    /// <summary>
    /// Runs up to a number of ticks, giving the orders that fall due on the way. Returns false,
    /// with an error, when the session does not do as it is asked.
    /// </summary>
    public bool Run(int ticks)
    {
        var client = _session.Client;
        for (var i = 0; i < ticks && !IsDone; i++)
        {
            var elapsed = _match.World.Tick - _firstTick;
            while (_nextOrder < Script.Length && Script[_nextOrder].AtTick <= elapsed)
            {
                if (!Submit(Script[_nextOrder++]))
                {
                    return false;
                }
            }

            var ran = _session.Advance(client.TickInterval);
            if (ran != 1)
            {
                GD.PrintErr($"Smoke test: the session ran {ran} ticks in the time of one, at tick {_match.World.Tick}.");
                return false;
            }
        }

        return true;
    }

    /// <summary>Prints the marker line. Returns false, with an error, when the match or the view is not as it should be.</summary>
    public bool Report(int entitiesDrawn)
    {
        var entities = _match.Bodies.Count;
        if (entitiesDrawn != entities)
        {
            GD.PrintErr($"Smoke test: the view draws {entitiesDrawn} entities and the match has {entities}.");
            return false;
        }

        if (!IsDone || _nextOrder != Script.Length || _session.Client.Phase != SessionPhase.Running)
        {
            GD.PrintErr($"Smoke test: the match stopped short, at tick {_match.World.Tick} with {_nextOrder} orders given.");
            return false;
        }

        // Every order of the script is one the simulation accepts. A dropped one means the game and
        // the simulation disagree about what a valid order is.
        var dropped = _match.Simulation.DroppedCommandCount;
        if (dropped != 0)
        {
            GD.PrintErr($"Smoke test: the simulation dropped {dropped} of the script's orders.");
            return false;
        }

        var simFramework = typeof(SimInfo).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var gameFramework = typeof(SmokeTest).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var simOptimized = IsOptimized(typeof(SimInfo).Assembly) ? "true" : "false";
        var stateHash = _match.World.ComputeHash().ToString("x16", CultureInfo.InvariantCulture);

        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"{Marker} game_version={GameInfo.Version} runtime={System.Environment.Version} game=[{gameFramework}] sim=[{simFramework}] " +
            $"sim_optimized={simOptimized} ticks_per_second={SimInfo.TicksPerSecond} map={_match.Map.Id} " +
            $"tick={_match.World.Tick} entities={entities} orders={Script.Length} " +
            $"content_hash=sha256:{_match.Data.ContentHash} state_hash={stateHash}"));
        return true;
    }

    private bool Submit(Order order)
    {
        if (order.FirstUnit + order.UnitCount > _units.Length)
        {
            GD.PrintErr($"Smoke test: the script orders unit {order.FirstUnit + order.UnitCount - 1} and the player has {_units.Length}.");
            return false;
        }

        var units = _units.AsSpan(order.FirstUnit, order.UnitCount);
        var x = Fixed.FromRaw(order.X16 * (Fixed.RawOne / 16));
        var y = Fixed.FromRaw(order.Y16 * (Fixed.RawOne / 16));

        // The far edge of the map is not on it. Its last point is one distance unit inside.
        x = Fixed.FromRaw(Math.Min(x.Raw, (_match.Map.Width * Fixed.RawOne) - 1));
        y = Fixed.FromRaw(Math.Min(y.Raw, (_match.Map.Height * Fixed.RawOne) - 1));

        if (!_session.Client.Submit(new Command(CommandType.Move, _session.Client.Player, units, x, y)))
        {
            GD.PrintErr($"Smoke test: the session refused an order at tick {_match.World.Tick}.");
            return false;
        }

        return true;
    }

    // An unoptimized build carries a DebuggableAttribute that turns the JIT optimizer off.
    private static bool IsOptimized(Assembly assembly) =>
        assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    private readonly record struct Order(int AtTick, int FirstUnit, int UnitCount, int X16, int Y16);
}
