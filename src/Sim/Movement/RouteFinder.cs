using Sim.Map;

namespace Sim.Movement;

/// <summary>
/// Finds the cheapest walk between two tiles of the grid: A* with whole-number costs. The result
/// depends only on the grid and the two tiles, never on what was searched before, so every machine
/// finds the same route and a route can be found again at any time.
/// </summary>
/// <remarks>
/// A step goes to one of the eight tiles around a tile. A straight step costs 70 and a diagonal one
/// 99, which is the square root of two to four places. A diagonal step needs both tiles beside it
/// to be passable, so a route never cuts a corner. Among tiles that promise the same total cost the
/// search takes the one nearest the straight line from start to goal, which keeps routes over open
/// ground straight; the tile's index breaks what is left, so two routes of the same cost on a
/// mirrored map need not be mirror images.
/// <para>
/// A search runs on the grid the finder was made for or on the grid of one player
/// (<see cref="MapGrid.For"/>), which has the same tiles with that player's gates open.
/// </para>
/// </remarks>
public sealed class RouteFinder
{
    public const int StraightCost = 70;

    public const int DiagonalCost = 99;

    private static readonly int[] StepX = [0, 1, 0, -1, 1, 1, -1, -1];
    private static readonly int[] StepY = [-1, 0, 1, 0, -1, 1, 1, -1];

    private readonly MapGrid _grid;
    private int[] _cost = [];
    private int[] _cameFrom = [];
    private int[] _seenIn = [];
    private int[] _closedIn = [];
    private int _search;
    private Candidate[] _heap = new Candidate[256];
    private int _heapCount;

    public RouteFinder(MapGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _grid = grid;
    }

    /// <summary>
    /// The tiles of the cheapest walk from the start tile to the goal tile, both included, each as
    /// <c>y * width + x</c>. Both tiles must be in the same region, so a walk exists.
    /// </summary>
    /// <param name="expansions">The number of tiles the search expanded: the unit work budgets are counted in.</param>
    public int[] Find(int startX, int startY, int goalX, int goalY, out int expansions) =>
        Find(_grid, startX, startY, goalX, goalY, out expansions);

    /// <summary>
    /// <see cref="Find(int, int, int, int, out int)"/> on the grid of one player, which has the
    /// size of the finder's own.
    /// </summary>
    public int[] Find(MapGrid grid, int startX, int startY, int goalX, int goalY, out int expansions)
    {
        CheckSize(grid);
        var region = grid.RegionOf(startX, startY);
        if (region == 0 || region != grid.RegionOf(goalX, goalY))
        {
            throw new ArgumentException(
                $"No walk leads from tile ({startX}, {startY}) to tile ({goalX}, {goalY}).", nameof(goalX));
        }

        return Search(grid, startX, startY, goalX, goalY, null, 1, out expansions) ?? throw new InvalidOperationException(
            $"The search found no walk from tile ({startX}, {startY}) to tile ({goalX}, {goalY}) of one region.");
    }

    /// <summary>
    /// The second search of design 2.5: the tiles of the cheapest way from the start tile to the
    /// goal tile, both included, for a walker that may also cross the tiles marked in
    /// <paramref name="crossable"/>, each at a high cost. Null when there is no way even so.
    /// </summary>
    /// <remarks>
    /// The step that enters a marked tile costs <paramref name="crossingCost"/> times what it
    /// would cost otherwise. A marked tile is not ground to walk on: a diagonal step still needs
    /// both tiles beside it to be passable as they are now, so the way never slips between two
    /// marked tiles that touch at a corner, and a way that crosses such a line goes through one
    /// of them. Both ends must be passable or marked.
    /// </remarks>
    /// <param name="startX">The x of the start tile.</param>
    /// <param name="startY">The y of the start tile.</param>
    /// <param name="goalX">The x of the goal tile.</param>
    /// <param name="goalY">The y of the goal tile.</param>
    /// <param name="crossable">For each tile of the grid, as <c>y * width + x</c>, whether the walker may cross it though it is blocked.</param>
    /// <param name="crossingCost">How many steps one step into a marked tile costs, at least 1.</param>
    /// <param name="expansions">The number of tiles the search expanded.</param>
    public int[]? FindAcross(int startX, int startY, int goalX, int goalY, bool[] crossable, int crossingCost, out int expansions) =>
        FindAcross(_grid, startX, startY, goalX, goalY, crossable, crossingCost, out expansions);

    /// <summary>
    /// <see cref="FindAcross(int, int, int, int, bool[], int, out int)"/> on the grid of one
    /// player, which has the size of the finder's own.
    /// </summary>
    public int[]? FindAcross(MapGrid grid, int startX, int startY, int goalX, int goalY, bool[] crossable, int crossingCost, out int expansions)
    {
        CheckSize(grid);
        ArgumentNullException.ThrowIfNull(crossable);
        ArgumentOutOfRangeException.ThrowIfLessThan(crossingCost, 1);
        if (crossable.Length != grid.Width * grid.Height)
        {
            throw new ArgumentException(
                $"The marks are for {crossable.Length} tiles and the grid has {grid.Width * grid.Height}.", nameof(crossable));
        }

        if (!CanStandOrCross(startX, startY) || !CanStandOrCross(goalX, goalY))
        {
            throw new ArgumentException(
                $"The walker can be on neither tile ({startX}, {startY}) nor tile ({goalX}, {goalY}), or on only one of them.", nameof(goalX));
        }

        return Search(grid, startX, startY, goalX, goalY, crossable, crossingCost, out expansions);

        bool CanStandOrCross(int x, int y) => grid.Contains(x, y) && (grid.IsPassable(x, y) || crossable[(y * grid.Width) + x]);
    }

    private void CheckSize(MapGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Width != _grid.Width || grid.Height != _grid.Height)
        {
            throw new ArgumentException(
                $"The grid is {grid.Width} by {grid.Height} and the finder's is {_grid.Width} by {_grid.Height}.", nameof(grid));
        }
    }

    private int[]? Search(MapGrid grid, int startX, int startY, int goalX, int goalY, bool[]? crossable, int crossingCost, out int expansions)
    {
        var width = grid.Width;
        if (_cost.Length == 0)
        {
            var tiles = width * grid.Height;
            _cost = new int[tiles];
            _cameFrom = new int[tiles];
            _seenIn = new int[tiles];
            _closedIn = new int[tiles];
        }

        _search = checked(_search + 1);
        _heapCount = 0;
        expansions = 0;

        var start = (startY * width) + startX;
        var goal = (goalY * width) + goalX;
        _cost[start] = 0;
        _cameFrom[start] = start;
        _seenIn[start] = _search;
        Push(new Candidate(Estimate(startX, startY), 0, start));

        var found = false;
        while (_heapCount > 0)
        {
            var tile = Pop().Tile;
            if (_closedIn[tile] == _search)
            {
                continue;
            }

            _closedIn[tile] = _search;
            expansions++;
            if (tile == goal)
            {
                found = true;
                break;
            }

            var x = tile % width;
            var y = tile / width;
            for (var direction = 0; direction < StepX.Length; direction++)
            {
                var nextX = x + StepX[direction];
                var nextY = y + StepY[direction];
                var steps = 1;
                if (!grid.IsPassable(nextX, nextY))
                {
                    if (crossable is null || !grid.Contains(nextX, nextY) || !crossable[(nextY * width) + nextX])
                    {
                        continue;
                    }

                    steps = crossingCost;
                }

                var diagonal = direction >= 4;
                if (diagonal && (!grid.IsPassable(nextX, y) || !grid.IsPassable(x, nextY)))
                {
                    continue;
                }

                var next = (nextY * width) + nextX;
                if (_closedIn[next] == _search)
                {
                    continue;
                }

                var cost = checked(_cost[tile] + (steps * (diagonal ? DiagonalCost : StraightCost)));
                if (_seenIn[next] == _search && _cost[next] <= cost)
                {
                    continue;
                }

                _cost[next] = cost;
                _cameFrom[next] = tile;
                _seenIn[next] = _search;
                Push(new Candidate(checked(cost + Estimate(nextX, nextY)), OffLine(nextX, nextY), next));
            }
        }

        if (!found)
        {
            return null;
        }

        var length = 1;
        for (var tile = goal; tile != start; tile = _cameFrom[tile])
        {
            length++;
        }

        var route = new int[length];
        for (var tile = goal; length > 0; tile = _cameFrom[tile])
        {
            route[--length] = tile;
        }

        return route;

        // The cost of the walk to the goal over open ground: never more than the true cost, and
        // never falling by more than a step costs, so the first time a tile is expanded its cost is final.
        int Estimate(int x, int y)
        {
            var dx = Math.Abs(x - goalX);
            var dy = Math.Abs(y - goalY);
            return (StraightCost * (dx + dy)) - (((2 * StraightCost) - DiagonalCost) * Math.Min(dx, dy));
        }

        // How far the tile lies from the straight line between start and goal, as a cross product.
        int OffLine(int x, int y) =>
            Math.Abs(((x - goalX) * (startY - goalY)) - ((startX - goalX) * (y - goalY)));
    }

    private void Push(Candidate candidate)
    {
        if (_heapCount == _heap.Length)
        {
            Array.Resize(ref _heap, _heapCount * 2);
        }

        var index = _heapCount++;
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (!candidate.IsBefore(_heap[parent]))
            {
                break;
            }

            _heap[index] = _heap[parent];
            index = parent;
        }

        _heap[index] = candidate;
    }

    private Candidate Pop()
    {
        var first = _heap[0];
        var last = _heap[--_heapCount];
        var index = 0;
        while (true)
        {
            var child = (2 * index) + 1;
            if (child >= _heapCount)
            {
                break;
            }

            if (child + 1 < _heapCount && _heap[child + 1].IsBefore(_heap[child]))
            {
                child++;
            }

            if (!_heap[child].IsBefore(last))
            {
                break;
            }

            _heap[index] = _heap[child];
            index = child;
        }

        _heap[index] = last;
        return first;
    }

    /// <summary>A tile waiting to be expanded. The order is total, so the search is the same on every machine.</summary>
    private readonly record struct Candidate(int Total, int OffLine, int Tile)
    {
        public bool IsBefore(Candidate other) =>
            Total != other.Total ? Total < other.Total
            : OffLine != other.OffLine ? OffLine < other.OffLine
            : Tile < other.Tile;
    }
}
