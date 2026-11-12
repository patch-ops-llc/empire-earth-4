using Sim.Core;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Blasts (16a): clumps of units made close together on one field, for every player, and round
/// the field units whose attack has a blast radius, which shell them. Also units made on the
/// spot of an enemy, so that a shell lands on both sides at once, and units made beside an enemy
/// that has a blast, inside its minimum range.
/// </summary>
/// <remarks>
/// It makes its units with the development spawn, as <see cref="FightOrders"/> does, and needs
/// <c>debug.spawn</c> in the rules of the data for it. Which chassis have a blast it reads from
/// the blueprints, so it follows the data.
/// </remarks>
[OrderScript("blast")]
internal sealed class BlastOrders : OrderScript
{
    private const int LongestPause = 50;

    /// <summary>The units with a blast a player has before the script stops making more for it.</summary>
    private const int LargestBattery = 4;

    /// <summary>The other fighting units a player has before the script stops making clumps for it.</summary>
    private const int MostInClumps = 10;

    /// <summary>What the seed of the blast orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D16_0000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<int> _shellChassis = [];
    private readonly List<int> _otherChassis = [];
    private readonly List<EntityId> _battery = [];
    private readonly List<EntityId> _enemies = [];
    private readonly List<EntityId> _enemyBattery = [];
    private readonly FixedVector2 _field;
    private int _nextTick;
    private int _inClumps;

    public BlastOrders(ScriptSetup setup)
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

        // Whether a chassis has a blast is the same for every player and at every tier.
        var blueprints = Match.Research.Tech(0).Blueprints;
        for (var chassis = 0; chassis < blueprints.Count; chassis++)
        {
            (blueprints[chassis].BlastRadius is null ? _otherChassis : _shellChassis).Add(chassis);
        }
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        var kind = _random.NextInt(100);
        if (_shellChassis.Count == 0 || _otherChassis.Count == 0)
        {
            return;
        }

        Collect(player);
        if (kind < 25)
        {
            // A clump: a few units of one chassis within a tile of one point near the middle of the field.
            var chassis = _otherChassis[_random.NextInt(_otherChassis.Count)];
            var middle = Near(_field, 2);
            var count = 3 + _random.NextInt(3);
            for (var i = 0; i < count && _inClumps + i < MostInClumps; i++)
            {
                commands.Add(Spawn(player, chassis, Near(middle, 1)));
            }
        }
        else if (kind < 50)
        {
            // One or two units with a blast, out on the edge of the field: 5 to 8 tiles from its middle.
            var count = 1 + _random.NextInt(2);
            for (var i = 0; i < count && _battery.Count + i < LargestBattery; i++)
            {
                commands.Add(Spawn(player, _shellChassis[_random.NextInt(_shellChassis.Count)], OnTheEdge()));
            }
        }
        else if (kind < 72)
        {
            // The whole battery at one enemy. Those out of range walk up; the shells fall where it stands.
            if (_battery.Count > 0 && _enemies.Count > 0)
            {
                commands.Add(new Command(CommandType.Attack, player, [.. _battery], targetEntity: _enemies[_random.NextInt(_enemies.Count)]));
            }
        }
        else if (kind < 84)
        {
            // On the very spot of an enemy: whatever is shelled there, one side of the two is spared.
            if (_enemies.Count > 0)
            {
                var spot = Match.Bodies.Get(_enemies[_random.NextInt(_enemies.Count)]).Position;
                commands.Add(Spawn(player, _otherChassis[_random.NextInt(_otherChassis.Count)], spot));
            }
        }
        else if (kind < 94)
        {
            // Beside an enemy that has a blast, well inside its minimum range.
            if (_enemyBattery.Count > 0)
            {
                var beside = Near(Match.Bodies.Get(_enemyBattery[_random.NextInt(_enemyBattery.Count)]).Position, 1);
                commands.Add(Spawn(player, _otherChassis[_random.NextInt(_otherChassis.Count)], beside));
            }
        }
        else if (_battery.Count > 0)
        {
            // The battery stops. Shells in the air still land.
            commands.Add(new Command(CommandType.Stop, player, [.. _battery]));
        }
    }

    private static Command Spawn(int player, int chassis, FixedVector2 point) =>
        new(CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: chassis);

    /// <summary>
    /// Sorts what there is for a player: its units with a blast, how many other fighting units it
    /// has, everything of the other players that can be attacked, and their units with a blast.
    /// </summary>
    private void Collect(int player)
    {
        _battery.Clear();
        _enemies.Clear();
        _enemyBattery.Clear();
        _inClumps = 0;
        var bodies = Match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            if (!Match.World.Entities.IsAlive(entity) || !Match.Health.Contains(entity))
            {
                continue;
            }

            var own = bodies.ValueAt(i).Owner == player;
            var shells = Match.Combatants.TryGet(entity, out var unit) && _shellChassis.Contains(unit.Chassis);
            if (!own)
            {
                _enemies.Add(entity);
                if (shells)
                {
                    _enemyBattery.Add(entity);
                }
            }
            else if (shells)
            {
                _battery.Add(entity);
            }
            else if (Match.Combatants.Contains(entity))
            {
                _inClumps++;
            }
        }
    }

    /// <summary>A point within so many tiles of another along each axis, which may be on blocked ground or off the map.</summary>
    private FixedVector2 Near(FixedVector2 point, int tiles)
    {
        var reach = tiles * Fixed.RawOne;
        var x = Fixed.FromRaw(point.X.Raw + _random.NextInt(-reach, reach + 1));
        var y = Fixed.FromRaw(point.Y.Raw + _random.NextInt(-reach, reach + 1));
        return new FixedVector2(x, y);
    }

    /// <summary>A point 5 to 8 tiles from the middle of the field along one axis and up to 8 along the other.</summary>
    private FixedVector2 OnTheEdge()
    {
        var far = (5 * Fixed.RawOne) + _random.NextInt((3 * Fixed.RawOne) + 1);
        if (_random.NextInt(2) == 0)
        {
            far = -far;
        }

        var across = _random.NextInt(-8 * Fixed.RawOne, (8 * Fixed.RawOne) + 1);
        return _random.NextInt(2) == 0
            ? new FixedVector2(Fixed.FromRaw(_field.X.Raw + far), Fixed.FromRaw(_field.Y.Raw + across))
            : new FixedVector2(Fixed.FromRaw(_field.X.Raw + across), Fixed.FromRaw(_field.Y.Raw + far));
    }
}
