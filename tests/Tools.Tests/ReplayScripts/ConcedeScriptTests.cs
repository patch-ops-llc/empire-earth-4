using Sim.Core;
using Sim.Outcome;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>concede</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
/// <remarks>
/// No recorded match reaches conquest yet: no script can destroy a Capitol until structures can
/// be attacked (16b). The replay recorded with that piece's script is where it belongs.
/// </remarks>
public class ConcedeScriptTests
{
    private const string GoldenReplay = "switchback-concede.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "an order to concede",
        "an order to concede from nobody in the match",
        "an order to concede from a player in the match",
        "an order to concede from a player who is out",
        "an order to concede from the winner of a decided match",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match of three
    /// players on each map.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--starts", "0,-,1", "--ticks", "12000", "--orders", "34", "--script", "concede");

        Assert.All(replay.Commands, entry => Assert.Equal(CommandType.Concede, entry.Command.Type));
        var orders = WhatItReaches(replay).Where(EveryKindOfOrder.Contains);
        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), orders.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>The players' own orders wait: for a minute at least the script concedes only in the name of nobody.</summary>
    [Theory]
    [InlineData("5")]
    [InlineData("21")]
    [InlineData("34")]
    public void NoPlayerConcedesInTheFirstMinute(string orders)
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--ticks", "1200", "--orders", orders, "--script", "concede");

        Assert.NotEmpty(replay.Commands);
        Assert.All(replay.Commands, entry => Assert.True(entry.Command.Player >= 2));
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of conceding that a change could
    /// break, with the orders of another script running into them.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        string[] expected =
        [
            .. EveryKindOfOrder,
            "a player out by its own order while the match goes on",
            "a move order of a player who is out, which is dropped",
            "the order to concede that decides the match",
            "a move order of the winner after the match is decided",
        ];

        var seen = WhatItReaches(ReplayRuns.ReadGolden(GoldenReplay));

        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    /// <summary>What a replay reaches, in the words of the lists above. It runs the match to know who is out when.</summary>
    private static HashSet<string> WhatItReaches(Sim.Serialization.Replay replay)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var match = ReplayRuns.Start(replay);
        var standings = match.Standings;
        var orders = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            var (decided, dropped) = (standings.IsDecided, match.Simulation.DroppedCommandCount);
            var expectDropped = 0;
            foreach (var command in orders[tick])
            {
                var isPlayer = command.Player >= 0 && command.Player < standings.PlayerCount;
                if (command.Type == CommandType.Concede)
                {
                    seen.Add("an order to concede");
                    seen.Add(!isPlayer ? "an order to concede from nobody in the match"
                        : !standings.IsPlaying(command.Player) ? "an order to concede from a player who is out"
                        : decided ? "an order to concede from the winner of a decided match"
                        : "an order to concede from a player in the match");
                    expectDropped += !isPlayer || decided || !standings.IsPlaying(command.Player) ? 1 : 0;
                }
                else if (command.Type == CommandType.Move && isPlayer)
                {
                    if (!standings.IsPlaying(command.Player))
                    {
                        seen.Add("a move order of a player who is out, which is dropped");
                        expectDropped++;
                    }
                    else if (decided)
                    {
                        seen.Add("a move order of the winner after the match is decided");
                    }
                }
            }

            var outBefore = standings.PlayerCount - standings.PlayingCount;
            match.Simulation.Tick();

            // Only one order to concede comes in a tick, so what the standings were before it says what it does.
            Assert.True(match.Simulation.DroppedCommandCount - dropped >= expectDropped, $"Tick {tick} dropped too few commands.");
            var went = standings.PlayerCount - standings.PlayingCount - outBefore;
            if (went > 0)
            {
                Assert.Equal(1, went);
                var player = Enumerable.Range(0, standings.PlayerCount).Single(candidate => standings.OutTick(candidate) == tick);
                Assert.Equal(PlayerStanding.Conceded, standings.Of(player));
                seen.Add(standings.IsDecided ? "the order to concede that decides the match" : "a player out by its own order while the match goes on");
            }
        }

        return seen;
    }
}
