using System.Globalization;
using System.Text.RegularExpressions;
using Tools.Benchmark;

namespace Tools.Tests;

/// <summary>
/// Runs <c>benchmark</c> the way the command line does, as a quick run: fewer units and ticks,
/// the same scenarios. The times of a run depend on the machine and are not tested; what the
/// scenarios show is the same on every machine and is.
/// </summary>
public class BenchmarkToolTests
{
    private static readonly string[] ScenarioNames =
    [
        "chokepoint",
        "chokepoint-held",
        "march",
        "crossing",
        "separate-orders",
        "search-ridge",
        "search-256",
        "fight",
        "siege",
        "move-everything",
    ];

    [Fact]
    public void AQuickRunPlaysEveryScenarioAndPasses()
    {
        var (exitCode, output, errors) = Run("--quick");

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Equal(ScenarioNames, Rows(output).Select(row => row.Name));
        Assert.Contains("budget 8 ms average and 25 ms worst per tick", output, StringComparison.Ordinal);
        Assert.Matches("benchmark: (every scenario is within the budget|over the budget) on this machine", output);
    }

    /// <summary>
    /// The scenarios are matches, so what they show is the same in every run: the units that get
    /// through a pass, the tick the last one arrives, the tiles a search expands.
    /// </summary>
    [Fact]
    public void WhatAScenarioShowsIsTheSameInEveryRun()
    {
        var first = Rows(Run("--quick").Output);
        var second = Rows(Run("--quick").Output);

        Assert.Equal(
            first.Select(row => (row.Name, row.Units, row.Ticks, row.EndState, row.Findings)),
            second.Select(row => (row.Name, row.Units, row.Ticks, row.EndState, row.Findings)));
        Assert.All(first, row => Assert.Matches("^[0-9a-f]{16}$", row.EndState));
        Assert.Equal(first.Count, first.Select(row => row.EndState).Distinct().Count());
    }

    /// <summary>
    /// The worst cases the budget names, played by units of a chassis: they fight with shots,
    /// melee and the blast, bring structures down, and take one order for everything a player has.
    /// </summary>
    [Fact]
    public void TheArmyScenariosDoWhatTheyAreThereToShow()
    {
        var rows = Rows(Run("--quick").Output).ToDictionary(row => row.Name, StringComparer.Ordinal);

        var fight = Regex.Match(
            rows["fight"].Findings,
            @"^30 against 30 of four lines on the open field: (\d+) died, (\d+) melee hits, (\d+) shots with at most (\d+) in flight, (\d+) blasts; (over at tick \d+|not over after \d+ ticks) with \d+ and \d+ left$");
        Assert.True(fight.Success, rows["fight"].Findings);
        Assert.All(Enumerable.Range(1, 5), group => Assert.True(Number(fight.Groups[group]) > 0, rows["fight"].Findings));

        var siege = Regex.Match(
            rows["siege"].Findings,
            @"^36 sent at a base of 60 structures held by 24: (\d+) units died, (\d+) structures fell, (\d+) shots with at most \d+ in flight, (\d+) blasts, \d+ route searches; after \d+ ticks \d+ attackers and \d+ defenders are left$");
        Assert.True(siege.Success, rows["siege"].Findings);
        Assert.All(Enumerable.Range(1, 4), group => Assert.True(Number(siege.Groups[group]) > 0, rows["siege"].Findings));

        var move = Regex.Match(
            rows["move-everything"].Findings,
            @"^30 and 30 units from all over the map, each player's sent to a corner by one order: (\d+) route searches, at most (\d+) walking at once, (not at rest after \d+ ticks|at rest at tick \d+); \d+ alive; the players see (\d+) and (\d+) tiles$");
        Assert.True(move.Success, rows["move-everything"].Findings);
        Assert.True(Number(move.Groups[1]) > 0, rows["move-everything"].Findings);
        Assert.True(Number(move.Groups[2]) > 30, rows["move-everything"].Findings);
        Assert.True(Number(move.Groups[4]) > 0 && Number(move.Groups[5]) > 0, rows["move-everything"].Findings);

        Assert.All(["fight", "siege", "move-everything"], name => Assert.Equal(60, rows[name].Units));
    }

    /// <summary>The number of units is the budget's 500, and 60 in a quick run, unless it is named.</summary>
    [Fact]
    public void TheNumberOfUnitsCanBeNamed()
    {
        var rows = Rows(Run("--quick", "--units", "80", "--scenario", "march", "--scenario", "fight", "--scenario", "chokepoint").Output)
            .ToDictionary(row => row.Name, StringComparer.Ordinal);

        Assert.Equal(80, rows["march"].Units);
        Assert.Equal(80, rows["fight"].Units);
        Assert.StartsWith("40 against 40 ", rows["fight"].Findings, StringComparison.Ordinal);

        // The pass is the plan's own case, 200 units through 6 tiles, whatever is asked of the others.
        Assert.Equal(48, rows["chokepoint"].Units);
        Assert.Equal(500, BenchmarkTool.BudgetUnits);
        Assert.Equal(1000, BenchmarkTool.MostUnits);
    }

    /// <summary>
    /// With <c>--systems</c> the tool says where the time of a tick goes: the commands, every
    /// system of the simulation in the order they run, and the rest. The clock that times a
    /// system reaches the simulation's list of systems by name, so this test is also what says
    /// so when that list is renamed.
    /// </summary>
    [Fact]
    public void WhereTheTimeGoesIsReportedForEverySystem()
    {
        var (exitCode, output, errors) = Run("--quick", "--systems", "--scenario", "fight", "--scenario", "march");

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Equal(["march", "fight"], Rows(output).Select(row => row.Name));

        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var attacking = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (scenario, units) in new[] { ("march", 60), ("fight", 60) })
        {
            var heading = Array.FindIndex(lines, line => line.StartsWith($"{scenario}, {units} units: the worst tick was tick ", StringComparison.Ordinal));
            Assert.True(heading >= 0, $"No shares for {scenario}.");
            Assert.Matches(@"^  part +average +share +worst tick +its worst$", lines[heading + 1]);

            var parts = new List<(string Name, double Average, double Share, double InWorstTick, double Worst)>();
            for (var i = heading + 2; i < lines.Length && lines[i].Length > 0; i++)
            {
                var columns = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var numbers = columns[^4..].Select(column => double.Parse(column.TrimEnd('%'), CultureInfo.InvariantCulture)).ToArray();
                parts.Add((string.Join(' ', columns[..^4]), numbers[0], numbers[1], numbers[2], numbers[3]));
            }

            // The systems of a match, whatever they are, between the commands and the rest.
            Assert.Equal("commands", parts[0].Name);
            Assert.Equal("the rest", parts[^1].Name);
            Assert.Contains("MovementSystem", parts.Select(part => part.Name));
            Assert.Contains("AttackSystem", parts.Select(part => part.Name));
            Assert.All(parts[1..^1], part => Assert.EndsWith("System", part.Name, StringComparison.Ordinal));
            Assert.All(parts, part => Assert.True(part.Average >= 0 && part.Worst >= part.InWorstTick && part.Worst >= part.Average));
            Assert.InRange(parts.Sum(part => part.Share), 99, 101);
            Assert.True(parts.Single(part => part.Name == "MovementSystem").Average > 0);
            attacking[scenario] = parts.Single(part => part.Name == "AttackSystem").Average;
        }

        // The time lands on the system that took it: units that fight spend more of it attacking
        // than units that only march. One time is held against the other and not against a
        // number of milliseconds, which a slow or busy machine would not keep.
        Assert.True(attacking["fight"] > attacking["march"], $"AttackSystem took {attacking["fight"]} ms in the fight and {attacking["march"]} ms in the march.");
    }

    /// <summary>What the plan asks the benchmark to cover, in the words the scenarios report it.</summary>
    [Fact]
    public void TheScenariosReportWhatThePlanAsksFor()
    {
        var rows = Rows(Run("--quick").Output).ToDictionary(row => row.Name, StringComparer.Ordinal);

        Assert.Matches(@"^first unit through at tick \d+, last at \d+, at rest at \d+; at most \d+ units in the pass and \d+ on one tile; closest pair at rest [\d.]+ tiles$", rows["chokepoint"].Findings);
        Assert.Matches(@"^pass held by 3 enemy units: 0 of \d+ got through; closest enemy pair [\d.]+ tiles; pairs brought closer than their spacing: 0$", rows["chokepoint-held"].Findings);
        Assert.Matches(@"^two blocks across the map, at rest at tick \d+; closest pair at rest [\d.]+ tiles$", rows["march"].Findings);
        Assert.Matches(@"pairs brought closer than their spacing: 0$", rows["crossing"].Findings);
        Assert.Matches(@"^\d+ separate orders in one tick: \d+ route searches, the last unit waited \d+ ticks for its route$", rows["separate-orders"].Findings);
        Assert.Matches(@"^one search across a map of 96 by 96: \d+ tiles expanded, a route of \d+ tiles; the worst tick is the search$", rows["search-ridge"].Findings);
        Assert.Matches(@"^one search across a map of \d+ by \d+: \d+ tiles expanded, a route of \d+ tiles; the worst tick is the search$", rows["search-256"].Findings);

        Assert.Equal(1, rows["search-ridge"].Units);
        Assert.Equal(rows["chokepoint"].Units + 3, rows["chokepoint-held"].Units);
        Assert.All(rows.Values, row => Assert.True(row.Ticks > 0));
    }

    [Fact]
    public void OnlyTheScenariosNamedArePlayed()
    {
        var (exitCode, output, errors) = Run("--quick", "--scenario", "search-ridge", "--scenario", "chokepoint");

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Equal(["chokepoint", "search-ridge"], Rows(output).Select(row => row.Name));
    }

    [Theory]
    [InlineData("benchmark: there is no scenario 'nope'. The scenarios are: chokepoint, chokepoint-held, march, crossing, separate-orders, search-ridge, search-256, fight, siege, move-everything.", "--scenario", "nope")]
    [InlineData("benchmark: unknown or incomplete argument '--units'.", "--units")]
    [InlineData("benchmark: unknown or incomplete argument '--units'.", "--units", "19")]
    [InlineData("benchmark: unknown or incomplete argument '--units'.", "--units", "1001")]
    [InlineData("benchmark: unknown or incomplete argument '--units'.", "--units", "many")]
    [InlineData("benchmark: unknown or incomplete argument '--fast'.", "--fast")]
    [InlineData("benchmark: unknown or incomplete argument '--scenario'.", "--quick", "--scenario")]
    [InlineData("benchmark: unknown or incomplete argument '--data'.", "--data")]
    [InlineData("benchmark: there is no folder 'no/such/folder'.", "--data", "no/such/folder")]
    public void ArgumentsItCannotUseAreNamed(string expectedError, params string[] arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();

        var exitCode = ToolRunner.Run(["benchmark", .. arguments], output, errors);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains(expectedError, errors.ToString(), StringComparison.Ordinal);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void DataThatIsNotValidFails()
    {
        using var data = Repository.CopyOfData();
        File.Delete(Path.Combine(data.Path, "rules.jsonc"));

        var (exitCode, output, errors) = Run("--quick", "--data", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains("is not valid. Run datacheck on it.", errors, StringComparison.Ordinal);
        Assert.Equal("", output);
    }

    [Fact]
    public void TheBudgetIsThePlans()
    {
        Assert.Equal(8, BenchmarkTool.AverageBudget);
        Assert.Equal(25, BenchmarkTool.WorstBudget);
        Assert.Equal("game/data", BenchmarkTool.DefaultFolder);
    }

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        string[] folder = arguments.Contains("--data") ? [] : ["--data", Repository.DataFolder];
        var exitCode = ToolRunner.Run(["benchmark", .. folder, .. arguments], output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }

    /// <summary>The rows of the table: the lines between its heading and the empty line after it.</summary>
    private static List<Row> Rows(string output)
    {
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var heading = Array.FindIndex(lines, line => line.StartsWith("scenario ", StringComparison.Ordinal));
        Assert.True(heading >= 0, "The output has no table.");

        var rows = new List<Row>();
        for (var i = heading + 1; i < lines.Length && lines[i].Length > 0; i++)
        {
            // Name, units, ticks, the average, the high and the worst tick, bytes, the state at the end, and then what the scenario shows.
            var parts = lines[i].Split(' ', 9, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(9, parts.Length);
            var (average, high, worst) = (Number(parts[3]), Number(parts[4]), Number(parts[5]));
            Assert.True(average >= 0 && high >= 0);
            Assert.True(worst >= average && worst >= high);
            Assert.True(Number(parts[6]) >= 0);
            rows.Add(new Row(
                parts[0],
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture),
                parts[7],
                parts[8]));
        }

        return rows;
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    private sealed record Row(string Name, int Units, int Ticks, string EndState, string Findings);
}
