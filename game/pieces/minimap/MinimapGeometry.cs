using Godot;
using Sim.Numerics;

namespace Game;

/// <summary>
/// How the map lies on the minimap: the whole map, to one scale, with map x to the right and map
/// y downward, as the camera shows it. The minimap, its orders and its part of the smoke test
/// all turn points with this, so that they agree.
/// </summary>
/// <param name="MapWidth">The width of the map, in tiles.</param>
/// <param name="MapHeight">The height of the map, in tiles.</param>
/// <param name="PixelsPerTile">The side of a tile on the minimap, in pixels of the interface.</param>
public readonly record struct MinimapGeometry(int MapWidth, int MapHeight, float PixelsPerTile)
{
    /// <summary>The size of the minimap, in pixels of the interface.</summary>
    public Vector2 Size => new(MapWidth * PixelsPerTile, MapHeight * PixelsPerTile);

    /// <summary>The minimap of a map whose longer side is this many pixels.</summary>
    public static MinimapGeometry Fit(int mapWidth, int mapHeight, float longestSide)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mapWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(mapHeight, 1);
        return new MinimapGeometry(mapWidth, mapHeight, longestSide / Math.Max(mapWidth, mapHeight));
    }

    /// <summary>A point of the ground, as map x and y in tiles, as a point of the minimap.</summary>
    public Vector2 ToMinimap(Vector2 ground) => ground * PixelsPerTile;

    /// <summary>A point of the minimap as a point of the ground. A point beyond its edge gives the nearest point of the map.</summary>
    public Vector2 ToGround(Vector2 point) => point.Clamp(Vector2.Zero, Size) / PixelsPerTile;

    /// <summary>
    /// A point of the minimap as the simulation has it, for an order. The map's far edges are not
    /// on the map: its last point is one distance unit inside, and the simulation drops an order
    /// whose target is off the map (design 5.3).
    /// </summary>
    public FixedVector2 ToMapPoint(Vector2 point)
    {
        var ground = ToGround(point);
        return new FixedVector2(ToMapCoordinate(ground.X, MapWidth), ToMapCoordinate(ground.Y, MapHeight));
    }

    // In double, because a float cannot hold every distance unit of a large map.
    private static Fixed ToMapCoordinate(float tiles, int mapTiles)
    {
        var last = ((double)mapTiles * Fixed.RawOne) - 1.0;
        return Fixed.FromRaw((int)Math.Clamp(Math.Round((double)tiles * Fixed.RawOne), 0.0, last));
    }
}
