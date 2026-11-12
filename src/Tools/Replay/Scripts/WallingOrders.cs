using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Walls (18a): wall orders across the chokepoints of the map, round small camps and anywhere
/// else, orders that end a wall order early, and fighting units sent to places behind the walls
/// of an enemy, which is what the wall rule of design 2.5 is for.
/// </summary>
/// <remarks>
/// <para>
/// A wall must stand in its owner's territory, and a chokepoint lies far from where a player
/// starts. So the script puts an Outpost beside a chokepoint with the development placement, and
/// makes the units that break in with the development spawn, as <see cref="SiegeOrders"/> does:
/// it needs <c>debug.spawn</c> in the rules of the data. The walls themselves are ordered, paid
/// for and laid by the players' workers.
/// </para>
/// <para>
/// A camp is a ring whose tiles touch only at their corners: the tiles one, two or three steps
/// from its middle. It is ordered as four runs, each to another worker and each from one corner
/// of the ring to the tile before the next. A new camp is one or two steps wide, and a player
/// pays for it with what it starts with.
/// Half the time the script orders a camp round the middle of the player's last one, one step
/// wider or narrower, so that one ring stands inside the other and a unit that breaks in has a
/// second wall in its way. It also makes units in the middle of a camp, the owner's or an
/// enemy's, so that there is something behind the walls to attack and something to break out.
/// </para>
/// </remarks>
[OrderScript("walls")]
internal sealed class WallingOrders : OrderScript
{
    private const int LongestPause = 50;

    /// <summary>The Outposts a player has before the script stops placing more for it.</summary>
    private const int MostOutposts = 2;

    /// <summary>The fighting units a player has before the script stops making more for it.</summary>
    private const int MostFighters = 10;

    /// <summary>How many places the script tries before it settles for one that may not do.</summary>
    private const int PlacesTried = 16;

    /// <summary>What the seed of the wall orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D18_A000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<MapMarker> _chokepoints = [];
    private readonly StructureDef? _outpost;
    private readonly List<EntityId> _workers = [];
    private readonly List<EntityId> _fighters = [];
    private readonly List<EntityId> _found = [];
    private readonly List<EntityId> _enemyWalls = [];
    private readonly List<EntityId> _walls = [];
    private readonly ((int X, int Y) Middle, int Radius)?[] _lastCamp;
    private int _nextTick;

    public WallingOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
        _lastCamp = new ((int X, int Y) Middle, int Radius)?[Match.Players.Count];
        _chokepoints.AddRange(Match.Map.Markers.Where(marker => marker.Kind == MarkerKind.Chokepoint));
        _outpost = Match.Data.Structures.FirstOrDefault(
            structure => structure.TerritoryRadius is not null && structure.Index != Match.Data.StartStructure.Index);
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
        if (kind < 8)
        {
            PlaceOutpost(player, commands);
        }
        else if (kind < 26)
        {
            // A wall from one side of a chokepoint to the other, by some of the workers.
            if (_workers.Count > 0 && _chokepoints.Count > 0)
            {
                var (first, last) = Across(_chokepoints[_random.NextInt(_chokepoints.Count)]);
                commands.Add(Wall(player, SomeWorkers(), first, last));
            }
        }
        else if (kind < 42)
        {
            Camp(player, commands);
        }
        else if (kind < 54)
        {
            // A run in any direction from a tile of the player's territory, over whatever is there,
            // or from a tile that one of the player's wall orders has yet to lay: whichever worker
            // comes to that tile second skips it.
            if (_workers.Count > 0 && TryPickTileInTerritory(player, mustBeFree: false, out var first))
            {
                if (TryPickPlannedTile(player, out var planned) && _random.NextInt(2) == 0)
                {
                    first = planned;
                }

                var last = (first.X + _random.NextInt(-4, 5), first.Y + _random.NextInt(-4, 5));
                commands.Add(Wall(player, SomeWorkers(), first, last));
            }
        }
        else if (kind < 60)
        {
            Refused(player, commands);
        }
        else if (kind < 68)
        {
            // Cancel: a worker's wall order, or a tile of wall that is building itself.
            CollectWorkersWithWallOrders();
            if (_found.Count == 0 || _random.NextInt(3) == 0)
            {
                CollectWallFoundations(player);
            }

            if (_found.Count > 0)
            {
                commands.Add(new Command(CommandType.CancelBuild, player, [_found[_random.NextInt(_found.Count)]]));
            }
        }
        else if (kind < 74)
        {
            // A worker with a wall order is sent somewhere else, which ends the order.
            CollectWorkersWithWallOrders();
            if (_found.Count > 0)
            {
                var point = AnyPoint(_random);
                var type = _random.NextInt(3) == 0 ? CommandType.Stop : CommandType.Move;
                commands.Add(new Command(type, player, [_found[_random.NextInt(_found.Count)]], point.X, point.Y));
            }
        }
        else if (kind < 84)
        {
            // One to three units of one chassis, within five tiles of a tile of wall, the player's
            // own or an enemy's: outside the wall or inside it.
            // Or one unit in the middle of the last camp of a player, this one or another.
            var chassis = _random.NextInt(Match.Data.Chassis.Count);
            var count = 1 + _random.NextInt(3);
            if (_fighters.Count < MostFighters && _random.NextInt(2) == 0 && _lastCamp[_random.NextInt(_lastCamp.Length)] is { } camp)
            {
                var middle = MapGrid.TileCenter(camp.Middle.X, camp.Middle.Y);
                commands.Add(new Command(CommandType.DebugSpawn, player, targetX: middle.X, targetY: middle.Y, contentId: chassis));
            }
            else if (_walls.Count > 0)
            {
                var near = Match.Bodies.Get(Any(_walls)).Position;
                for (var i = 0; i < count && _fighters.Count + i < MostFighters; i++)
                {
                    commands.Add(new Command(CommandType.DebugSpawn, player, targetX: Near(near.X, 5), targetY: Near(near.Y, 5), contentId: chassis));
                }
            }
        }
        else if (_fighters.Count > 0)
        {
            OrderFighters(player, commands);
        }
    }

    private static (int X, int Y) TileOf(FixedVector2 point) => (point.X.FloorToInt(), point.Y.FloorToInt());

    /// <summary>The run that closes a chokepoint: across its width, in one of the lines of tiles it is long.</summary>
    private ((int X, int Y) First, (int X, int Y) Last) Across(MapMarker marker)
    {
        var area = marker.Area;
        if (marker.Across == MapAxis.Y)
        {
            var x = area.X + _random.NextInt(area.SizeX);
            return ((x, area.Y), (x, area.Y + area.SizeY - 1));
        }

        var y = area.Y + _random.NextInt(area.SizeY);
        return ((area.X, y), (area.X + area.SizeX - 1, y));
    }

    /// <summary>
    /// An Outpost four to seven tiles from a chokepoint, on the side the player's first territory
    /// lies on, so that the chokepoint is the player's ground. Refused where it has no room.
    /// </summary>
    private void PlaceOutpost(int player, List<Command> commands)
    {
        if (_outpost is null || _workers.Count == 0 || _chokepoints.Count == 0 || CountOf(player, _outpost.Index) >= MostOutposts)
        {
            return;
        }

        var marker = _chokepoints[_random.NextInt(_chokepoints.Count)];
        var area = marker.Area;
        var away = 4 + _random.NextInt(4);
        var along = _random.NextInt(-2, 3);
        var home = HomeOf(player);
        int x;
        int y;
        if (marker.Across == MapAxis.Y)
        {
            x = home.X < area.X ? area.X - away - _outpost.Footprint : area.X + area.SizeX + away;
            y = area.Y + (area.SizeY / 2) + along;
        }
        else
        {
            x = area.X + (area.SizeX / 2) + along;
            y = home.Y < area.Y ? area.Y - away - _outpost.Footprint : area.Y + area.SizeY + away;
        }

        var point = MapGrid.TileCenter(x, y);
        commands.Add(new Command(CommandType.DebugPlace, player, targetX: point.X, targetY: point.Y, contentId: _outpost.Index));
    }

    /// <summary>
    /// A camp round a free tile of the player's territory: the tiles one or two steps from it, as
    /// four runs, each given to another worker. Half the time it goes round the middle of the
    /// player's last camp instead, one step wider or narrower.
    /// </summary>
    private void Camp(int player, List<Command> commands)
    {
        if (_workers.Count == 0)
        {
            return;
        }

        (int X, int Y) middle;
        int radius;
        if (_lastCamp[player] is { } last && _random.NextInt(2) == 0)
        {
            middle = last.Middle;
            radius = last.Radius == 2 ? 1 + (2 * _random.NextInt(2)) : 2;
        }
        else if (TryPickTileInTerritory(player, mustBeFree: true, out middle))
        {
            radius = 1 + _random.NextInt(2);
        }
        else
        {
            return;
        }

        _lastCamp[player] = (middle, radius);
        (int X, int Y)[] corners =
        [
            (middle.X - radius, middle.Y), (middle.X, middle.Y - radius), (middle.X + radius, middle.Y), (middle.X, middle.Y + radius),
        ];

        // The workers that have no wall order come first, so that one camp does not end another.
        _found.Clear();
        _found.AddRange(_workers.Where(worker => !Match.Walls.Has(worker)));
        _found.AddRange(_workers.Where(Match.Walls.Has));
        var workers = _found.ToArray();
        for (var side = 0; side < 4; side++)
        {
            // From one corner to the tile before the next, which the next run begins with.
            var from = corners[side];
            var to = corners[(side + 1) % 4];
            var before = (to.X - Math.Sign(to.X - from.X), to.Y - Math.Sign(to.Y - from.Y));
            commands.Add(Wall(player, [workers[side % workers.Length]], from, before));
        }

        // Now and then one side is ordered twice. Both workers pay for it, and whichever comes
        // to a tile second skips it.
        if (_random.NextInt(3) == 0)
        {
            var from = corners[0];
            var before = (corners[1].X - 1, corners[1].Y + 1);
            commands.Add(Wall(player, [workers[4 % workers.Length]], from, before));
        }
    }

    /// <summary>Wall orders that are refused: outside the map or the territory, by nobody who can take them, or from nobody.</summary>
    private void Refused(int player, List<Command> commands)
    {
        var first = TileOf(AnyPoint(_random));
        var tiles = Match.Grid.Width * Match.Grid.Height;
        switch (_random.NextInt(4))
        {
            case 0:
                // Anywhere on the map: outside the territory, most of the time.
                commands.Add(Wall(player, Own, first, (first.X + _random.NextInt(-6, 7), first.Y + _random.NextInt(-6, 7))));
                break;
            case 1:
                // The last tile is no tile of the map.
                var point = MapGrid.TileCenter(first.X, first.Y);
                var number = _random.NextInt(2) == 0 ? -1 - _random.NextInt(5) : tiles + _random.NextInt(5);
                commands.Add(new Command(CommandType.BuildWall, player, [.. Own], point.X, point.Y, contentId: number));
                break;
            case 2:
                // By the units that fight, which lay no walls.
                if (_fighters.Count > 0 && TryPickTileInTerritory(player, mustBeFree: true, out first))
                {
                    commands.Add(Wall(player, _fighters, first, (first.X + 3, first.Y)));
                }

                break;
            default:
                // From a player who is not in the match.
                if (TryPickTileInTerritory(player, mustBeFree: true, out first))
                {
                    commands.Add(Wall(Match.Players.Count + _random.NextInt(5), Own, first, (first.X + 3, first.Y)));
                }

                break;
        }
    }

    /// <summary>
    /// Sends some of the player's fighting units to a place that the walls of an enemy keep the
    /// first of them from, or at something of an enemy that those walls keep it from, if the
    /// script finds one. A unit that has a wall in its way is mostly left to it.
    /// </summary>
    private void OrderFighters(int player, List<Command> commands)
    {
        var disturb = _random.NextInt(4) == 0;
        _found.Clear();
        _found.AddRange(_fighters.Where(fighter => disturb || Match.Combatants.Get(fighter).WallInWay == EntityId.None));
        if (_found.Count == 0)
        {
            return;
        }

        EntityId[] sent = [.. Some(_found)];
        var from = Match.Bodies.Get(sent[0]).Position;
        var way = _random.NextInt(10);
        if (way < 6)
        {
            var place = AnyPoint(_random);
            for (var i = 0; i < PlacesTried && _enemyWalls.Count > 0; i++)
            {
                var wall = Match.Bodies.Get(Any(_enemyWalls)).Position;
                place = new FixedVector2(Near(wall.X, 3), Near(wall.Y, 3));
                if (Match.Grid.Contains(place) && Match.Breaching.IsCutOff(player, from, place))
                {
                    break;
                }
            }

            commands.Add(new Command(way < 3 ? CommandType.Move : CommandType.AttackMove, player, sent, place.X, place.Y));
        }
        else if (way < 9)
        {
            // Anything of an enemy that can be attacked, a wall as likely as anything else.
            _found.Clear();
            for (var i = 0; i < Match.Health.Count; i++)
            {
                var entity = Match.Health.EntityAt(i);
                if (Match.World.Entities.IsAlive(entity) && Match.Bodies.TryGet(entity, out var body) && body.Owner != player)
                {
                    _found.Add(entity);
                }
            }

            if (_found.Count == 0)
            {
                return;
            }

            var target = Any(_found);
            for (var i = 0; i < PlacesTried; i++)
            {
                if (Match.Breaching.IsCutOff(player, from, Match.Bodies.Get(target).Position))
                {
                    break;
                }

                target = Any(_found);
            }

            commands.Add(new Command(CommandType.Attack, player, sent, targetEntity: target));
        }
        else
        {
            commands.Add(new Command(CommandType.Stop, player, sent));
        }
    }

    private Command Wall(int player, List<EntityId> workers, (int X, int Y) first, (int X, int Y) last)
    {
        // A point anywhere in the first tile, and the number of the last, which may be off the map.
        var x = Fixed.FromRaw((first.X * Fixed.RawOne) + _random.NextInt(Fixed.RawOne));
        var y = Fixed.FromRaw((first.Y * Fixed.RawOne) + _random.NextInt(Fixed.RawOne));
        var width = Match.Grid.Width;
        var number = last.X < 0 || last.X >= width ? -1 : (last.Y * width) + last.X;
        return new Command(CommandType.BuildWall, player, [.. workers], x, y, contentId: number);
    }

    /// <summary>A tile inside one of the player's territories, of those tried the first that is free ground if that is asked for.</summary>
    private bool TryPickTileInTerritory(int player, bool mustBeFree, out (int X, int Y) tile)
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
            if (!mustBeFree || (Match.Grid.Contains(tile.X, tile.Y) && Match.Grid.IsPassable(tile.X, tile.Y)))
            {
                break;
            }
        }

        return true;
    }

    /// <summary>A tile that a wall order of the player has paid for and not yet laid.</summary>
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

        var pick = _random.NextInt(own);
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

    /// <summary>The middle tile of the player's first territory, or of the map when it has none.</summary>
    private (int X, int Y) HomeOf(int player)
    {
        var territories = Match.Territories;
        for (var i = 0; i < territories.Count; i++)
        {
            var territory = territories.ValueAt(i);
            if (territory.Owner == player && Match.World.Entities.IsAlive(territories.EntityAt(i)))
            {
                return (territory.CenterX2 / 2, territory.CenterY2 / 2);
            }
        }

        return (Match.Grid.Width / 2, Match.Grid.Height / 2);
    }

    private int CountOf(int player, int structure)
    {
        var count = 0;
        for (var i = 0; i < Match.Structures.Count; i++)
        {
            var entity = Match.Structures.EntityAt(i);
            if (Match.Structures.ValueAt(i).Type == structure
                && Match.World.Entities.IsAlive(entity)
                && Match.Bodies.Get(entity).Owner == player)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Sorts what there is for a player: its workers, its units that fight, every tile of wall, and those of the others.</summary>
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

        _walls.Clear();
        _enemyWalls.Clear();
        var structures = Match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            if (Match.WallMap.IsWall(structures.ValueAt(i).Type)
                && Match.World.Entities.IsAlive(entity)
                && Match.Bodies.TryGet(entity, out var body))
            {
                _walls.Add(entity);
                if (body.Owner != player)
                {
                    _enemyWalls.Add(entity);
                }
            }
        }
    }

    /// <summary>The workers in <see cref="_workers"/> that have a wall order.</summary>
    private void CollectWorkersWithWallOrders()
    {
        _found.Clear();
        foreach (var worker in _workers)
        {
            if (Match.Walls.Has(worker))
            {
                _found.Add(worker);
            }
        }
    }

    private void CollectWallFoundations(int player)
    {
        _found.Clear();
        var foundations = Match.Foundations;
        for (var i = 0; i < foundations.Count; i++)
        {
            var structure = foundations.EntityAt(i);
            if (Match.World.Entities.IsAlive(structure)
                && Match.Bodies.Get(structure).Owner == player
                && Match.WallMap.IsWall(Match.Structures.Get(structure).Type))
            {
                _found.Add(structure);
            }
        }
    }

    private List<EntityId> SomeWorkers() => Some(_workers);

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
