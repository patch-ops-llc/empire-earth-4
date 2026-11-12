using Sim.Core;
using Sim.Data;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Structures in combat (16b): Towers, walls and other structures placed close to the structures
/// that are there already, fighting units made near a structure of an enemy, and orders to attack
/// a structure, to attack-move onto one, and to stop. Also orders that are refused: a structure
/// placed where it has no room, and an attack on a structure of one's own.
/// </summary>
/// <remarks>
/// It places its structures with the development placement and makes its units with the
/// development spawn, as <see cref="FightOrders"/> does, and needs <c>debug.spawn</c> in the rules
/// of the data for both. Which structures attack and which are walls it reads from the data. With
/// the script <c>research</c> beside it, the tiers of the players drift apart, which is what the
/// fortification rule needs.
/// </remarks>
[OrderScript("siege")]
internal sealed class SiegeOrders : OrderScript
{
    private const int LongestPause = 40;

    /// <summary>The structures a player has before the script stops placing more for it.</summary>
    private const int MostStructures = 9;

    /// <summary>The fighting units a player has before the script stops making more for it.</summary>
    private const int MostFighters = 12;

    /// <summary>What the seed of the siege orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D16_B000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<StructureDef> _towers = [];
    private readonly List<StructureDef> _walls = [];
    private readonly List<StructureDef> _others = [];
    private readonly List<EntityId> _ownStructures = [];
    private readonly List<EntityId> _enemyStructures = [];
    private readonly List<EntityId> _enemyWalls = [];
    private readonly List<EntityId> _fighters = [];
    private int _nextTick;

    public SiegeOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
        foreach (var structure in Match.Data.Structures)
        {
            (structure.Attack is not null ? _towers : structure.Wall is not null ? _walls : _others).Add(structure);
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
        Collect(player);
        if (kind < 24)
        {
            Place(player, commands);
        }
        else if (kind < 46)
        {
            // One to three units of one chassis, within six tiles of a structure of an enemy.
            var chassis = _random.NextInt(Match.Data.Chassis.Count);
            var count = 1 + _random.NextInt(3);
            var near = _enemyStructures.Count > 0 ? CenterOf(Any(_enemyStructures)) : AnyPoint(_random);
            for (var i = 0; i < count && _fighters.Count + i < MostFighters; i++)
            {
                commands.Add(new Command(CommandType.DebugSpawn, player, targetX: Near(near.X, 6), targetY: Near(near.Y, 6), contentId: chassis));
            }
        }
        else if (kind < 66)
        {
            if (_fighters.Count > 0 && _enemyStructures.Count > 0)
            {
                commands.Add(new Command(CommandType.Attack, player, [.. Some(_fighters)], targetEntity: Any(_enemyStructures)));
            }
        }
        else if (kind < 80)
        {
            // Onto a structure of an enemy: on the way the units pick for themselves.
            if (_fighters.Count > 0 && _enemyStructures.Count > 0)
            {
                var onto = CenterOf(Any(_enemyStructures));
                commands.Add(new Command(CommandType.AttackMove, player, [.. Some(_fighters)], onto.X, onto.Y));
            }
        }
        else if (kind < 88)
        {
            // A wall is attacked only on an order.
            if (_fighters.Count > 0 && _enemyWalls.Count > 0)
            {
                commands.Add(new Command(CommandType.Attack, player, [.. Some(_fighters)], targetEntity: Any(_enemyWalls)));
            }
        }
        else if (kind < 94)
        {
            // A structure of one's own. Refused.
            if (_fighters.Count > 0 && _ownStructures.Count > 0)
            {
                commands.Add(new Command(CommandType.Attack, player, [.. Some(_fighters)], targetEntity: Any(_ownStructures)));
            }
        }
        else if (_fighters.Count > 0)
        {
            commands.Add(new Command(CommandType.Stop, player, [.. Some(_fighters)]));
        }
    }

    /// <summary>
    /// A Tower, a short row of walls or another structure, within eight tiles of a structure that
    /// is there already, of any player. Wherever that is: on blocked ground, on a structure or on
    /// a unit it is refused.
    /// </summary>
    private void Place(int player, List<Command> commands)
    {
        var draw = _random.NextInt(10);
        var from = draw < 5 ? _towers : draw < 7 ? _walls : _others;
        var anchor = Match.Structures.Count > 0
            ? CenterOf(Match.Structures.EntityAt(_random.NextInt(Match.Structures.Count)))
            : AnyPoint(_random);
        var x = Near(anchor.X, 8);
        var y = Near(anchor.Y, 8);
        var along = _random.NextInt(2) == 0;
        var count = ReferenceEquals(from, _walls) ? 2 + _random.NextInt(4) : 1;
        if (from.Count == 0 || _ownStructures.Count >= MostStructures)
        {
            return;
        }

        var structure = from[_random.NextInt(from.Count)];
        for (var i = 0; i < count; i++)
        {
            var step = Fixed.FromInt(i * structure.Footprint);
            commands.Add(new Command(
                CommandType.DebugPlace,
                player,
                targetX: along ? x + step : x,
                targetY: along ? y : y + step,
                contentId: structure.Index));
        }
    }

    /// <summary>
    /// Sorts what there is for a player: its structures, those of the other players with the
    /// walls among them apart, and its units that fight.
    /// </summary>
    private void Collect(int player)
    {
        _ownStructures.Clear();
        _enemyStructures.Clear();
        _enemyWalls.Clear();
        _fighters.Clear();
        var structures = Match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            if (!Match.World.Entities.IsAlive(entity) || !Match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            if (body.Owner == player)
            {
                _ownStructures.Add(entity);
                continue;
            }

            _enemyStructures.Add(entity);
            if (Match.Data.StructureOf(structures.ValueAt(i).Type)?.Wall is not null)
            {
                _enemyWalls.Add(entity);
            }
        }

        var combatants = Match.Combatants;
        for (var i = 0; i < combatants.Count; i++)
        {
            var entity = combatants.EntityAt(i);
            if (Match.World.Entities.IsAlive(entity) && Match.Bodies.TryGet(entity, out var body) && body.Owner == player)
            {
                _fighters.Add(entity);
            }
        }
    }

    private FixedVector2 CenterOf(EntityId structure) => Match.Bodies.Get(structure).Position;

    private EntityId Any(List<EntityId> entities) => entities[_random.NextInt(entities.Count)];

    /// <summary>Each of the entities with an even chance, and at least one.</summary>
    private List<EntityId> Some(List<EntityId> entities)
    {
        Picked.Clear();
        foreach (var entity in entities)
        {
            if (_random.NextInt(2) == 0)
            {
                Picked.Add(entity);
            }
        }

        if (Picked.Count == 0)
        {
            Picked.Add(Any(entities));
        }

        return Picked;
    }

    /// <summary>A coordinate within so many tiles of another, which may be on blocked ground or off the map.</summary>
    private Fixed Near(Fixed coordinate, int tiles)
    {
        var reach = tiles * Fixed.RawOne;
        return Fixed.FromRaw(coordinate.Raw + _random.NextInt(-reach, reach + 1));
    }
}
