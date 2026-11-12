using Sim.Combat;
using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Vision;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The script <c>fog</c> of <c>replay record</c> and the golden replay recorded for it: what the
/// script must give, and what that replay must reach.
/// </summary>
public class FogScriptTests
{
    private const string GoldenReplay = "switchback-fog.replay";

    private static readonly string[] EveryKindOfOrder =
    [
        "a unit made",
        "a unit that reaches further than it sees made within its sight of a unit of another player",
        "units sent across the map",
        "an attack-move across the map",
        "an attack on a unit the player sees",
        "an attack on a unit the player does not see",
        "an attack on a unit that its owner sends away in the same tick",
        "a unit sent away from a shot that has far to go",
        "a stop",
    ];

    /// <summary>
    /// The script gives every kind of order it is meant to: here in one long match on each map.
    /// </summary>
    [Theory]
    [InlineData("ridge")]
    [InlineData("switchback")]
    public void TheScriptGivesEveryKindOfItsOrders(string map)
    {
        var replay = ReplayRuns.Record("--map", map, "--ticks", "6000", "--orders", "33", "--script", "fog");

        Assert.All(replay.Commands, entry => Assert.True(IsOfThisScript(entry.Command), $"The script gave a command of type {entry.Command.Type}."));
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            seen.UnionWith(KindsOfOrders(match, [.. due[tick]]));
            match.Simulation.Tick();
        }

        Assert.Equal(EveryKindOfOrder.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.True(replay.Run(ReplayRuns.GoldenData).Reproduced);
    }

    /// <summary>
    /// What this recorded match is for: it must reach the rules of the fog of war that a change
    /// could break (design 5.3; design 8.4, rows 38 to 40). The match has the orders of this
    /// script, of gathering and of research, so that workers are at deposits when they are found
    /// and tiers rise under units that see by them. It has not the orders of movement and of
    /// fights: those order every unit about so often that a unit sent to where its target was
    /// last seen seldom gets there.
    /// </summary>
    /// <remarks>
    /// The match was recorded before a shell had a blast and before Towers (16a, 16b). Under those
    /// rules its fights go another way, and the one unit its owner sent away from a shot is no
    /// longer under fire in the tick it is sent. What that order was for is still reached: a shot
    /// that lands on a target its owner no longer sees. Nor does a target of an attack order die
    /// in it any more while the order stands, so "a unit whose attack order ends where it stands
    /// because its target died" is not reached here: the replay of the script <c>combat-perks</c>
    /// reaches it, and <see cref="CombatPerkScriptTests"/> has it on its list. An existing replay
    /// is never recorded again, so this one keeps the orders it has.
    /// </remarks>
    [Fact]
    public void ItsGoldenReplayReachesTheRulesItGuards()
    {
        var replay = ReplayRuns.ReadGolden(GoldenReplay);
        var match = ReplayRuns.Start(replay);
        var due = replay.Commands.ToLookup(entry => entry.Tick, entry => entry.Command);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var watch = new Watch(match);
        for (var tick = 0; tick < replay.TickCount; tick++)
        {
            Command[] commands = [.. due[tick]];
            seen.UnionWith(KindsOfOrders(match, [.. commands.Where(IsOfThisScript)]));
            watch.BeforeTheTick(commands);
            match.Simulation.Tick();
            seen.UnionWith(watch.WhatTheTickDid(tick));
        }

        string[] expected =
        [
            .. EveryKindOfOrder.Where(kind => kind != "a unit sent away from a shot that has far to go"),
            "sight worked out with another result",
            "a tick in which sight is not worked out",
            "a unit that sees further than a worker",
            "ground that is explored and no longer visible",
            "an enemy unit on ground a player has explored and does not see now",
            "an enemy unit seen",
            "an enemy unit that comes into sight",
            "an enemy unit that goes out of sight by walking off the tiles its enemy sees",
            "an attack order taken for a unit the player sees",
            "an attack order refused for a unit the player does not see",
            "a unit sent to where its target was last seen",
            "a unit idle after it walked to where its target was last seen",
            "a unit fighting a target that only another unit of its owner sees",
            "a shot that lands on a target its owner no longer sees",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));

        // By the end every player has been to more of the map than it sees, and none has seen all of it.
        var grid = match.Vision.Grid;
        for (var player = 0; player < match.Players.Count; player++)
        {
            Assert.True(grid.CountExplored(player) > 2 * grid.CountVisible(player), $"Player {player} has explored {grid.CountExplored(player)} tiles.");
            Assert.True(grid.CountExplored(player) < grid.Width * grid.Height);
        }
    }

    private static bool IsOfThisScript(Command command) =>
        command.Type is CommandType.DebugSpawn or CommandType.Move or CommandType.AttackMove or CommandType.Attack or CommandType.Stop;

    /// <summary>
    /// What the commands that are due in the next tick are, in the words of
    /// <see cref="EveryKindOfOrder"/>. The match is as it is before that tick, which is what the
    /// script saw when it gave them, and what the players see is what they see when the commands run.
    /// </summary>
    private static HashSet<string> KindsOfOrders(Match match, IReadOnlyList<Command> commands)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var players = match.Players.Count;
        foreach (var command in commands)
        {
            if (command.Player >= players)
            {
                continue;
            }

            switch (command.Type)
            {
                case CommandType.DebugSpawn when IsMade(match, command):
                    seen.Add(IsLongReachBesideAnEnemy(match, command)
                        ? "a unit that reaches further than it sees made within its sight of a unit of another player"
                        : "a unit made");
                    break;
                case CommandType.Move when IsSentAwayFromAShot(match, command):
                    seen.Add("a unit sent away from a shot that has far to go");
                    break;
                case CommandType.Move when OwnFighters(match, command) > 0 && !IsSentAway(match, command, commands):
                    seen.Add("units sent across the map");
                    break;
                case CommandType.AttackMove when OwnFighters(match, command) > 0:
                    seen.Add("an attack-move across the map");
                    break;
                case CommandType.Attack when OwnFighters(match, command) > 0 && IsEnemy(match, command):
                    seen.Add(match.Vision.Sees(command.Player, command.TargetEntity)
                        ? "an attack on a unit the player sees"
                        : "an attack on a unit the player does not see");
                    if (commands.Any(other => IsSentAwayBy(match, command, other)))
                    {
                        seen.Add("an attack on a unit that its owner sends away in the same tick");
                    }

                    break;
                case CommandType.Stop when OwnFighters(match, command) > 0:
                    seen.Add("a stop");
                    break;
                default:
                    break;
            }
        }

        return seen;
    }

    private static bool IsMade(Match match, Command command)
    {
        var point = new FixedVector2(command.TargetX, command.TargetY);
        return command.ContentId >= 0 && command.ContentId < match.Data.Chassis.Count && match.Grid.Contains(point) && match.Grid.IsPassable(point);
    }

    /// <summary>True when a spawn is of a chassis whose range is more than its sight, with a unit of another player within that sight.</summary>
    private static bool IsLongReachBesideAnEnemy(Match match, Command spawn)
    {
        var blueprint = match.Research.Tech(spawn.Player).Blueprints[spawn.ContentId];
        var point = new FixedVector2(spawn.TargetX, spawn.TargetY);
        var sight = Fixed.FromInt(blueprint.Sight);
        return blueprint.Range is { } range
            && range > sight
            && Enumerable.Range(0, match.Bodies.Count).Any(i =>
                match.Bodies.ValueAt(i).Owner != spawn.Player
                && match.World.Entities.IsAlive(match.Bodies.EntityAt(i))
                && !match.Structures.Contains(match.Bodies.EntityAt(i))
                && FixedVector2.DistanceSquaredRaw(match.Bodies.ValueAt(i).Position, point) <= FixedVector2.SquareRaw(sight));
    }

    /// <summary>True when a move order is for one unit, from its owner, at which a shot of another player is in flight from 6 tiles or further.</summary>
    private static bool IsSentAwayFromAShot(Match match, Command move) =>
        move.Entities.Length == 1
        && match.Bodies.TryGet(move.Entities[0], out var target)
        && target.Owner == move.Player
        && Enumerable.Range(0, match.Shots.Count).Any(i =>
            match.Shots.ValueAt(i).Target == move.Entities[0]
            && match.Shots.ValueAt(i).Owner != move.Player
            && FixedVector2.DistanceSquaredRaw(match.Shots.ValueAt(i).Position, target.Position) >= FixedVector2.SquareRaw(Fixed.FromInt(6)));

    /// <summary>The fighting units of the command's player among those it lists.</summary>
    private static int OwnFighters(Match match, Command command) =>
        command.Entities.ToArray().Count(entity => match.Combatants.Contains(entity) && match.Bodies.Get(entity).Owner == command.Player);

    /// <summary>True when an attack order names a living unit of another player.</summary>
    private static bool IsEnemy(Match match, Command attack) =>
        match.World.Entities.IsAlive(attack.TargetEntity)
        && match.Bodies.TryGet(attack.TargetEntity, out var target)
        && target.Owner != attack.Player
        && match.Health.Contains(attack.TargetEntity);

    /// <summary>True when a move order is its owner's for the one unit an attack order of the same tick names.</summary>
    private static bool IsSentAwayBy(Match match, Command attack, Command move) =>
        move.Type == CommandType.Move
        && move.Entities.Length == 1
        && move.Entities[0] == attack.TargetEntity
        && match.Bodies.TryGet(attack.TargetEntity, out var target)
        && target.Owner == move.Player;

    private static bool IsSentAway(Match match, Command move, IReadOnlyList<Command> commands) =>
        commands.Any(attack => attack.Type == CommandType.Attack && IsSentAwayBy(match, attack, move));

    /// <summary>
    /// Follows a match tick by tick and says what each tick did, in the words of the list of
    /// <see cref="ItsGoldenReplayReachesTheRulesItGuards"/>. It looks at the match as a test may:
    /// at all of it, and at what each player sees.
    /// </summary>
    private sealed class Watch(Match match)
    {
        private readonly Dictionary<EntityId, Combatant> _unitsBefore = [];
        private readonly Dictionary<(int Player, EntityId Unit), bool> _seenBefore = [];
        private readonly Dictionary<EntityId, FixedVector2> _positionsBefore = [];
        private readonly HashSet<EntityId> _ordered = [];
        private readonly HashSet<EntityId> _walkingToWhereLastSeen = [];
        private readonly List<(Command Attack, bool Seen)> _attacks = [];
        private int _revisionBefore;

        /// <summary>Notes what the tick will change. Call it with the commands that are due in the tick.</summary>
        public void BeforeTheTick(IReadOnlyList<Command> commands)
        {
            _unitsBefore.Clear();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                _unitsBefore[match.Combatants.EntityAt(i)] = match.Combatants.ValueAt(i);
            }

            _seenBefore.Clear();
            _positionsBefore.Clear();
            for (var i = 0; i < match.Bodies.Count; i++)
            {
                var entity = match.Bodies.EntityAt(i);
                _positionsBefore[entity] = match.Bodies.ValueAt(i).Position;
                for (var player = 0; player < match.Players.Count; player++)
                {
                    _seenBefore[(player, entity)] = match.Vision.Sees(player, entity);
                }
            }

            _ordered.Clear();
            _attacks.Clear();
            foreach (var command in commands)
            {
                // The four orders a fighting unit takes. A gather order lists it and leaves it as it is.
                if (command.Type is CommandType.Move or CommandType.AttackMove or CommandType.Attack or CommandType.Stop)
                {
                    _ordered.UnionWith(command.Entities.ToArray());
                }

                if (command.Type == CommandType.Attack && command.Player < match.Players.Count && OwnFighters(match, command) > 0 && IsEnemy(match, command))
                {
                    _attacks.Add((command, match.Vision.Sees(command.Player, command.TargetEntity)));
                }
            }

            _walkingToWhereLastSeen.ExceptWith(_ordered);
            _revisionBefore = match.Vision.Grid.Revision;
        }

        public HashSet<string> WhatTheTickDid(int tick)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var grid = match.Vision.Grid;
            var refreshTicks = match.Data.Rules.Vision.RefreshTicks;
            if (tick % refreshTicks != 0)
            {
                seen.Add("a tick in which sight is not worked out");
                Assert.Equal(_revisionBefore, grid.Revision);
            }
            else if (grid.Revision != _revisionBefore)
            {
                seen.Add("sight worked out with another result");
            }

            foreach (var (attack, wasSeen) in _attacks)
            {
                var units = attack.Entities.ToArray().Where(match.Combatants.Contains).ToArray();
                var refused = match.Simulation.Events.Any(reported =>
                    reported.Kind == SimEventKind.OrderRefused
                    && (CommandType)reported.Amount == CommandType.Attack
                    && reported.Player == attack.Player
                    && reported.Other == attack.TargetEntity);
                if (!wasSeen)
                {
                    // The fog alone refuses it: the player has a fighting unit for it, and the target is an enemy that lives.
                    Assert.True(refused, $"Tick {tick}: an attack order on a unit its player does not see was carried out.");
                    seen.Add("an attack order refused for a unit the player does not see");
                }
                else if (!refused && units.Any(unit => match.Combatants.Get(unit) is { Order: UnitOrder.Attack } taken && taken.Target == attack.TargetEntity))
                {
                    seen.Add("an attack order taken for a unit the player sees");
                }
            }

            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                var unit = match.Combatants.ValueAt(i);
                if (!match.World.Entities.IsAlive(entity) || !_unitsBefore.TryGetValue(entity, out var before))
                {
                    continue;
                }

                var body = match.Bodies.Get(entity);
                seen.UnionWith(WhatBecameOfItsAttackOrder(entity, in unit, in before, in body));
                if (_walkingToWhereLastSeen.Contains(entity) && unit.Order != UnitOrder.Move)
                {
                    _walkingToWhereLastSeen.Remove(entity);
                    if (unit.Order == UnitOrder.Idle && match.Movers.Get(entity).State == MoveState.Idle)
                    {
                        seen.Add("a unit idle after it walked to where its target was last seen");
                    }
                }

                if (match.Vision.TryGetSight(entity, out var sight) && sight > Fixed.FromInt(match.Data.Resources.Worker.Sight))
                {
                    seen.Add("a unit that sees further than a worker");
                }

                // A target is only ever one its owner sees. Here the unit would not see it on its own.
                if (unit.Target != EntityId.None
                    && match.World.Entities.IsAlive(unit.Target)
                    && match.Bodies.TryGet(unit.Target, out var target)
                    && match.Vision.Sees(body.Owner, in target)
                    && FixedVector2.DistanceSquaredRaw(body.Position, TileCenter(target.Position)) > FixedVector2.SquareRaw(sight))
                {
                    seen.Add("a unit fighting a target that only another unit of its owner sees");
                }
            }

            foreach (var reported in match.Simulation.Events)
            {
                if (reported.Kind == SimEventKind.ShotHit
                    && match.World.Entities.IsAlive(reported.Entity)
                    && !match.Vision.Sees(reported.Player, reported.Entity))
                {
                    seen.Add("a shot that lands on a target its owner no longer sees");
                }
            }

            seen.UnionWith(WhatThePlayersSee(tick % refreshTicks == 0));
            _walkingToWhereLastSeen.RemoveWhere(unit => !match.World.Entities.IsAlive(unit));
            return seen;
        }

        private static FixedVector2 TileCenter(FixedVector2 position) =>
            new(Fixed.FromInt(position.X.FloorToInt()) + Fixed.Half, Fixed.FromInt(position.Y.FloorToInt()) + Fixed.Half);

        /// <summary>What a unit that was on an attack order before the tick, and was given no order in it, is doing now.</summary>
        private HashSet<string> WhatBecameOfItsAttackOrder(EntityId entity, in Combatant unit, in Combatant before, in Sim.Map.Body body)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (before.Order != UnitOrder.Attack || unit.Order == UnitOrder.Attack || _ordered.Contains(entity))
            {
                return seen;
            }

            var targetLives = match.World.Entities.IsAlive(before.Target) && match.Bodies.Contains(before.Target);
            if (!targetLives)
            {
                Assert.Equal(UnitOrder.Idle, unit.Order);
                Assert.Equal(body.Position, unit.Anchor);
                seen.Add("a unit whose attack order ends where it stands because its target died");
                return seen;
            }

            // The target lives, so only the fog ends the order: the unit is sent to where the
            // target was when its owner saw it last, which the order kept as it went.
            Assert.False(match.Vision.Sees(body.Owner, before.Target));
            Assert.Equal(EntityId.None, unit.Target);
            if (unit.Order == UnitOrder.Move)
            {
                Assert.Equal(match.Movers.Get(entity).Destination, unit.Anchor);
                _walkingToWhereLastSeen.Add(entity);
                seen.Add("a unit sent to where its target was last seen");
            }
            else
            {
                // It could not walk there, or stood there already.
                Assert.Equal(UnitOrder.Idle, unit.Order);
            }

            return seen;
        }

        private HashSet<string> WhatThePlayersSee(bool workedOut)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var grid = match.Vision.Grid;
            for (var player = 0; player < match.Players.Count; player++)
            {
                if (grid.CountExplored(player) > grid.CountVisible(player))
                {
                    seen.Add("ground that is explored and no longer visible");
                }
            }

            for (var i = 0; i < match.Bodies.Count; i++)
            {
                var entity = match.Bodies.EntityAt(i);
                var body = match.Bodies.ValueAt(i);
                if (!match.World.Entities.IsAlive(entity))
                {
                    continue;
                }

                for (var player = 0; player < match.Players.Count; player++)
                {
                    if (player == body.Owner)
                    {
                        Assert.True(match.Vision.Sees(player, entity));
                        continue;
                    }

                    if (match.Structures.TryGet(entity, out var structure))
                    {
                        // Design 8.4, row 39: a structure is seen exactly while any tile of its footprint is visible.
                        Assert.Equal(grid.IsAnyVisible(player, structure.Tiles), match.Vision.Sees(player, entity));
                        continue;
                    }

                    // Design 8.4, row 39: an enemy unit is seen exactly while the tile under its center is visible.
                    var visible = grid.IsVisible(player, body.Position);
                    Assert.Equal(visible, match.Vision.Sees(player, entity));
                    if (visible)
                    {
                        seen.Add("an enemy unit seen");
                    }
                    else if (grid.StateOf(player, body.Position.X.FloorToInt(), body.Position.Y.FloorToInt()) == FogState.Explored)
                    {
                        seen.Add("an enemy unit on ground a player has explored and does not see now");
                    }

                    if (!_seenBefore.TryGetValue((player, entity), out var before) || before == visible)
                    {
                        continue;
                    }

                    if (visible)
                    {
                        seen.Add("an enemy unit that comes into sight");
                    }
                    else if (!workedOut && _positionsBefore[entity] != body.Position)
                    {
                        // Nothing was worked out in this tick: the unit left the tiles that are visible.
                        seen.Add("an enemy unit that goes out of sight by walking off the tiles its enemy sees");
                    }
                }
            }

            return seen;
        }
    }
}
