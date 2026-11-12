using Sim.Core;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Fights (M11): fighting units made with the development spawn round one field, for every
/// player, so that they meet, then attack-move, attack and stop orders for them and for the
/// workers, and the orders and spawns the simulation must refuse.
/// </summary>
/// <remarks>
/// No match has a fighting unit of its own before units can be trained, so the script makes its
/// forces from nothing. That needs <c>debug.spawn</c> in the rules of the data; without it every
/// spawn is dropped and the other orders find only workers.
/// </remarks>
[OrderScript("fight")]
internal sealed class FightOrders : OrderScript
{
    private const int LongestPause = 40;

    /// <summary>The fighting units a player has before the script stops making more for it.</summary>
    private const int LargestForce = 12;

    /// <summary>How far from the middle of the field a unit is made, in tiles, along each axis.</summary>
    private const int FieldReach = 5;

    /// <summary>What the seed of the fight orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D11_0000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<EntityId> _fighters = [];
    private readonly List<EntityId> _workers = [];
    private readonly List<EntityId> _others = [];
    private readonly FixedVector2 _field;
    private int _nextTick;

    public FightOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);

        // The field is on open ground. A map has plenty, so a few draws find some.
        _field = AnyPoint(_random);
        for (var tries = 0; tries < 1000 && !Match.Grid.IsPassable(_field); tries++)
        {
            _field = AnyPoint(_random);
        }
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var players = Match.Players.Count;
        var player = _random.NextInt(players);
        var kind = _random.NextInt(100);
        Collect(player);

        if (kind < 27 || Own.Count == 0)
        {
            // A few units for one player, each of any chassis and each somewhere on the field.
            // A player with nothing left gets them whatever the kind, and is in the match again.
            var count = 1 + _random.NextInt(3);
            for (var i = 0; i < count && _fighters.Count + i < LargestForce; i++)
            {
                commands.Add(Spawn(player, AnyChassis(), OnTheField()));
            }
        }
        else if (kind < 33)
        {
            // A unit for every player on one point in the same tick: they stand in each other and fight at once.
            var point = OnTheField();
            for (var other = 0; other < players; other++)
            {
                commands.Add(Spawn(other, AnyChassis(), point));
            }
        }
        else if (kind < 47)
        {
            // Workers and fighting units together. For a worker an attack-move is a move order.
            PickSome(_random);
            commands.Add(Order(CommandType.AttackMove, player, Picked, _random.NextInt(3) == 0 ? AnyPoint(_random) : OnTheField()));
        }
        else if (kind < 61)
        {
            if (_fighters.Count > 0 && _others.Count > 0)
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

                commands.Add(Attack(player, Picked, AnyOther()));
            }
        }
        else if (kind < 67)
        {
            // Every unit the player has. The workers among them cannot attack and are skipped.
            if (_others.Count > 0)
            {
                commands.Add(Attack(player, Own, AnyOther()));
            }
        }
        else if (kind < 76)
        {
            PickSome(_random);
            commands.Add(Order(CommandType.Stop, player, Picked, default));
        }
        else if (kind < 79)
        {
            // At one of the player's own.
            commands.Add(Attack(player, Own, Own[_random.NextInt(Own.Count)]));
        }
        else if (kind < 82)
        {
            commands.Add(Attack(player, Own, NeverExisted()));
        }
        else if (kind < 85)
        {
            // Units of the other players and one that never existed: none is the player's to order.
            if (_others.Count > 0)
            {
                var target = AnyOther();
                Picked.Clear();
                Picked.AddRange(_others);
                Picked.Add(NeverExisted());
                commands.Add(Attack(player, Picked, target));
            }
        }
        else if (kind < 88)
        {
            // Workers alone: none of them can attack.
            if (_workers.Count > 0 && _others.Count > 0)
            {
                commands.Add(Attack(player, _workers, AnyOther()));
            }
        }
        else if (kind < 91)
        {
            if (_others.Count > 0)
            {
                commands.Add(Order(CommandType.Stop, player, _others, default));
            }
        }
        else if (kind < 94)
        {
            // A chassis the data does not have: past the last, or below the first.
            var chassis = _random.NextInt(2) == 0 ? Match.Data.Chassis.Count + _random.NextInt(1000) : -1 - _random.NextInt(1000);
            commands.Add(Spawn(player, chassis, OnTheField()));
        }
        else if (kind < 97)
        {
            // Where no unit can stand: on blocked ground, and off the map.
            var blocked = AnyPoint(_random);
            for (var tries = 0; tries < 1000 && Match.Grid.IsPassable(blocked); tries++)
            {
                blocked = AnyPoint(_random);
            }

            var outside = new FixedVector2(Fixed.FromInt(Match.Grid.Width + _random.NextInt(50)), Fixed.FromInt(-1 - _random.NextInt(50)));
            commands.Add(Spawn(player, AnyChassis(), blocked));
            commands.Add(Spawn(player, AnyChassis(), outside));
        }
        else
        {
            // Each order from somebody who is not in the match, and each without a unit.
            var nobody = players + _random.NextInt(5);
            commands.Add(Spawn(nobody, AnyChassis(), OnTheField()));
            commands.Add(Order(CommandType.AttackMove, nobody, Own, OnTheField()));
            commands.Add(Order(CommandType.Stop, nobody, Own, default));
            Picked.Clear();
            commands.Add(Order(CommandType.AttackMove, player, Picked, OnTheField()));
            commands.Add(Order(CommandType.Stop, player, Picked, default));
            if (_others.Count > 0)
            {
                var target = AnyOther();
                commands.Add(Attack(nobody, Own, target));
                commands.Add(Attack(player, Picked, target));
            }
        }
    }

    private static Command Spawn(int player, int chassis, FixedVector2 point) =>
        new(CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: chassis);

    private static Command Order(CommandType type, int player, List<EntityId> units, FixedVector2 point) =>
        new(type, player, [.. units], point.X, point.Y);

    private static Command Attack(int player, List<EntityId> units, EntityId target) =>
        new(CommandType.Attack, player, [.. units], targetEntity: target);

    /// <summary>
    /// Sorts what there is for a player: its units into <see cref="OrderScript.Own"/>, those of a
    /// chassis and the workers among them apart, and everything of the other players that can be
    /// attacked.
    /// </summary>
    private void Collect(int player)
    {
        CollectUnitsOf(player);
        _fighters.Clear();
        _workers.Clear();
        foreach (var unit in Own)
        {
            (Match.Combatants.Contains(unit) ? _fighters : _workers).Add(unit);
        }

        _others.Clear();
        var bodies = Match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            if (bodies.ValueAt(i).Owner != player && Match.World.Entities.IsAlive(entity) && Match.Health.Contains(entity))
            {
                _others.Add(entity);
            }
        }
    }

    private int AnyChassis() => _random.NextInt(Match.Data.Chassis.Count);

    private EntityId AnyOther() => _others[_random.NextInt(_others.Count)];

    private EntityId NeverExisted() => new(1_000_000 + _random.NextInt(1000));

    /// <summary>A point on the field, which may be on blocked ground or off the map.</summary>
    private FixedVector2 OnTheField()
    {
        var reach = FieldReach * Fixed.RawOne;
        var x = Fixed.FromRaw(_field.X.Raw + _random.NextInt(-reach, reach + 1));
        var y = Fixed.FromRaw(_field.Y.Raw + _random.NextInt(-reach, reach + 1));
        return new FixedVector2(x, y);
    }
}
