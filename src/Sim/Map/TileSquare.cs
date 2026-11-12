namespace Sim.Map;

/// <summary>A square of tiles: <see cref="Size"/> tiles a side, with its lowest tile at (X, Y).</summary>
public readonly record struct TileSquare(int X, int Y, int Size)
{
    /// <summary>True when the tile is one of the square's.</summary>
    public bool Covers(int x, int y) => x >= X && y >= Y && x < X + Size && y < Y + Size;
}
