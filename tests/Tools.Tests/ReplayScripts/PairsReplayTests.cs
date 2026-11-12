using Sim.Core;
using Sim.Production;
using Sim.Setup;
using Sim.Tech;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The golden replay recorded for the Batch perk (19b): a match of the scripts <c>gather</c>,
/// <c>production</c> and <c>research</c> as they are, in which a research brings Batch to a
/// chassis and the queues then train it in pairs.
/// </summary>
public class PairsReplayTests
{
    private const string GoldenReplay = "switchback-pairs.replay";

    /// <summary>What the golden replay must reach. It may reach more.</summary>
    private static readonly string[] Expected =
    [
        "a research that brings Batch to a chassis",
        "a pair ordered",
        "a single unit ordered of a chassis with Batch, because only one is covered",
        "a single unit ordered of a chassis without Batch",
        "a pair in production",
        "a pair cancelled",
        "a pair that appears",
        "a single unit of a chassis with Batch that appears",
    ];

    /// <summary>
    /// What this recorded match is for: it must reach the rules of a pair that a change could
    /// break (design 2.3). No script was changed for it; the seed of the orders is one in which
    /// a player researches Manufacturing and trains line infantry afterwards.
    /// </summary>
    /// <remarks>
    /// Throughout it a pair pays the price and holds the supply of two units as they are when it
    /// is ordered, takes the pair's time of the blueprint, and its two units appear in one tick.
    /// It does not reach Light footprint, a displaced accent or an order that is waiting when its
    /// chassis gets Batch: the first two take more research than the players of a match of 7.5
    /// minutes pay for. The scenario tests of the simulation cover those.
    /// </remarks>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesOfAPair()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            var before = Enumerable.Range(0, match.Players.Count).Select(player => match.Research.Tech(player).Blueprints).ToArray();

            match.Simulation.Tick();

            seen.UnionWith(WhatTheOrdersDid(match, before));
            seen.UnionWith(WhatTheQueuesHold(match));
            for (var player = 0; player < match.Players.Count; player++)
            {
                Assert.True(match.Supply.Used(player) <= match.Supply.Cap(player), $"Player {player} uses more supply than the cap in tick {tick}.");
                var after = match.Research.Tech(player).Blueprints;
                for (var chassis = 0; chassis < after.Count; chassis++)
                {
                    // No research of this match takes Batch away again.
                    Assert.True(after[chassis].BatchSize >= before[player][chassis].BatchSize);
                    if (after[chassis].BatchSize > before[player][chassis].BatchSize)
                    {
                        seen.Add("a research that brings Batch to a chassis");
                    }
                }
            }
        }

        Assert.Empty(Expected.Except(seen, StringComparer.Ordinal));
    }

    /// <summary>What the tick that has just run ordered and trained, from its events. The blueprints are those from before the tick, which its orders were placed under.</summary>
    private static HashSet<string> WhatTheOrdersDid(Match match, IReadOnlyList<ChassisBlueprint>[] before)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var appeared = new Dictionary<(EntityId Structure, int Unit), int>();
        foreach (var simEvent in match.Simulation.Events)
        {
            if (!TrainEvent.TryRead(simEvent, out var train) || train.Unit == TrainOrder.Worker)
            {
                continue;
            }

            switch (train.Kind)
            {
                case SimEventKind.TrainOrdered when train.Count == 2:
                    var blueprint = before[train.Player][train.Unit];
                    Assert.Equal(2, blueprint.BatchSize);
                    Assert.Contains(
                        new TrainOrder
                        {
                            Unit = train.Unit,
                            Count = 2,
                            Materials = 2 * blueprint.Materials,
                            Energy = 2 * blueprint.Energy,
                            Supply = 2 * blueprint.Supply,
                        },
                        OrdersAt(match, train.Structure));
                    seen.Add("a pair ordered");
                    break;
                case SimEventKind.TrainOrdered:
                    Assert.Equal(1, train.Count);
                    seen.Add(before[train.Player][train.Unit].BatchSize == 2
                        ? "a single unit ordered of a chassis with Batch, because only one is covered"
                        : "a single unit ordered of a chassis without Batch");
                    break;
                case SimEventKind.TrainCancelled when train.Count == 2:
                    seen.Add("a pair cancelled");
                    break;
                case SimEventKind.UnitTrained:
                    appeared[(train.Structure, train.Unit)] = appeared.GetValueOrDefault((train.Structure, train.Unit)) + 1;
                    break;
                default:
                    break;
            }
        }

        foreach (var ((structure, unit), count) in appeared)
        {
            Assert.InRange(count, 1, 2);
            var hasBatch = before[match.Bodies.Get(structure).Owner][unit].BatchSize == 2;
            Assert.True(count == 1 || hasBatch);
            seen.Add(count == 2 ? "a pair that appears"
                : hasBatch ? "a single unit of a chassis with Batch that appears"
                : "a single unit of a chassis without Batch that appears");
        }

        return seen;
    }

    /// <summary>What is in production after the tick: a pair takes the pair's time of its owner's blueprint.</summary>
    private static HashSet<string> WhatTheQueuesHold(Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var producers = match.Production.Producers;
        for (var i = 0; i < producers.Count; i++)
        {
            var producer = producers.ValueAt(i);
            if (producer.Count == 0 || producer[0].Count != 2 || producer.BuildTicks == 0)
            {
                continue;
            }

            var owner = match.Bodies.Get(producers.EntityAt(i)).Owner;
            var blueprint = match.Research.Tech(owner).Blueprints[producer[0].Unit];
            Assert.True(blueprint.BatchBuildTicks < 2 * blueprint.BuildTicks);
            // The build time was set when production began, and no research of the match has changed it since if it is still the blueprint's.
            if (producer.BuildTicks == blueprint.BatchBuildTicks)
            {
                seen.Add("a pair in production");
            }
        }

        return seen;
    }

    private static TrainOrder[] OrdersAt(Match match, EntityId structure)
    {
        var producer = match.Production.Producers.Get(structure);
        return [.. Enumerable.Range(0, producer.Count).Select(index => producer[index])];
    }
}
