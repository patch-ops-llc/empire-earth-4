using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// Sends a unit walking to a place on combat's behalf: after a target, back to its post, or on with
/// its attack-move. It places the walk as a move order of one unit does (design 5.3).
/// </summary>
/// <remarks>
/// A point beside blocked ground is moved off it by <see cref="Mover.StandOff"/>. A point the unit
/// cannot reach is replaced by the center of the nearest tile it can reach, so a target across
/// ground that cannot be crossed is treated as a place the unit cannot reach (design 2.4). A unit
/// with a clear way walks straight; any other unit takes a route, and keeps the route it has when
/// that already ends in the right tile, so that following a target does not search again each time
/// the target takes a step.
/// </remarks>
public sealed class Approach
{
    private readonly MapGrid _grid;

    public Approach(MapGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _grid = grid;
    }

    /// <summary>
    /// Starts or redirects a walk from <paramref name="position"/> up to a structure: to the
    /// center of the tile beside its footprint that is nearest to the unit, of those the unit can
    /// walk to. Every tile round a footprint, the corners too, has the footprint within melee
    /// reach of its center, and a unit that shoots is stopped on the way as soon as it is in range.
    /// </summary>
    /// <remarks>
    /// Of two tiles equally near, the one with the lower y wins, then the lower x. When the unit
    /// can walk to no tile beside the footprint, it goes toward the nearest point of the
    /// footprint as toward any place it cannot reach (design 2.4).
    /// </remarks>
    public void WalkToStructure(ref Mover mover, FixedVector2 position, TileSquare tiles)
    {
        var region = _grid.RegionOf(position);
        var found = false;
        var nearest = long.MaxValue;
        var beside = default(FixedVector2);
        for (var y = tiles.Y - 1; region != 0 && y <= tiles.Y + tiles.Size; y++)
        {
            for (var x = tiles.X - 1; x <= tiles.X + tiles.Size; x++)
            {
                if (tiles.Covers(x, y) || _grid.RegionOf(x, y) != region)
                {
                    continue;
                }

                var center = MapGrid.TileCenter(x, y);
                var distance = FixedVector2.DistanceSquaredRaw(position, center);
                if (distance < nearest)
                {
                    found = true;
                    nearest = distance;
                    beside = center;
                }
            }
        }

        WalkTo(ref mover, position, found ? beside : tiles.NearestPointTo(position));
    }

    /// <summary>Starts or redirects a walk from <paramref name="position"/> toward <paramref name="target"/>, a point on the map.</summary>
    public void WalkTo(ref Mover mover, FixedVector2 position, FixedVector2 target)
    {
        var region = _grid.RegionOf(position);
        if (region == 0)
        {
            mover.Stop();
            return;
        }

        var tileX = target.X.FloorToInt();
        var tileY = target.Y.FloorToInt();
        FixedVector2 destination;
        if (_grid.RegionOf(tileX, tileY) == region)
        {
            destination = _grid.KeepClear(target, Mover.StandOff);
        }
        else
        {
            // Every region has a tile, so this finds one.
            _grid.TryFindNearestTile(target, region, position, out tileX, out tileY);
            destination = MapGrid.TileCenter(tileX, tileY);
        }

        var goal = (tileY * _grid.Width) + tileX;
        var tile = (position.Y.FloorToInt() * _grid.Width) + position.X.FloorToInt();
        if (tile == goal || _grid.IsCorridorClear(position, destination, Mover.Clearance))
        {
            mover.WalkStraightTo(destination);
        }
        else if (mover.State is MoveState.Planning or MoveState.Following && mover.RouteGoal == goal)
        {
            mover.Destination = destination;
        }
        else
        {
            mover.WalkRouteTo(destination, tile, goal);
        }
    }
}
