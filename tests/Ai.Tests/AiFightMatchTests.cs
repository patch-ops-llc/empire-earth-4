using Sim;
using Sim.Core;
using Sim.Serialization;

namespace Ai.Tests;

/// <summary>
/// Whole matches on Ridge with the game's own data, played by nothing but the commands of the
/// computer players: each personality attacks, and Swarm against Elite is played to an end.
/// </summary>
/// <remarks>
/// Conquest (design 5.1: a player with no Capitol and no Outpost is out) is not in the
/// simulation yet, so the end is found here, by the same rule.
/// </remarks>
public class AiFightMatchTests
{
    /// <summary>The longest the match may take: long enough that the numbers can be tuned a good way before this fails.</summary>
    private const int MostTicks = 40 * 60 * SimInfo.TicksPerSecond;

    /// <summary>Each personality sends its army out, and it does so after it has built one, not with its first unit. Before that it has sent a scout.</summary>
    [Theory]
    [InlineData("swarm", "elite")]
    [InlineData("elite", "swarm")]
    [InlineData("swarm", "swarm")]
    [InlineData("elite", "elite")]
    public void EachPersonalityAttacksOnRidge(string first, string second)
    {
        var run = new AiMatch(GameFiles.Data, "ridge", playerCount: 2, commandDelay: 2);
        var players = new[] { run.Add(0, first), run.Add(1, second) };

        run.RunUntil(_ => players.All(player => player.Fight.FirstAttackTick >= 0), 10 * 60 * SimInfo.TicksPerSecond);

        Assert.All(players, player =>
        {
            Assert.InRange(player.Fight.FirstAttackTick, 2 * 60 * SimInfo.TicksPerSecond, 10 * 60 * SimInfo.TicksPerSecond);
            Assert.True(player.Fight.ArmyValue > 0);

            // Its first scout went at three minutes, or with the first unit it had after that (design 5.5, item 4).
            Assert.InRange(player.Scout.FirstSentTick, 3 * 60 * SimInfo.TicksPerSecond, 4 * 60 * SimInfo.TicksPerSecond);
            Assert.InRange(player.Scout.Sent, 1, 3);
        });

        // What they ordered of their units, they ordered of their own, and with the three orders of design 2.4.
        Assert.Contains(run.Given, given => given.Command.Type == CommandType.AttackMove);
        Assert.All(run.Given, given => Assert.InRange(given.Command.Player, 0, 1));
    }

    [Fact]
    public void SwarmAgainstEliteIsPlayedToAnEndByConquestAndItsReplayReproduces()
    {
        var run = new AiMatch(GameFiles.Data, "ridge", playerCount: 2, commandDelay: 2);
        var swarm = run.Add(0, "swarm");
        var elite = run.Add(1, "elite");

        var ended = run.RunUntil(_ => run.TerritoryStructuresOf(0) == 0 || run.TerritoryStructuresOf(1) == 0, MostTicks);

        var match = run.Match;
        Assert.True(
            ended,
            $"After {match.World.Tick} ticks Swarm holds {run.TerritoryStructuresOf(0)} and Elite {run.TerritoryStructuresOf(1)} structures with territory;"
            + $" Swarm attacked {swarm.Fight.Attacks} times and Elite {elite.Fight.Attacks} times.");

        // One of them is out, the other is not, and the winner attacked to get there.
        var winner = run.TerritoryStructuresOf(0) == 0 ? elite : swarm;
        Assert.True(run.TerritoryStructuresOf(winner.Player) > 0);
        Assert.True(winner.Fight.Attacks > 0);

        // The replay holds the description of the match and the commands. Run again from those alone, it ends in the same state.
        var replay = Replay.Read(new ByteReader(run.FinishReplay()));
        Assert.True(replay.IsComplete);
        Assert.Equal(match.World.Tick, replay.TickCount);
        Assert.Equal(run.Given.Count, replay.Commands.Count);

        var result = replay.Run(GameFiles.Data);

        Assert.True(result.Reproduced, $"The replay parted from the match at tick {result.Mismatch?.Tick}.");
        Assert.Equal(match.World.Tick, result.TicksRun);
        Assert.Equal(match.World.ComputeHash(), result.FinalHash);
        Assert.True(result.HashesChecked > match.World.Tick / ReplayRecorder.DefaultHashInterval);
    }
}
