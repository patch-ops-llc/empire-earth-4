using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Gates (18b): build orders for gates in the lines of wall that the player's workers are laying,
/// beside its walls and anywhere else, camps with a gate in their ring, orders that cancel a
/// gate, the owner's units sent through their gates and into them, and fighting units sent past
/// the gates of an enemy and at them.
/// </summary>
/// <remarks>
/// <para>
/// The script makes no units that fight and places no Outposts, so it shows the most beside
/// <see cref="WallingOrders"/>, which orders walls across the chokepoints, makes the ground
/// there a player's own, and makes the units that break in. By itself it gives the build and
/// wall orders of its camps, and the orders for the workers.
/// </para>
/// <para>
/// A gate can only be built on a free tile, so a gate gets into a line of wall by being ordered
/// on a tile that a wall order has paid for and not yet laid: the worker that lays the line skips
/// the tile when it comes to it, and the line closes on both sides of the gate. That is the kind
/// of order the script gives most often.
/// </para>
/// <para>
/// Whether the gate or the wall is laid first on such a tile is a race, so the script also
/// orders camps of its own: a gate on one corner of a ring whose tiles touch only at their
/// corners, as the camps of <see cref="WallingOrders"/> do, and the rest of the ring as wall
/// orders that leave the gate's tile out, or now and then come to it last. The ground inside is
/// closed to everyone but the owner.
/// </para>
/// </remarks>
[OrderScript("gates")]
internal sealed class GateOrders : OrderScript
{
    private const int LongestPause = 60;

    /// <summary>How many places the script tries before it settles for one that may not do.</summary>
    private const int PlacesTried = 16;

    /// <summary>What the seed of the gate orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D18_B000_0000_0000;

    private static readonly (int X, int Y)[] Sides = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private readonly DeterministicRandom _random;
    private readonly StructureDef? _gate;
    private readonly List<EntityId> _workers = [];
    private readonly List<EntityId> _fighters = [];
    private readonly List<EntityId> _found = [];
    private readonly List<EntityId> _gates = [];
    private readonly List<EntityId> _enemyGates = [];
    private readonly List<EntityId> _walls = [];
    private int _nextTick;

    public GateOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
        _gate = Match.Data.Gate;
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (_gate is null || tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        var kind = _random.NextInt(100);
        Collect(player);
        if (kind < 24)
        {
            // In a line of wall that is being laid: on a tile one of the player's wall orders
            // has paid for and not come to.
            if (_workers.Count > 0 && TryPickPlannedTile(player, out var tile))
            {
                commands.Add(Gate(player, tile));
            }
        }
        else if (kind < 32)
        {
            Camp(player, commands);
        }
        else if (kind < 40)
        {
            // On a tile next to one of the player's tiles of wall, which makes the wall a tile longer.
            if (_workers.Count > 0 && _walls.Count > 0)
            {
                commands.Add(Gate(player, BesideWall(player)));
            }
        }
        else if (kind < 44)
        {
            if (_workers.Count > 0 && TryPickTileInTerritory(player, out var tile))
            {
                commands.Add(Gate(player, tile));
            }
        }
        else if (kind < 49)
        {
            // Where no gate can be: on one of the player's tiles of wall, or anywhere on the
            // map, which is outside the territory or on ground that is not free most of the time.
            if (_workers.Count > 0)
            {
                var point = _walls.Count > 0 && _random.NextInt(2) == 0 ? Match.Bodies.Get(Any(_walls)).Position : AnyPoint(_random);
                commands.Add(Gate(player, (point.X.FloorToInt(), point.Y.FloorToInt())));
            }
        }
        else if (kind < 58)
        {
            // Cancel: a worker on its way to lay a gate, or a gate that is building itself.
            CollectWorkersWithGateOrders();
            if (_found.Count == 0 || _random.NextInt(3) == 0)
            {
                CollectGateFoundations(player);
            }

            if (_found.Count > 0)
            {
                commands.Add(new Command(CommandType.CancelBuild, player, [Any(_found)]));
            }
        }
        else if (kind < 73)
        {
            ThroughOwnGate(player, commands);
        }
        else if (kind < 80)
        {
            // Into one of its own gates, where a unit may stand, finished or not: a unit sent
            // to one that is still building itself stops beside it.
            if (_gates.Count > 0 && Own.Count > 0)
            {
                var gate = Match.Bodies.Get(Any(_gates)).Position;
                commands.Add(new Command(CommandType.Move, player, [Any(Idle())], Near(gate.X, Fixed.RawOne / 2), Near(gate.Y, Fixed.RawOne / 2)));
            }
        }
        else if (_fighters.Count > 0 && _enemyGates.Count > 0)
        {
            var gate = Any(_enemyGates);
            EntityId[] sent = [.. Some(_fighters)];
            if (kind < 90)
            {
                // To ground behind a gate of an enemy. The script looks for a gate and a place
                // within three tiles of it that the gate, of all the tiles of wall, is in the
                // way to. Where it finds none, the place is one or two tiles past a gate on
                // the side away from the first of the units.
                var from = Match.Bodies.Get(sent[0]).Position;
                var at = Match.Bodies.Get(gate).Position;
                var (dx, dy) = (at.X.Raw - from.X.Raw, at.Y.Raw - from.Y.Raw);
                var past = (1 + _random.NextInt(2)) * Fixed.RawOne;
                var place = Math.Abs(dx) >= Math.Abs(dy)
                    ? new FixedVector2(Fixed.FromRaw(at.X.Raw + (Math.Sign(dx) * past)), Near(at.Y, Fixed.RawOne / 2))
                    : new FixedVector2(Near(at.X, Fixed.RawOne / 2), Fixed.FromRaw(at.Y.Raw + (Math.Sign(dy) * past)));
                for (var i = 0; i < PlacesTried; i++)
                {
                    var tried = Any(_enemyGates);
                    at = Match.Bodies.Get(tried).Position;
                    var behind = new FixedVector2(Near(at.X, 3 * Fixed.RawOne), Near(at.Y, 3 * Fixed.RawOne));
                    if (Match.Grid.Contains(behind) && Match.Breaching.FindWall(player, from, behind, out _) == tried)
                    {
                        place = behind;
                        break;
                    }
                }

                commands.Add(new Command(_random.NextInt(2) == 0 ? CommandType.Move : CommandType.AttackMove, player, sent, place.X, place.Y));
            }
            else
            {
                // An attack order on the gate itself, which an attack-move would walk past.
                commands.Add(new Command(CommandType.Attack, player, sent, targetEntity: gate));
            }
        }
    }

    /// <summary>
    /// Sends one or two of the player's units to a place near one of its gates: one that only
    /// the gate lets them reach, if the script finds such a place, and any place near it
    /// otherwise, where the gate may be the shorter way.
    /// </summary>
    private void ThroughOwnGate(int player, List<Command> commands)
    {
        if (_gates.Count == 0 || Own.Count == 0)
        {
            return;
        }

        var gate = Match.Bodies.Get(Any(_gates)).Position;
        var idle = Idle();
        var first = Any(idle);
        var second = Any(idle);
        EntityId[] sent = first == second || _random.NextInt(2) == 0 ? [first] : [first, second];
        var from = Match.Bodies.Get(first).Position;
        var own = Match.Grid.For(player);
        var place = gate;
        for (var i = 0; i < PlacesTried; i++)
        {
            place = new FixedVector2(Near(gate.X, 4 * Fixed.RawOne), Near(gate.Y, 4 * Fixed.RawOne));
            if (Match.Grid.RegionOf(place) != 0
                && own.RegionOf(place) == own.RegionOf(from)
                && Match.Grid.RegionOf(place) != Match.Grid.RegionOf(from))
            {
                break;
            }
        }

        commands.Add(new Command(CommandType.Move, player, sent, place.X, place.Y));
    }

    /// <summary>
    /// A camp with a gate in its ring, round a free tile of the player's territory: the tiles
    /// one or two steps from it. The gate goes on one corner, to one worker, and the rest of the
    /// ring is ordered as wall runs that leave that corner out, each to another worker.
    /// </summary>
    private void Camp(int player, List<Command> commands)
    {
        if (_workers.Count < 2 || !TryPickTileInTerritory(player, out var middle))
        {
            return;
        }

        var radius = 1 + _random.NextInt(2);
        (int X, int Y)[] corners =
        [
            (middle.X - radius, middle.Y), (middle.X, middle.Y - radius), (middle.X + radius, middle.Y), (middle.X, middle.Y + radius),
        ];

        // The workers that have no wall order come first, so that one camp does not end another.
        _found.Clear();
        _found.AddRange(_workers.Where(worker => !Match.Walls.Has(worker)));
        _found.AddRange(_workers.Where(Match.Walls.Has));
        var workers = _found.ToArray();
        var first = _random.NextInt(corners.Length);
        var overlap = _random.NextInt(3) == 0;
        commands.Add(Gate(player, corners[first], workers[0]));
        for (var side = 0; side < corners.Length; side++)
        {
            // From one corner to the tile before the next, which the next run begins with. The
            // first run begins a tile late, because the gate has its corner.
            var from = corners[(first + side) % corners.Length];
            var to = corners[(first + side + 1) % corners.Length];
            var step = (X: Math.Sign(to.X - from.X), Y: Math.Sign(to.Y - from.Y));
            if (side == 0)
            {
                if (radius == 1)
                {
                    continue;
                }

                from = (from.X + step.X, from.Y + step.Y);
            }

            // One time in three the last run goes on to the gate's corner. Whichever of the two
            // workers comes to that tile second finds it taken: the wall order skips it, or the
            // gate order ends.
            var end = side == corners.Length - 1 && overlap ? to : (X: to.X - step.X, Y: to.Y - step.Y);
            var worker = workers[1 + (side % (workers.Length - 1))];
            var point = PointIn(from);
            var last = (end.Y * Match.Grid.Width) + end.X;
            commands.Add(new Command(CommandType.BuildWall, player, [worker], point.X, point.Y, contentId: last));
        }
    }

    /// <summary>
    /// The player's units that have nothing to build, so that sending one somewhere ends no
    /// order: all of its units when every one has something to build.
    /// </summary>
    private List<EntityId> Idle()
    {
        _found.Clear();
        _found.AddRange(Own.Where(unit => !Match.Construction.HasOrder(unit)));
        return _found.Count > 0 ? _found : Own;
    }

    /// <summary>A build order for a gate on a tile, to a worker: to one of the player's without a wall order, if it has one, when none is named.</summary>
    private Command Gate(int player, (int X, int Y) tile, EntityId worker = default)
    {
        if (worker == EntityId.None)
        {
            _found.Clear();
            _found.AddRange(_workers.Where(candidate => !Match.Walls.Has(candidate)));
            worker = Any(_found.Count > 0 ? _found : _workers);
        }

        var point = PointIn(tile);
        return new Command(CommandType.Build, player, [worker], point.X, point.Y, contentId: _gate!.Index);
    }

    /// <summary>A point anywhere in a tile.</summary>
    private FixedVector2 PointIn((int X, int Y) tile) => new(
        Fixed.FromRaw((tile.X * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)),
        Fixed.FromRaw((tile.Y * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)));

    /// <summary>A tile beside one of the player's tiles of wall: one a gate may be built on, if one of those tried is.</summary>
    private (int X, int Y) BesideWall(int player)
    {
        var tile = (X: 0, Y: 0);
        for (var i = 0; i < PlacesTried; i++)
        {
            var wall = Match.Bodies.Get(Any(_walls)).Position;
            var side = Sides[_random.NextInt(Sides.Length)];
            tile = (wall.X.FloorToInt() + side.X, wall.Y.FloorToInt() + side.Y);

            // The first half of the tries are for a gap: a tile with wall on its far side too.
            if (Match.Construction.CheckSite(player, _gate!, tile.X, tile.Y) == BuildProblem.None
                && (i >= PlacesTried / 2 || Match.WallMap.WallAt(tile.X + side.X, tile.Y + side.Y) != EntityId.None))
            {
                break;
            }
        }

        return (Math.Clamp(tile.X, 0, Match.Grid.Width - 1), Math.Clamp(tile.Y, 0, Match.Grid.Height - 1));
    }

    /// <summary>A tile inside one of the player's territories: one a gate may be built on, if one of those tried is.</summary>
    private bool TryPickTileInTerritory(int player, out (int X, int Y) tile)
    {
        tile = default;
        var territories = Match.Territories;
        _found.Clear();
        for (var i = 0; i < territories.Count; i++)
        {
            if (territories.ValueAt(i).Owner == player && Match.World.Entities.IsAlive(territories.EntityAt(i)))
            {
                _found.Add(territories.EntityAt(i));
            }
        }

        if (_found.Count == 0)
        {
            return false;
        }

        var territory = territories.Get(Any(_found));
        var reach = (2 * territory.Radius) + 1;
        for (var i = 0; i < PlacesTried; i++)
        {
            tile = (
                (territory.CenterX2 / 2) - territory.Radius + _random.NextInt(reach),
                (territory.CenterY2 / 2) - territory.Radius + _random.NextInt(reach));
            if (Match.Construction.CheckSite(player, _gate!, tile.X, tile.Y) == BuildProblem.None)
            {
                break;
            }
        }

        tile = (Math.Clamp(tile.X, 0, Match.Grid.Width - 1), Math.Clamp(tile.Y, 0, Match.Grid.Height - 1));
        return true;
    }

    /// <summary>
    /// A tile that a wall order of the player has paid for and not yet laid: half the time the
    /// last of them, which its worker comes to latest, so that the gate has time to be there first.
    /// </summary>
    private bool TryPickPlannedTile(int player, out (int X, int Y) tile)
    {
        tile = default;
        var plans = Match.WallPlans;
        var own = 0;
        for (var i = 0; i < plans.Count; i++)
        {
            own += plans.ValueAt(i).Owner == player ? 1 : 0;
        }

        if (own == 0)
        {
            return false;
        }

        var pick = _random.NextInt(2) == 0 ? own - 1 : _random.NextInt(own);
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans.ValueAt(i);
            if (plan.Owner == player && pick-- == 0)
            {
                tile = (plan.X, plan.Y);
            }
        }

        return true;
    }

    /// <summary>
    /// Sorts what there is for a player: its workers, its units that fight, its gates and its
    /// plain tiles of wall, finished or not, and the gates of the others.
    /// </summary>
    private void Collect(int player)
    {
        CollectUnitsOf(player);
        _workers.Clear();
        _fighters.Clear();
        foreach (var unit in Own)
        {
            if (Match.Workers.Contains(unit))
            {
                _workers.Add(unit);
            }
            else if (Match.Combatants.Contains(unit))
            {
                _fighters.Add(unit);
            }
        }

        _gates.Clear();
        _enemyGates.Clear();
        _walls.Clear();
        var structures = Match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            var type = structures.ValueAt(i).Type;
            if (!Match.WallMap.IsWall(type) || !Match.World.Entities.IsAlive(entity) || !Match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            if (type != _gate!.Index)
            {
                if (body.Owner == player)
                {
                    _walls.Add(entity);
                }
            }
            else if (body.Owner == player)
            {
                _gates.Add(entity);
            }
            else
            {
                _enemyGates.Add(entity);
            }
        }
    }

    /// <summary>The workers in <see cref="_workers"/> that are on their way to lay a gate.</summary>
    private void CollectWorkersWithGateOrders()
    {
        _found.Clear();
        foreach (var worker in _workers)
        {
            if (Match.BuildOrders.TryGet(worker, out var order) && order.Type == _gate!.Index)
            {
                _found.Add(worker);
            }
        }
    }

    private void CollectGateFoundations(int player)
    {
        _found.Clear();
        var foundations = Match.Foundations;
        for (var i = 0; i < foundations.Count; i++)
        {
            var structure = foundations.EntityAt(i);
            if (Match.World.Entities.IsAlive(structure)
                && Match.Bodies.Get(structure).Owner == player
                && Match.Structures.Get(structure).Type == _gate!.Index)
            {
                _found.Add(structure);
            }
        }
    }

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

    /// <summary>A coordinate within so much of another, which may be on blocked ground or off the map.</summary>
    private Fixed Near(Fixed coordinate, int reach) =>
        Fixed.FromRaw(coordinate.Raw + _random.NextInt(-reach, reach + 1));
}
