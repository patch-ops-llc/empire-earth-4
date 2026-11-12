using System.Text.RegularExpressions;
using Sim.Core;
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
        Assert.Equal(MatchEnd.TimeLimit, result.Ended);
        Assert.Null(result.Winner);
        Assert.Equal((HeadlessMatch.CommandDelay, -1), (result.CommandDelay, result.FirstHitTick));
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
            Assert.Equal((-1, -1, -1), (seat.OutTick, seat.FirstAttackOrderTick, seat.EveryTrackAtCapTick));
            Assert.Equal((0, 0, 0), (seat.TicksAtSupplyCap, seat.TicksAboveSupplyCap, seat.MostAboveSupplyCap));
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
    /// A match ends by conquest (design 5.1): a player whose last Capitol falls is out, whatever
    /// else it still has, and the one player left has won.
    /// </summary>
    [Fact]
    public void TheMatchEndsWhenOnlyOnePlayerHasACapitolLeft()
    {
        var match = MatchSetup.Create(Data, Ridge, 1, [new PlayerSetup(0), new PlayerSetup(1)]);
        Raze(match, 1);

        var result = HeadlessMatch.Play(match, [Data.PersonalityOf("swarm")!, Data.PersonalityOf("elite")!], FourMinutes);

        Assert.Equal((MatchEnd.Conquest, 0, 1), (result.Ended, result.Winner, result.Ticks));
        var lost = result.Seats[1];
        Assert.Equal((1, 0, 1), (lost.OutTick, lost.UnitsLost, lost.StructuresLost));
        Assert.Equal((Data.Rules.Start.Workers, 0), (lost.Workers, lost.Structures.Count));
        var won = result.Seats[0];
        Assert.Equal((-1, 0, 0, 1), (won.OutTick, won.UnitsLost, won.StructuresLost, won.Structures.Count));
    }

    /// <summary>Players who lose their last Capitol in the same tick go out together, and nobody has won.</summary>
    [Fact]
    public void PlayersWhoLoseTheirLastCapitolInOneTickGoOutTogether()
    {
        var match = MatchSetup.Create(Data, Ridge, 1, [new PlayerSetup(0), new PlayerSetup(1)]);
        Raze(match, 0);
        Raze(match, 1);

        var result = HeadlessMatch.Play(match, [Data.PersonalityOf("swarm")!, Data.PersonalityOf("elite")!], FourMinutes);

        Assert.Equal((MatchEnd.Conquest, null, 1), (result.Ended, result.Winner, result.Ticks));
        Assert.Equal([1, 1], result.Seats.Select(seat => seat.OutTick));
    }

    /// <summary>A player without a start has no Capitol to lose and is never out, so a match of such players runs to the time limit.</summary>
    [Fact]
    public void APlayerWithoutAStartIsNeverOut()
    {
        var match = MatchSetup.Create(Data, Ridge, 1, [new PlayerSetup(0), PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);
        var swarm = Data.PersonalityOf("swarm")!;

        var result = HeadlessMatch.Play(match, [swarm, swarm, swarm], 40);

        Assert.Equal((MatchEnd.TimeLimit, null, 40), (result.Ended, result.Winner, result.Ticks));
        Assert.Equal([-1, -1, -1], result.Seats.Select(seat => seat.OutTick));
    }

    /// <summary>
    /// The seed changes nothing in a match, because nothing in a match is random: the same
    /// commands in the same ticks. What changes a match is the command delay, and who has
    /// which start.
    /// </summary>
    [Fact]
    public void TheCommandDelayAndTheStartsMakeAnotherMatchAndTheSeedDoesNot()
    {
        var first = FourMinutesOnRidge.Value;
        var players = new[] { Data.PersonalityOf("swarm")!, Data.PersonalityOf("elite")! };

        var seeded = HeadlessMatch.Play(Data, Ridge, players, 99, FourMinutes);
        var delayed = HeadlessMatch.Play(Data, Ridge, players, 1, FourMinutes, commandDelay: 5);
        var swapped = HeadlessMatch.Play(Data, Ridge, players, 1, FourMinutes, starts: [1, 0]);

        Assert.Equal(first.CommandsGiven, seeded.CommandsGiven);
        Assert.Equal(first.Seats.Select(Built), seeded.Seats.Select(Built));
        Assert.Equal(5, delayed.CommandDelay);
        Assert.NotEqual(first.Seats.Select(Built), delayed.Seats.Select(Built));
        Assert.Equal([(0, 1), (1, 0)], swapped.Seats.Select(seat => (seat.Player, seat.Start)));
        Assert.Equal(["swarm", "elite"], swapped.Seats.Select(seat => seat.Personality));
        Assert.NotEqual(first.StateHash, swapped.StateHash);

        static string Built(SeatResult seat) =>
            $"{string.Join(",", seat.EpochTicks)} {string.Join(",", seat.Tiers)} {seat.SupplyUsed} {seat.Workers} {seat.Units} {seat.Materials} {seat.Energy} {string.Join(",", seat.Structures)}";
    }

    /// <summary>What a player paid for research is counted per track, and what it had is noted every ten seconds.</summary>
    [Fact]
    public void AMatchSaysWhatEachPlayerPaidForResearchAndWhatItHadEveryTenSeconds()
    {
        var result = FourMinutesOnRidge.Value;

        Assert.All(result.Seats, seat =>
        {
            Assert.Equal(Data.Tracks.Count, seat.ResearchMaterials.Count);
            Assert.Equal(Data.Tracks.Count, seat.ResearchEnergy.Count);
            for (var track = 0; track < Data.Tracks.Count; track++)
            {
                // A track above the tier a match starts at was paid for, and one that was never started was not.
                var paid = seat.ResearchMaterials[track] + seat.ResearchEnergy[track];
                Assert.True(paid >= 0, $"{seat.Personality} paid {paid} on {Data.Tracks[track].Id}.");
                Assert.True(seat.Tiers[track] == Data.Rules.Match.StartEpoch || paid > 0, $"{seat.Personality} has tier {seat.Tiers[track]} on {Data.Tracks[track].Id} for nothing.");
            }

            Assert.Equal(Enumerable.Range(1, 24).Select(sample => sample * SeatWatch.SampleEvery), seat.Samples.Select(sample => sample.Tick));
            var last = seat.Samples[^1];
            Assert.Equal((seat.Workers, seat.Units, seat.ArmyValue, seat.SupplyUsed, seat.SupplyCap, seat.Materials, seat.Energy), (last.Workers, last.Units, last.ArmyValue, last.SupplyUsed, last.SupplyCap, last.Materials, last.Energy));
            Assert.InRange(seat.Samples[0].Workers, Data.Rules.Start.Workers, seat.Workers);
        });
    }

    /// <summary>
    /// The price of a research is counted when it starts and comes off again when it is
    /// cancelled, which gives it back in full; a research that finishes stays counted.
    /// </summary>
    [Fact]
    public void ResearchThatIsCancelledIsNotCountedAsPaidFor()
    {
        var match = MatchSetup.Create(Data, Ridge, 1, [new PlayerSetup(0), new PlayerSetup(1)]);
        var watch = new SeatWatch(match, 0);
        var other = new SeatWatch(match, 1);
        var (materials, energy) = (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0));
        var (firepower, manufacturing) = (Data.TrackRoles.Firepower, Data.TrackRoles.Manufacturing);

        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.StartResearch, 0, contentId: manufacturing));
        Tick(1);

        var price = watch.ResearchMaterials[manufacturing] + watch.ResearchEnergy[manufacturing];
        Assert.True(price > 0);
        Assert.Equal(Spent(), price);
        Assert.Equal(price, watch.ResearchMaterials.Sum() + watch.ResearchEnergy.Sum());

        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.CancelResearch, 0, contentId: manufacturing));
        Tick(1);

        Assert.Equal((0, 0), (Spent(), watch.ResearchMaterials.Sum() + watch.ResearchEnergy.Sum()));

        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.StartResearch, 0, contentId: firepower));
        while (match.Research.State.Tier(0, firepower) == Data.Rules.Match.StartEpoch)
        {
            Tick(1);
        }

        Assert.Equal(Spent(), watch.ResearchMaterials[firepower] + watch.ResearchEnergy[firepower]);
        Assert.Equal(Spent(), watch.ResearchMaterials.Sum() + watch.ResearchEnergy.Sum());
        Assert.True(Spent() > 0);
        Assert.Equal(0, other.ResearchMaterials.Sum() + other.ResearchEnergy.Sum());
        Assert.Equal((-1, -1, 0), (watch.FirstAttackOrderTick, watch.EveryTrackAtCapTick, watch.TicksAboveSupplyCap));

        long Spent() => (materials - match.Stockpiles.Materials(0) + energy - match.Stockpiles.Energy(0)) / Sim.Economy.Stockpiles.PerResource;

        void Tick(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                match.Simulation.Tick();
                watch.AfterTick();
                other.AfterTick();
            }
        }
    }

    /// <summary>The first attack-move or attack order a player gives is noted with its tick; other orders are not.</summary>
    [Fact]
    public void TheFirstAttackOrderIsNotedOnce()
    {
        var match = MatchSetup.Create(Data, Ridge, 1, [new PlayerSetup(0), new PlayerSetup(1)]);
        var watch = new SeatWatch(match, 0);

        watch.Given(new Command(CommandType.Move, 0), 10);
        Assert.Equal(-1, watch.FirstAttackOrderTick);

        watch.Given(new Command(CommandType.AttackMove, 0), 20);
        watch.Given(new Command(CommandType.Attack, 0), 30);
        Assert.Equal(20, watch.FirstAttackOrderTick);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessMatch.Play(Data, Ridge, [swarm, swarm], 1, 100, commandDelay: 0));
        Assert.Throws<ArgumentException>(() => HeadlessMatch.Play(Data, Ridge, [swarm, swarm], 1, 100, starts: [0]));
        Assert.Throws<ArgumentException>(() => HeadlessMatch.Play(Data, Ridge, [swarm, swarm], 1, 100, starts: [0, 0]));
        Assert.Throws<ArgumentException>(() => HeadlessMatch.Play(Data, Ridge, [swarm, swarm], 1, 100, starts: [0, Ridge.Starts.Count]));
    }

    /// <summary>The tool the way the command line runs it: each pairing both ways round, and what a build without attacks comes to.</summary>
    [Fact(Skip = ReplayScripts.TestBuild.HeadlessMatchMeetsTheScout)]
    public void TheToolPlaysBothWaysRoundAndReportsEachMatch()
    {
        var (exitCode, output, errors) = Run("--data", Repository.DataFolder, "--players", "swarm,elite", "--minutes", "4", "--seed", "7", "--delay", "3");

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var lines = output.Split(Environment.NewLine);
        Assert.StartsWith($"ai-match: computer players on 'ridge' with the data in {Repository.DataFolder}, seed 7, command delay 3, at most 4 minutes of game time.", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            ["swarm (player 0, start 0) against elite (player 1, start 1)", "elite (player 0, start 0) against swarm (player 1, start 1)"],
            lines.Where(line => line.Contains(" against ", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(line => line == "  ended after 4:00 of game time, at the time limit, with no winner; nothing was hit"));
        Assert.Equal(2, lines.Count(line => Regex.IsMatch(line, @"^  speed: 4,800 ticks in \d+\.\d s, \d+ times the speed of play; a tick took \d+\.\d{3} ms on average and \d+\.\d ms at worst; the players thought for \d+\.\d s in all$")));
        Assert.Equal(2, lines.Count(line => Regex.IsMatch(line, @"^  commands: \d+ given, 0 dropped; state hash [0-9a-f]{16}$")));
        Assert.Equal(["  swarm, player 0:", "  elite, player 1:", "  elite, player 0:", "  swarm, player 1:"], lines.Where(line => line.EndsWith(':')));
        Assert.Equal(4, lines.Count(line => Regex.IsMatch(line, @"^    epoch 2 \(epoch 2 at 3:\d\d\); tiers \d(, \d){5}$")));
        Assert.Equal(4, lines.Count(line => Regex.IsMatch(line, @"^    research paid for: [\d,]+ \(firepower [\d,]+, protection [\d,]+, mobility [\d,]+, manufacturing [\d,]+, logistics [\d,]+, extraction [\d,]+\)$")));
        Assert.Equal(4, lines.Count(line => line == "    first attack order: none"));
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
    [InlineData("--delay", "0")]
    [InlineData("--delay", "21")]
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

    /// <summary>Destroys every structure of a player that keeps it in the match, in the next tick.</summary>
    private static void Raze(Sim.Setup.Match match, int player)
    {
        for (var i = 0; i < match.Structures.Count; i++)
        {
            var entity = match.Structures.EntityAt(i);
            if (match.Bodies.Get(entity).Owner == player && match.Data.StructureOf(match.Structures.ValueAt(i).Type)?.TerritoryRadius is not null)
            {
                match.Health.Get(entity).HitPoints = 0;
            }
        }
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
