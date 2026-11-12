using Sim.Core;
using Sim.Research;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>research</c> of <c>replay record</c> and the golden replay recorded for it:
/// what the script must give, and what that replay must reach.
/// </summary>
public class ResearchScriptTests
{
    private const string GoldenReplay = "switchback-research.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a research order",
        "a cancel of a research",
        "a research order for a track that does not exist",
        "a research order from nobody in the match",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kind comes about once in 3,000 ticks, so a shorter match can miss it.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "30000", "--orders", "33", "--script", "research");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
        var seen = KindsOfOrders(replay, replay.Description.CreateMatch(ReplayRuns.GoldenData));
        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of research that a change could
    /// break, and each way a research order is dropped.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var seen = KindsOfOrders(replay, match);
        match.Research.Reported += reported => seen.Add(reported.Kind switch
        {
            ResearchEventKind.Started => "a research started and paid for",
            ResearchEventKind.Cancelled => "a research cancelled and refunded",
            ResearchEventKind.Finished => "a research finished and the tier raised",
            _ => $"research event {reported.Kind}",
        });
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(OrdersRefused(match, due[tick]));
            match.Simulation.Tick();
        }

        string[] expected =
        [
            .. EveryKindOfOrder,
            "a research started and paid for",
            "a research cancelled and refunded",
            "a research finished and the tier raised",
            "a research refused: its track is in research already",
            "a research refused: no free slot",
            "a research refused: the stockpile does not cover it",
            "a research refused: the player has no slot",
            "a cancel refused: the track is not in research",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    private static bool IsOfThisScript(Command command) => command.Type is CommandType.StartResearch or CommandType.CancelResearch;

    /// <summary>What the commands of this script in a replay are, in the words of <see cref="EveryKindOfOrder"/>.</summary>
    private static HashSet<string> KindsOfOrders(Sim.Serialization.Replay replay, Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, command) in replay.Commands.Where(entry => IsOfThisScript(entry.Command)))
        {
            seen.Add(command.Type == CommandType.StartResearch ? "a research order" : "a cancel of a research");
            if (command.Player >= match.Players.Count)
            {
                seen.Add("a research order from nobody in the match");
            }
            else if (command.ContentId < 0 || command.ContentId >= match.Data.Tracks.Count)
            {
                seen.Add("a research order for a track that does not exist");
            }
        }

        return seen;
    }

    /// <summary>
    /// Why the research orders due in the tick that runs next will be dropped, for those that
    /// will. An order is judged only when it is its player's one research order of the tick, so
    /// that the state it is judged against here is the state it runs in.
    /// </summary>
    private static IEnumerable<string> OrdersRefused(Match match, IEnumerable<Command> due)
    {
        var orders = due.Where(IsOfThisScript).ToList();
        var tracks = match.Data.Tracks.Count;
        foreach (var order in orders)
        {
            var alone = orders.Count(other => other.Player == order.Player) == 1;
            if (!alone || order.Player >= match.Players.Count || order.ContentId < 0 || order.ContentId >= tracks)
            {
                continue;
            }

            if (order.Type == CommandType.CancelResearch)
            {
                if (match.Research.State.SlotResearching(order.Player, order.ContentId) < 0)
                {
                    yield return "a cancel refused: the track is not in research";
                }

                continue;
            }

            var check = match.Research.CheckStart(order.Player, order.ContentId);
            if (check == ResearchStartCheck.NoFreeSlot && !match.Players[order.Player].HasStart)
            {
                yield return "a research refused: the player has no slot";
            }
            else if (check != ResearchStartCheck.Allowed)
            {
                yield return check switch
                {
                    ResearchStartCheck.InProgress => "a research refused: its track is in research already",
                    ResearchStartCheck.NoFreeSlot => "a research refused: no free slot",
                    ResearchStartCheck.TooExpensive => "a research refused: the stockpile does not cover it",
                    _ => $"a research refused: {check}",
                };
            }
        }
    }
}
