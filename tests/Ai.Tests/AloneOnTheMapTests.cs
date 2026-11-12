using Sim;
using Sim.Core;
using Sim.Setup;

namespace Ai.Tests;

/// <summary>
/// The milestone's promise: a computer player alone on the map, with the game's own data,
/// reaches the epoch cap and fills its supply, by nothing but commands.
/// </summary>
public class AloneOnTheMapTests
{
    /// <summary>
    /// Twenty-five minutes of game time. A computer player needs about half of it with the data
    /// as it is, so the numbers can be tuned a long way before this fails.
    /// </summary>
    private const int MostTicks = 25 * 60 * SimInfo.TicksPerSecond;

    [Theory]
    [InlineData("swarm", 1)]
    [InlineData("swarm", 5)]
    [InlineData("elite", 1)]
    [InlineData("elite", 5)]
    [InlineData("swarm", 12)]
    [InlineData("elite", 20)]
    public void ItReachesTheEpochCapAndFillsItsSupply(string personality, int commandDelay)
    {
        var run = new AiMatch(GameFiles.Data, "ridge", playerCount: 1, commandDelay);
        run.Add(0, personality);

        var reached = run.RunUntil(HasEpochCapAndFullSupply, MostTicks);

        var match = run.Match;
        Assert.True(
            reached,
            $"After {match.World.Tick} ticks it is in epoch {match.Research.Tech(0).Epoch} of {match.Research.EpochCap}"
            + $" and uses {match.Supply.Used(0)} of {match.Supply.Cap(0)} supply.");
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    [Theory]
    [InlineData("swarm")]
    [InlineData("elite")]
    public void ItsArmyIsMadeOfTheLinesOfItsPersonality(string personality)
    {
        var run = new AiMatch(GameFiles.Data, "ridge", playerCount: 1);
        var ai = run.Add(0, personality);

        Assert.True(run.RunUntil(HasEpochCapAndFullSupply, MostTicks));

        var match = run.Match;
        var unitsByLine = new int[match.Data.Rules.Lines.Count];
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            unitsByLine[match.Data.Chassis[match.Combatants.ValueAt(i).Chassis].Line]++;
        }

        var wanted = ai.Personality.Lines.Select(line => line.Line).ToList();
        for (var line = 0; line < unitsByLine.Length; line++)
        {
            Assert.Equal(wanted.Contains(line), unitsByLine[line] > 0);
        }

        // The lines stand in the ratio of the file, to within the units of one order.
        var first = ai.Personality.Lines[0];
        foreach (var other in ai.Personality.Lines.Skip(1))
        {
            var difference = (unitsByLine[first.Line] * other.Share) - (unitsByLine[other.Line] * first.Share);
            Assert.InRange(difference, -4 * first.Share, 4 * Math.Max(first.Share, other.Share));
        }
    }

    /// <summary>
    /// Under the fog it knows no deposit but those of its start. It still claims another: a
    /// worker looks first, and no order of the whole match is refused.
    /// </summary>
    [Theory]
    [InlineData("swarm")]
    [InlineData("elite")]
    public void ItClaimsAnotherDepositUnderTheFog(string personality)
    {
        var run = new AiMatch(GameFiles.Data, "ridge", playerCount: 1, commandDelay: 2);
        run.Add(0, personality);

        var claimed = run.RunUntil(_ => run.TerritoryStructuresOf(0) >= 3, MostTicks);

        Assert.True(claimed, $"After {run.Match.World.Tick} ticks it holds {run.TerritoryStructuresOf(0)} structures with territory.");
        Assert.Equal(0, run.Match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void TheSameMatchGivesTheSameCommandsAndTheSameState()
    {
        var first = Play();
        var second = Play();

        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(first.Commands, second.Commands);

        static (ulong Hash, List<string> Commands) Play()
        {
            var run = new AiMatch(GameFiles.Data, "ridge", playerCount: 2, commandDelay: 2);
            run.Add(0, "swarm");
            run.Add(1, "elite");
            // Long enough that both have attacked, retreated and attacked again.
            for (var tick = 0; tick < 8 * 60 * SimInfo.TicksPerSecond; tick++)
            {
                run.Tick();
            }

            return (run.Match.World.ComputeHash(), run.Given.Select(given => Describe(given.Tick, given.Command)).ToList());
        }
    }

    [Fact]
    public void ASeatWithNothingOnTheMapGivesNoOrders()
    {
        var data = GameFiles.Data;
        var match = MatchSetup.Create(data, data.Maps.Single(map => map.Id == "ridge"), seed: 1, [new PlayerSetup(0), PlayerSetup.WithoutStart]);
        var ai = new AiPlayer(match, 1, data.PersonalityOf("swarm")!);
        var commands = new List<Command>();

        for (var tick = 0; tick < 100; tick++)
        {
            match.Simulation.Tick();
            ai.Think(commands);
        }

        Assert.Empty(commands);
    }

    [Fact]
    public void ItThinksOnceInEveryInterval()
    {
        var data = GameFiles.Data;
        var match = MatchSetup.Create(data, data.Maps.Single(map => map.Id == "ridge"), seed: 1, [new PlayerSetup(0)]);
        var ai = new AiPlayer(match, 0, data.PersonalityOf("elite")!);
        var commands = new List<Command>();

        match.Simulation.Tick();
        ai.Think(commands);
        var afterFirst = commands.Count;
        Assert.NotEqual(0, afterFirst);

        // Its orders are carried out in the next tick. Until the next round it gives none.
        foreach (var command in commands)
        {
            match.Simulation.Commands.Enqueue(match.World.Tick, command);
        }

        for (var tick = 1; tick < AiPlayer.ThinkInterval; tick++)
        {
            match.Simulation.Tick();
            ai.Think(commands);
            Assert.Equal(afterFirst, commands.Count);
        }

        // The Capitol can now pay for the next worker, so the round has an order to give.
        match.Simulation.Tick();
        ai.Think(commands);
        Assert.True(commands.Count > afterFirst);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    private static bool HasEpochCapAndFullSupply(Match match) =>
        match.Research.Tech(0).Epoch >= match.Research.EpochCap && match.Supply.Used(0) >= match.Supply.Cap(0);

    private static string Describe(int tick, Command command) =>
        $"{tick} {command.Type} p{command.Player} [{string.Join(",", command.Entities.ToArray())}] "
        + $"{command.TargetX.Raw},{command.TargetY.Raw} {command.TargetEntity} {command.ContentId}";
}
