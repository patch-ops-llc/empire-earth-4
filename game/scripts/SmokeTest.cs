using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using Godot;
using Sim;
using Sim.Setup;

namespace Game;

/// <summary>
/// What the game does when it is started with <see cref="Main.SmokeTestArgument"/>: it builds the
/// same match on every machine, prints one line that describes it and quits. CI and the export
/// workflow look for that line, because Godot exits with 0 even when the game assembly or a script
/// fails to load, and a game that failed never prints it.
/// </summary>
/// <remarks>
/// The line carries two hashes for the workflows to compare. <c>content_hash</c> is the hash of the
/// data files as the game read them, written the way the <c>datacheck</c> tool prints it for
/// <c>game/data</c>, and the two must be equal. <c>state_hash</c> is the hash of the match's state, which must be the same on
/// every operating system and processor.
/// </remarks>
public static class SmokeTest
{
    public const string Marker = "SMOKE_TEST_OK";

    /// <summary>The seed of the smoke test's match. Every machine uses this one.</summary>
    public const ulong Seed = 20261111;

    /// <summary>The start of the local player in the smoke test's match.</summary>
    public const int LocalStart = 0;

    /// <summary>Prints the marker line. Returns false, with an error, when the view is not drawing the match.</summary>
    public static bool Report(Match match, int entitiesDrawn)
    {
        var entities = match.Bodies.Count;
        if (entitiesDrawn != entities)
        {
            GD.PushError($"Smoke test: the view draws {entitiesDrawn} entities and the match has {entities}.");
            return false;
        }

        var simFramework = typeof(SimInfo).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var gameFramework = typeof(SmokeTest).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var simOptimized = IsOptimized(typeof(SimInfo).Assembly) ? "true" : "false";
        var stateHash = match.World.ComputeHash().ToString("x16", CultureInfo.InvariantCulture);

        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"{Marker} runtime={System.Environment.Version} game=[{gameFramework}] sim=[{simFramework}] " +
            $"sim_optimized={simOptimized} ticks_per_second={SimInfo.TicksPerSecond} map={match.Map.Id} " +
            $"tick={match.World.Tick} entities={entities} content_hash=sha256:{match.Data.ContentHash} state_hash={stateHash}"));
        return true;
    }

    // An unoptimized build carries a DebuggableAttribute that turns the JIT optimizer off.
    private static bool IsOptimized(Assembly assembly) =>
        assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };
}
