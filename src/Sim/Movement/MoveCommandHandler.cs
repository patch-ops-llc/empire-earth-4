using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Executes <see cref="CommandType.Move"/>: sends the listed entities to the target position,
/// replacing whatever move order each had.
/// </summary>
/// <remarks>
/// <para>
/// The command is valid when its player is in the match, its target is a point on the map, and at
/// least one listed entity can take the order: alive, owned by the player, and a mover. Entities
/// that cannot take it are skipped, because a unit may die between the moment a player gives an
/// order and the tick it runs, and the rest of the group should still go. A command that moves
/// nobody is dropped.
/// </para>
/// <para>
/// A group keeps its shape (design 5.3). Each unit goes to the target plus its own offset from the
/// middle of the group, which is the center of the box that holds the centers of all the units
/// that take the order. A group whose box is more than <see cref="LongestGroup"/> tiles long or
/// wide has no shape to keep: all its units go to the target and settle around it.
/// </para>
/// <para>
/// Each unit's own place then follows the rules for any destination. A place the unit cannot
/// stand on, or one cut off from it, becomes the center of the nearest tile it can reach; among
/// tiles equally near, the one nearest the first unit of the order in the same area wins, then the
/// lower y, then the lower x. A place right beside blocked ground is moved off it by
/// <see cref="Mover.StandOff"/>, inside its tile. A place that falls outside the map is first
/// taken to the nearest point of the map.
/// </para>
/// <para>
/// A unit whose way to its place is a clear straight line walks it. Any other unit gets a route.
/// One order makes one route for the whole group where it can. The route ends on the target's
/// tile, or on the nearest tile to the target that the units of an area can reach, and a unit
/// takes it when the walk from that tile's center to its own place is clear; it then walks beside
/// the route by its offset in the group (<see cref="Mover.Spread"/>). A unit whose place cannot be
/// reached that way gets a route to its place's own tile. Either way a unit takes the route of an
/// earlier unit in the list when it can walk straight to that route's first tile, and starts a
/// route of its own from the tile it stands on when it cannot. Routes ignore units.
/// </para>
/// </remarks>
public sealed class MoveCommandHandler : ICommandHandler
{
    /// <summary>The most first tiles that one order's routes start from, in each region.</summary>
    public const int MaxSharedRoutes = 8;

    /// <summary>A group whose box is longer or wider than this many tiles has no shape to keep (design 5.3).</summary>
    public const int LongestGroup = 16;

    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly int _playerCount;

    // Scratch for one command, emptied at its start. Not state.
    private readonly List<Area> _areas = [];
    private readonly List<Member> _members = [];
    private readonly List<SharedStart> _starts = [];
    private readonly List<(int Mover, FixedVector2 Place)> _places = [];

    // The grid of the player whose order is being carried out. Scratch, like the lists above.
    private MapGrid _walked;

    public MoveCommandHandler(MapGrid grid, ComponentStore<Body> bodies, ComponentStore<Mover> movers, int playerCount)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        _grid = grid;
        _walked = grid;
        _bodies = bodies;
        _movers = movers;
        _playerCount = playerCount;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        var target = new FixedVector2(command.TargetX, command.TargetY);
        if (command.Player < 0 || command.Player >= _playerCount || !_grid.Contains(target))
        {
            return false;
        }

        // The order is worked out on the ground as the player's units walk it.
        _walked = _grid.For(command.Player);

        // First who takes the order and the box that holds them, then where each of them goes.
        _areas.Clear();
        _members.Clear();
        _starts.Clear();
        _places.Clear();
        var low = new FixedVector2(Fixed.MaxValue, Fixed.MaxValue);
        var high = new FixedVector2(Fixed.MinValue, Fixed.MinValue);
        foreach (var entity in command.Entities)
        {
            if (!world.Entities.IsAlive(entity)
                || !_bodies.TryGet(entity, out var body)
                || body.Owner != command.Player)
            {
                continue;
            }

            var index = _movers.IndexOf(entity);
            if (index < 0)
            {
                continue;
            }

            var position = body.Position;
            var region = _walked.RegionOf(position);
            if (region == 0)
            {
                continue;
            }

            low = new FixedVector2(Fixed.Min(low.X, position.X), Fixed.Min(low.Y, position.Y));
            high = new FixedVector2(Fixed.Max(high.X, position.X), Fixed.Max(high.Y, position.Y));
            _members.Add(new Member(index, AreaOf(region, target, position), position));
        }

        if (_members.Count == 0)
        {
            return false;
        }

        var longest = Fixed.FromInt(LongestGroup);
        var keepsShape = high.X - low.X <= longest && high.Y - low.Y <= longest;
        var middle = (low + high) / 2;

        foreach (var member in _members)
        {
            var area = _areas[member.Area];
            var position = member.Position;
            var offset = keepsShape ? position - middle : FixedVector2.Zero;
            var destination = offset == FixedVector2.Zero ? area.Destination : PlaceFor(area, target + offset);
            _places.Add((member.Mover, offset == FixedVector2.Zero ? target : OnMap(target + offset)));

            ref var mover = ref _movers.ValueAt(member.Mover);
            if (TileOf(position) == TileOf(destination) || _walked.IsCorridorClear(position, destination, Mover.Clearance))
            {
                mover.WalkStraightTo(destination);
            }
            else if (destination == area.Destination || _walked.IsCorridorClear(CenterOf(area.Tile), destination, Mover.Clearance))
            {
                mover.WalkRouteTo(destination, StartFor(area.Region, TileOf(position), position), area.Tile, offset);
            }
            else
            {
                mover.WalkRouteTo(destination, StartFor(area.Region, TileOf(position), position), TileOf(destination));
            }
        }

        return true;
    }

    /// <summary>
    /// The place the command that was carried out last named for one of the units it sent: the
    /// target plus the unit's offset in the group, taken to the nearest point of the map, before
    /// the ground had its say. The wall rule of design 2.5 judges it. False for a mover the
    /// command did not send.
    /// </summary>
    /// <param name="mover">The unit's position in the store of movers.</param>
    /// <param name="place">The place.</param>
    public bool TryGetOrderedPlace(int mover, out FixedVector2 place)
    {
        foreach (var (index, ordered) in _places)
        {
            if (index == mover)
            {
                place = ordered;
                return true;
            }
        }

        place = default;
        return false;
    }

    /// <summary>
    /// The units of the order that stand in one region, and where the target itself takes them:
    /// the target, or the nearest tile to it that they can reach.
    /// </summary>
    private int AreaOf(int region, FixedVector2 target, FixedVector2 firstPosition)
    {
        for (var i = 0; i < _areas.Count; i++)
        {
            if (_areas[i].Region == region)
            {
                return i;
            }
        }

        var destination = Reachable(region, target, firstPosition);
        _areas.Add(new Area(region, TileOf(destination), destination, firstPosition));
        return _areas.Count - 1;
    }

    /// <summary>Where a unit of an area goes when its own place in the group is a point.</summary>
    private FixedVector2 PlaceFor(Area area, FixedVector2 point)
    {
        return Reachable(area.Region, OnMap(point), area.FirstPosition);
    }

    /// <summary>A point taken to the nearest point of the map. The last point of the map on each axis: a tile ends just before the next begins.</summary>
    private FixedVector2 OnMap(FixedVector2 point) => new(
        Fixed.Clamp(point.X, Fixed.Zero, Fixed.FromInt(_grid.Width) - Fixed.Epsilon),
        Fixed.Clamp(point.Y, Fixed.Zero, Fixed.FromInt(_grid.Height) - Fixed.Epsilon));

    /// <summary>
    /// A point as a destination for the units of a region: itself, kept off blocked ground, when
    /// they can reach its tile, and else the center of the nearest tile they can reach.
    /// </summary>
    private FixedVector2 Reachable(int region, FixedVector2 point, FixedVector2 firstPosition)
    {
        if (_walked.RegionOf(point) == region)
        {
            return _walked.KeepClear(point, Mover.StandOff);
        }

        // Every region has a tile, so this finds one.
        _walked.TryFindNearestTile(point, region, firstPosition, out var tileX, out var tileY);
        return MapGrid.TileCenter(tileX, tileY);
    }

    /// <summary>The first tile of the route a unit takes: that of an earlier unit of this order if it can walk straight there, else its own.</summary>
    private int StartFor(int region, int ownTile, FixedVector2 position)
    {
        var shared = 0;
        foreach (var start in _starts)
        {
            if (start.Region != region)
            {
                continue;
            }

            shared++;
            if (start.Tile == ownTile || _walked.IsCorridorClear(position, CenterOf(start.Tile), Mover.Clearance))
            {
                return start.Tile;
            }
        }

        if (shared < MaxSharedRoutes)
        {
            _starts.Add(new SharedStart(region, ownTile));
        }

        return ownTile;
    }

    private int TileOf(FixedVector2 point) => (point.Y.FloorToInt() * _grid.Width) + point.X.FloorToInt();

    private FixedVector2 CenterOf(int tile) => MapGrid.TileCenter(tile % _grid.Width, tile / _grid.Width);

    /// <param name="Region">The region its units stand in.</param>
    /// <param name="Tile">The last tile of the route its units share.</param>
    /// <param name="Destination">Where the target takes its units: the place of a unit in the middle of the group.</param>
    /// <param name="FirstPosition">Where the first unit of the order in this region stands.</param>
    private readonly record struct Area(int Region, int Tile, FixedVector2 Destination, FixedVector2 FirstPosition);

    private readonly record struct Member(int Mover, int Area, FixedVector2 Position);

    private readonly record struct SharedStart(int Region, int Tile);
}
