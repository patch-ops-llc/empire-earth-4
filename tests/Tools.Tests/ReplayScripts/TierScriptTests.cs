using Sim.Core;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>set-tier</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach. It is there for the development tier
/// (<see cref="CommandType.DebugSetTier"/>), which raises a track the way a research that finishes
/// does.
/// </summary>
public class TierScriptTests
{
    private const string GoldenReplay = "switchback-tiers.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a tier order for the next tier",
        "a tier order for a tier several above the track's",
        "a tier order for a track whose research is under way",
        "a tier order for the tier the track is at",
        "a tier order for a tier below the track's",
        "a tier order for a tier above the epoch cap",
        "a tier order for a tier that is no whole number",
        "a tier order for a track the data does not have",
        "a tier order from nobody in the match",
        "a research order",
        "a unit made beside the player's own",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kinds come about once in 750 ticks, so a shorter match can miss one.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "12000", "--orders", "33", "--script", "set-tier");

        Assert.All(
            replay.Commands,
            entry => Assert.Contains(entry.Command.Type, new[] { CommandType.DebugSetTier, CommandType.StartResearch, CommandType.DebugSpawn }));
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(match, due[tick]));
            match.Simulation.Tick();
        }

        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What this recorded match is for: it must reach what the development tier does, each tier
    /// finished as a research finishes it, with what stands on the map refreshed, and each
    /// command it must refuse.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var seen = WhatItReaches(ReplayRuns.ReadGolden(GoldenReplay));

        string[] expected =
        [
            .. EveryKindOfOrder,
            "a track raised by one tier",
            "a track raised by several tiers in one tick",
            "a research under way cancelled and its price given back",
            "an epoch raised by a tier order",
            "a tier order refused",
            "a worker's hit points raised in the tick of the order",
            "a fighting unit's hit points raised in the tick of the order",
            "a fighting unit's speed raised in the tick of the order",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    /// <summary>Runs a recorded match and says what it reached, in the words of the two lists above.</summary>
    private static HashSet<string> WhatItReaches(Sim.Serialization.Replay replay)
    {
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var before = new Dictionary<EntityId, (long Maximum, Fixed Speed)>();
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            Command[] commands = [.. due[tick]];
            seen.UnionWith(KindsOfOrders(match, commands));
            before.Clear();
            for (var i = 0; i < match.Health.Count; i++)
            {
                var entity = match.Health.EntityAt(i);
                before[entity] = (match.Health.ValueAt(i).Maximum, match.Movers.TryGet(entity, out var mover) ? mover.Speed : Fixed.Zero);
            }

            long[] materials = [.. Enumerable.Range(0, match.Players.Count).Select(match.Stockpiles.Materials)];
            match.Simulation.Tick();
            seen.UnionWith(WhatTheTickDid(match, commands, before, materials));
        }

        return seen;
    }

    /// <summary>
    /// What the commands of this script that are due in the next tick are, in the words of
    /// <see cref="EveryKindOfOrder"/>, by the state of the match before that tick. The script
    /// gives one command a tick, so none of them changes what the next is judged by.
    /// </summary>
    private static HashSet<string> KindsOfOrders(Match match, IEnumerable<Command> commands)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var research = match.Research;
        foreach (var command in commands)
        {
            switch (command.Type)
            {
                case CommandType.StartResearch:
                    seen.Add("a research order");
                    break;
                case CommandType.DebugSpawn:
                    seen.Add("a unit made beside the player's own");
                    break;
                case CommandType.DebugSetTier when command.Player >= match.Players.Count:
                    seen.Add("a tier order from nobody in the match");
                    break;
                case CommandType.DebugSetTier when command.ContentId < 0 || command.ContentId >= match.Data.Tracks.Count:
                    seen.Add("a tier order for a track the data does not have");
                    break;
                case CommandType.DebugSetTier when Fixed.FromInt(command.TargetX.FloorToInt()) != command.TargetX:
                    seen.Add("a tier order for a tier that is no whole number");
                    break;
                case CommandType.DebugSetTier:
                    var tier = command.TargetX.FloorToInt();
                    var now = research.State.Tier(command.Player, command.ContentId);
                    if (tier > research.EpochCap)
                    {
                        seen.Add("a tier order for a tier above the epoch cap");
                    }
                    else if (tier <= now)
                    {
                        seen.Add(tier == now ? "a tier order for the tier the track is at" : "a tier order for a tier below the track's");
                    }
                    else
                    {
                        seen.Add(tier == now + 1 ? "a tier order for the next tier" : "a tier order for a tier several above the track's");
                        if (research.State.SlotResearching(command.Player, command.ContentId) >= 0)
                        {
                            seen.Add("a tier order for a track whose research is under way");
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return seen;
    }

    /// <summary>
    /// What the tick that has just run did, from its events and from the state after it, in the
    /// words of the list of <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>.
    /// </summary>
    /// <param name="commands">The commands that were due in the tick.</param>
    /// <param name="before">What had hit points before the tick, each with its maximum and its speed.</param>
    /// <param name="materialsBefore">Each player's Materials before the tick.</param>
    private static HashSet<string> WhatTheTickDid(
        Match match,
        Command[] commands,
        Dictionary<EntityId, (long Maximum, Fixed Speed)> before,
        long[] materialsBefore)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            if (command.Type != CommandType.DebugSetTier)
            {
                continue;
            }

            var finished = 0;
            foreach (var listed in match.Simulation.Events)
            {
                if (listed.Kind == SimEventKind.OrderRefused && (CommandType)listed.Amount == CommandType.DebugSetTier)
                {
                    seen.Add("a tier order refused");
                }

                if (!ResearchEvent.TryRead(listed, out var reported) || reported.Player != command.Player)
                {
                    continue;
                }

                switch (reported.Kind)
                {
                    case SimEventKind.ResearchFinished when reported.Track == command.ContentId:
                        finished++;
                        break;
                    case SimEventKind.ResearchCancelled when reported.Track == command.ContentId
                        && match.Stockpiles.Materials(command.Player) > materialsBefore[command.Player]:
                        seen.Add("a research under way cancelled and its price given back");
                        break;
                    case SimEventKind.EpochChanged:
                        seen.Add("an epoch raised by a tier order");
                        break;
                    default:
                        break;
                }
            }

            if (finished == 0)
            {
                continue;
            }

            seen.Add(finished == 1 ? "a track raised by one tier" : "a track raised by several tiers in one tick");
            foreach (var (entity, was) in before)
            {
                if (!match.Bodies.TryGet(entity, out var body) || body.Owner != command.Player)
                {
                    continue;
                }

                var fights = match.Combatants.Contains(entity);
                if (match.Health.Get(entity).Maximum > was.Maximum)
                {
                    seen.Add(fights ? "a fighting unit's hit points raised in the tick of the order" : "a worker's hit points raised in the tick of the order");
                }

                if (fights && match.Movers.Get(entity).Speed > was.Speed)
                {
                    seen.Add("a fighting unit's speed raised in the tick of the order");
                }
            }
        }

        return seen;
    }
}
