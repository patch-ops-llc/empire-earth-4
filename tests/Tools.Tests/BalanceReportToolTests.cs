using System.Globalization;
using Sim.Data;
using Sim.Numerics;
using Sim.Tech;
using Tools.BalanceReport;

namespace Tools.Tests;

/// <summary>
/// Runs <c>balance-report</c> the way the command line does, on the real <c>game/data</c>. The tests
/// check the report against the resolver and not against fixed numbers, so tuning the data does not
/// break them; the numbers themselves are tested against the game design in <c>Sim.Tests.Tech</c>.
/// </summary>
public class BalanceReportToolTests
{
    private const string Swarm = "1,1,2,4,4,2";
    private const string Elite = "4,4,2,1,1,2";

    private static readonly GameData Data = DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!;

    [Fact]
    public void WithoutArgumentsItReportsTheStartOfAMatchInThreeTables()
    {
        var (exitCode, output, errors) = Run();

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var tables = Tables(output);
        Assert.Equal(3, tables.Count);

        Assert.Equal("mix,firepower,protection,mobility,manufacturing,logistics,extraction,epoch,epoch_name,supply_cap,income_multiplier", tables[0][0]);
        var start = Data.Rules.Match.StartEpoch;
        var tech = TechResolver.Resolve(Data, Enumerable.Repeat(start, Data.Tracks.Count).ToArray());
        Assert.Equal(
            [$"start,{start},{start},{start},{start},{start},{start},{tech.Epoch},{Data.EpochNames[tech.Epoch - 1]},{tech.SupplyCap},{ReportFormat.Rounded(tech.IncomeMultiplier, 4)}"],
            tables[0].Skip(1));

        Assert.Equal("mix,track,tier,status,band,materials,energy,seconds", tables[1][0]);
        Assert.Equal(Data.Tracks.Select(track => $"start,{track.Id},"), tables[1].Skip(1).Select(row => row[..(row.IndexOf(',', 6) + 1)]));

        Assert.StartsWith("mix,line,chassis,epoch,unlocked,offset_firepower,", tables[2][0], StringComparison.Ordinal);
        Assert.Equal(Data.Chassis.Count, tables[2].Count - 1);
    }

    [Fact]
    public void EveryChassisRowIsTheBlueprintTheResolverGives()
    {
        var (exitCode, output, _) = Run("--table", "chassis", $"swarm={Swarm}", $"elite={Elite}");

        Assert.Equal(ExitCodes.Ok, exitCode);
        var rows = Tables(output).Single();
        var header = rows[0].Split(',');
        var expected = new List<string>();
        foreach (var (name, tiers) in new[] { ("swarm", Tiers(Swarm)), ("elite", Tiers(Elite)) })
        {
            var tech = TechResolver.Resolve(Data, tiers);
            foreach (var line in Data.Lines)
            {
                foreach (var chassis in line.Chassis)
                {
                    var blueprint = tech.Blueprints[chassis.Index];
                    expected.Add(string.Join(
                        ",",
                        name,
                        line.Id,
                        chassis.Id,
                        chassis.Epoch,
                        blueprint.Unlocked ? "yes" : "no",
                        string.Join(",", blueprint.Offsets),
                        string.Join(" + ", blueprint.Accents.Select(accent => $"{Data.AccentOf(accent.Track)!.Name} {(accent.Level == 1 ? "I" : "II")}")),
                        ReportFormat.Thousandths(blueprint.HitPoints),
                        ReportFormat.Thousandths(blueprint.DamagePerSecond),
                        ReportFormat.Rounded(blueprint.DamagePerHit, 3),
                        blueprint.Attack.Kind == AttackKind.Melee ? "melee" : "ranged",
                        ReportFormat.Seconds(blueprint.Attack.IntervalTicks),
                        ReportFormat.Tiles(blueprint.Range),
                        ReportFormat.Tiles(blueprint.MinRange),
                        ReportFormat.Tiles(blueprint.BlastRadius),
                        ReportFormat.Rounded(blueprint.SpeedPerTick.ToFraction() * 20, 3),
                        blueprint.Sight,
                        blueprint.Supply,
                        blueprint.Materials,
                        blueprint.Energy,
                        ReportFormat.Seconds(blueprint.BuildTicks),
                        blueprint.BatchSize,
                        ReportFormat.Seconds(blueprint.BatchBuildTicks)));
                }
            }
        }

        Assert.Equal(expected, rows.Skip(1));
        Assert.All(rows, row => Assert.Equal(header.Length, row.Split(',').Length));
        Assert.Equal(
            [
                "mix", "line", "chassis", "epoch", "unlocked",
                "offset_firepower", "offset_protection", "offset_mobility", "offset_manufacturing", "offset_logistics", "offset_extraction",
                "accents", "hit_points", "damage_per_second", "damage_per_hit", "attack", "attack_interval_seconds",
                "range", "min_range", "blast_radius", "speed", "sight", "supply", "materials", "energy",
                "build_seconds", "batch_size", "batch_build_seconds",
            ],
            header);
    }

    [Fact]
    public void EveryResearchRowIsTheOfferTheResolverGives()
    {
        var (exitCode, output, _) = Run("--table", "research", $"swarm={Swarm}");

        Assert.Equal(ExitCodes.Ok, exitCode);
        var tech = TechResolver.Resolve(Data, Tiers(Swarm));
        var expected = tech.Research.Select(offer => offer.Price is { } price
            ? $"swarm,{Data.Tracks[offer.Track].Id},{offer.Tier},available,{BandText(price.Band)},{price.Materials},{price.Energy},{ReportFormat.Seconds(price.Ticks)}"
            : $"swarm,{Data.Tracks[offer.Track].Id},{offer.Tier},{(offer.Status == ResearchStatus.AboveEpochCap ? "above_epoch_cap" : "beyond_reach")},,,,");

        Assert.Equal(expected, Tables(output).Single().Skip(1));
    }

    [Fact]
    public void AMixWithoutANameIsNamedByItsTiers()
    {
        var (exitCode, output, _) = Run("--table", "mixes", Swarm, $"elite={Elite}");

        Assert.Equal(ExitCodes.Ok, exitCode);
        var rows = Tables(output).Single();
        Assert.StartsWith("1/1/2/4/4/2,1,1,2,4,4,2,", rows[1], StringComparison.Ordinal);
        Assert.StartsWith("elite,4,4,2,1,1,2,", rows[2], StringComparison.Ordinal);
    }

    [Fact]
    public void TheEpochCapOfTheReportCanBeSet()
    {
        var cap = Data.Rules.Match.EpochCap;
        Assert.True(cap < Data.TierCount, "This test needs data whose epoch cap is below the last tier.");
        var top = string.Join(",", Enumerable.Repeat(cap, Data.Tracks.Count));

        var (_, capped, _) = Run("--table", "research", top);
        var (exitCode, open, errors) = Run("--table", "research", "--epoch-cap", $"{cap + 1}", top);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.All(Tables(capped).Single().Skip(1), row => Assert.Contains($",{cap + 1},above_epoch_cap,", row, StringComparison.Ordinal));
        Assert.All(Tables(open).Single().Skip(1), row => Assert.Contains($",{cap + 1},available,frontier,", row, StringComparison.Ordinal));
    }

    [Fact]
    public void ATrackAtItsLastTierHasNoNextTier()
    {
        var last = Data.TierCount;
        var top = string.Join(",", Enumerable.Repeat(last, Data.Tracks.Count));

        var (exitCode, output, _) = Run("--table", "research", "--epoch-cap", $"{last}", top);

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.All(Tables(output).Single().Skip(1), row => Assert.EndsWith(",,at_top_tier,,,,", row, StringComparison.Ordinal));
    }

    [Fact]
    public void TheReportReadsTheFolderItIsGiven()
    {
        using var data = Repository.CopyOfData();
        data.Edit("rules.jsonc", "\"base\": 40", "\"base\": 41");

        var (exitCode, output, errors) = Run("--data", data.Path, "--table", "mixes");

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var expected = TechResolver.Resolve(Data, Enumerable.Repeat(Data.Rules.Match.StartEpoch, Data.Tracks.Count).ToArray()).SupplyCap + 1;
        Assert.Contains($",{expected},", Tables(output).Single()[1], StringComparison.Ordinal);
    }

    [Fact]
    public void DataWithErrorsIsNotReportedOn()
    {
        using var data = Repository.CopyOfData();
        data.Edit("lines/guard.jsonc", "\"hp\": 130", "\"hp\": 0");

        var (exitCode, output, errors) = Run("--data", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal("", output);
        Assert.Contains($"has 1 error; run datacheck {data.Path} to list them.", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1,1,2,4,4", "the mix '1,1,2,4,4' needs 6 tiers, one per track in the order firepower, protection, mobility, manufacturing, logistics, extraction; found 5")]
    [InlineData("1,1,2,4,4,2,1", "needs 6 tiers")]
    [InlineData("1,1,2,4,4,x", "in the mix '1,1,2,4,4,x' the tier of extraction must be a whole number from 1 to 9, found 'x'")]
    [InlineData("0,1,2,4,4,2", "the tier of firepower must be a whole number from 1 to 9, found '0'")]
    [InlineData("1,1,2,4,4,10", "the tier of extraction must be a whole number from 1 to 9, found '10'")]
    [InlineData("1,1,2,4,4,+2", "the tier of extraction must be a whole number from 1 to 9, found '+2'")]
    [InlineData("1, 1,2,4,4,2", "the tier of protection must be a whole number from 1 to 9, found ' 1'")]
    [InlineData("=1,1,2,4,4,2", "the mix '=1,1,2,4,4,2' has no name before '='")]
    [InlineData("--tables", "unknown option '--tables'")]
    [InlineData("--table", "--table needs a value")]
    public void AnArgumentItCannotReadIsAUsageError(string argument, string message)
    {
        var (exitCode, output, errors) = Run(argument);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.StartsWith("balance-report: ", errors, StringComparison.Ordinal);
        Assert.Contains(message, errors, StringComparison.Ordinal);
        Assert.Contains("Usage: balance-report ", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--table", "units", "unknown table 'units'; use mixes, research, chassis")]
    [InlineData("--epoch-cap", "0", "--epoch-cap must be a tier from 1 to 9, found '0'")]
    [InlineData("--epoch-cap", "ten", "--epoch-cap must be a tier from 1 to 9, found 'ten'")]
    [InlineData("a=1,1,1,1,1,1", "a=2,2,2,2,2,2", "two mixes are named 'a'")]
    public void TwoArgumentsThatDoNotFitAreAUsageError(string first, string second, string message)
    {
        var (exitCode, output, errors) = Run(first, second);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains($"balance-report: {message}", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatDoesNotExistIsAUsageError()
    {
        var (exitCode, output, errors) = Run("--data", Path.Combine(Repository.Root, "no-such-folder"));

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains("there is no folder", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(317_383, "317.383")]
    [InlineData(75_000, "75")]
    [InlineData(7_500, "7.5")]
    [InlineData(23_438, "23.438")]
    [InlineData(5, "0.005")]
    [InlineData(0, "0")]
    [InlineData(-1_250, "-1.25")]
    public void ThousandthsAreWrittenWithoutTrailingZeros(long thousandths, string text)
    {
        Assert.Equal(text, ReportFormat.Thousandths(thousandths));
    }

    [Theory]
    [InlineData(154, "7.7")]
    [InlineData(563, "28.15")]
    [InlineData(20, "1")]
    [InlineData(1, "0.05")]
    [InlineData(750, "37.5")]
    public void TicksAreWrittenAsSeconds(int ticks, string text)
    {
        Assert.Equal(text, ReportFormat.Seconds(ticks));
    }

    [Fact]
    public void FractionsAndDistancesAreRoundedHalfUp()
    {
        Assert.Equal("93.75", ReportFormat.Rounded(new Fraction(375, 4), 3));
        Assert.Equal("0.667", ReportFormat.Rounded(new Fraction(2, 3), 3));
        Assert.Equal("2.4760", ReportFormat.Rounded(new Fraction(28, 25).Pow(8), 4) + "0");
        Assert.Equal("3", ReportFormat.Rounded(new Fraction(5, 2), 0));
        Assert.Equal("2.25", ReportFormat.Tiles(Fixed.FromRaw(147_456)));
        Assert.Equal("6.9", ReportFormat.Tiles(Fixed.FromRaw(452_198)));
        Assert.Equal("", ReportFormat.Tiles(null));
    }

    [Fact]
    public void AFieldWithACommaOrAQuoteIsQuoted()
    {
        Assert.Equal("a,\"b, c\",\"say \"\"d\"\"\",e", ReportFormat.Row(["a", "b, c", "say \"d\"", "e"]));
    }

    /// <summary>Runs the tool on the real <c>game/data</c> unless the arguments name another folder.</summary>
    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        string[] folder = arguments.Contains("--data") ? [] : ["--data", Repository.DataFolder];
        var exitCode = ToolRunner.Run(["balance-report", .. folder, .. arguments], output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }

    /// <summary>The tables of a report, each as its lines. Tables are separated by one empty line.</summary>
    private static List<List<string>> Tables(string output)
    {
        var tables = new List<List<string>> { new() };
        foreach (var line in output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
        {
            if (line.Length == 0)
            {
                tables.Add([]);
            }
            else
            {
                tables[^1].Add(line);
            }
        }

        return tables;
    }

    private static int[] Tiers(string mix) => [.. mix.Split(',').Select(tier => int.Parse(tier, CultureInfo.InvariantCulture))];

    private static string BandText(ResearchBand band) => band switch
    {
        ResearchBand.CatchUp => "catch_up",
        ResearchBand.Frontier => "frontier",
        _ => "ahead",
    };
}
