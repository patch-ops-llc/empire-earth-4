using Sim.Data;
using Sim.Numerics;

namespace Sim.Map;

/// <summary>
/// The tile grid of a match, built from a <see cref="MapDef"/>: which tiles units can stand on.
/// Open ground is passable; forest and cliff are not (design 5.3), and neither is anything outside
/// the map.
/// </summary>
/// <remarks>
/// Tile (x, y) covers the points from (x, y) up to but not including (x + 1, y + 1), with (0, 0) at
/// the top-left corner of the map and y growing downward. The terrain never changes during a match,
/// so the grid is not part of the state hash.
/// </remarks>
public sealed class MapGrid
{
    /// <summary>The most tiles a map can have on one side. Squared distances across it stay far inside 64 bits.</summary>
    public const int MaxSize = 4096;

    private readonly bool[] _passable;

    public MapGrid(MapDef map)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfLessThan(map.Width, 1, nameof(map));
        ArgumentOutOfRangeException.ThrowIfLessThan(map.Height, 1, nameof(map));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(map.Width, MaxSize, nameof(map));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(map.Height, MaxSize, nameof(map));
        if (map.Tiles.Count != map.Width * map.Height)
        {
            throw new ArgumentException(
                $"Map '{map.Id}' is {map.Width} by {map.Height} but has {map.Tiles.Count} tiles.", nameof(map));
        }

        Width = map.Width;
        Height = map.Height;
        _passable = new bool[Width * Height];
        for (var i = 0; i < _passable.Length; i++)
        {
            _passable[i] = map.Tiles[i] == TerrainKind.Open;
        }
    }

    /// <summary>The number of tiles along x.</summary>
    public int Width { get; }

    /// <summary>The number of tiles along y.</summary>
    public int Height { get; }

    /// <summary>The center of a tile, where a unit standing on it is placed.</summary>
    public static FixedVector2 TileCenter(int x, int y) =>
        new(Fixed.FromInt(x) + Fixed.Half, Fixed.FromInt(y) + Fixed.Half);

    /// <summary>True when the tile is on the map.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>True when the point is on the map: inside one of its tiles.</summary>
    public bool Contains(FixedVector2 point) => Contains(point.X.FloorToInt(), point.Y.FloorToInt());

    /// <summary>True when units can stand on the tile. False for every tile outside the map.</summary>
    public bool IsPassable(int x, int y) => Contains(x, y) && _passable[(y * Width) + x];

    /// <summary>True when units can stand on the tile that holds the point.</summary>
    public bool IsPassable(FixedVector2 point) => IsPassable(point.X.FloorToInt(), point.Y.FloorToInt());
}
