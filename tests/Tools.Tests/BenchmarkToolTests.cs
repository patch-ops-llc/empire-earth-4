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
        "wall-breach",
        "wall-breach-apart",
        "gates",
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

        Assert.Equal(first.Select(row => (row.Name, row.Units, row.Ticks, row.Findings)), second.Select(row => (row.Name, row.Units, row.Ticks, row.Findings)));
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

        // Walls and gates (design 2.5): no more searches for a wall in the tick of the orders than
        // the orders of one player may run and the attacks of a tick may add, however many orders.
        const string Breach = @"every unit had its wall at tick \d+; first hit on it at tick \d+, first tile down at \d+, \d+ down in all; first unit inside at \d+, (\d+) of \1 inside and at rest at \d+";
        Assert.Matches("^one order into a closed camp: 1 searches for a wall in that tick, " + Breach + "$", rows["wall-breach"].Findings);
        Assert.Matches(@"^\d+ orders in one tick into a closed camp: (?:\d|1\d) searches for a wall in that tick, " + Breach + "$", rows["wall-breach-apart"].Findings);
        Assert.Matches(
            "^one order into a closed camp: 1 searches for a wall in that tick, " + Breach + @"; each player has a gate, and the camp's \d+ workers were in through theirs at tick \d+$",
            rows["gates"].Findings);

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
    [InlineData("benchmark: there is no scenario 'nope'. The scenarios are: chokepoint, chokepoint-held, march, crossing, separate-orders, search-ridge, search-256, wall-breach, wall-breach-apart, gates.", "--scenario", "nope")]
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
            // Name, units, ticks, average, worst, bytes, and then what the scenario shows.
            var parts = lines[i].Split(' ', 7, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(7, parts.Length);
            Assert.True(double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) >= 0);
            Assert.True(double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) >= double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(double.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture) >= 0);
            rows.Add(new Row(
                parts[0],
                int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                parts[6]));
        }

        return rows;
    }

    private sealed record Row(string Name, int Units, int Ticks, string Findings);
}
