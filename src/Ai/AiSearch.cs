namespace Ai;

/// <summary>
/// The map in squares of <see cref="CellSize"/> tiles, with what the player knows of each: whether
/// it has explored it and when it last saw it. A worker that looks for a deposit and an army that
/// looks for the enemy both ask it where to go next. A square is judged by its middle tile.
/// </summary>
internal sealed class AiSearch
{
    /// <summary>The side of a square, in tiles. Less than the sight of anything that walks, so whatever reaches a square sees its middle.</summary>
    public const int CellSize = 8;

    /// <summary>The ticks it allows for each tile of a walk, at the speed of the slowest unit, and what it adds for the way round.</summary>
    public const int TicksPerTile = 12;

    /// <summary>The ticks it adds to the time of every walk before it gives the walk up.</summary>
    public const int TicksToSpare = 10 * Sim.SimInfo.TicksPerSecond;

    private readonly AiSight _sight;
    private readonly int _columns;
    private readonly int _rows;
    private readonly int[] _lastSeen;
    private readonly bool[] _givenUp;

    public AiSearch(AiSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        _sight = sight;
        _columns = (sight.MapWidth + CellSize - 1) / CellSize;
        _rows = (sight.MapHeight + CellSize - 1) / CellSize;
        _lastSeen = new int[_columns * _rows];
        _givenUp = new bool[_columns * _rows];
    }

    /// <summary>The tick by which a walk of so many tiles has surely ended.</summary>
    public static int DeadlineFor(int tick, long distanceSquared) =>
        tick + TicksToSpare + (TicksPerTile * (int)Sim.Numerics.IntegerMath.Sqrt((ulong)distanceSquared));

    /// <summary>Notes the squares the player sees now. Call it once a round, after the sight was read.</summary>
    public void Update()
    {
        for (var cell = 0; cell < _lastSeen.Length; cell++)
        {
            var (x, y) = MiddleOf(cell);
            if (_sight.IsVisible(x, y))
            {
                _lastSeen[cell] = _sight.Tick + 1;
            }
        }
    }

    /// <summary>The middle of the nearest square the player has not explored and has not given up.</summary>
    public bool TryFindUnexplored(int fromX, int fromY, out int x, out int y)
    {
        var best = -1;
        var bestDistance = long.MaxValue;
        for (var cell = 0; cell < _lastSeen.Length; cell++)
        {
            var (middleX, middleY) = MiddleOf(cell);
            if (_givenUp[cell] || _sight.IsExplored(middleX, middleY))
            {
                continue;
            }

            var distance = DistanceSquared(middleX, middleY, fromX, fromY);
            if (distance < bestDistance)
            {
                best = cell;
                bestDistance = distance;
            }
        }

        (x, y) = best < 0 ? (0, 0) : MiddleOf(best);
        return best >= 0;
    }

    /// <summary>
    /// The middle of the square the player has gone longest without seeing, the nearest of
    /// those. A square it never saw is the longest of all. False when it sees every square now.
    /// </summary>
    public bool TryFindLongestUnseen(int fromX, int fromY, out int x, out int y)
    {
        var best = -1;
        var bestSeen = int.MaxValue;
        var bestDistance = long.MaxValue;
        for (var cell = 0; cell < _lastSeen.Length; cell++)
        {
            var (middleX, middleY) = MiddleOf(cell);
            if (_lastSeen[cell] == _sight.Tick + 1)
            {
                continue;
            }

            var distance = DistanceSquared(middleX, middleY, fromX, fromY);
            if (_lastSeen[cell] < bestSeen || (_lastSeen[cell] == bestSeen && distance < bestDistance))
            {
                best = cell;
                bestSeen = _lastSeen[cell];
                bestDistance = distance;
            }
        }

        (x, y) = best < 0 ? (0, 0) : MiddleOf(best);
        return best >= 0;
    }

    /// <summary>
    /// Gives up the square a tile is in: nothing that was sent there came to see it. It is not
    /// looked for again as unexplored, and counts as seen now.
    /// </summary>
    public void GiveUp(int x, int y)
    {
        var cell = (Math.Clamp(y / CellSize, 0, _rows - 1) * _columns) + Math.Clamp(x / CellSize, 0, _columns - 1);
        _givenUp[cell] = true;
        _lastSeen[cell] = _sight.Tick + 1;
    }

    public static long DistanceSquared(int x, int y, int otherX, int otherY)
    {
        long dx = x - otherX;
        long dy = y - otherY;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>The square of the distance in tiles from a tile to the nearest tile of a square footprint. Zero for a tile of it.</summary>
    public static long DistanceToSquare(int x, int y, int size, int tileX, int tileY)
    {
        long dx = Math.Max(0, Math.Max(x - tileX, tileX - (x + size - 1)));
        long dy = Math.Max(0, Math.Max(y - tileY, tileY - (y + size - 1)));
        return (dx * dx) + (dy * dy);
    }

    private (int X, int Y) MiddleOf(int cell)
    {
        var x = Math.Min(_sight.MapWidth - 1, ((cell % _columns) * CellSize) + (CellSize / 2));
        var y = Math.Min(_sight.MapHeight - 1, ((cell / _columns) * CellSize) + (CellSize / 2));
        return (x, y);
    }
}
