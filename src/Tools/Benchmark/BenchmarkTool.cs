using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Sim.Data;

namespace Tools.Benchmark;

/// <summary>
/// Times the simulation on the cases that cost it most and reports the time of a tick against
/// the budget of the technical plan: 500 units at 20 ticks per second, with an average tick under
/// 8 ms and a worst tick under 25 ms. It also reports what each case is there to show, such as
/// how long 200 units take through a pass 6 tiles wide, and, when asked, where the time of a
/// tick goes: what the commands and each system took.
/// </summary>
/// <remarks>
/// <para>
/// Every scenario is a match that plays the same on every machine: only the times differ. Each is
/// played once without timing, so that the times are not those of code being compiled, and then
/// <see cref="TimedRuns"/> times with a clock around every tick. The time of a tick is the least
/// of its times in those runs: the runs do the same work in every tick, so what one of them took
/// longer is the machine doing something else, and on a shared machine that is several
/// milliseconds every few ticks. Memory is counted around the tick alone, in the last run.
/// </para>
/// <para>
/// Three numbers are reported for the ticks of a scenario: their average, the time that 99 of
/// 100 ticks stay within (<see cref="HighPercentile"/>), and the worst. The first and the last
/// are the budget's; the one between says whether the worst tick is alone or one of many.
/// </para>
/// <para>
/// The hash of the state a scenario ends in is reported beside its times. A change that is
/// meant to make the simulation quicker and nothing else leaves every one of them as it was,
/// and one that changes how a fight or a move comes out does not.
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

    /// <summary>How often a scenario is played with the clock running. A quick run plays it once.</summary>
    public const int TimedRuns = 3;

    /// <summary>The share of a scenario's ticks, in hundredths, that the column between the average and the worst stays above.</summary>
    public const int HighPercentile = 99;

    /// <summary>The number of units of the scenarios that play the budget's case, unless <c>--units</c> names another.</summary>
    public const int BudgetUnits = 500;

    /// <summary>The most units <c>--units</c> takes: the plan's stretch, which the map of the scenarios has room for.</summary>
    public const int MostUnits = 1000;

    private const int LeastUnits = 20;

    private const string Usage =
        """
        Usage:
          benchmark [--data <folder>] [--scenario <name>]... [--units <number>] [--systems] [--quick]

        --data      The data folder, default game/data. The scenarios play on its map 'ridge'.
        --scenario  Run only the scenario named. May be given more than once.
        --units     The number of units in the scenarios that play the budget's 500, from 20 to 1000.
                    The plan's stretch is 1000.
        --systems   After the table, where the time of a tick goes in each scenario: what the
                    commands and each system took, on average, in the worst tick, and at its most.
        --quick     Fewer units and ticks, to see that the scenarios run. The times mean little.
        """;

    public string Name => "benchmark";

    public string Summary =>
        "Time the simulation on its costliest cases and report the time of a tick against the budget. "
        + $"Arguments: [--data <folder>, default {DefaultFolder}] [--scenario <name>]... [--units <number>, default {BudgetUnits}] [--systems] [--quick].";

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        var folder = DefaultFolder;
        var quick = false;
        var systems = false;
        int? units = null;
        var wanted = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--quick":
                    quick = true;
                    break;
                case "--systems":
                    systems = true;
                    break;
                case "--units" when i + 1 < arguments.Count
                    && int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number is >= LeastUnits and <= MostUnits:
                    units = number;
                    i++;
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

        var scenarios = Scenarios.All(quick, units ?? (quick ? Scenarios.QuickUnits : BudgetUnits));
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
            $"benchmark: {(optimized ? "optimized build" : "NOT an optimized build: run with -c Release for times that mean something")}, {Sim.SimInfo.TicksPerSecond} ticks per second, budget {AverageBudget:0.##} ms average and {WorstBudget:0.##} ms worst per tick, {(quick ? "quick run: each scenario timed once" : $"each tick timed {TimedRuns} times and its least time kept")}"));
        output.WriteLine();
        output.WriteLine($"{"scenario",-18} {"units",5} {"ticks",6} {"average ms",10} {"p" + HighPercentile + " ms",9} {"worst ms",9} {"bytes/tick",10} {"end state",16}  what it shows");

        var failed = 0;
        var overBudget = new List<string>();
        var shares = new List<(string Scenario, Result Result, Times Times, IReadOnlyList<string> Parts)>();
        foreach (var scenario in scenarios)
        {
            if (wanted.Count > 0 && !wanted.Contains(scenario.Name))
            {
                continue;
            }

            // Once for the compiler, then for the clock.
            Play(scenario.Create(loaded.Data), times: null, withSystems: false);
            var times = new Times();
            Result result = default;
            IReadOnlyList<string> parts = [];
            for (var run = 0; run < (quick ? 1 : TimedRuns); run++)
            {
                (result, parts) = Play(scenario.Create(loaded.Data), times, systems);
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenario.Name,-18} {result.Units,5} {result.Ticks,6} {result.AverageMilliseconds,10:0.000} {result.HighMilliseconds,9:0.000} {result.WorstMilliseconds,9:0.000} {result.BytesPerTick,10:0} {result.EndState:x16}  {result.Findings}"));
            if (result.Problem is { } problem)
            {
                failed++;
                errors.WriteLine($"benchmark: {scenario.Name}: {problem}");
            }

            if (result.AverageMilliseconds > AverageBudget || result.WorstMilliseconds > WorstBudget)
            {
                overBudget.Add(scenario.Name);
            }

            if (systems)
            {
                shares.Add((scenario.Name, result, times, parts));
            }
        }

        output.WriteLine();
        output.WriteLine(overBudget.Count == 0
            ? "benchmark: every scenario is within the budget on this machine."
            : $"benchmark: over the budget on this machine: {string.Join(", ", overBudget)}.");
        if (systems)
        {
            WriteShares(output, shares);
        }

        if (failed > 0)
        {
            errors.WriteLine($"benchmark: {failed} {(failed == 1 ? "scenario" : "scenarios")} did not do what it is there to show.");
            return ExitCodes.Failed;
        }

        return ExitCodes.Ok;
    }

    /// <summary>
    /// Where the time of a tick went in each scenario: the commands, each system in the order
    /// they run, and the rest, which is the removal of what was destroyed.
    /// </summary>
    private static void WriteShares(TextWriter output, List<(string Scenario, Result Result, Times Times, IReadOnlyList<string> Parts)> shares)
    {
        var toMilliseconds = 1000.0 / Stopwatch.Frequency;
        output.WriteLine();
        output.WriteLine("benchmark: where the time of a tick goes, in milliseconds: on average, as a share of the average tick, in the scenario's worst tick, and the most the part took in any tick.");
        foreach (var (scenario, result, times, parts) in shares)
        {
            var ticks = result.Ticks;
            var worst = 0;
            for (var tick = 1; tick < ticks; tick++)
            {
                worst = times.Total[tick] > times.Total[worst] ? tick : worst;
            }

            long all = 0;
            for (var tick = 0; tick < ticks; tick++)
            {
                all += times.Total[tick];
            }

            output.WriteLine();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{scenario}, {result.Units} units: the worst tick was tick {worst} of {ticks}"));
            output.WriteLine($"  {"part",-24} {"average",9} {"share",7} {"worst tick",11} {"its worst",10}");
            for (var part = 0; part < parts.Count; part++)
            {
                long sum = 0;
                long most = 0;
                for (var tick = 0; tick < ticks; tick++)
                {
                    sum += times.Parts[tick][part];
                    most = Math.Max(most, times.Parts[tick][part]);
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {parts[part],-24} {sum * toMilliseconds / ticks,9:0.000} {(all == 0 ? 0 : 100.0 * sum / all),6:0.0}% {times.Parts[worst][part] * toMilliseconds,11:0.000} {most * toMilliseconds,10:0.000}"));
            }
        }
    }

    /// <summary>
    /// Plays a scenario to its end. With <paramref name="times"/>, the time of each tick goes
    /// into it where it is less than the time already there, and the result is computed from
    /// it. With <paramref name="withSystems"/>, the parts of each tick are timed as well.
    /// </summary>
    private static (Result Result, IReadOnlyList<string> Parts) Play(Run run, Times? times, bool withSystems)
    {
        var simulation = run.Match.Simulation;
        var clock = withSystems ? SystemClock.Attach(simulation) : null;
        IReadOnlyList<string> parts = clock is null ? [] : ["commands", .. clock.Names, "the rest"];
        var units = run.Match.Movers.Count;
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
            if (times is not null && (ticks == times.Total.Count || elapsed < times.Total[ticks]))
            {
                var ofParts = new long[parts.Count];
                if (clock is not null)
                {
                    ofParts[0] = clock.FirstSystemBegan - before;
                    var rest = elapsed - ofParts[0];
                    for (var i = 0; i < clock.Elapsed.Length; i++)
                    {
                        ofParts[i + 1] = clock.Elapsed[i];
                        rest -= clock.Elapsed[i];
                    }

                    ofParts[^1] = rest;
                }

                if (ticks == times.Total.Count)
                {
                    times.Total.Add(elapsed);
                    times.Parts.Add(ofParts);
                }
                else
                {
                    times.Total[ticks] = elapsed;
                    times.Parts[ticks] = ofParts;
                }
            }

            ticks++;
            if (run.AfterTick(ticks))
            {
                break;
            }
        }

        if (times is null)
        {
            return default;
        }

        var toMilliseconds = 1000.0 / Stopwatch.Frequency;
        var (findings, problem) = run.Finish(ticks);
        var sorted = times.Total.Take(ticks).Order().ToArray();
        var high = sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * HighPercentile / 100.0) - 1)];
        return (
            new Result(
                units,
                ticks,
                sorted.Sum() * toMilliseconds / ticks,
                high * toMilliseconds,
                sorted[^1] * toMilliseconds,
                (double)bytes / ticks,
                run.Match.World.ComputeHash(),
                findings,
                problem),
            parts);
    }

    private static bool IsOptimized(Assembly assembly) =>
        assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    private readonly record struct Result(
        int Units,
        int Ticks,
        double AverageMilliseconds,
        double HighMilliseconds,
        double WorstMilliseconds,
        double BytesPerTick,
        ulong EndState,
        string Findings,
        string? Problem);

    /// <summary>The least time of each tick over the runs of a scenario, and what the parts of that tick took.</summary>
    private sealed class Times
    {
        public List<long> Total { get; } = [];

        public List<long[]> Parts { get; } = [];
    }
}
