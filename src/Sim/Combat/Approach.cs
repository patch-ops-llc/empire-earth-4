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

        if (destination == position)
        {
            // It stands as near as it can get, so there is no walk to start.
            mover.Stop();
            return;
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
