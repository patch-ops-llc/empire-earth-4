using Sim.Core;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Fog of war (M17): fighting units made far from each other, anywhere on the map, and sent
/// across it, so that the players explore, find each other and lose each other again; attack
/// orders on units the player sees and on units it does not; attack orders on a unit that its
/// owner sends away in the same tick, so that the target is lost from sight; and units that
/// reach further than they see, made within reach of an enemy that is sent away from their shots.
/// </summary>
/// <remarks>
/// Fog adds no command of its own. The script gives the commands of movement and combat in the
/// situations the fog decides: it reads what each player sees from the match, as it reads
/// everything else, and picks its targets by it. Like <see cref="FightOrders"/> it makes its
/// units with the development spawn, so it needs <c>debug.spawn</c> in the rules of the data.
/// </remarks>
[OrderScript("fog")]
internal sealed class FogOrders : OrderScript
{
    private const int LongestPause = 40;

    /// <summary>The fighting units a player has before the script stops making more for it.</summary>
    private const int LargestForce = 8;

    /// <summary>How much longer the script waits after an attack on a unit that is sent away.</summary>
    private const int QuietTicks = 120;

    /// <summary>How far from a unit of another player a unit that reaches further than it sees is made, in tiles: within its sight.</summary>
    private const int BesideTiles = 7;

    /// <summary>How far a shot has to go, in tiles, for its target to be sent away from it.</summary>
    private const int LongShot = 6;

    /// <summary>How far from a target a unit may be to count as near it, in tiles.</summary>
    private const int Near = 14;

    /// <summary>What the seed of the fog orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D17_0000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<EntityId> _fighters = [];
    private readonly List<EntityId> _seen = [];
    private readonly List<EntityId> _unseen = [];
    private readonly List<int> _longReach = [];
    private HashSet<EntityId> _shots = [];
    private HashSet<EntityId> _shotsBefore = [];
    private int _nextTick;

    public FogOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        SendAwayFromLongShots(commands);
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        var kind = _random.NextInt(100);
        Collect(player);

        if (kind < 18 || _fighters.Count == 0)
        {
            // A unit of any chassis on open ground anywhere on the map: mostly out of everyone's sight.
            // A player with no fighting unit gets one whatever the kind.
            if (_fighters.Count < LargestForce)
            {
                commands.Add(Spawn(player, _random.NextInt(Match.Data.Chassis.Count), OpenGround()));
            }
        }
        else if (kind < 26)
        {
            // A unit that reaches further than it sees, 7 tiles from a unit of another player,
            // seen or not: it sees that unit, fires, and then stands alone with what it sees.
            var others = _seen.Count + _unseen.Count;
            if (others > 0 && _fighters.Count < 2 * LargestForce)
            {
                var pick = _random.NextInt(others);
                var target = pick < _seen.Count ? _seen[pick] : _unseen[pick - _seen.Count];
                commands.Add(Spawn(player, LongReach(player), Beside(Match.Bodies.Get(target).Position)));
            }
        }
        else if (kind < 38)
        {
            // Across the map, into ground the player has not seen.
            PickFighters();
            commands.Add(Order(CommandType.Move, player, Picked, AnyPoint(_random)));
        }
        else if (kind < 48)
        {
            // The same, fighting what comes into sight on the way.
            PickFighters();
            commands.Add(Order(CommandType.AttackMove, player, Picked, AnyPoint(_random)));
        }
        else if (kind < 60)
        {
            // At a unit the player sees, if there is one, and otherwise at one it does not.
            var targets = _seen.Count > 0 ? _seen : _unseen;
            if (targets.Count > 0)
            {
                PickFighters();
                commands.Add(Attack(player, Picked, targets[_random.NextInt(targets.Count)]));
            }
        }
        else if (kind < 70)
        {
            // At a unit the player does not see: the order must be refused.
            if (_unseen.Count > 0)
            {
                PickFighters();
                commands.Add(Attack(player, Picked, _unseen[_random.NextInt(_unseen.Count)]));
            }
        }
        else if (kind < 93)
        {
            // At a unit the player sees, which its owner sends away in the same tick: the
            // attackers follow, and lose it when it leaves what their owner sees. They are the
            // player's units near it, so that they have not far to go to where they saw it last.
            if (_seen.Count > 0)
            {
                var target = _seen[_random.NextInt(_seen.Count)];
                PickFightersNear(Match.Bodies.Get(target).Position);
                commands.Add(Attack(player, Picked, target));
                Picked.Clear();
                Picked.Add(target);
                commands.Add(Order(CommandType.Move, Match.Bodies.Get(target).Owner, Picked, AnyPoint(_random)));

                // The script then keeps quiet for a while, so that the attackers can lose the unit
                // and get to where they saw it last before it orders them again.
                _nextTick += QuietTicks;
            }
        }
        else
        {
            PickFighters();
            commands.Add(Order(CommandType.Stop, player, Picked, default));
        }
    }

    /// <summary>
    /// A shot that was fired from <see cref="LongShot"/> tiles or further is still on its way when
    /// its target has left what the unit that fired it sees. So, half the time, the target's owner
    /// sends the target straight away from such a shot in the tick after it was fired: a shot in
    /// flight lands all the same.
    /// </summary>
    private void SendAwayFromLongShots(List<Command> commands)
    {
        (_shots, _shotsBefore) = (_shotsBefore, _shots);
        _shots.Clear();
        var shots = Match.Shots;
        for (var i = 0; i < shots.Count; i++)
        {
            var entity = shots.EntityAt(i);
            ref var shot = ref shots.ValueAt(i);
            if (!Match.World.Entities.IsAlive(entity))
            {
                continue;
            }

            _shots.Add(entity);
            if (_shotsBefore.Contains(entity)
                || !Match.World.Entities.IsAlive(shot.Target)
                || !Match.Movers.Contains(shot.Target)
                || !Match.Bodies.TryGet(shot.Target, out var target)
                || FixedVector2.DistanceSquaredRaw(shot.Position, target.Position) < FixedVector2.SquareRaw(Fixed.FromInt(LongShot))
                || _random.NextInt(2) != 0)
            {
                continue;
            }

            // Three times as far again as the shot has to go, and no further than the edge of the map.
            var x = Math.Clamp(target.Position.X.Raw + (3L * (target.Position.X.Raw - shot.Position.X.Raw)), 0, (Match.Grid.Width * (long)Fixed.RawOne) - 1);
            var y = Math.Clamp(target.Position.Y.Raw + (3L * (target.Position.Y.Raw - shot.Position.Y.Raw)), 0, (Match.Grid.Height * (long)Fixed.RawOne) - 1);
            commands.Add(new Command(CommandType.Move, target.Owner, [shot.Target], Fixed.FromRaw((int)x), Fixed.FromRaw((int)y)));
        }
    }

    private static Command Spawn(int player, int chassis, FixedVector2 point) =>
        new(CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: chassis);

    private static Command Order(CommandType type, int player, List<EntityId> units, FixedVector2 point) =>
        new(type, player, [.. units], point.X, point.Y);

    private static Command Attack(int player, List<EntityId> units, EntityId target) =>
        new(CommandType.Attack, player, [.. units], targetEntity: target);

    /// <summary>
    /// Sorts what there is for a player: its fighting units, and the units of the other players
    /// that can be attacked, those the player sees apart from those it does not.
    /// </summary>
    private void Collect(int player)
    {
        CollectUnitsOf(player);
        _fighters.Clear();
        foreach (var unit in Own)
        {
            if (Match.Combatants.Contains(unit))
            {
                _fighters.Add(unit);
            }
        }

        _seen.Clear();
        _unseen.Clear();
        var bodies = Match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            ref var body = ref bodies.ValueAt(i);
            if (body.Owner != player && Match.World.Entities.IsAlive(entity) && Match.Health.Contains(entity) && Match.Movers.Contains(entity))
            {
                (Match.Vision.Sees(player, in body) ? _seen : _unseen).Add(entity);
            }
        }
    }

    /// <summary>Puts each fighting unit of the player into <see cref="OrderScript.Picked"/> with an even chance, and at least one.</summary>
    private void PickFighters()
    {
        Picked.Clear();
        foreach (var unit in _fighters)
        {
            if (_random.NextInt(2) == 0)
            {
                Picked.Add(unit);
            }
        }

        if (Picked.Count == 0)
        {
            Picked.Add(_fighters[_random.NextInt(_fighters.Count)]);
        }
    }

    /// <summary>
    /// Puts the player's fighting units within <see cref="Near"/> tiles of a point into
    /// <see cref="OrderScript.Picked"/>, and when there is none, the nearest.
    /// </summary>
    private void PickFightersNear(FixedVector2 point)
    {
        Picked.Clear();
        var nearest = _fighters[0];
        var least = long.MaxValue;
        foreach (var unit in _fighters)
        {
            var distance = FixedVector2.DistanceSquaredRaw(Match.Bodies.Get(unit).Position, point);
            if (distance <= FixedVector2.SquareRaw(Fixed.FromInt(Near)))
            {
                Picked.Add(unit);
            }

            if (distance < least)
            {
                least = distance;
                nearest = unit;
            }
        }

        if (Picked.Count == 0)
        {
            Picked.Add(nearest);
        }
    }

    /// <summary>
    /// A chassis that, with the player's tiers, reaches further than it sees, so that it can fire
    /// at what only another unit sees. Any chassis when the data has none.
    /// </summary>
    private int LongReach(int player)
    {
        _longReach.Clear();
        var blueprints = Match.Research.Tech(player).Blueprints;
        for (var chassis = 0; chassis < Match.Data.Chassis.Count; chassis++)
        {
            if (blueprints[chassis].Range is { } range && range > Fixed.FromInt(blueprints[chassis].Sight))
            {
                _longReach.Add(chassis);
            }
        }

        return _longReach.Count > 0 ? _longReach[_random.NextInt(_longReach.Count)] : _random.NextInt(Match.Data.Chassis.Count);
    }

    /// <summary>A point <see cref="BesideTiles"/> tiles from another, along an axis or a diagonal. It may be on blocked ground or off the map.</summary>
    private FixedVector2 Beside(FixedVector2 point)
    {
        var direction = _random.NextInt(8);
        var along = Fixed.FromInt(BesideTiles);
        var diagonal = Fixed.FromInt(BesideTiles * 5 / 7);
        var (x, y) = direction switch
        {
            0 => (along, Fixed.Zero),
            1 => (-along, Fixed.Zero),
            2 => (Fixed.Zero, along),
            3 => (Fixed.Zero, -along),
            4 => (diagonal, diagonal),
            5 => (diagonal, -diagonal),
            6 => (-diagonal, diagonal),
            _ => (-diagonal, -diagonal),
        };
        return new FixedVector2(point.X + x, point.Y + y);
    }

    /// <summary>A point on ground a unit can stand on. A map has plenty, so a few draws find some.</summary>
    private FixedVector2 OpenGround()
    {
        var point = AnyPoint(_random);
        for (var tries = 0; tries < 1000 && !Match.Grid.IsPassable(point); tries++)
        {
            point = AnyPoint(_random);
        }

        return point;
    }
}
