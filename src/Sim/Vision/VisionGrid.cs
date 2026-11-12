using System.Numerics;
using Sim.Core;
using Sim.Numerics;

namespace Sim.Vision;

/// <summary>
/// The fog of war (design 5.3): for each player and each tile, whether the player has ever seen
/// the tile and whether the player sees it now. It is state: what a player sees decides which
/// targets its units pick and which attack orders are carried out.
/// </summary>
/// <remarks>
/// <para>
/// The grid is worked out again only every few ticks (<see cref="Data.VisionRules.RefreshTicks"/>),
/// by <see cref="MatchVision.Refresh"/>, and holds between two refreshes what the last one found.
/// So a snapshot keeps it as it is, and restoring one does not work it out again.
/// </para>
/// <para>
/// Each player has two bits for a tile, kept as two rows of 64-bit words, tile (x, y) at bit
/// <c>y * Width + x</c>. A tile that is visible is always explored. The walk hands over whole
/// words, so the grid of a 96 by 96 map is 288 fields for each player.
/// </para>
/// </remarks>
public sealed class VisionGrid : IStateStore
{
    public const string StoreName = "vision";

    private const int BitsPerWord = 64;

    private readonly int _wordsPerPlane;
    private readonly ulong _lastWordMask;

    // The words of player p begin at p * _wordsPerPlane.
    private readonly ulong[] _explored;
    private readonly ulong[] _visible;

    // What was visible before the grid was last worked out again, to tell whether that changed anything. Not state.
    private readonly ulong[] _visibleBefore;

    public VisionGrid(int players, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(players);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        PlayerCount = players;
        Width = width;
        Height = height;

        var tiles = checked(width * height);
        _wordsPerPlane = ((tiles - 1) / BitsPerWord) + 1;
        var bitsInLastWord = tiles - ((_wordsPerPlane - 1) * BitsPerWord);
        _lastWordMask = bitsInLastWord == BitsPerWord ? ulong.MaxValue : (1UL << bitsInLastWord) - 1;
        _explored = new ulong[checked(players * _wordsPerPlane)];
        _visible = new ulong[_explored.Length];
        _visibleBefore = new ulong[_explored.Length];
    }

    public string Name => StoreName;

    public int PlayerCount { get; }

    /// <summary>The width of the map in tiles.</summary>
    public int Width { get; }

    /// <summary>The height of the map in tiles.</summary>
    public int Height { get; }

    /// <summary>
    /// Goes up whenever the grid has changed: when working sight out again gave another picture,
    /// and when a snapshot is restored. A view that draws the fog draws it again when this is
    /// another number than last time. It is not state, and nothing in the simulation reads it.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>True when the player sees the tile now. False for a tile off the map and for a player who is not in the match.</summary>
    public bool IsVisible(int player, int x, int y) => Has(_visible, player, x, y);

    /// <summary>True when the player sees the tile under a point now.</summary>
    public bool IsVisible(int player, FixedVector2 point) => IsVisible(player, point.X.FloorToInt(), point.Y.FloorToInt());

    /// <summary>True when the player has seen the tile at least once, whether or not it sees it now.</summary>
    public bool IsExplored(int player, int x, int y) => Has(_explored, player, x, y);

    /// <summary>What the player knows of a tile. A tile off the map is <see cref="FogState.Unexplored"/>.</summary>
    public FogState StateOf(int player, int x, int y) =>
        IsVisible(player, x, y) ? FogState.Visible
        : IsExplored(player, x, y) ? FogState.Explored
        : FogState.Unexplored;

    /// <summary>
    /// Writes what the player knows of every tile, one byte for each, the number of its
    /// <see cref="FogState"/>: row by row from y = 0, and in a row from x = 0. For a view that
    /// draws the fog as a texture, or a minimap.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="states">Room for <see cref="Width"/> times <see cref="Height"/> bytes.</param>
    public void CopyStates(int player, Span<byte> states)
    {
        CheckPlayer(player);
        var tiles = Width * Height;
        ArgumentOutOfRangeException.ThrowIfLessThan(states.Length, tiles, nameof(states));

        var first = player * _wordsPerPlane;
        for (var tile = 0; tile < tiles; tile++)
        {
            var word = first + (tile / BitsPerWord);
            var bit = 1UL << (tile % BitsPerWord);
            states[tile] = (_visible[word] & bit) != 0 ? (byte)FogState.Visible
                : (_explored[word] & bit) != 0 ? (byte)FogState.Explored
                : (byte)FogState.Unexplored;
        }
    }

    /// <summary>How many tiles the player sees now.</summary>
    public int CountVisible(int player) => Count(_visible, player);

    /// <summary>How many tiles the player has seen at least once.</summary>
    public int CountExplored(int player) => Count(_explored, player);

    public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
    {
    }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        for (var word = 0; word < _explored.Length; word++)
        {
            _explored[word] = walker.Field(_explored[word]);
        }

        for (var word = 0; word < _visible.Length; word++)
        {
            _visible[word] = walker.Field(_visible[word]);
        }

        if (!walker.IsRestoring)
        {
            return;
        }

        Revision++;
        for (var word = 0; word < _explored.Length; word++)
        {
            var isLast = (word + 1) % _wordsPerPlane == 0;
            if (isLast && ((_explored[word] | _visible[word]) & ~_lastWordMask) != 0)
            {
                throw new InvalidDataException("The fog of war has a tile that is not on the map.");
            }

            if ((_visible[word] & ~_explored[word]) != 0)
            {
                throw new InvalidDataException("The fog of war has a tile that is visible and not explored.");
            }
        }
    }

    /// <summary>
    /// Begins working sight out again: nobody sees anything now, and what has been explored stays.
    /// <see cref="Reveal"/> then adds what each unit sees, and <see cref="EndRefresh"/> ends it.
    /// In a match only <see cref="MatchVision.Refresh"/> calls the three.
    /// </summary>
    public void BeginRefresh()
    {
        _visible.CopyTo(_visibleBefore, 0);
        Array.Clear(_visible);
    }

    /// <summary>Ends what <see cref="BeginRefresh"/> began.</summary>
    public void EndRefresh()
    {
        // Nothing is explored without being visible first, so a picture with the same visible
        // tiles as before is the same picture.
        if (!_visible.AsSpan().SequenceEqual(_visibleBefore))
        {
            Revision++;
        }
    }

    /// <summary>
    /// Makes visible, and so explored, every tile of the map whose center is within
    /// <paramref name="sight"/> of <paramref name="center"/>, the exact distance included
    /// (design 5.3). Nothing blocks sight.
    /// </summary>
    public void Reveal(int player, FixedVector2 center, Fixed sight)
    {
        CheckPlayer(player);
        if (sight.Raw < 0)
        {
            return;
        }

        // A tile's center is half a tile past its corner. With every length in raw units, tile
        // (x, y) is seen when (x * one + half - center.X)^2 + (y * one + half - center.Y)^2 is
        // not above sight^2. For one row that is |x * one + half - center.X| <= the whole root
        // of what the row leaves of sight^2, which is exact for whole numbers.
        const long One = Fixed.RawOne;
        const long Half = Fixed.RawOne / 2;
        var radius = (long)sight.Raw;
        var radiusSquared = radius * radius;
        var firstRow = Math.Max(0, CeilingToTile(center.Y.Raw - Half - radius));
        var lastRow = Math.Min(Height - 1, FloorToTile(center.Y.Raw - Half + radius));
        var plane = player * _wordsPerPlane;
        for (var y = firstRow; y <= lastRow; y++)
        {
            var dy = (y * One) + Half - center.Y.Raw;
            var left = radiusSquared - (dy * dy);
            if (left < 0)
            {
                continue;
            }

            var reach = (long)IntegerMath.Sqrt((ulong)left);
            var firstTile = Math.Max(0, CeilingToTile(center.X.Raw - Half - reach));
            var lastTile = Math.Min(Width - 1, FloorToTile(center.X.Raw - Half + reach));
            if (firstTile <= lastTile)
            {
                SetBits(plane, (y * Width) + firstTile, (y * Width) + lastTile);
            }
        }
    }

    private static int FloorToTile(long raw) => (int)(raw >> Fixed.FractionalBits);

    private static int CeilingToTile(long raw) => (int)((raw + Fixed.RawOne - 1) >> Fixed.FractionalBits);

    /// <summary>Sets the bits from <paramref name="first"/> to <paramref name="last"/>, both included, in both rows of a player.</summary>
    private void SetBits(int plane, int first, int last)
    {
        var firstWord = first / BitsPerWord;
        var lastWord = last / BitsPerWord;
        for (var word = firstWord; word <= lastWord; word++)
        {
            var mask = ulong.MaxValue;
            if (word == firstWord)
            {
                mask &= ulong.MaxValue << (first % BitsPerWord);
            }

            if (word == lastWord)
            {
                mask &= ulong.MaxValue >> (BitsPerWord - 1 - (last % BitsPerWord));
            }

            _visible[plane + word] |= mask;
            _explored[plane + word] |= mask;
        }
    }

    private bool Has(ulong[] plane, int player, int x, int y)
    {
        if (player < 0 || player >= PlayerCount || x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return false;
        }

        var tile = (y * Width) + x;
        return (plane[(player * _wordsPerPlane) + (tile / BitsPerWord)] & (1UL << (tile % BitsPerWord))) != 0;
    }

    private int Count(ulong[] plane, int player)
    {
        CheckPlayer(player);
        var count = 0;
        var first = player * _wordsPerPlane;
        for (var word = first; word < first + _wordsPerPlane; word++)
        {
            count += BitOperations.PopCount(plane[word]);
        }

        return count;
    }

    private void CheckPlayer(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
    }
}
