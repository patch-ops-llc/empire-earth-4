namespace Sim.Data;

public enum TerrainKind
{
    Open,
    Forest,
    Cliff,
}

/// <summary>How a map mirrors: left to right (<see cref="MirrorX"/>) or top to bottom (<see cref="MirrorY"/>).</summary>
public enum MapSymmetry
{
    MirrorX,
    MirrorY,
}

public enum DepositKind
{
    Main,
    Natural,
    Contested,
}

public enum MarkerKind
{
    Chokepoint,
    OpenField,
}

public enum MapAxis
{
    X,
    Y,
}

/// <summary>A rectangle of tiles. <see cref="X"/> and <see cref="Y"/> are its lowest tile on each axis.</summary>
public readonly record struct TileRect(int X, int Y, int SizeX, int SizeY)
{
    public bool Overlaps(TileRect other) =>
        X < other.X + other.SizeX && other.X < X + SizeX && Y < other.Y + other.SizeY && other.Y < Y + SizeY;
}

/// <summary>A deposit. <see cref="X"/> and <see cref="Y"/> are the lowest tile of its square footprint.</summary>
public sealed record MapDeposit(DepositKind Kind, int X, int Y);

/// <summary>
/// An authored chokepoint or open field (design 5.3). <see cref="Area"/> covers the passage and
/// <see cref="Across"/> is the axis its width is measured along.
/// </summary>
public sealed record MapMarker(MarkerKind Kind, TileRect Area, MapAxis Across)
{
    public int Width => Across == MapAxis.X ? Area.SizeX : Area.SizeY;
}

/// <summary>Where a player starts: the lowest tile of the starting Capitol's footprint.</summary>
public sealed record MapStart(int X, int Y);

/// <summary>A map (design 5.3). <see cref="Tiles"/> holds the terrain row by row, row 0 first.</summary>
public sealed record MapDef(
    string Id,
    int Width,
    int Height,
    MapSymmetry Symmetry,
    IReadOnlyList<TerrainKind> Tiles,
    IReadOnlyList<MapStart> Starts,
    IReadOnlyList<MapDeposit> Deposits,
    IReadOnlyList<MapMarker> Markers)
{
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public TerrainKind TerrainAt(int x, int y) => Tiles[(y * Width) + x];
}
