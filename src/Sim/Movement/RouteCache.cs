using Sim.Map;

namespace Sim.Movement;

/// <summary>
/// Keeps the routes that have been found, so that the units of a group and the ticks of a walk do
/// not search again. A mover's state names its route by its two end tiles; the tiles in between
/// are looked up here.
/// </summary>
/// <remarks>
/// Not part of the state, and nothing in the state may depend on what it holds. A route is a pure
/// function of the grid and its two ends, so a route that is missing is found again and is the same
/// route. That is why the cache can be emptied at any moment, and why a world restored from a
/// snapshot needs nothing from it. It is only ever asked for one route by its ends, never walked.
/// <para>
/// A finished gate is open to its owner alone, so a route is a route on one player's grid
/// (<see cref="MapGrid.For"/>) and is kept under that grid with its two ends. Players with no
/// gate to walk through walk the grid everyone shares, and share its routes.
/// </para>
/// </remarks>
public sealed class RouteCache
{
    /// <summary>
    /// How many routes are kept before the ones not asked for lately are dropped. Routes are kept
    /// in two halves of this size: a full half replaces the older one, and a route asked for from
    /// the older half moves to the newer, so a route in use is never searched for twice.
    /// </summary>
    public const int Capacity = 1024;

    private readonly RouteFinder _finder;
    private readonly int _tileCount;
    private Dictionary<long, Route> _recent = [];
    private Dictionary<long, Route> _older = [];

    public RouteCache(MapGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _finder = new RouteFinder(grid);
        Grid = grid;
        _tileCount = grid.Width * grid.Height;
    }

    public MapGrid Grid { get; }

    /// <summary>How many searches have been run. For tests and diagnostics; nothing in the state may depend on it.</summary>
    public long SearchCount { get; private set; }

    /// <summary>
    /// The route between two tiles on the grid everyone shares, each given as
    /// <c>y * width + x</c>. The tiles must be in the same region.
    /// </summary>
    public Route Get(int startTile, int goalTile) => Get(Grid, startTile, goalTile);

    /// <summary>
    /// The route between two tiles for the units of a player. The tiles must be in the same
    /// region of the player's grid.
    /// </summary>
    public Route Get(int player, int startTile, int goalTile) => Get(Grid.For(player), startTile, goalTile);

    private Route Get(MapGrid grid, int startTile, int goalTile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startTile);
        ArgumentOutOfRangeException.ThrowIfNegative(goalTile);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startTile, _tileCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(goalTile, _tileCount);

        var key = (((grid.View * (long)_tileCount) + startTile) * _tileCount) + goalTile;
        if (_recent.TryGetValue(key, out var route))
        {
            return route;
        }

        if (!_older.TryGetValue(key, out route))
        {
            var width = grid.Width;
            var tiles = _finder.Find(grid, startTile % width, startTile / width, goalTile % width, goalTile / width, out var expansions);
            route = new Route(tiles, expansions);
            SearchCount++;
        }

        if (_recent.Count >= Capacity)
        {
            (_recent, _older) = (_older, _recent);
            _recent.Clear();
        }

        _recent.Add(key, route);
        return route;
    }

    /// <summary>Forgets every route. Changes nothing but the work done later.</summary>
    public void Clear()
    {
        _recent.Clear();
        _older.Clear();
    }
}
