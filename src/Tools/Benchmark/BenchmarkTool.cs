using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Sim.Data;

namespace Tools.Benchmark;

/// <summary>
/// Times the simulation on the cases that cost it most and reports the time of a tick against
/// the budget of the technical plan: 500 units at 20 ticks per second, with an average tick under
/// 8 ms and a worst tick under 25 ms. It also reports what each case is there to show, such as
/// how long 200 units take through a pass 6 tiles wide.
/// </summary>
/// <remarks>
/// <para>
/// Every scenario is a match that plays the same on every machine: only the times differ. Each is
/// played once without timing, so that the times are not those of code being compiled, and then
/// once more with a clock around every tick. Memory is counted around the tick alone.
/// </para>
/// <para>
/// A scenario that does not do what it is there to show (units that never arrive, enemies closer
/// than their spacing) fails the tool. A time over the budget does not: times depend on the
/// machine, so they are reported and never judged.
/// </para>
/// </remarks>
public sealed class BenchmarkTool : ITool
{
    public const string DefaultFolder = "game/data";

    /// <summary>The plan's budget for the average tick, in milliseconds.</summary>
    public const double AverageBudget = 8;

    /// <summary>The plan's budget for the worst tick, in milliseconds.</summary>
    public const double WorstBudget = 25;

    private const string Usage =
        """
        Usage:
          benchmark [--data <folder>] [--scenario <name>]... [--quick]

        --data      The data folder, default game/data. The scenarios play on its map 'ridge'.
        --scenario  Run only the scenario named. May be given more than once.
        --quick     Fewer units and ticks, to see that the scenarios run. The times mean little.
        """;

    public string Name => "benchmark";

    public string Summary =>
        "Time the simulation on its costliest cases and report the time of a tick against the budget. "
        + $"Arguments: [--data <folder>, default {DefaultFolder}] [--scenario <name>]... [--quick].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var folder = DefaultFolder;
        var quick = false;
        var wanted = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--quick":
                    quick = true;
                    break;
                case "--data" when i + 1 < arguments.Count:
                    folder = arguments[++i];
                    break;
                case "--scenario" when i + 1 < arguments.Count:
                    wanted.Add(arguments[++i]);
                    break;
                default:
                    errors.WriteLine($"benchmark: unknown or incomplete argument '{arguments[i]}'.");
                    errors.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        var scenarios = Scenarios.All(quick);
        var unknown = wanted.Where(name => scenarios.All(scenario => scenario.Name != name)).ToList();
        if (unknown.Count > 0)
        {
            errors.WriteLine($"benchmark: there is no scenario '{unknown[0]}'. The scenarios are: {string.Join(", ", scenarios.Select(scenario => scenario.Name))}.");
            return ExitCodes.Usage;
        }

        if (!Directory.Exists(folder))
        {
            errors.WriteLine($"benchmark: there is no folder '{folder}'.");
            return ExitCodes.Usage;
        }

        var loaded = DataLoader.Load(new DiskDataFiles(folder));
        if (!loaded.IsValid)
        {
            errors.WriteLine($"benchmark: the data in {folder} is not valid. Run datacheck on it.");
            return ExitCodes.Failed;
        }

        if (loaded.Data.Maps.All(map => map.Id != Scenarios.MapId))
        {
            errors.WriteLine($"benchmark: the data in {folder} has no map '{Scenarios.MapId}'.");
            return ExitCodes.Failed;
        }

        var optimized = IsOptimized(typeof(Sim.SimInfo).Assembly);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"benchmark: {(optimized ? "optimized build" : "NOT an optimized build: run with -c Release for times that mean something")}, {Sim.SimInfo.TicksPerSecond} ticks per second, budget {AverageBudget:0.##} ms average and {WorstBudget:0.##} ms worst per tick{(quick ? ", quick run" : "")}"));
        output.WriteLine();
        output.WriteLine($"{"scenario",-18} {"units",5} {"ticks",6} {"average ms",10} {"worst ms",9} {"bytes/tick",10}  what it shows");

        var failed = 0;
        var overBudget = new List<string>();
        foreach (var scenario in scenarios)
        {
            if (wanted.Count > 0 && !wanted.Contains(scenario.Name))
            {
                continue;
            }

            // Once for the compiler, once for the clock.
            Play(scenario.Create(loaded.Data), timed: false);
            var result = Play(scenario.Create(loaded.Data), timed: true);
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenario.Name,-18} {result.Units,5} {result.Ticks,6} {result.AverageMilliseconds,10:0.000} {result.WorstMilliseconds,9:0.000} {result.BytesPerTick,10:0}  {result.Findings}"));
            if (result.Problem is { } problem)
            {
                failed++;
                errors.WriteLine($"benchmark: {scenario.Name}: {problem}");
            }

            if (result.AverageMilliseconds > AverageBudget || result.WorstMilliseconds > WorstBudget)
            {
                overBudget.Add(scenario.Name);
            }
        }

        output.WriteLine();
        output.WriteLine(overBudget.Count == 0
            ? "benchmark: every scenario is within the budget on this machine."
            : $"benchmark: over the budget on this machine: {string.Join(", ", overBudget)}.");
        if (failed > 0)
        {
            errors.WriteLine($"benchmark: {failed} {(failed == 1 ? "scenario" : "scenarios")} did not do what it is there to show.");
            return ExitCodes.Failed;
        }

        return ExitCodes.Ok;
    }

    private static Result Play(Run run, bool timed)
    {
        var simulation = run.Match.Simulation;
        long total = 0;
        long worst = 0;
        long bytes = 0;
        var ticks = 0;
        while (ticks < run.MostTicks)
        {
            run.BeforeTick(ticks);
            var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            var before = Stopwatch.GetTimestamp();
            simulation.Tick();
            var elapsed = Stopwatch.GetTimestamp() - before;
            bytes += GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            total += elapsed;
            worst = Math.Max(worst, elapsed);
            ticks++;
            if (run.AfterTick(ticks))
            {
                break;
            }
        }

        if (!timed)
        {
            return default;
        }

        var toMilliseconds = 1000.0 / Stopwatch.Frequency;
        var (findings, problem) = run.Finish(ticks);
        return new Result(
            run.Match.Movers.Count,
            ticks,
            total * toMilliseconds / ticks,
            worst * toMilliseconds,
            (double)bytes / ticks,
            findings,
            problem);
    }

    private static bool IsOptimized(Assembly assembly) =>
        assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    private readonly record struct Result(
        int Units,
        int Ticks,
        double AverageMilliseconds,
        double WorstMilliseconds,
        double BytesPerTick,
        string Findings,
        string? Problem);
}
