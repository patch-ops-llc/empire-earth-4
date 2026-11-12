using Sim.Economy;

namespace Game;

/// <summary>
/// Which players hold each tile of the map, as far as the local player knows (design 5.3): a
/// tile is a player's when <see cref="Territory.Contains"/> says so for one of the territories
/// the view's <see cref="PlayerSight"/> lists. Those are the player's own, and those of the
/// finished Capitols and Outposts of other players that the player sees or remembers. The view
/// asks nothing else about territory, so what it shows of the player's own ends exactly where
/// income and the right to build end, and what it shows of another player's is what the
/// placement of a structure is judged by when the order is given.
/// </summary>
/// <remarks>
/// This is the view's own copy and no state of the match. <see cref="Refresh"/> asks again and
/// works the tiles out only when a territory has come, gone or changed. Whatever shows
/// territory reads it here: the layer on the ground, and after it the minimap and the
/// placement preview of the build menu.
/// </remarks>
public sealed class TerritoryTiles
{
    /// <summary>The most players the tiles are kept for: one bit of a tile's byte each.</summary>
    public const int MaxPlayers = 8;

    private readonly List<Territory> _seen = [];
    private readonly List<Territory> _now = [];
    private byte[] _holders = [];
    private bool _read;

    /// <summary>The width of the map in tiles.</summary>
    public int Width { get; private set; }

    /// <summary>The height of the map in tiles.</summary>
    public int Height { get; private set; }

    /// <summary>The players who hold some tile, as a bit each.</summary>
    public int Players { get; private set; }

    /// <summary>
    /// Asks which territories the local player knows of. Returns true when the tiles are not
    /// what they were after the last call, and on the first call.
    /// </summary>
    public bool Refresh(PlayerSight sight)
    {
        sight.CollectTerritories(_now);

        if (_read && sight.Width == Width && sight.Height == Height && Same(_now, _seen))
        {
            return false;
        }

        _read = true;
        Width = sight.Width;
        Height = sight.Height;
        _seen.Clear();
        _seen.AddRange(_now);

        if (_holders.Length != Width * Height)
        {
            _holders = new byte[Width * Height];
        }
        else
        {
            Array.Clear(_holders);
        }

        Players = 0;
        foreach (var territory in _seen)
        {
            Mark(territory);
        }

        return true;
    }

    /// <summary>True when the player holds the tile. False for a tile off the map.</summary>
    public bool Holds(int player, int x, int y) =>
        player >= 0 && player < MaxPlayers && (HoldersAt(x, y) & (1 << player)) != 0;

    /// <summary>The players who hold the tile, as a bit each. None for a tile off the map.</summary>
    public int HoldersAt(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height ? _holders[(y * Width) + x] : 0;

    // Only the tiles of the square round the circle are asked, and the simulation's own test
    // decides each.
    private void Mark(in Territory territory)
    {
        if (territory.Owner < 0 || territory.Owner >= MaxPlayers)
        {
            return;
        }

        var bit = (byte)(1 << territory.Owner);
        var fromX = Math.Max(0, (territory.CenterX2 / 2) - territory.Radius - 1);
        var fromY = Math.Max(0, (territory.CenterY2 / 2) - territory.Radius - 1);
        var toX = Math.Min(Width - 1, (territory.CenterX2 / 2) + territory.Radius + 1);
        var toY = Math.Min(Height - 1, (territory.CenterY2 / 2) + territory.Radius + 1);
        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                if (territory.Contains(x, y))
                {
                    _holders[(y * Width) + x] |= bit;
                    Players |= bit;
                }
            }
        }
    }

    private static bool Same(List<Territory> a, List<Territory> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Owner != b[i].Owner
                || a[i].CenterX2 != b[i].CenterX2
                || a[i].CenterY2 != b[i].CenterY2
                || a[i].Radius != b[i].Radius)
            {
                return false;
            }
        }

        return true;
    }
}
