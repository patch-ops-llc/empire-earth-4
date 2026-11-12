using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>walls</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class WallsScriptTests
{
    private const string GoldenReplay = "ridge-walls.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "an Outpost placed beside a chokepoint",
        "a wall order across a chokepoint",
        "a wall order for a camp",
        "a wall order for another run",
        "a wall order taken",
        "a wall order refused: no worker",
        "a wall order refused: no tile that can be laid",
        "a wall order refused: outside the territory",
        "a wall order refused: cannot be paid for",
        "a wall order cancelled",
        "a worker with a wall order sent elsewhere",
        "a fighting unit made",
        "a move order to a place behind enemy walls",
        "an attack-move to a place behind enemy walls",
        "an attack order",
        "a stop",
    ];

    /// <summary>What the golden replay must reach: every kind of order, and what the orders lead to.</summary>
    private static readonly string[] EveryRule =
    [
        .. EveryKindOfOrder,
        "a tile of wall laid",
        "a tile of wall finished",
        "a tile skipped: the site is not free",
        "a tile skipped: its worker cannot get to it",
        "a wall order ended early",
        "a tile of wall cancelled while it built itself",
        "a wall finished across a chokepoint",
        "ground closed off by walls that touch at their corners",
        "a unit on a move order with a wall in its way",
        "a unit on an attack-move with a wall in its way",
        "a unit on an attack order with a wall in its way",
        "a unit given its wall with its order",
        "a unit that found its wall without a new order",
        "a melee unit walking to its place by the wall in its way",
        "a melee unit on its place by the wall in its way",
        "a melee hit on the wall in a unit's way",
        "a shot at the wall in a unit's way",
        "a tile of wall destroyed",
        "a unit goes on when its wall is destroyed",
        "a unit goes on when the way has opened elsewhere",
        "a unit that broke in ends its order",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "9000", "--orders", "7", "--script", "walls");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
        var seen = new Watch(ReplayRuns.Start(replay)).Run(replay, command => true);

        var missing = string.Join("; ", EveryKindOfOrder.Except(seen).Order(StringComparer.Ordinal));
        Assert.True(missing.Length == 0, $"Not given: {missing}.");
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>A match without workers has nobody to lay a wall, and no wall to break: the script gives no orders.</summary>
    [Fact]
    public void TheScriptGivesNothingInAMatchWithoutWorkers()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--starts", "-,-", "--ticks", "600", "--orders", "33", "--script", "walls");

        Assert.Empty(replay.Commands);
    }

    /// <summary>
    /// What this recorded match is for (plan 18a): a wall is built across the north pass of
    /// <c>ridge</c>, the only chokepoint of that map; camps are closed by walls that touch only
    /// at their corners; and units with every kind of order break in at the tile in their way
    /// and go on. The match has gather orders beside the script's own, so that the players can
    /// pay for more than their first walls. Every tick is also held against what must never
    /// happen: see <see cref="Watch.CheckWhatMustHold"/>.
    /// </summary>
    [Fact(Skip = TestBuild.WallsMeetTheRest)]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        Assert.Equal("ridge", replay.Description.MapId);

        Assert.Single(ReplayRuns.GoldenData.Maps.Single(map => map.Id == "ridge").Markers, marker => marker.Kind == MarkerKind.Chokepoint);

        var seen = new Watch(ReplayRuns.Start(replay)).Run(replay, IsOfThisScript);

        var missing = string.Join("; ", EveryRule.Except(seen).Order(StringComparer.Ordinal));
        var unexpected = string.Join("; ", seen.Except(EveryRule).Order(StringComparer.Ordinal));
        Assert.True(missing.Length == 0 && unexpected.Length == 0, $"Not reached: {missing}. Reached and not on the list: {unexpected}.");
    }

    private static bool IsOfThisScript(Command command) =>
        command.Type is CommandType.BuildWall or CommandType.CancelBuild or CommandType.DebugPlace or CommandType.DebugSpawn
            or CommandType.Move or CommandType.AttackMove or CommandType.Attack or CommandType.Stop;

    /// <summary>
    /// Runs a recorded match tick by tick and says, in the words of the lists above, what its
    /// orders were and what each tick did.
    /// </summary>
    private sealed class Watch(Match match)
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly Dictionary<EntityId, EntityId> _wallInWay = [];
        private readonly HashSet<EntityId> _brokeIn = [];
        private readonly HashSet<EntityId> _withWallOrder = [];
        private readonly HashSet<EntityId> _walls = [];
        private readonly HashSet<EntityId> _cancelled = [];
        private readonly HashSet<EntityId> _ordered = [];
        private readonly int _regionsAtStart = match.Grid.RegionCount;

        public HashSet<string> Run(Sim.Serialization.Replay replay, Func<Command, bool> ofThisScript)
        {
            var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
            for (var tick = 0; tick < replay.TickCount; tick++)
            {
                Step([.. due[tick].Where(ofThisScript)]);
            }

            return _seen;
        }

        private void Step(Command[] commands)
        {
            TakeBefore();
            OrdersBefore(commands);
            match.Simulation.Tick();
            OrdersAfter(commands);
            WhatTheTickDid();
            CheckWhatMustHold();
        }

        private bool IsWall(int structure) => match.Data.StructureOf(structure)?.Wall is not null;

        /// <summary>True for a tile of wall that stood before the tick, also when the tick took it off the map.</summary>
        private bool IsWall(EntityId entity) => _walls.Contains(entity);

        private void TakeBefore()
        {
            _wallInWay.Clear();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                _wallInWay[match.Combatants.EntityAt(i)] = match.Combatants.ValueAt(i).WallInWay;
            }

            _walls.Clear();
            for (var i = 0; i < match.Structures.Count; i++)
            {
                if (IsWall(match.Structures.ValueAt(i).Type))
                {
                    _walls.Add(match.Structures.EntityAt(i));
                }
            }

            _withWallOrder.Clear();
            for (var i = 0; i < match.WallPlans.Count; i++)
            {
                _withWallOrder.Add(match.WallPlans.ValueAt(i).Worker);
            }
        }

        /// <summary>What the commands due in the next tick are, judged on the match as the script saw it.</summary>
        private void OrdersBefore(Command[] commands)
        {
            _ordered.Clear();
            _cancelled.Clear();
            foreach (var command in commands)
            {
                var entities = command.Entities.ToArray();
                if (command.Type == CommandType.CancelBuild)
                {
                    _cancelled.UnionWith(entities.Where(match.Foundations.Contains));
                }

                var fighters = entities.Where(entity =>
                    match.Combatants.Contains(entity) && match.Bodies.TryGet(entity, out var body) && body.Owner == command.Player).ToArray();
                var point = new FixedVector2(command.TargetX, command.TargetY);
                var behindWalls = match.Grid.Contains(point)
                    && fighters.Any(fighter => match.Breaching.IsCutOff(command.Player, match.Bodies.Get(fighter).Position, point));
                switch (command.Type)
                {
                    case CommandType.BuildWall:
                        var first = (X: command.TargetX.FloorToInt(), Y: command.TargetY.FloorToInt());
                        var last = (X: command.ContentId % match.Grid.Width, Y: command.ContentId / match.Grid.Width);
                        var across = match.Map.Markers.Any(marker => marker.Kind == MarkerKind.Chokepoint && IsAcross(marker.Area, marker.Across, first, last));
                        var camp = commands.Count(other => other.Type == CommandType.BuildWall && other.Player == command.Player) >= 4;
                        _seen.Add(across ? "a wall order across a chokepoint" : camp ? "a wall order for a camp" : "a wall order for another run");
                        break;
                    case CommandType.CancelBuild when entities.Any(_withWallOrder.Contains):
                        _seen.Add("a wall order cancelled");
                        break;
                    case CommandType.Move or CommandType.Stop when entities.Any(_withWallOrder.Contains):
                        _seen.Add("a worker with a wall order sent elsewhere");
                        break;
                    case CommandType.DebugSpawn when match.Grid.IsPassable(point):
                        _seen.Add("a fighting unit made");
                        break;
                    case CommandType.Move when behindWalls:
                        _seen.Add("a move order to a place behind enemy walls");
                        break;
                    case CommandType.AttackMove when behindWalls:
                        _seen.Add("an attack-move to a place behind enemy walls");
                        break;
                    case CommandType.Attack when fighters.Length > 0:
                        _seen.Add("an attack order");
                        break;
                    case CommandType.Stop when fighters.Length > 0:
                        _seen.Add("a stop");
                        break;
                    default:
                        break;
                }

                if (command.Type is CommandType.Move or CommandType.AttackMove or CommandType.Attack)
                {
                    _ordered.UnionWith(fighters);
                }
            }
        }

        private static bool IsAcross(TileRect area, MapAxis across, (int X, int Y) first, (int X, int Y) last) =>
            across == MapAxis.Y
                ? first.X == last.X && first.X >= area.X && first.X < area.X + area.SizeX && first.Y == area.Y && last.Y == area.Y + area.SizeY - 1
                : first.Y == last.Y && first.Y >= area.Y && first.Y < area.Y + area.SizeY && first.X == area.X && last.X == area.X + area.SizeX - 1;

        /// <summary>What the commands of the tick that has just run came to, from its events.</summary>
        private void OrdersAfter(Command[] commands)
        {
            var refused = 0;
            foreach (var reported in match.Simulation.Events)
            {
                if (reported.Kind == SimEventKind.BuildRefused && commands.Any(command => command.Type == CommandType.BuildWall))
                {
                    refused++;
                    _seen.Add((BuildProblem)reported.Amount switch
                    {
                        BuildProblem.NoWorker => "a wall order refused: no worker",
                        BuildProblem.SiteNotFree => "a wall order refused: no tile that can be laid",
                        BuildProblem.OutsideTerritory => "a wall order refused: outside the territory",
                        BuildProblem.CannotAfford => "a wall order refused: cannot be paid for",
                        var other => $"a wall order refused: {other}",
                    });
                }

                if (reported.Kind == SimEventKind.StructurePlaced
                    && match.Data.StructureOf((int)reported.Amount)?.TerritoryRadius is not null
                    && commands.Any(command => command.Type == CommandType.DebugPlace))
                {
                    _seen.Add("an Outpost placed beside a chokepoint");
                }
            }

            if (commands.Count(command => command.Type == CommandType.BuildWall) > refused)
            {
                _seen.Add("a wall order taken");
            }
        }

        /// <summary>What the tick that has just run did with walls, from its events and from the state after it.</summary>
        private void WhatTheTickDid()
        {
            foreach (var reported in match.Simulation.Events)
            {
                switch (reported.Kind)
                {
                    case SimEventKind.StructurePlaced when IsWall((int)reported.Amount):
                        _seen.Add("a tile of wall laid");
                        break;
                    case SimEventKind.StructureFinished when IsWall(reported.Entity):
                        _seen.Add("a tile of wall finished");
                        break;
                    case SimEventKind.WallTileSkipped:
                        _seen.Add((BuildProblem)reported.Amount switch
                        {
                            BuildProblem.SiteNotFree => "a tile skipped: the site is not free",
                            BuildProblem.CannotReach => "a tile skipped: its worker cannot get to it",
                            var other => $"a tile skipped: {other}",
                        });
                        break;
                    case SimEventKind.BuildOrderEnded when _withWallOrder.Contains(reported.Entity):
                        _seen.Add("a wall order ended early");
                        break;
                    case SimEventKind.StructureRemoved when IsWall(reported.Entity) && _cancelled.Contains(reported.Entity):
                        _seen.Add("a tile of wall cancelled while it built itself");
                        break;
                    case SimEventKind.Death when IsWall(reported.Entity):
                        _seen.Add("a tile of wall destroyed");
                        break;
                    case SimEventKind.MeleeHit when _wallInWay.TryGetValue(reported.Other, out var struck) && struck == reported.Entity:
                        _seen.Add("a melee hit on the wall in a unit's way");
                        break;
                    case SimEventKind.ShotFired when _wallInWay.TryGetValue(reported.Other, out var aimedAt)
                        && aimedAt != EntityId.None
                        && match.Shots.Get(reported.Entity).Target == aimedAt:
                        _seen.Add("a shot at the wall in a unit's way");
                        break;
                    default:
                        break;
                }
            }

            foreach (var marker in match.Map.Markers)
            {
                if (marker.Kind == MarkerKind.Chokepoint && IsClosed(marker))
                {
                    _seen.Add("a wall finished across a chokepoint");
                }
            }

            if (match.Grid.RegionCount > _regionsAtStart)
            {
                _seen.Add("ground closed off by walls that touch at their corners");
            }

            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                var unit = match.Combatants.ValueAt(i);
                if (!match.World.Entities.IsAlive(entity))
                {
                    continue;
                }

                var before = _wallInWay.GetValueOrDefault(entity);
                if (unit.WallInWay != EntityId.None)
                {
                    _seen.Add(unit.Order switch
                    {
                        UnitOrder.Move => "a unit on a move order with a wall in its way",
                        UnitOrder.AttackMove => "a unit on an attack-move with a wall in its way",
                        UnitOrder.Attack => "a unit on an attack order with a wall in its way",
                        _ => "an idle unit with a wall in its way",
                    });
                    if (unit.WallInWay != before)
                    {
                        _seen.Add(_ordered.Contains(entity) ? "a unit given its wall with its order" : "a unit that found its wall without a new order");
                    }

                    if (unit.PlaceFor == unit.WallInWay)
                    {
                        _seen.Add(match.Bodies.Get(entity).Position == unit.Place
                            ? "a melee unit on its place by the wall in its way"
                            : "a melee unit walking to its place by the wall in its way");
                    }
                }

                if (before != EntityId.None && unit.WallInWay != before && unit.Order != UnitOrder.Idle && !_ordered.Contains(entity))
                {
                    _brokeIn.Add(entity);
                    _seen.Add(match.World.Entities.IsAlive(before) ? "a unit goes on when the way has opened elsewhere" : "a unit goes on when its wall is destroyed");
                }

                if (unit.Order == UnitOrder.Idle && _brokeIn.Remove(entity))
                {
                    _seen.Add("a unit that broke in ends its order");
                }
            }
        }

        /// <summary>True when some line of tiles across the chokepoint is finished wall from side to side.</summary>
        private bool IsClosed(MapMarker marker)
        {
            var area = marker.Area;
            var lines = marker.Across == MapAxis.Y ? area.SizeX : area.SizeY;
            var length = marker.Across == MapAxis.Y ? area.SizeY : area.SizeX;
            for (var line = 0; line < lines; line++)
            {
                var closed = true;
                for (var step = 0; step < length && closed; step++)
                {
                    var (x, y) = marker.Across == MapAxis.Y ? (area.X + line, area.Y + step) : (area.X + step, area.Y + line);
                    var wall = match.WallMap.WallAt(x, y);
                    closed = wall != EntityId.None && !match.Foundations.Contains(wall);
                }

                if (closed)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What must hold after every tick: no unit stands on a tile of wall, and the wall in a
        /// unit's way, while it stands, is a wall of another player.
        /// </summary>
        private void CheckWhatMustHold()
        {
            for (var i = 0; i < match.Movers.Count; i++)
            {
                var entity = match.Movers.EntityAt(i);
                if (!match.World.Entities.IsAlive(entity))
                {
                    continue;
                }

                var position = match.Bodies.Get(entity).Position;
                var on = match.WallMap.WallAt(position.X.FloorToInt(), position.Y.FloorToInt());
                Assert.True(on == EntityId.None, $"Tick {match.World.Tick}: {entity} stands on the wall tile {on}.");
            }

            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                var wall = match.Combatants.ValueAt(i).WallInWay;
                if (wall == EntityId.None || !match.World.Entities.IsAlive(entity) || !match.World.Entities.IsAlive(wall))
                {
                    continue;
                }

                Assert.True(match.Structures.TryGet(wall, out var tile) && IsWall(tile.Type), $"Tick {match.World.Tick}: {entity} has {wall} in its way, which is no wall.");
                Assert.NotEqual(match.Bodies.Get(entity).Owner, match.Bodies.Get(wall).Owner);
            }
        }
    }
}
