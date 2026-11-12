using System.Text.RegularExpressions;
using Sim.Data;
using Sim.Setup;
using Tools.AiMatch;

namespace Tools.Tests;

/// <summary>
/// The headless match: computer players against each other with nobody watching, measured from
/// outside. The matches here are a few minutes of game time, long enough for the second epoch.
/// </summary>
/// <remarks>
/// How well a computer player plays is tested with the computer player. These tests are about
/// what the tool measures and reports.
/// </remarks>
public class AiMatchToolTests
{
    private const int FourMinutes = 4 * 60 * Sim.SimInfo.TicksPerSecond;

    private static readonly Lazy<GameData> Loaded = new(() => DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!);

    private static readonly Lazy<HeadlessResult> FourMinutesOnRidge = new(() => Play("swarm", "elite"));

    private static GameData Data => Loaded.Value;

    private static MapDef Ridge => Data.Maps.Single(map => map.Id == "ridge");

    [Fact(Skip = ReplayScripts.TestBuild.HeadlessMatchMeetsTheScout)]
    public void AMatchRunsToTheTimeLimitAndSaysWhatEachPlayerBuilt()
    {
        var result = FourMinutesOnRidge.Value;

        Assert.Equal(FourMinutes, result.Ticks);
        Assert.Equal(240.0, result.GameSeconds);
        Assert.Null(result.Winner);
        Assert.Equal(0, result.CommandsDropped);
        Assert.True(result.CommandsGiven > 20, $"{result.CommandsGiven} commands in four minutes.");
        Assert.Equal(["swarm", "elite"], result.Seats.Select(seat => seat.Personality));
        Assert.Equal([(0, 0), (1, 1)], result.Seats.Select(seat => (seat.Player, seat.Start)));
        Assert.All(result.Seats, seat =>
        {
            // Both personalities reach the second epoch a little after three minutes.
            Assert.Equal(2, seat.Epoch);
            var reached = Assert.Single(seat.EpochTicks);
            Assert.InRange(reached, 1, FourMinutes);
            Assert.Equal(2, seat.Tiers.Max());
            Assert.True(seat.Workers > Data.Rules.Start.Workers, $"{seat.Personality} has {seat.Workers} workers.");
            Assert.InRange(seat.SupplyUsed, seat.Workers, seat.SupplyCap);
            Assert.Contains(("capitol", 1), seat.Structures);
            Assert.True(seat.Structures.Count > 1, $"{seat.Personality} built nothing.");
            Assert.Equal(seat.Units, seat.UnitsByChassis.Sum(entry => entry.Count));
            Assert.Equal(seat.Units == 0, seat.ArmyValue == 0);
            Assert.Equal((0, 0), (seat.UnitsLost, seat.StructuresLost));
            Assert.True(seat.Materials >= 0 && seat.Energy >= 0);
        });
        Assert.True(result.TickSeconds > 0 && result.WorstTickSeconds > 0 && result.ThinkSeconds > 0);
        Assert.True(result.WorstTickSeconds <= result.TickSeconds);
    }

    /// <summary>Nothing is random beyond the seed, so the same match ends in the same state after the same commands.</summary>
    [Fact]
    public void TheSameMatchEndsInTheSameState()
    {
        var first = FourMinutesOnRidge.Value;
        var second = Play("swarm", "elite");

        Assert.Equal((first.StateHash, first.CommandsGiven), (second.StateHash, second.CommandsGiven));
        Assert.Equal(first.Seats.Select(Summary), second.Seats.Select(Summary));

        static string Summary(SeatResult seat) =>
            $"{seat.Epoch} {string.Join(",", seat.EpochTicks)} {string.Join(",", seat.Tiers)} {seat.SupplyUsed} {seat.Workers} {seat.Units} {seat.ArmyValue} {seat.Materials} {seat.Energy} {string.Join(",", seat.Structures)}";
    }

    /// <summary>The other way round is another match: the seats and the starts go with the personalities.</summary>
    [Fact]
    public void TheOtherWayRoundIsAnotherMatch()
    {
        var first = FourMinutesOnRidge.Value;
        var second = Play("elite", "swarm");

        Assert.Equal(["elite", "swarm"], second.Seats.Select(seat => seat.Personality));
        Assert.NotEqual(first.StateHash, second.StateHash);
    }

    /// <summary>
    /// The match ends as soon as only one player has anything on the map, and that player has
    /// won. Here everything of the second player falls in the first tick.
    /// </summary>
    [Fact]
    public void TheMatchEndsWhenOnlyOnePlayerHasAnythingLeft()
    {
        var match = MatchSetup.Create(Data, Ridge, 1, [new PlayerSetup(0), new PlayerSetup(1)]);
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            if (match.Bodies.ValueAt(i).Owner == 1)
            {
                match.Health.Get(match.Bodies.EntityAt(i)).HitPoints = 0;
            }
        }

        var result = HeadlessMatch.Play(match, [Data.PersonalityOf("swarm")!, Data.PersonalityOf("elite")!], FourMinutes);

        Assert.Equal(0, result.Winner);
        Assert.Equal(1, result.Ticks);
        var lost = result.Seats[1];
        Assert.Equal((Data.Rules.Start.Workers, 1), (lost.UnitsLost, lost.StructuresLost));
        Assert.Equal((0, 0), (lost.Workers, lost.Structures.Count));
        Assert.Equal((0, 0, 1), (result.Seats[0].UnitsLost, result.Seats[0].StructuresLost, result.Seats[0].Structures.Count));
    }

    [Fact]
    public void AMatchNeedsAPersonalityForEverySeatAndAStartForEveryPlayer()
    {
        var swarm = Data.PersonalityOf("swarm")!;
        var match = MatchSetup.Create(Data, Ridge, 1, [new PlayerSetup(0), new PlayerSetup(1)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Play(Data, Ridge, [swarm], 1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Play(Data, Ridge, [.. Enumerable.Repeat(swarm, Ridge.Starts.Count + 1)], 1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Play(Data, Ridge, [swarm, swarm], 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Play(match, [swarm], 100));
    }

    /// <summary>The tool the way the command line runs it: each pairing both ways round, and what a build without attacks comes to.</summary>
    [Fact(Skip = ReplayScripts.TestBuild.HeadlessMatchMeetsTheScout)]
    public void TheToolPlaysBothWaysRoundAndReportsEachMatch()
    {
        var (exitCode, output, errors) = Run("--data", Repository.DataFolder, "--players", "swarm,elite", "--minutes", "4", "--seed", "7");

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var lines = output.Split(Environment.NewLine);
        Assert.StartsWith($"ai-match: computer players on 'ridge' with the data in {Repository.DataFolder}, seed 7, at most 4 minutes of game time.", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            ["swarm (player 0, start 0) against elite (player 1, start 1)", "elite (player 0, start 0) against swarm (player 1, start 1)"],
            lines.Where(line => line.Contains(" against ", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(line => line == "  ended after 4:00 of game time, at the time limit, with no winner"));
        Assert.Equal(2, lines.Count(line => Regex.IsMatch(line, @"^  speed: 4,800 ticks in \d+\.\d s, \d+ times the speed of play; a tick took \d+\.\d{3} ms on average and \d+\.\d ms at worst; the players thought for \d+\.\d s in all$")));
        Assert.Equal(2, lines.Count(line => Regex.IsMatch(line, @"^  commands: \d+ given, 0 dropped; state hash [0-9a-f]{16}$")));
        Assert.Equal(["  swarm, player 0:", "  elite, player 1:", "  elite, player 0:", "  swarm, player 1:"], lines.Where(line => line.EndsWith(':')));
        Assert.Equal(4, lines.Count(line => Regex.IsMatch(line, @"^    epoch 2 \(epoch 2 at 3:\d\d\); tiers \d(, \d){5}$")));
        Assert.Equal(4, lines.Count(line => Regex.IsMatch(line, @"^    supply \d+ of \d+; \d+ workers; \d+ fighting units worth [\d,]+( \(.+\))?$")));
        Assert.Equal(4, lines.Count(line => Regex.IsMatch(line, @"^    structures: (\d+ [a-z_]+, )*1 capitol(, \d+ [a-z_]+)*$")));
        Assert.Equal(4, lines.Count(line => Regex.IsMatch(line, @"^    lost 0 units and 0 structures; stockpile [\d,]+ Materials and [\d,]+ Energy$")));
        Assert.Equal("ai-match: no winner. A computer player of this build builds, researches and trains, and does not attack.", lines[^2]);
    }

    /// <summary>A pairing that reads the same both ways round is played once.</summary>
    [Fact]
    public void APersonalityAgainstItselfIsPlayedOnce()
    {
        var (exitCode, output, _) = Run("--data", Repository.DataFolder, "--players", "swarm,swarm", "--minutes", "1");

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Single(output.Split(Environment.NewLine), line => line.Contains(" against ", StringComparison.Ordinal));
        Assert.Contains("ended after 1:00 of game time", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutPlayersNamedTheFirstTwoPersonalitiesOfTheDataPlay()
    {
        var (exitCode, output, _) = Run("--data", Repository.DataFolder, "--minutes", "1");

        Assert.Equal(ExitCodes.Ok, exitCode);
        var first = Data.Personalities.Take(2).Select(personality => personality.Id).ToList();
        Assert.Contains($"{first[0]} (player 0, start 0) against {first[1]} (player 1, start 1)", output, StringComparison.Ordinal);
        Assert.Contains($"{first[1]} (player 0, start 0) against {first[0]} (player 1, start 1)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLastLineSaysWhoWon()
    {
        Assert.Equal("ai-match: swarm won both ways round.", AiMatchTool.Verdict(["swarm", "swarm"]));
        Assert.Equal("ai-match: the result changed with the seats: swarm in the first match, elite in the second.", AiMatchTool.Verdict(["swarm", "elite"]));
        Assert.Equal("ai-match: the result changed with the seats: no winner in the first match, elite in the second.", AiMatchTool.Verdict([null, "elite"]));
        Assert.Equal("ai-match: swarm won.", AiMatchTool.Verdict(["swarm"]));
        Assert.StartsWith("ai-match: no winner.", AiMatchTool.Verdict([null, null]), StringComparison.Ordinal);
        Assert.StartsWith("ai-match: no winner.", AiMatchTool.Verdict([null]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--minutes", "0")]
    [InlineData("--minutes", "soon")]
    [InlineData("--seed", "-1")]
    [InlineData("--players")]
    [InlineData("--quickly")]
    public void AnArgumentItDoesNotKnowIsAnErrorOfUsage(params string[] arguments)
    {
        var (exitCode, output, errors) = Run(["--data", Repository.DataFolder, .. arguments]);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains("Usage:", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("has no map 'atlantis'. Its maps are: ", "--map", "atlantis")]
    [InlineData("has no personality 'turtle'. Its personalities are: elite, swarm.", "--players", "swarm,turtle")]
    [InlineData("has 2 to 2 players, and 1 were named", "--players", "swarm")]
    [InlineData("has 2 to 2 players, and 3 were named", "--players", "swarm,elite,swarm")]
    public void AMatchThatCannotBePlayedAsAskedIsAnErrorOfUsage(string message, params string[] arguments)
    {
        var (exitCode, output, errors) = Run(["--data", Repository.DataFolder, .. arguments]);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains(message, errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatIsNotThereIsAnErrorOfUsage()
    {
        var (exitCode, _, errors) = Run("--data", Path.Combine(Repository.Root, "no-such-folder"));

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("there is no folder", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void DataThatIsNotValidFailsTheTool()
    {
        using var folder = Repository.CopyOfData();
        folder.Edit("ai/swarm.jsonc", "\"id\": \"swarm\"", "\"id\": \"Swarm!\"");

        var (exitCode, output, errors) = Run("--data", folder.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal("", output);
        Assert.Contains("is not valid. Run datacheck on it.", errors, StringComparison.Ordinal);
    }

    private static HeadlessResult Play(params string[] personalities) =>
        HeadlessMatch.Play(Data, Ridge, [.. personalities.Select(id => Data.PersonalityOf(id)!)], 1, FourMinutes);

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var exitCode = ToolRunner.Run(["ai-match", .. arguments], output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }
}
