using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>build</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class BuildScriptTests
{
    private const string GoldenReplay = "ridge-builders.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "an order to build a Generator",
        "an order to build an Outpost",
        "an order to build another structure",
        "an order to build something that is no structure",
        "an order to build from nobody in the match",
        "an order to build for one worker",
        "an order to build for a group",
        "an order to build for units that are not the player's",
        "an order to build for an entity that never existed",
        "an order to cancel",
        "an order to gather",
        "an order to move",
        "an order to stop",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// The rarest kind comes about once in 1,100 ticks.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "9000", "--orders", "33", "--script", "build");

        var seen = KindsOfOrders(replay, replay.Description.CreateMatch(ReplayRuns.GoldenData));
        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>A match without workers has nobody to build, and the script gives no orders.</summary>
    [Fact]
    public void TheScriptGivesNothingInAMatchWithoutWorkers()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--starts", "-,-", "--ticks", "600", "--orders", "33", "--script", "build");

        Assert.Empty(replay.Commands);
    }

    /// <summary>
    /// What this recorded match is for (plan 15c): a Generator and an Outpost are built, a build
    /// is cancelled before its foundation and another after it, and workers earn Energy at a
    /// Generator that was built. It also has an Outpost refused on arrival for the enemy's
    /// territory. Throughout it no unit stands on a tile under a structure, no stockpile is below
    /// nothing, and nobody has more of a structure than its limit.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var seen = KindsOfOrders(replay, match);
        var watch = new Watch(match);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            match.Simulation.Tick();
            seen.UnionWith(watch.WhatTheTickDid(tick));
        }

        string[] expected =
        [
            .. EveryKindOfOrder,
            "a build order refused: CannotAfford",
            "a build order refused: NoStructure",
            "a build order refused: OutsideTerritory",
            "a build order refused: SiteNotFree",
            "a build order ended: Cancelled",
            "a build order ended: OtherOrder",
            "a build order ended: TooCloseToEnemyTerritory",
            "a foundation laid",
            "a foundation cancelled with three quarters of its price back",
            "a Generator finished",
            "an Outpost finished",
            "a structure finished: barracks",
            "a territory that an Outpost gave",
            "a worker back at its work after it laid a foundation",
            "a worker paid Energy at a Generator that was built",
            "a unit moved off a site",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    /// <summary>What the commands of this script in a replay are, in the words of <see cref="EveryKindOfOrder"/>.</summary>
    private static HashSet<string> KindsOfOrders(Sim.Serialization.Replay replay, Match match)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var roster = match.Data.Rules.Structures;
        foreach (var (_, command) in replay.Commands)
        {
            switch (command.Type)
            {
                case CommandType.Build:
                    break;
                case CommandType.CancelBuild:
                    seen.Add("an order to cancel");
                    continue;
                case CommandType.Gather:
                    seen.Add("an order to gather");
                    continue;
                case CommandType.Move:
                    seen.Add("an order to move");
                    continue;
                case CommandType.Stop:
                    seen.Add("an order to stop");
                    continue;
                default:
                    Assert.Fail($"The script gave a command of type {command.Type}.");
                    continue;
            }

            if (command.Player >= match.Players.Count)
            {
                seen.Add("an order to build from nobody in the match");
                continue;
            }

            seen.Add(command.ContentId >= roster.Count || match.Construction.Buildable(command.ContentId) is null ? "an order to build something that is no structure"
                : roster[command.ContentId] == "generator" ? "an order to build a Generator"
                : roster[command.ContentId] == "outpost" ? "an order to build an Outpost"
                : "an order to build another structure");
            seen.Add(command.Entities.Length == 1 ? "an order to build for one worker" : "an order to build for a group");
            foreach (var entity in command.Entities)
            {
                if (entity.Value >= 1_000_000)
                {
                    seen.Add("an order to build for an entity that never existed");
                }
                else if (match.Bodies.TryGet(entity, out var body) && body.Owner != command.Player)
                {
                    seen.Add("an order to build for units that are not the player's");
                }
            }
        }

        return seen;
    }

    /// <summary>Follows a match tick by tick and says what each tick did, in the words of the golden replay's list.</summary>
    private sealed class Watch(Match match)
    {
        private readonly Dictionary<EntityId, Foundation> _foundations = [];
        private readonly Dictionary<EntityId, EntityId> _siteOfBuilder = [];
        private readonly HashSet<EntityId> _built = [];
        private readonly Dictionary<EntityId, Sim.Numerics.FixedVector2> _positions = [];
        private readonly Dictionary<EntityId, int> _timers = [];
        private long[] _materials = [.. Enumerable.Range(0, match.Players.Count).Select(match.Stockpiles.Materials)];
        private long[] _energy = [.. Enumerable.Range(0, match.Players.Count).Select(match.Stockpiles.Energy)];
        private Dictionary<EntityId, EntityId> _orders = [];

        public HashSet<string> WhatTheTickDid(int tick)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var roster = match.Data.Rules.Structures;
            var events = match.Simulation.Events;
            var laid = new List<Sim.Map.TileSquare>();
            foreach (var simEvent in events)
            {
                switch (simEvent.Kind)
                {
                    case SimEventKind.BuildRefused:
                        seen.Add($"a build order refused: {(BuildProblem)simEvent.Amount}");
                        break;
                    case SimEventKind.BuildOrderEnded:
                        seen.Add($"a build order ended: {(BuildProblem)simEvent.Amount}");
                        break;
                    case SimEventKind.StructurePlaced:
                        seen.Add("a foundation laid");
                        laid.Add(match.Structures.Get(simEvent.Entity).Tiles);
                        break;
                    case SimEventKind.StructureFinished:
                        _built.Add(simEvent.Entity);
                        seen.Add(roster[(int)simEvent.Amount] switch
                        {
                            "generator" => "a Generator finished",
                            "outpost" => "an Outpost finished",
                            var other => $"a structure finished: {other}",
                        });
                        if (match.Territories.Contains(simEvent.Entity) && roster[(int)simEvent.Amount] == "outpost")
                        {
                            seen.Add("a territory that an Outpost gave");
                        }

                        break;
                    case SimEventKind.StructureRemoved when _foundations.TryGetValue(simEvent.Entity, out var foundation)
                        && !events.Any(other => other.Kind == SimEventKind.Death && other.Entity == simEvent.Entity):
                        // Nothing else pays or is paid in the tick of this replay in which a foundation is cancelled.
                        var materials = match.Stockpiles.Materials(simEvent.Player) - _materials[simEvent.Player];
                        var energy = match.Stockpiles.Energy(simEvent.Player) - _energy[simEvent.Player];
                        if (materials / 1000 == ConstructionRules.RefundOf(foundation.Materials) && energy / 1000 == ConstructionRules.RefundOf(foundation.Energy))
                        {
                            seen.Add("a foundation cancelled with three quarters of its price back");
                        }

                        break;
                    default:
                        break;
                }
            }

            // A worker that had a build order before this tick and has laid its foundation in it.
            foreach (var (worker, site) in _orders)
            {
                if (!match.BuildOrders.Contains(worker)
                    && site != EntityId.None
                    && events.Any(simEvent => simEvent.Kind == SimEventKind.StructurePlaced)
                    && !events.Any(simEvent => simEvent.Kind == SimEventKind.BuildOrderEnded && simEvent.Entity == worker))
                {
                    _siteOfBuilder[worker] = site;
                }
            }

            for (var i = 0; i < match.Workers.Count; i++)
            {
                var entity = match.Workers.EntityAt(i);
                var worker = match.Workers.ValueAt(i);
                if (!match.World.Entities.IsAlive(entity))
                {
                    continue;
                }

                if (_siteOfBuilder.TryGetValue(entity, out var workSite))
                {
                    if (worker.State == WorkerState.Working && worker.Site == workSite)
                    {
                        seen.Add("a worker back at its work after it laid a foundation");
                    }
                    else if (worker.Site != workSite)
                    {
                        _siteOfBuilder.Remove(entity);
                    }
                }

                // A payment is due in the tick in which a working worker's count goes back to nothing.
                var paid = worker.State == WorkerState.Working && worker.Timer == 0 && _timers.GetValueOrDefault(entity) == Sim.SimInfo.TicksPerSecond - 1;
                var owner = match.Bodies.Get(entity).Owner;
                if (paid && _built.Contains(worker.Site) && match.Stockpiles.Energy(owner) > _energy[owner])
                {
                    seen.Add("a worker paid Energy at a Generator that was built");
                }

                _timers[entity] = worker.State == WorkerState.Working ? worker.Timer : -1;
            }

            for (var i = 0; i < match.Movers.Count; i++)
            {
                var unit = match.Movers.EntityAt(i);
                if (!match.World.Entities.IsAlive(unit))
                {
                    continue;
                }

                var position = match.Bodies.Get(unit).Position;
                Assert.True(match.Grid.IsPassable(position), $"Unit {unit.Value} stands on blocked ground at {position} in tick {tick}.");
                // A unit that stood on a site laid in this tick, and is further from where it stood than a tick's walk takes it.
                if (_positions.TryGetValue(unit, out var before)
                    && laid.Any(site => site.Covers(before))
                    && Sim.Numerics.FixedVector2.DistanceSquaredRaw(before, position) >= Sim.Numerics.FixedVector2.SquareRaw(Sim.Numerics.Fixed.One / 2))
                {
                    seen.Add("a unit moved off a site");
                }

                _positions[unit] = position;
            }

            _foundations.Clear();
            for (var i = 0; i < match.Foundations.Count; i++)
            {
                _foundations[match.Foundations.EntityAt(i)] = match.Foundations.ValueAt(i);
            }

            _orders = [];
            for (var i = 0; i < match.BuildOrders.Count; i++)
            {
                var worker = match.BuildOrders.EntityAt(i);
                _orders[worker] = match.Workers.Get(worker).Site;
            }

            for (var player = 0; player < match.Players.Count; player++)
            {
                _materials[player] = match.Stockpiles.Materials(player);
                _energy[player] = match.Stockpiles.Energy(player);
                Assert.True(_materials[player] >= 0 && _energy[player] >= 0, $"Player {player} owes resources in tick {tick}.");
                foreach (var structure in match.Data.Structures.Where(def => def.MaxPerPlayer is not null))
                {
                    Assert.True(match.Construction.CountOf(player, structure.Index) <= structure.MaxPerPlayer, $"Player {player} has more of '{structure.Id}' than its limit in tick {tick}.");
                }
            }

            return seen;
        }
    }
}
