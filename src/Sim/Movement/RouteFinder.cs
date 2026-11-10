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
    public int[] Find(int startX, int startY, int goalX, int goalY, out int expansions)
    {
        var region = _grid.RegionOf(startX, startY);
        if (region == 0 || region != _grid.RegionOf(goalX, goalY))
        {
            throw new ArgumentException(
                $"No walk leads from tile ({startX}, {startY}) to tile ({goalX}, {goalY}).", nameof(goalX));
        }

        var width = _grid.Width;
        if (_cost.Length == 0)
        {
            var tiles = width * _grid.Height;
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
                break;
            }

            var x = tile % width;
            var y = tile / width;
            for (var direction = 0; direction < StepX.Length; direction++)
            {
                var nextX = x + StepX[direction];
                var nextY = y + StepY[direction];
                if (!_grid.IsPassable(nextX, nextY))
                {
                    continue;
                }

                var diagonal = direction >= 4;
                if (diagonal && (!_grid.IsPassable(nextX, y) || !_grid.IsPassable(x, nextY)))
                {
                    continue;
                }

                var next = (nextY * width) + nextX;
                if (_closedIn[next] == _search)
                {
                    continue;
                }

                var cost = checked(_cost[tile] + (diagonal ? DiagonalCost : StraightCost));
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
