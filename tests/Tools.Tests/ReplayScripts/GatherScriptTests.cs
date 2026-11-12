using Sim.Core;
using Sim.Economy;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>gather</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and the rules of workers at deposits it must reach.
/// </summary>
public class GatherScriptTests
{
    private const string GoldenReplay = "switchback-workers.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a gather order",
        "a gather order for a deposit the player holds",
        "a gather order for a deposit the player does not hold",
        "gather orders from every player for one deposit in one tick",
        "a gather order at something that is no work site",
        "a gather order from nobody in the match",
        "a gather order for one unit",
        "a gather order for a group",
        "a gather order for units that are not the player's",
        "a gather order for an entity that never existed",
    ];

    /// <summary>
    /// The first recording is the golden replay's own line. Which workers the script sends
    /// where, and when, is drawn, and in a match of that length about half of 30 other seeds
    /// miss a rule; the script makes none of them happen. In a match four times as long all 30
    /// reach every rule, so the second recording is that, for when the first no longer does.
    /// </summary>
    private static readonly ScriptReach Reach = new(
        [
            .. EveryKindOfOrder,
            "a worker walking to its place",
            "a worker at work",
            "a worker waiting for a place",
            "a worker given a place it waited for",
            "a worker taken off its work by another order",
            "a payment",
            "a payment skipped at a deposit outside its owner's territory",
        ],
        Reached,
        ["--map switchback --starts 0,1 --seed 5 --orders 13 --ticks 4800 --script gather,movement"],
        ["--map switchback --starts 0,1 --seed 5 --orders 13 --ticks 19200 --script gather,movement"]);

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kinds come about once in 1,700 ticks, so a shorter match can miss one.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "12000", "--orders", "33", "--script", "gather");

        Assert.All(replay.Commands, entry => Assert.Equal(CommandType.Gather, entry.Command.Type));
        var seen = KindsOfOrders(replay, replay.Description.CreateMatch(ReplayRuns.GoldenData));
        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What the script is for: a match recorded with it must reach the rules of workers at
    /// deposits that a change could break.
    /// </summary>
    [Fact]
    public void TheScriptReachesTheRulesItGuards() => Reach.MustHoldOnARecordingMadeNow();

    /// <summary>Says, without failing, what the stored replay no longer reaches of them.</summary>
    [Fact]
    public void ItsGoldenReplayStillReachesThem() => Reach.SayWhatTheGoldenReplaysNoLongerReach(GoldenReplay);

    /// <summary>What the match of a replay reaches, in the words of the list of <see cref="Reach"/>.</summary>
    private static HashSet<string> Reached(Sim.Serialization.Replay replay)
    {
        var match = ReplayRuns.Start(replay);
        var seen = KindsOfOrders(replay, match);
        var workers = new Worker[match.Workers.Count];
        long[] materials = [.. Enumerable.Range(0, match.Players.Count).Select(match.Stockpiles.Materials)];
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            match.Simulation.Tick();
            seen.UnionWith(WhatTheWorkersDid(match, workers, materials));
        }

        return seen;
    }

    /// <summary>What the commands of this script in a replay are, in the words of <see cref="EveryKindOfOrder"/>.</summary>
    private static HashSet<string> KindsOfOrders(Sim.Serialization.Replay replay, Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var players = match.Players.Count;
        foreach (var (tick, command) in replay.Commands.Where(entry => entry.Command.Type == CommandType.Gather))
        {
            seen.Add("a gather order");
            if (command.Player >= players)
            {
                seen.Add("a gather order from nobody in the match");
                continue;
            }

            if (!match.WorkSites.TryGet(command.TargetEntity, out var site))
            {
                seen.Add("a gather order at something that is no work site");
                continue;
            }

            // The territories of a scripted match are those of the start and do not change.
            seen.Add(Territory.Holds(match.Territories, match.World.Entities, command.Player, site.X, site.Y, site.Size)
                ? "a gather order for a deposit the player holds"
                : "a gather order for a deposit the player does not hold");
            var others = replay.Commands.Count(other =>
                other.Tick == tick && other.Command.Type == CommandType.Gather && other.Command.TargetEntity == command.TargetEntity && other.Command.Player != command.Player);
            if (others == players - 1)
            {
                seen.Add("gather orders from every player for one deposit in one tick");
            }

            seen.Add(command.Entities.Length == 1 ? "a gather order for one unit" : "a gather order for a group");
            foreach (var entity in command.Entities)
            {
                if (!match.Bodies.TryGet(entity, out var body))
                {
                    seen.Add("a gather order for an entity that never existed");
                }
                else if (body.Owner != command.Player)
                {
                    seen.Add("a gather order for units that are not the player's");
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// What the tick that has just run did to the workers, in the words of the list of
    /// <see cref="Reach"/>. The two arrays hold what the workers
    /// and the stockpiles of Materials were before it, and are brought up to date.
    /// </summary>
    private static HashSet<string> WhatTheWorkersDid(Match match, Worker[] before, long[] materialsBefore)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < before.Length; i++)
        {
            var worker = match.Workers.ValueAt(i);
            var owner = match.Bodies.Get(match.Workers.EntityAt(i)).Owner;
            switch (worker.State)
            {
                case WorkerState.Walking:
                    seen.Add(before[i].State == WorkerState.Waiting ? "a worker given a place it waited for" : "a worker walking to its place");
                    break;
                case WorkerState.Working:
                    seen.Add("a worker at work");
                    var site = match.WorkSites.Get(worker.Site);
                    if (before[i].Timer == Sim.SimInfo.TicksPerSecond - 1
                        && worker.Timer == 0
                        && !Territory.Holds(match.Territories, match.World.Entities, owner, site.X, site.Y, site.Size))
                    {
                        seen.Add("a payment skipped at a deposit outside its owner's territory");
                    }

                    break;
                case WorkerState.Waiting:
                    seen.Add("a worker waiting for a place");
                    break;
                case WorkerState.Idle when before[i].State == WorkerState.Working:
                    seen.Add("a worker taken off its work by another order");
                    break;
                default:
                    break;
            }

            before[i] = worker;
        }

        for (var player = 0; player < materialsBefore.Length; player++)
        {
            var materials = match.Stockpiles.Materials(player);
            if (materials > materialsBefore[player])
            {
                seen.Add("a payment");
            }

            materialsBefore[player] = materials;
        }

        return seen;
    }
}
