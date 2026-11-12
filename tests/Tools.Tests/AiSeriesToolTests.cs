using Sim.Data;
using Tools.AiMatch;
using Tools.AiSeries;

namespace Tools.Tests;

/// <summary>
/// The series of headless matches: which matches it plays, and what it reports of them. The
/// matches here are a minute or two of game time; what a whole match comes to is for the
/// balance report, not for a test.
/// </summary>
public class AiSeriesToolTests
{
    private const int OneMinute = 60 * Sim.SimInfo.TicksPerSecond;

    private static readonly Lazy<GameData> Loaded = new(() => DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!);

    private static readonly Lazy<IReadOnlyList<SeriesMatch>> OneMinuteEach = new(() => Series.Play(Data, [Ridge], Personalities, [2, 3], OneMinute, jobs: 4));

    private static GameData Data => Loaded.Value;

    private static MapDef Ridge => Data.Maps.Single(map => map.Id == "ridge");

    private static List<AiPersonalityDef> Personalities => [Data.PersonalityOf("swarm")!, Data.PersonalityOf("elite")!];

    /// <summary>Every pairing, itself too, from both seats, with the starts both ways round, at every delay, in a fixed order.</summary>
    [Fact]
    public void ASeriesPlaysEveryPairingFromBothSeatsAndBothStartsAtEveryDelay()
    {
        var matches = OneMinuteEach.Value;

        Assert.Equal(
            [
                "swarm@0 swarm@1 2", "swarm@0 swarm@1 3", "swarm@1 swarm@0 2", "swarm@1 swarm@0 3",
                "swarm@0 elite@1 2", "swarm@0 elite@1 3", "swarm@1 elite@0 2", "swarm@1 elite@0 3",
                "elite@0 swarm@1 2", "elite@0 swarm@1 3", "elite@1 swarm@0 2", "elite@1 swarm@0 3",
                "elite@0 elite@1 2", "elite@0 elite@1 3", "elite@1 elite@0 2", "elite@1 elite@0 3",
            ],
            matches.Select(match => $"{string.Join(" ", match.Result.Seats.Select(seat => $"{seat.Personality}@{seat.Start}"))} {match.CommandDelay}"));
        Assert.All(matches, match =>
        {
            Assert.Same(Ridge, match.Map);
            Assert.Equal((OneMinute, MatchEnd.TimeLimit, match.CommandDelay), (match.Result.Ticks, match.Result.Ended, match.Result.CommandDelay));
            Assert.Equal([0, 1], match.Result.Seats.Select(seat => seat.Player));
            Assert.Null(match.Decided);
            Assert.Null(match.LoserBehindFromTick);
        });
    }

    /// <summary>The matches are played side by side, and each comes to the same as when it is played alone.</summary>
    [Fact]
    public void HowManyMatchesArePlayedSideBySideChangesNothing()
    {
        var called = 0;

        var alone = Series.Play(Data, [Ridge], Personalities, [2, 3], OneMinute, jobs: 1, () => called++);

        Assert.Equal(16, called);
        Assert.Equal(OneMinuteEach.Value.Select(match => match.Result.StateHash), alone.Select(match => match.Result.StateHash));
    }

    [Fact]
    public void ASeriesNeedsAMapWithTwoStartsATimeLimitAndAThread()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Series.Play(Data, [Ridge], Personalities, [2], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Series.Play(Data, [Ridge], Personalities, [2], 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Series.Play(Data, [Ridge with { Starts = [Ridge.Starts[0]] }], Personalities, [2], 100, 1));
        Assert.Empty(Series.Play(Data, [], Personalities, [2], 100, 1));
    }

    /// <summary>
    /// The player who lost was behind for good from the last sampled moment at which it still had
    /// half the winner's army value.
    /// </summary>
    [Fact]
    public void TheLoserIsBehindFromTheLastMomentItHadHalfTheWinnersArmy()
    {
        var winner = Seat(0, [100, 300, 400, 500]);
        var loser = Seat(1, [100, 200, 199, 0]);

        var match = new SeriesMatch(Ridge, 2, Result(winner: 0, winner, loser));
        var other = new SeriesMatch(Ridge, 2, Result(winner: 1, loser with { Player = 0 }, winner with { Player = 1 }));
        var never = new SeriesMatch(Ridge, 2, Result(winner: 0, Seat(0, [100, 100]), Seat(1, [0, 0])));
        var open = new SeriesMatch(Ridge, 2, Result(winner: null, winner, loser));

        Assert.Equal(400, match.LoserBehindFromTick);
        Assert.Equal((winner, loser), match.Decided);
        Assert.Equal(400, other.LoserBehindFromTick);
        Assert.Equal(1, other.Decided!.Value.Winner.Player);
        Assert.Equal(0, never.LoserBehindFromTick);
        Assert.Null(open.LoserBehindFromTick);

    }

    /// <summary>What the summary and the file say of matches with a winner: who won from which seat and start, and how long the loser went on.</summary>
    [Fact]
    public void TheSummaryAndTheFileSayWhoWonFromWhere()
    {
        var elite = Seat(0, [100, 300, 400, 500], "elite") with { Start = 1, FirstAttackOrderTick = 600, ResearchMaterials = [0, 0, 0, 300, 0, 0], ResearchEnergy = [0, 0, 0, 100, 0, 0] };
        var swarm = Seat(1, [100, 200, 199, 0], "swarm") with { Start = 0, OutTick = 1000, Workers = 4, TicksAtSupplyCap = 100, TicksAboveSupplyCap = 40, MostAboveSupplyCap = 3 };
        SeriesMatch[] matches =
        [
            new(Ridge, 2, Result(winner: 0, elite, swarm)),
            new(Ridge, 3, Result(winner: 1, swarm with { Player = 0 }, elite with { Player = 1 }) with { Ticks = 2000, FirstHitTick = 700 }),
        ];
        var summary = new StringWriter();
        var file = new StringWriter { NewLine = "\n" };

        SeriesReport.Write(summary, Data, matches);
        SeriesReport.WriteCsv(file, Data, matches);

        var lines = summary.ToString().Split(Environment.NewLine);
        Assert.Contains("  by conquest, with a winner        2", lines);
        Assert.Contains("  length of those that ended by conquest: 0:50 (0:50 to 1:40)", lines);
        Assert.Contains("  first hit of a match: 0:35 (0:35 to 0:35)", lines);
        Assert.Contains("  ridge  elite     swarm     1        1             0             0          0:50 (0:50 to 0:50)", lines);
        Assert.Contains("  ridge  swarm     elite     1        0             1             0          1:40 (1:40 to 1:40)", lines);
        Assert.Contains("  elite      2 of 2 (100%)", lines);
        Assert.Contains("  swarm      0 of 2 (0%)", lines);
        Assert.Contains("  ridge  player 0 1 of 2 (50%)  player 1 1 of 2 (50%)  start 0 0 of 2 (0%)  start 1 2 of 2 (100%)", lines);
        Assert.Matches(@"^  elite        2      3:20 \(3:20 to 3:20\), 2 of 2  never    never    never +0:30 \(0:30 to 0:30\), 2 of 2$", lines.Single(line => line.StartsWith("  elite        2", StringComparison.Ordinal)));
        Assert.Contains("  firepower      0 (0%)      0 (0%)", lines);
        Assert.Contains("  manufacturing  400 (100%)  0 (0%)", lines);
        Assert.Contains("  swarm  at the cap in 2 of 2 (100%) seats, for 0:05 (0:05 to 0:05)  above it in 2 of 2 (100%) seats, by at most 3, for 0:02 (0:02 to 0:02)", lines);
        Assert.Contains("  from the last moment the loser had half the winner's army value to the end: 0:30 (0:30 to 1:20)", lines);
        Assert.Contains("  workers the loser still had at the end: 4 (4 to 4)", lines);

        var rows = file.ToString().Split('\n');
        Assert.StartsWith("1,ridge,2,conquest,50,elite,,20,0,0,0000000000000000,0,1,elite,swarm,won,,200,,,,30,", rows[1], StringComparison.Ordinal);
        Assert.StartsWith("1,ridge,2,conquest,50,elite,,20,0,0,0000000000000000,1,0,swarm,elite,lost,50,200,,,,,", rows[2], StringComparison.Ordinal);
        Assert.StartsWith("2,ridge,3,conquest,100,elite,35,20,0,0,0000000000000000,0,0,swarm,elite,lost,50,", rows[3], StringComparison.Ordinal);
        Assert.StartsWith("2,ridge,3,conquest,100,elite,35,20,0,0,0000000000000000,1,1,elite,swarm,won,,", rows[4], StringComparison.Ordinal);
        Assert.Contains(",2,2,2,2,2,2,0,0,0,300,0,0,0,0,0,100,0,0,0,40,0,0,0,", rows[1], StringComparison.Ordinal);
        Assert.EndsWith(",0,40,5,2,3,4,0,0,0,0,0,0", rows[2], StringComparison.Ordinal);
    }

    private static SeatResult Seat(int player, int[] armyValues, string personality = "swarm") => new(
        player, personality, player, 2, [4000], [2, 2, 2, 2, 2, 2], 0, 40, 0, 0, 0, [], [], 0, 0, 0, 0, -1, -1, -1, [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0], 0, 0, 0,
        [.. armyValues.Select((value, index) => new SeatSample((index + 1) * SeatWatch.SampleEvery, 0, 0, value, 0, 40, 0, 0))]);

    private static HeadlessResult Result(int? winner, params SeatResult[] seats) =>
        new(1000, winner is null ? MatchEnd.TimeLimit : MatchEnd.Conquest, winner, 2, -1, seats, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void TheSpreadIsTheMiddleTheLeastAndTheMost()
    {
        Assert.Null(Series.Spread([]));
        Assert.Equal((7, 7, 7), Series.Spread([7]));
        Assert.Equal((3, 1, 9), Series.Spread([9, 1, 3]));
        Assert.Equal((3, 1, 9), Series.Spread([9, 1, 4, 3]));
    }

    [Fact]
    public void TheTickOfAnEpochIsReadFromTheEpochsASeatReached()
    {
        var seat = OneMinuteEach.Value[0].Result.Seats[0] with { Epoch = 3, EpochTicks = [4000, 9000] };

        Assert.Equal([-1, 4000, 9000, -1], Enumerable.Range(1, 4).Select(epoch => Series.EpochTick(seat, epoch)));
        Assert.Equal("7:30", Series.Clock(9000));
        Assert.Equal("0:09", Series.Clock(199));
    }

    /// <summary>The tool the way the command line runs it: the summary, and the two files.</summary>
    [Fact]
    public void TheToolPlaysTheSeriesAndWritesTheSummaryAndTheFiles()
    {
        using var folder = Repository.CopyOfData();
        var csv = Path.Combine(folder.Path, "out", "series.csv");
        var samples = Path.Combine(folder.Path, "out", "samples.csv");

        var (exitCode, output, errors) = Run("--data", Repository.DataFolder, "--delays", "2,5", "--minutes", "4", "--csv", csv, "--samples", samples);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var lines = output.Split(Environment.NewLine);
        Assert.Equal(
            $"ai-series: 16 matches between computer players with the data in {Repository.DataFolder}: on ridge; between elite, swarm, every pairing from both seats and with the starts both ways round; at command delays of 2, 5 ticks; at most 4 minutes of game time each.",
            lines[0]);
        Assert.StartsWith("Nothing in a match is random, so a seed changes nothing", lines[1], StringComparison.Ordinal);
        Assert.Contains("  by conquest, with a winner        0", lines);
        Assert.Contains("  at the time limit                16", lines);
        Assert.Contains("  length of those that ended by conquest: none", lines);
        Assert.Contains("  first hit of a match: none", lines);
        Assert.Contains("  map    player 0  player 1  matches  player 0 won  player 1 won  no winner  length by conquest", lines);
        Assert.Contains("  ridge  elite     swarm     4        0             0             4          none", lines);
        Assert.Contains("  ridge  swarm     swarm     4        0             0             4          none", lines);
        Assert.Contains("  elite      0 of 8 (0%)", lines);
        Assert.Contains("  no winner  8 of 8 (100%)", lines);
        Assert.Contains("  ridge  player 0 0 of 16 (0%)  player 1 0 of 16 (0%)  start 0 0 of 16 (0%)  start 1 0 of 16 (0%)", lines);
        Assert.Contains("  personality  seats  epoch 2", lines.Single(line => line.Contains("first attack order", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Matches(@"^  swarm        16     3:\d\d \(3:\d\d to 3:\d\d\), 16 of 16  never    never    never +never$", lines.Single(line => line.StartsWith("  swarm        16", StringComparison.Ordinal)));
        Assert.Matches(@"^  manufacturing  [\d,]+ \(\d+%\) +[\d,]+ \(\d+%\)$", lines.Single(line => line.StartsWith("  manufacturing", StringComparison.Ordinal)));
        Assert.Contains("  swarm  at the cap in 0 of 16 (0%) seats  above it in 0 of 16 (0%) seats", lines);
        Assert.Contains("  from the last moment the loser had half the winner's army value to the end: none", lines);
        Assert.Equal($"ai-series: one line per seat of every match is in {csv}.", lines[^3]);
        Assert.Equal($"ai-series: what each seat had every ten seconds is in {samples}.", lines[^2]);

        var rows = File.ReadAllLines(csv);
        Assert.Equal(1 + 32, rows.Length);
        Assert.StartsWith("match,map,command_delay,ended,length_s,winner,first_hit_s,loser_behind_from_s,commands_given,commands_dropped,state_hash,player,start,personality,opponent,result,out_s,epoch_2_s,epoch_3_s,epoch_4_s,every_track_at_cap_s,first_attack_order_s,tier_firepower,", rows[0], StringComparison.Ordinal);
        Assert.EndsWith(",supply_used,supply_cap,at_cap_s,above_cap_s,most_above_cap,workers,units,army_value,units_lost,structures_lost,materials,energy", rows[0], StringComparison.Ordinal);
        Assert.All(rows, row => Assert.Equal(rows[0].Split(',').Length, row.Split(',').Length));
        Assert.Matches(@"^1,ridge,2,time_limit,240,,,,\d+,0,[0-9a-f]{16},0,0,elite,elite,none,,2\d\d(\.\d+)?,,,,,", rows[1]);
        Assert.Matches(@"^16,ridge,5,time_limit,240,,,,\d+,0,[0-9a-f]{16},1,0,swarm,swarm,none,", rows[^1]);

        var moments = File.ReadAllLines(samples);
        Assert.Equal("match,map,command_delay,player,start,personality,time_s,workers,units,army_value,supply_used,supply_cap,materials,energy", moments[0]);
        Assert.Equal(1 + (16 * 2 * 24), moments.Length);
        Assert.StartsWith("1,ridge,2,0,0,elite,10,", moments[1], StringComparison.Ordinal);
        Assert.StartsWith("16,ridge,5,1,0,swarm,240,", moments[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheMapsAndThePersonalitiesCanBeChosen()
    {
        var (exitCode, output, _) = Run("--data", Repository.DataFolder, "--maps", "ridge", "--personalities", "swarm", "--minutes", "1", "--jobs", "1");

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.StartsWith("ai-series: 2 matches between computer players", output, StringComparison.Ordinal);
        Assert.Contains("on ridge; between swarm, every pairing", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Wins by personality", output, StringComparison.Ordinal);
        Assert.DoesNotContain("is in ", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--minutes", "0")]
    [InlineData("--minutes", "1,2")]
    [InlineData("--delays", "0")]
    [InlineData("--delays", "2,21")]
    [InlineData("--delays", "2,,3")]
    [InlineData("--jobs", "0")]
    [InlineData("--csv")]
    [InlineData("--seed", "3")]
    public void AnArgumentItDoesNotKnowIsAnErrorOfUsage(params string[] arguments)
    {
        var (exitCode, output, errors) = Run(["--data", Repository.DataFolder, .. arguments]);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains("Usage:", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("has no map 'atlantis'. Its maps are: ", "--maps", "ridge,atlantis")]
    [InlineData("has no personality 'turtle'. Its personalities are: elite, swarm.", "--personalities", "swarm,turtle")]
    public void ASeriesThatCannotBePlayedAsAskedIsAnErrorOfUsage(string message, params string[] arguments)
    {
        var (exitCode, output, errors) = Run(["--data", Repository.DataFolder, .. arguments]);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains(message, errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatIsNotThereIsAnErrorOfUsageAndDataThatIsNotValidFailsTheTool()
    {
        using var folder = Repository.CopyOfData();
        folder.Edit("ai/swarm.jsonc", "\"id\": \"swarm\"", "\"id\": \"Swarm!\"");

        var missing = Run("--data", Path.Combine(Repository.Root, "no-such-folder"));
        var broken = Run("--data", folder.Path);

        Assert.Equal(ExitCodes.Usage, missing.ExitCode);
        Assert.Contains("there is no folder", missing.Errors, StringComparison.Ordinal);
        Assert.Equal((ExitCodes.Failed, ""), (broken.ExitCode, broken.Output));
        Assert.Contains("is not valid. Run datacheck on it.", broken.Errors, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var exitCode = ToolRunner.Run(["ai-series", .. arguments], output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }
}
