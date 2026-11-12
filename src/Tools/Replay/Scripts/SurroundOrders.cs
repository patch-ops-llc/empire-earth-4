using Sim.Core;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Many against one (M12): bands of melee units made with the development spawn, single units
/// made for them to go after, and attack and attack-move orders that send a whole band at one
/// target, so that more units come at it than have a place round it.
/// </summary>
/// <remarks>
/// Like <c>fight</c> it makes its forces from nothing and needs <c>debug.spawn</c> in the rules
/// of the data. A band is of one chassis and is made close together beside the field; a target is
/// made on the field, and a band goes for a target of another player wherever that stands, a
/// worker at its deposit and a structure as well.
/// <para>
/// An attack order names only a target that the band has in sight: one that a unit of it stands
/// a tile nearer to than it sees. A player may order an attack only on what it sees (design
/// 5.3), so an order at anything else is one that a match with fog refuses, and a match recorded
/// with such orders takes another course once there is fog. To a target out of sight the band is
/// sent by attack-move, and it can be sent at it when it has come near. The script works sight
/// out from the positions and the profiles and asks the match nothing about fog, so it gives
/// the same orders with it and without.
/// </para>
/// </remarks>
[OrderScript("surround")]
internal sealed class SurroundOrders : OrderScript
{
    private const int LongestPause = 50;

    /// <summary>The melee units a player has before the script stops making bands for it.</summary>
    private const int LargestForce = 14;

    /// <summary>The fewest and the most units of a band.</summary>
    private const int SmallestBand = 4;
    private const int LargestBand = 9;

    /// <summary>How far from the middle of the field a target is made, in tiles, along each axis.</summary>
    private const int FieldReach = 3;

    /// <summary>How far from the middle of the field a band is made, in tiles, and how far apart its units are.</summary>
    private const int BandDistance = 7;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D12_0000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<int> _melee = [];
    private readonly List<EntityId> _band = [];
    private readonly List<EntityId> _targets = [];
    private readonly List<EntityId> _inSight = [];
    private readonly FixedVector2 _field;
    private int _nextTick;

    public SurroundOrders(ScriptSetup setup)
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

        for (var chassis = 0; chassis < Match.Data.Chassis.Count; chassis++)
        {
            if (Match.Combat.ProfileOf(0, chassis).Melee)
            {
                _melee.Add(chassis);
            }
        }
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick || _melee.Count == 0)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var players = Match.Players.Count;
        var player = _random.NextInt(players);
        var kind = _random.NextInt(100);
        Collect(player);

        if ((kind < 25 || _band.Count == 0) && _band.Count + SmallestBand <= LargestForce)
        {
            // A band of one melee chassis, in files a little wider apart than any spacing, on
            // one side of the field.
            var chassis = _melee[_random.NextInt(_melee.Count)];
            var count = Math.Min(SmallestBand + _random.NextInt(LargestBand - SmallestBand + 1), LargestForce - _band.Count);
            var side = _random.NextInt(4);
            var corner = new FixedVector2(
                _field.X + Fixed.FromInt(side switch { 0 => -BandDistance, 1 => BandDistance, _ => -2 }),
                _field.Y + Fixed.FromInt(side switch { 2 => -BandDistance, 3 => BandDistance, _ => -2 }));
            for (var i = 0; i < count; i++)
            {
                commands.Add(Spawn(player, chassis, corner + new FixedVector2(Fixed.FromInt(2 * (i % 3)), Fixed.FromInt(2 * (i / 3)))));
            }
        }
        else if (kind < 45 || _targets.Count == 0)
        {
            // One unit of any chassis on the field, for another player.
            var other = (player + 1 + _random.NextInt(players - 1)) % players;
            commands.Add(Spawn(other, _random.NextInt(Match.Data.Chassis.Count), OnTheField()));
        }
        else if (kind < 85 && _inSight.Count > 0)
        {
            // The whole band at one target it has in sight.
            commands.Add(new Command(CommandType.Attack, player, [.. _band], targetEntity: _inSight[_random.NextInt(_inSight.Count)]));
        }
        else
        {
            // The whole band to where a target stands, fighting what it meets: its turn, or the
            // band has nothing in sight to be sent at.
            var point = Match.Bodies.Get(AnyTarget()).Position;
            commands.Add(new Command(CommandType.AttackMove, player, [.. _band], point.X, point.Y));
        }
    }

    private static Command Spawn(int player, int chassis, FixedVector2 point) =>
        new(CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: chassis);

    /// <summary>
    /// Sorts what there is for a player: its melee units, everything of the other players that
    /// can be attacked, and what of that the band has in sight.
    /// </summary>
    private void Collect(int player)
    {
        CollectUnitsOf(player);
        _band.Clear();
        foreach (var unit in Own)
        {
            if (Match.Combatants.TryGet(unit, out var combatant) && Match.Combat.ProfileOf(player, combatant.Chassis).Melee)
            {
                _band.Add(unit);
            }
        }

        _targets.Clear();
        var bodies = Match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            if (bodies.ValueAt(i).Owner != player && Match.World.Entities.IsAlive(entity) && Match.Health.Contains(entity))
            {
                _targets.Add(entity);
            }
        }

        _inSight.Clear();
        foreach (var target in _targets)
        {
            if (IsInSightOfTheBand(player, target))
            {
                _inSight.Add(target);
            }
        }
    }

    /// <summary>
    /// Whether a unit of the band stands at least a tile nearer to a target than it sees. A
    /// structure is as far as the nearest edge of its footprint. A melee unit picks targets as
    /// far as it sees, so its sight is the acquisition distance of its profile; the tile less
    /// keeps the answer the same however sight is rounded to tiles.
    /// </summary>
    private bool IsInSightOfTheBand(int player, EntityId target)
    {
        var at = Match.Bodies.Get(target).Position;
        var isStructure = Match.Structures.TryGet(target, out var structure);
        foreach (var unit in _band)
        {
            var sight = Match.Combat.ProfileOf(player, Match.Combatants.Get(unit).Chassis).Acquisition - Fixed.One;
            var from = Match.Bodies.Get(unit).Position;
            var distance = isStructure ? structure.Tiles.DistanceSquaredRawTo(from) : FixedVector2.DistanceSquaredRaw(from, at);
            if (sight > Fixed.Zero && distance <= FixedVector2.SquareRaw(sight))
            {
                return true;
            }
        }

        return false;
    }

    private EntityId AnyTarget() => _targets[_random.NextInt(_targets.Count)];

    /// <summary>A point on the field, which may be on blocked ground or off the map.</summary>
    private FixedVector2 OnTheField()
    {
        var reach = FieldReach * Fixed.RawOne;
        var x = Fixed.FromRaw(_field.X.Raw + _random.NextInt(-reach, reach + 1));
        var y = Fixed.FromRaw(_field.Y.Raw + _random.NextInt(-reach, reach + 1));
        return new FixedVector2(x, y);
    }
}
