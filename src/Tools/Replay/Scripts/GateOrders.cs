using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Gates (18b): build orders for gates in the lines of wall that the player's workers are laying,
/// beside its walls and anywhere else, camps with a gate in their ring, a few such camps laid
/// whole with the development placement, orders that cancel a gate, the owner's units sent
/// through their gates and into them, and fighting units sent past the gates of an enemy and at
/// them.
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
/// <para>
/// A camp that workers lay takes long, and a match may end before one is closed. So the script
/// also lays a few camps whole, with the development placement, each with a finished gate on one
/// corner: at most <see cref="MostCampsLaidWhole"/> for a player. They are what the rules of a
/// closed camp are most often reached in: ground only the owner can reach, units that walk
/// through their gate, and units of an enemy with the gate in their way.
/// </para>
/// <para>
/// A player's units have only the gates in their way that the player sees or remembers, and an
/// attack order may only name such a gate (design 5.3). So the script sorts the gates of the
/// others into those the player knows and those it has never seen, and takes one of each kind
/// in turn when there are both, for each of its two orders: fighting units sent past a gate their player has never seen
/// come to know it on their way, and the attack order on it is refused. Of the gates it knows,
/// it tries first those that nobody of the player sees now, which its units have in their way
/// from memory.
/// </para>
/// <para>
/// A gate order is cancelled while its worker is on the way only when the order to cancel
/// comes in that time. So one gate order in four is followed by its own: the script notes the
/// worker and gives the order to cancel a few ticks later, if the worker still has the gate to
/// lay then.
/// </para>
/// </remarks>
[OrderScript("gates")]
internal sealed class GateOrders : OrderScript
{
    private const int LongestPause = 60;

    /// <summary>The most camps the script lays whole for one player.</summary>
    private const int MostCampsLaidWhole = 2;

    /// <summary>How many places the script tries before it settles for one that may not do.</summary>
    private const int PlacesTried = 16;

    /// <summary>What the seed of the gate orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D18_B000_0000_0000;

    private static readonly (int X, int Y)[] Sides = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private readonly DeterministicRandom _random;
    private readonly StructureDef? _gate;
    private readonly StructureDef? _wall;
    private readonly int[] _campsLaidWhole;
    private readonly List<EntityId> _workers = [];
    private readonly List<EntityId> _fighters = [];
    private readonly List<EntityId> _found = [];
    private readonly List<EntityId> _gates = [];
    private readonly List<EntityId> _enemyGates = [];
    private readonly List<EntityId> _unseenGates = [];
    private readonly List<EntityId> _rememberedGates = [];
    private readonly List<(int Tick, int Player, EntityId Worker)> _toCancel = [];

    // Whether the last gate picked was one its player had never seen: for the orders past a gate, and for those at one.
    private readonly bool[] _lastWasUnseen = new bool[2];
    private readonly List<EntityId> _walls = [];
    private int _nextTick;

    public GateOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
        _gate = Match.Data.Gate;
        _wall = Match.Data.Structures.FirstOrDefault(structure => structure.Wall is not null);
        _campsLaidWhole = new int[Match.Players.Count];
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (_gate is null)
        {
            return;
        }

        CancelWhatWasNoted(tick, commands);
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
        else if (kind < 70)
        {
            ThroughOwnGate(player, commands);
        }
        else if (kind < 73)
        {
            CampLaidWhole(player, commands);
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
        else if (_fighters.Count > 0 && _enemyGates.Count + _unseenGates.Count > 0)
        {
            var turn = kind < 90 ? 0 : 1;
            var gates = _enemyGates.Count > 0 && (_unseenGates.Count == 0 || _lastWasUnseen[turn]) ? _enemyGates : _unseenGates;
            _lastWasUnseen[turn] = gates == _unseenGates;
            var gate = Any(gates);
            EntityId[] sent = [.. Some(_fighters)];
            if (kind < 90)
            {
                // To ground behind a gate of an enemy. The script looks for a gate, a unit and
                // a place that the gate, of all the tiles of wall, is in the unit's way to: the
                // tile straight behind the gate as the unit comes to it, or a place within three
                // tiles. For a gate the player has never seen it looks for a place the unit
                // cannot reach and that no wall it knows keeps it from, so that the unit comes
                // to know the gate on its way. Where it finds none, the place is one or two
                // tiles past a gate on the side away from the first of the units.
                var known = gates == _enemyGates;
                var walked = Match.Grid.For(player);
                var place = Behind(Match.Bodies.Get(gate).Position, Match.Bodies.Get(sent[0]).Position, 1 + _random.NextInt(2), exact: false);
                for (var i = 0; i < PlacesTried; i++)
                {
                    var tried = Any(known && _rememberedGates.Count > 0 && i < PlacesTried / 2 ? _rememberedGates : gates);
                    var unit = Any(_fighters);
                    var from = Match.Bodies.Get(unit).Position;
                    var at = Match.Bodies.Get(tried).Position;
                    var behind = i % 2 == 0
                        ? Behind(at, from, 1, exact: true)
                        : new FixedVector2(Near(at.X, 3 * Fixed.RawOne), Near(at.Y, 3 * Fixed.RawOne));
                    if (!Match.Grid.Contains(behind))
                    {
                        continue;
                    }

                    if (known
                        ? Match.Breaching.FindWall(player, from, behind, out _) == tried
                        : Match.Grid.IsPassable(behind)
                            && walked.RegionOf(behind) != walked.RegionOf(from)
                            && !Match.Breaching.IsCutOff(player, from, behind)
                            && FixedVector2.DistanceSquaredRaw(from, at) < FixedVector2.DistanceSquaredRaw(from, behind))
                    {
                        place = behind;
                        sent = [unit, .. sent.Where(other => other != unit)];
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
    /// A camp one or two steps wide round a free tile of the player's territory, every tile of
    /// it put there finished with the development placement: a gate on one corner and wall on
    /// the rest. A tile that has no room is left out.
    /// </summary>
    private void CampLaidWhole(int player, List<Command> commands)
    {
        if (_wall is null
            || _workers.Count == 0
            || _campsLaidWhole[player] >= MostCampsLaidWhole
            || !TryPickTileInTerritory(player, out var middle))
        {
            return;
        }

        var radius = 1 + _random.NextInt(2);
        var corner = Sides[_random.NextInt(Sides.Length)];
        var gate = (X: middle.X + (corner.X * radius), Y: middle.Y + (corner.Y * radius));
        _campsLaidWhole[player]++;
        for (var dx = -radius; dx <= radius; dx++)
        {
            var dy = radius - Math.Abs(dx);
            AddTile(middle.X + dx, middle.Y + dy);
            if (dy != 0)
            {
                AddTile(middle.X + dx, middle.Y - dy);
            }
        }

        void AddTile(int x, int y)
        {
            var point = MapGrid.TileCenter(x, y);
            var structure = (x, y) == gate ? _gate! : _wall;
            commands.Add(new Command(CommandType.DebugPlace, player, targetX: point.X, targetY: point.Y, contentId: structure.Index));
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

    /// <summary>
    /// A place so many tiles past a gate on the side away from a unit, along the axis on which
    /// the unit is further from the gate: the middle of that tile, or a point near the gate's
    /// place on the other axis.
    /// </summary>
    private FixedVector2 Behind(FixedVector2 gate, FixedVector2 from, int tiles, bool exact)
    {
        var (dx, dy) = (gate.X.Raw - from.X.Raw, gate.Y.Raw - from.Y.Raw);
        var past = tiles * Fixed.RawOne;
        var slack = exact ? 0 : Fixed.RawOne / 2;
        return Math.Abs(dx) >= Math.Abs(dy)
            ? new FixedVector2(Fixed.FromRaw(gate.X.Raw + (Math.Sign(dx) * past)), Near(gate.Y, slack))
            : new FixedVector2(Near(gate.X, slack), Fixed.FromRaw(gate.Y.Raw + (Math.Sign(dy) * past)));
    }

    /// <summary>
    /// The orders to cancel that are due: each for a worker that was given a gate to lay a few
    /// ticks ago and is still on its way.
    /// </summary>
    private void CancelWhatWasNoted(int tick, List<Command> commands)
    {
        for (var i = _toCancel.Count - 1; i >= 0; i--)
        {
            var (due, player, worker) = _toCancel[i];
            if (tick < due)
            {
                continue;
            }

            _toCancel.RemoveAt(i);
            if (Match.World.Entities.IsAlive(worker) && Match.BuildOrders.TryGet(worker, out var order) && order.Type == _gate!.Index)
            {
                commands.Add(new Command(CommandType.CancelBuild, player, [worker]));
            }
        }
    }

    /// <summary>A build order for a gate on a tile, to a worker: to one of the player's without a wall order, if it has one, when none is named.</summary>
    private Command Gate(int player, (int X, int Y) tile, EntityId worker = default)
    {
        if (worker == EntityId.None)
        {
            _found.Clear();
            _found.AddRange(_workers.Where(candidate => !Match.Walls.Has(candidate)));
            worker = Any(_found.Count > 0 ? _found : _workers);
            if (_random.NextInt(4) == 0)
            {
                _toCancel.Add((Match.World.Tick + 3 + _random.NextInt(20), player, worker));
            }
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
    /// plain tiles of wall, finished or not, and the gates of the others: those the player sees
    /// or remembers, of them those it only remembers, and those it has never seen.
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
        _unseenGates.Clear();
        _rememberedGates.Clear();
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
            else if (!Match.Vision.KnowsStructure(player, entity))
            {
                _unseenGates.Add(entity);
            }
            else
            {
                _enemyGates.Add(entity);
                if (!Match.Vision.Sees(player, entity))
                {
                    _rememberedGates.Add(entity);
                }
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
