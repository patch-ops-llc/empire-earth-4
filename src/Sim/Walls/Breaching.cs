using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Walls;

/// <summary>
/// Finds the wall tile in a unit's way (design 2.5): "A unit ordered to a place it cannot reach
/// attacks the nearest enemy wall tile in its way. That tile is found by a second path search
/// that may cross enemy wall tiles at a high cost; the first such tile on that path is the
/// target."
/// </summary>
/// <remarks>
/// <para>
/// <b>When a wall is in the way.</b> A place is cut off from a unit by enemy walls when the unit
/// cannot walk to it now and could if the wall tiles of its enemies were out of the way. Both
/// are lookups, in the regions of the grid and in <see cref="WallMap"/>, so asking costs
/// nothing; the search runs only when it is sure to find a wall. Ground that is closed off by
/// anything else, the player's own walls among it, is not cut off in this sense, and the unit
/// goes to the nearest tile it can reach (design 5.3).
/// </para>
/// <para>
/// <b>The search</b> is <see cref="RouteFinder.FindAcross"/> from the unit's tile to the place's
/// tile, with one step into an enemy wall tile costing as many steps as the rules'
/// <c>wall_crossing_cost</c>. It depends only on the tiles, the walls and the two ends, so every
/// machine finds the same tile. Nothing is kept from one search to the next.
/// </para>
/// </remarks>
public sealed class Breaching
{
    /// <summary>
    /// The tiles that the searches for walls may expand in one tick's attacks before the rest
    /// wait for a later tick. As for routes, a search is started while any of it is left and
    /// always runs to its end.
    /// </summary>
    public const int ExpansionsPerTick = RoutePlanningSystem.ExpansionsPerTick;

    private readonly MapGrid _grid;
    private readonly WallMap _walls;
    private readonly RouteFinder _finder;
    private readonly int _crossingCost;

    public Breaching(MapGrid grid, WallMap walls, int wallCrossingCost)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(walls);
        ArgumentOutOfRangeException.ThrowIfLessThan(wallCrossingCost, 1);
        _grid = grid;
        _walls = walls;
        _finder = new RouteFinder(grid);
        _crossingCost = wallCrossingCost;
    }

    public WallMap Walls => _walls;

    /// <summary>How many searches have been run. For tests and diagnostics; nothing in the state may depend on it.</summary>
    public long SearchCount { get; private set; }

    /// <summary>
    /// Where an order to a place takes a unit of a player once the walls of its enemies are out
    /// of its way: the place itself when the unit could then walk to its tile, and else the
    /// center of the nearest tile it could then walk to, found as design 5.3 finds the nearest
    /// tile a unit can reach. The place must be on the map.
    /// </summary>
    /// <param name="player">The unit's owner.</param>
    /// <param name="position">Where the unit stands.</param>
    /// <param name="place">The place it is ordered to.</param>
    /// <param name="preferNear">Among tiles equally near the place, the one nearest this point wins.</param>
    /// <returns>The place, also for a unit that stands where no unit can.</returns>
    public FixedVector2 GoalFor(int player, FixedVector2 position, FixedVector2 place, FixedVector2 preferNear)
    {
        var past = _walls.PastWalls(player);
        var own = LabelOf(past, position);
        if (own == 0 || LabelOf(past, place) == own)
        {
            return place;
        }

        // The unit's own tile has the label, so this finds one.
        _grid.TryFindNearestTile(past, place, own, preferNear, out var x, out var y);
        return MapGrid.TileCenter(x, y);
    }

    /// <summary>
    /// True when a place is cut off from a unit of a player by enemy walls: the unit cannot walk
    /// to the place's tile now, and could if the wall tiles of its enemies were out of the way.
    /// </summary>
    public bool IsCutOff(int player, FixedVector2 position, FixedVector2 place)
    {
        var region = _grid.RegionOf(position);
        if (region == 0 || !_grid.Contains(place) || _grid.RegionOf(place) == region)
        {
            return false;
        }

        var past = _walls.PastWalls(player);
        var own = LabelOf(past, position);
        return own != 0 && LabelOf(past, place) == own;
    }

    /// <summary>
    /// The wall tile in the way of a unit of a player that is ordered to a place: the first
    /// enemy wall tile on the cheapest way there that may cross such tiles. None when the place
    /// is not cut off from the unit as <see cref="IsCutOff"/> says.
    /// </summary>
    /// <param name="player">The unit's owner.</param>
    /// <param name="position">Where the unit stands.</param>
    /// <param name="place">The place it is ordered to.</param>
    /// <param name="expansions">The number of tiles the search expanded, zero when none was run.</param>
    public EntityId FindWall(int player, FixedVector2 position, FixedVector2 place, out int expansions)
    {
        expansions = 0;
        if (!IsCutOff(player, position, place))
        {
            return EntityId.None;
        }

        var width = _grid.Width;
        var way = _finder.FindAcross(
            position.X.FloorToInt(),
            position.Y.FloorToInt(),
            place.X.FloorToInt(),
            place.Y.FloorToInt(),
            _walls.EnemyWalls(player),
            _crossingCost,
            out expansions);
        SearchCount++;
        if (way is null)
        {
            throw new InvalidOperationException($"No way leads from {position} to {place} though both are on the same ground past the walls.");
        }

        var enemyWalls = _walls.EnemyWalls(player);
        foreach (var tile in way)
        {
            if (enemyWalls[tile])
            {
                return _walls.WallAt(tile % width, tile / width);
            }
        }

        throw new InvalidOperationException($"The way from {position} to {place} crosses no wall though no walk leads there.");
    }

    private int LabelOf(ReadOnlySpan<int> labels, FixedVector2 point)
    {
        var x = point.X.FloorToInt();
        var y = point.Y.FloorToInt();
        return _grid.Contains(x, y) ? labels[(y * _grid.Width) + x] : 0;
    }
}
