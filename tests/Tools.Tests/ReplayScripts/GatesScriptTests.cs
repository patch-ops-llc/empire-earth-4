using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>gates</c> of <c>replay record</c> and the golden replay recorded for it: what
/// the script must give, and what that replay must reach.
/// </summary>
public class GatesScriptTests
{
    private const string GoldenReplay = "ridge-gates.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a gate ordered in a line of wall that is being laid",
        "a gate ordered beside a wall",
        "a gate ordered elsewhere",
        "a camp ordered with a gate in its ring",
        "a camp laid whole with a gate in its ring",
        "a gate order taken",
        "a gate order refused: the site is not free",
        "a gate order refused: outside the territory",
        "a gate order cancelled",
        "a unit sent into its own gate",
        "an attack order on a gate",
    ];

    /// <summary>
    /// What the golden replay must reach: every kind of order, and what the orders lead to. The
    /// first two are orders as well, of kinds the script gives in any match with a gate; they
    /// are what they are called here only where a ring with a gate in it has been closed.
    /// </summary>
    private static readonly string[] EveryRule =
    [
        .. EveryKindOfOrder,
        "units sent to where only their gate lets them",
        "fighting units sent to where a gate of an enemy is in their way",
        "a gate order refused: cannot be paid for",
        "a gate laid",
        "a gate finished",
        "a gate finished with wall on two sides of it",
        "a wall order skips the tile of a gate",
        "a gate order ended early",
        "a gate cancelled while it built itself",
        "ground that only the owner of a gate can reach",
        "a unit in its own gate",
        "a unit walks through its own gate",
        "a unit with a gate in its way",
        "a melee hit on a gate",
        "a shot at a gate",
        "a gate destroyed",

        // Under the fog of war (design 5.3): a gate of an enemy is known as a wall tile is.
        // The first is an order as well. Whether a match has a gate that a player with fighting
        // units has never seen turns on where its units have been, so it is asked of this
        // match and not of every match of the script.
        "an attack order on a gate its player has never seen",
        "an attack order on a gate its player has never seen is refused",
        "a unit has a gate in its way that its player came to know after its order",
        "a gate in a unit's way that nobody of its side sees",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map,
    /// beside the scripts that order the walls and earn what they cost.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "9000", "--orders", "1", "--script", "gates,gather,walls");

        var seen = new Watch(ReplayRuns.Start(replay)).Run(replay);

        var missing = string.Join("; ", EveryKindOfOrder.Except(seen).Order(StringComparer.Ordinal));
        Assert.True(missing.Length == 0, $"Not given: {missing}.");
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>By itself the script orders gates, the walls of its camps, and what the workers do with them.</summary>
    [Fact]
    public void TheScriptAloneOrdersGatesAndTheWallsOfItsCamps()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--ticks", "2400", "--orders", "7", "--script", "gates");

        Assert.NotEmpty(replay.Commands);
        Assert.All(replay.Commands, entry => Assert.True(
            entry.Command.Type is CommandType.Build or CommandType.BuildWall or CommandType.CancelBuild or CommandType.Move or CommandType.DebugPlace,
            $"The script gave a command of type {entry.Command.Type}."));
        Assert.All(
            replay.Commands.Where(entry => entry.Command.Type == CommandType.Build),
            entry => Assert.Equal(ReplayRuns.GoldenData.Gate!.Index, entry.Command.ContentId));
    }

    /// <summary>A match without workers has nobody to build a gate: the script gives no orders.</summary>
    [Fact]
    public void TheScriptGivesNothingInAMatchWithoutWorkers()
    {
        var replay = ReplayRuns.Record("--map", "ridge", "--starts", "-,-", "--ticks", "600", "--orders", "33", "--script", "gates");

        Assert.Empty(replay.Commands);
    }

    /// <summary>
    /// What this recorded match is for (plan 18b): gates are built in lines of wall and by
    /// themselves; their owners' units stand in them and walk through them to ground that
    /// nobody else can reach; and the units of an enemy have them in their way as they have
    /// walls, and break them. The match has the orders of <c>walls</c> and of <c>gather</c>
    /// beside the script's own. Every tick is also held against what must never happen: see
    /// <see cref="Watch.CheckWhatMustHold"/>.
    /// </summary>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        Assert.Equal("ridge", replay.Description.MapId);

        var seen = new Watch(ReplayRuns.Start(replay)).Run(replay);

        var missing = string.Join("; ", EveryRule.Except(seen).Order(StringComparer.Ordinal));
        var unexpected = string.Join("; ", seen.Except(EveryRule).Order(StringComparer.Ordinal));
        Assert.True(missing.Length == 0 && unexpected.Length == 0, $"Not reached: {missing}. Reached and not on the list: {unexpected}.");
    }

    /// <summary>
    /// Runs a recorded match tick by tick and says, in the words of the lists above, what its
    /// orders were and what each tick did.
    /// </summary>
    private sealed class Watch(Match match)
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly int _gate = match.Data.Gate!.Index;
        private readonly HashSet<EntityId> _gates = [];
        private readonly HashSet<EntityId> _cancelled = [];
        private readonly HashSet<EntityId> _withGateOrder = [];
        private readonly Dictionary<EntityId, (int X, int Y)> _tileBefore = [];
        private readonly Dictionary<EntityId, (int X, int Y)> _cameFrom = [];
        private readonly Dictionary<(int Player, EntityId Gate), int> _knownSince = [];
        private readonly Dictionary<EntityId, int> _orderedAt = [];
        private bool _laidWhole;

        public HashSet<string> Run(Sim.Serialization.Replay replay)
        {
            var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
            for (var tick = 0; tick < replay.TickCount; tick++)
            {
                Command[] commands = [.. due[tick]];
                TakeBefore();
                OrdersBefore(commands);
                match.Simulation.Tick();
                OrdersAfter(commands);
                WhatTheTickDid();
                CheckWhatMustHold();
            }

            return _seen;
        }

        private static (int X, int Y) TileOf(FixedVector2 point) => (point.X.FloorToInt(), point.Y.FloorToInt());

        /// <summary>True for a gate that stood before the tick, also when the tick took it off the map.</summary>
        private bool IsGate(EntityId entity) => _gates.Contains(entity);

        private bool IsGateOrder(Command command) => command.Type == CommandType.Build && command.ContentId == _gate;

        /// <summary>The gate on a tile, finished or not, or none.</summary>
        private EntityId GateAt((int X, int Y) tile)
        {
            var wall = match.WallMap.WallAt(tile.X, tile.Y);
            return wall != EntityId.None && match.Structures.Get(wall).Type == _gate ? wall : EntityId.None;
        }

        private bool IsWallOf(int player, int x, int y)
        {
            var wall = match.WallMap.WallAt(x, y);
            return wall != EntityId.None && match.Bodies.Get(wall).Owner == player;
        }

        private void TakeBefore()
        {
            _gates.Clear();
            for (var i = 0; i < match.Structures.Count; i++)
            {
                if (match.Structures.ValueAt(i).Type == _gate)
                {
                    _gates.Add(match.Structures.EntityAt(i));
                }
            }

            NoteWhatIsKnown();

            _withGateOrder.Clear();
            for (var i = 0; i < match.BuildOrders.Count; i++)
            {
                if (match.BuildOrders.ValueAt(i).Type == _gate)
                {
                    _withGateOrder.Add(match.BuildOrders.EntityAt(i));
                }
            }

            _tileBefore.Clear();
            for (var i = 0; i < match.Movers.Count; i++)
            {
                var entity = match.Movers.EntityAt(i);
                if (match.World.Entities.IsAlive(entity))
                {
                    _tileBefore[entity] = TileOf(match.Bodies.Get(entity).Position);
                }
            }
        }

        /// <summary>For each gate that stands and each player, the first tick before which the player knew of it.</summary>
        private void NoteWhatIsKnown()
        {
            for (var i = 0; i < match.Structures.Count; i++)
            {
                var gate = match.Structures.EntityAt(i);
                if (match.Structures.ValueAt(i).Type != _gate || !match.World.Entities.IsAlive(gate))
                {
                    continue;
                }

                for (var player = 0; player < match.Players.Count; player++)
                {
                    if (match.Vision.KnowsStructure(player, gate))
                    {
                        _knownSince.TryAdd((player, gate), match.World.Tick);
                    }
                }
            }
        }

        /// <summary>What the commands due in the next tick are, judged on the match as the script saw it.</summary>
        private void OrdersBefore(Command[] commands)
        {
            _cancelled.Clear();
            _laidWhole = commands.Any(command => command.Type == CommandType.DebugPlace && command.ContentId == _gate);
            if (_laidWhole)
            {
                _seen.Add("a camp laid whole with a gate in its ring");
            }

            foreach (var command in commands)
            {
                var entities = command.Entities.ToArray();
                if (command.Type is CommandType.Move or CommandType.AttackMove or CommandType.Attack or CommandType.Stop)
                {
                    foreach (var entity in entities)
                    {
                        _orderedAt[entity] = match.World.Tick;
                    }
                }

                var own = entities.Where(entity =>
                    match.Movers.Contains(entity) && match.Bodies.TryGet(entity, out var body) && body.Owner == command.Player).ToArray();
                var point = new FixedVector2(command.TargetX, command.TargetY);
                var tile = TileOf(point);
                switch (command.Type)
                {
                    case CommandType.Build when command.ContentId == _gate:
                        var planned = false;
                        for (var i = 0; i < match.WallPlans.Count; i++)
                        {
                            var plan = match.WallPlans.ValueAt(i);
                            planned |= plan.Owner == command.Player && (plan.X, plan.Y) == tile;
                        }

                        var beside = IsWallOf(command.Player, tile.X + 1, tile.Y) || IsWallOf(command.Player, tile.X - 1, tile.Y)
                            || IsWallOf(command.Player, tile.X, tile.Y + 1) || IsWallOf(command.Player, tile.X, tile.Y - 1);
                        var runs = commands.Count(other => other.Type == CommandType.BuildWall && other.Player == command.Player);
                        _seen.Add(planned ? "a gate ordered in a line of wall that is being laid"
                            : runs >= 3 ? "a camp ordered with a gate in its ring"
                            : beside ? "a gate ordered beside a wall"
                            : "a gate ordered elsewhere");
                        break;
                    case CommandType.CancelBuild:
                        _cancelled.UnionWith(entities.Where(IsGate));
                        if (entities.Any(_withGateOrder.Contains))
                        {
                            _seen.Add("a gate order cancelled");
                        }

                        break;
                    case CommandType.Move or CommandType.AttackMove when own.Length > 0 && match.Grid.Contains(point):
                        var fighter = own.FirstOrDefault(match.Combatants.Contains);
                        if (fighter != EntityId.None
                            && IsGate(match.Breaching.FindWall(command.Player, match.Bodies.Get(fighter).Position, point, out _)))
                        {
                            _seen.Add("fighting units sent to where a gate of an enemy is in their way");
                        }

                        var gate = GateAt(tile);
                        if (gate != EntityId.None && match.Bodies.Get(gate).Owner == command.Player)
                        {
                            _seen.Add("a unit sent into its own gate");
                        }

                        var walked = match.Grid.For(command.Player);
                        var from = match.Bodies.Get(own[0]).Position;
                        if (match.Grid.RegionOf(point) != 0
                            && walked.RegionOf(point) == walked.RegionOf(from)
                            && match.Grid.RegionOf(point) != match.Grid.RegionOf(from))
                        {
                            _seen.Add("units sent to where only their gate lets them");
                        }

                        break;
                    case CommandType.Attack when IsGate(command.TargetEntity)
                        && match.Bodies.TryGet(command.TargetEntity, out var target)
                        && target.Owner != command.Player
                        && own.Any(match.Combatants.Contains):
                        _seen.Add(match.Vision.KnowsStructure(command.Player, command.TargetEntity)
                            ? "an attack order on a gate"
                            : "an attack order on a gate its player has never seen");
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>What the gate orders of the tick that has just run came to, from its events.</summary>
        private void OrdersAfter(Command[] commands)
        {
            foreach (var reported in match.Simulation.Events)
            {
                // Whether the player knew of the gate is asked after the tick: it comes to know
                // nothing through an order that was refused.
                if (reported.Kind == SimEventKind.OrderRefused
                    && reported.Amount == (long)CommandType.Attack
                    && IsGate(reported.Other)
                    && !match.Vision.KnowsStructure(reported.Player, reported.Other))
                {
                    _seen.Add("an attack order on a gate its player has never seen is refused");
                }
            }

            var given = commands.Count(IsGateOrder);
            if (given == 0)
            {
                return;
            }

            var refused = 0;
            foreach (var reported in match.Simulation.Events)
            {
                if (reported.Kind == SimEventKind.BuildRefused
                    && commands.Any(command => IsGateOrder(command)
                        && command.Player == reported.Player
                        && TileOf(new FixedVector2(command.TargetX, command.TargetY)) == TileOf(reported.Position)))
                {
                    refused++;
                    _seen.Add((BuildProblem)reported.Amount switch
                    {
                        BuildProblem.SiteNotFree => "a gate order refused: the site is not free",
                        BuildProblem.OutsideTerritory => "a gate order refused: outside the territory",
                        BuildProblem.CannotAfford => "a gate order refused: cannot be paid for",
                        var other => $"a gate order refused: {other}",
                    });
                }
            }

            if (given > refused)
            {
                _seen.Add("a gate order taken");
            }
        }

        /// <summary>What the tick that has just run did with gates, from its events and from the state after it.</summary>
        private void WhatTheTickDid()
        {
            foreach (var reported in match.Simulation.Events)
            {
                switch (reported.Kind)
                {
                    case SimEventKind.StructurePlaced when reported.Amount == _gate && !_laidWhole:
                        _seen.Add("a gate laid");
                        break;
                    case SimEventKind.StructureFinished when IsGate(reported.Entity):
                        _seen.Add("a gate finished");
                        var (x, y) = TileOf(reported.Position);
                        var sides = 0;
                        for (var side = 0; side < 9; side++)
                        {
                            sides += side != 4 && IsWallOf(reported.Player, x - 1 + (side % 3), y - 1 + (side / 3)) ? 1 : 0;
                        }

                        if (sides >= 2)
                        {
                            _seen.Add("a gate finished with wall on two sides of it");
                        }

                        break;
                    case SimEventKind.WallTileSkipped when GateAt(TileOf(reported.Position)) != EntityId.None:
                        _seen.Add("a wall order skips the tile of a gate");
                        break;
                    case SimEventKind.BuildOrderEnded when _withGateOrder.Contains(reported.Entity):
                        _seen.Add("a gate order ended early");
                        break;
                    case SimEventKind.StructureRemoved when IsGate(reported.Entity) && _cancelled.Contains(reported.Entity):
                        _seen.Add("a gate cancelled while it built itself");
                        break;
                    case SimEventKind.Death when IsGate(reported.Entity):
                        _seen.Add("a gate destroyed");
                        break;
                    case SimEventKind.MeleeHit when IsGate(reported.Entity):
                        _seen.Add("a melee hit on a gate");
                        break;
                    case SimEventKind.ShotFired when IsGate(match.Shots.Get(reported.Entity).Target):
                        _seen.Add("a shot at a gate");
                        break;
                    default:
                        break;
                }
            }

            for (var player = 0; player < match.Players.Count; player++)
            {
                if (match.Grid.For(player).RegionCount < match.Grid.RegionCount)
                {
                    _seen.Add("ground that only the owner of a gate can reach");
                }
            }

            NoteWhatIsKnown();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var unit = match.Combatants.EntityAt(i);
                var wall = match.Combatants.ValueAt(i).WallInWay;
                if (wall == EntityId.None
                    || !match.World.Entities.IsAlive(unit)
                    || !match.World.Entities.IsAlive(wall)
                    || !match.Structures.TryGet(wall, out var tile)
                    || tile.Type != _gate)
                {
                    continue;
                }

                _seen.Add("a unit with a gate in its way");
                var owner = match.Bodies.Get(unit).Owner;
                if (!match.Vision.Sees(owner, wall))
                {
                    _seen.Add("a gate in a unit's way that nobody of its side sees");
                }

                if (_orderedAt.TryGetValue(unit, out var ordered) && _knownSince.TryGetValue((owner, wall), out var known) && known > ordered)
                {
                    _seen.Add("a unit has a gate in its way that its player came to know after its order");
                }
            }

            for (var i = 0; i < match.Movers.Count; i++)
            {
                var entity = match.Movers.EntityAt(i);
                if (!match.World.Entities.IsAlive(entity) || !_tileBefore.TryGetValue(entity, out var before))
                {
                    _cameFrom.Remove(entity);
                    continue;
                }

                var now = TileOf(match.Bodies.Get(entity).Position);
                if (GateAt(now) != EntityId.None)
                {
                    _seen.Add("a unit in its own gate");
                    if (now != before)
                    {
                        _cameFrom[entity] = before;
                    }
                }
                else if (_cameFrom.Remove(entity, out var from) && Math.Max(Math.Abs(now.X - from.X), Math.Abs(now.Y - from.Y)) == 2)
                {
                    // Out on the side across from the one it came in on.
                    _seen.Add("a unit walks through its own gate");
                }
            }
        }

        /// <summary>
        /// What must hold after every tick: a unit stands on a tile of wall only when the tile
        /// is a finished gate of its own player, and the wall in a unit's way, while it stands,
        /// is a wall or a gate of another player that the unit's player sees or remembers.
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
                if (on == EntityId.None)
                {
                    continue;
                }

                Assert.True(
                    match.Structures.Get(on).Type == _gate
                        && !match.Foundations.Contains(on)
                        && match.Bodies.Get(on).Owner == match.Bodies.Get(entity).Owner,
                    $"Tick {match.World.Tick}: {entity} stands on the wall tile {on}, which is no finished gate of its own.");
            }

            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                var wall = match.Combatants.ValueAt(i).WallInWay;
                if (wall == EntityId.None || !match.World.Entities.IsAlive(entity) || !match.World.Entities.IsAlive(wall))
                {
                    continue;
                }

                Assert.True(
                    match.Structures.TryGet(wall, out var tile) && match.WallMap.IsWall(tile.Type),
                    $"Tick {match.World.Tick}: {entity} has {wall} in its way, which is no wall.");
                Assert.NotEqual(match.Bodies.Get(entity).Owner, match.Bodies.Get(wall).Owner);
                Assert.True(
                    match.Vision.KnowsStructure(match.Bodies.Get(entity).Owner, wall),
                    $"Tick {match.World.Tick}: {entity} has {wall} in its way, which its player has never seen.");
            }
        }
    }
}
