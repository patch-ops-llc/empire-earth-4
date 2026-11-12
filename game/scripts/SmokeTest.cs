using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using Godot;
using Net;
using Sim;
using Sim.Setup;

namespace Game;

/// <summary>
/// What the game does when it is started with <see cref="Main.SmokeTestArgument"/>: it builds the
/// same match on every machine, plays the same script of orders in it, prints one line that
/// describes how the match stands at the end and quits. CI and the export workflow look for that
/// line, because Godot exits with 0 even when the game assembly or a script fails to load, and a
/// game that failed never prints it.
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
/// <para>
/// The match is shown as any match is, through the main scene: every layer of the world and every
/// part of the interface reads it after each tick. A view that fails on the match fails the test:
/// while it runs, <see cref="SmokeLog"/> counts the errors the engine logs, an exception thrown by
/// a piece of the view among them, and <see cref="Main"/> ends the test without the line on the
/// first of them.
/// </para>
/// </remarks>
public sealed class SmokeTest
{
    public const string Marker = "SMOKE_TEST_OK";

    /// <summary>The seed of the smoke test's match. Every machine uses this one.</summary>
    public const ulong Seed = 20261111;

    /// <summary>The start of the local player in the smoke test's match.</summary>
    public const int LocalStart = 0;

    private readonly SmokePart[] _script = Script();
    private readonly Match _match;
    private readonly LocalSession _session;
    private readonly SmokeOrders _orders;
    private readonly int _firstTick;

    public SmokeTest(Match match, LocalSession session, MatchView view)
    {
        _match = match;
        _session = session;
        _orders = new SmokeOrders(match, session.Client, session.Host);
        _firstTick = match.World.Tick;
        _orders.View = view;
    }

    /// <summary>The number of ticks the smoke test's match runs for: those of every part of the script.</summary>
    public int Ticks => _script.Sum(part => part.Ticks);

    /// <summary>True once the match has run all its ticks.</summary>
    public bool IsDone => _match.World.Tick - _firstTick >= Ticks;

    // The script: its parts, which run one after another in this order. A piece that adds a kind
    // of order adds its part directly under the line that names the piece, so that two pieces
    // never add at the same place. A change to this file also starts the export workflow, which
    // plays the script in every exported build.
    private static SmokePart[] Script() =>
    [
        // The match itself: its pause and speed, and what the end screen says. No orders.
        new MatchFlowSmoke(),

        // Units: move orders. 30 seconds of play.
        new MoveOrdersSmoke(),

        // 11v. Combat: a short fight.
        new FightSmoke(),

        // 13v. Economy: a work order.
        new GatherOrdersSmoke(),

        // 14v. Research: one research.
        new ResearchSmoke(),

        // 15v. Construction and production.
        new StructuresSmoke(),
        new BuildSmoke(),

        // 17v. Fog of war.

        // Inspection: what the panel shows for a selection.
        new InspectionSmoke(),

        // Later pieces.
    ];

    /// <summary>
    /// Runs up to a number of ticks, giving the orders that fall due on the way. Returns false,
    /// with an error, when the session does not do as it is asked.
    /// </summary>
    public bool Run(int ticks)
    {
        var client = _session.Client;
        for (var i = 0; i < ticks && !IsDone; i++)
        {
            var (part, tick) = PartAt(_match.World.Tick - _firstTick);
            part.GiveOrders(tick, _orders);
            if (_orders.Failed)
            {
                GD.PrintErr($"Smoke test: the part '{part.Name}' failed at its tick {tick}.");
                return false;
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
    public bool Report(int unitsDrawn)
    {
        var entities = _match.Bodies.Count;
        var units = 0;
        for (var i = 0; i < entities; i++)
        {
            units += _match.Movers.Contains(_match.Bodies.EntityAt(i)) ? 1 : 0;
        }

        if (unitsDrawn != units)
        {
            GD.PrintErr($"Smoke test: the view draws {unitsDrawn} units and the match has {units}.");
            return false;
        }

        if (!IsDone || _orders.Failed || _session.Client.Phase != SessionPhase.Running)
        {
            GD.PrintErr($"Smoke test: the match stopped short, at tick {_match.World.Tick} with {_orders.Count} orders given.");
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
            $"tick={_match.World.Tick} entities={entities} orders={_orders.Count} " +
            $"content_hash=sha256:{_match.Data.ContentHash} state_hash={stateHash}"));
        return true;
    }

    // The part that runs a tick of the script, and the number of ticks that part has run by then.
    private (SmokePart Part, int Tick) PartAt(int elapsed)
    {
        foreach (var part in _script)
        {
            if (elapsed < part.Ticks)
            {
                return (part, elapsed);
            }

            elapsed -= part.Ticks;
        }

        throw new InvalidOperationException("The script has no part for this tick.");
    }

    // An unoptimized build carries a DebuggableAttribute that turns the JIT optimizer off.
    private static bool IsOptimized(Assembly assembly) =>
        assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };
}
