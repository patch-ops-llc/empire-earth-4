using Godot;

namespace Game;

/// <summary>
/// Wall that is not there yet, drawn see-through: the tiles of a run under the pointer, and the
/// tiles of an order that are paid for and not yet laid. It draws marks on tiles, posts, the
/// arms between them and crosses, each in a color of its own, as two nodes under a layer.
/// </summary>
/// <remarks>
/// A layer starts a frame with <see cref="Begin"/>, adds what there is and ends it with
/// <see cref="End"/>. What was drawn before is gone then, so nothing has to be taken away.
/// </remarks>
public sealed class WallSketch
{
    // Above the territory's tint and line, and under the line round a selected structure.
    private const float TileLift = 0.045f;
    private const float TileSize = 0.9f;
    private const float PostSide = 0.68f;
    private const float PostHeight = 0.74f;
    private const float ArmWidth = 0.48f;
    private const float ArmHeight = 0.5f;
    private const float CrossLength = 0.84f;
    private const float CrossWidth = 0.13f;
    private const float CrossLift = 0.1f;
    private const float Diagonal = 0.70710678f;

    private readonly InstancedShapes _tiles;
    private readonly InstancedShapes _blocks;

    /// <summary>Makes the nodes of a sketch under a layer.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="map">The map of the match, for the area the sketch can be in.</param>
    public WallSketch(Node3D layer, Sim.Data.MapDef map)
    {
        var area = InstancedShapes.MapArea(map);
        _tiles = InstancedShapes.Under(layer, "Tiles", new PlaneMesh { Size = new Vector2(TileSize, TileSize) }, InstancedShapes.Material(seeThrough: true), area);
        _blocks = InstancedShapes.Under(layer, "Blocks", new BoxMesh { Size = Vector3.One }, InstancedShapes.Material(seeThrough: true), area);
    }

    /// <summary>Starts a frame with nothing in it.</summary>
    public void Begin()
    {
        _tiles.Begin();
        _blocks.Begin();
    }

    /// <summary>Shows what was added since <see cref="Begin"/>, and nothing else.</summary>
    public void End()
    {
        _tiles.End();
        _blocks.End();
    }

    /// <summary>A flat mark on a tile.</summary>
    public void Tile(int x, int y, Color color) =>
        _tiles.Add(new Transform3D(Basis.Identity, new Vector3(x + 0.5f, TileLift, y + 0.5f)), color);

    /// <summary>The post of a wall tile.</summary>
    public void Post(int x, int y, Color color) => Block(new Vector2(x + 0.5f, y + 0.5f), Vector2.Right, PostSide, PostSide, 0f, PostHeight, color);

    /// <summary>
    /// The arm of a wall tile toward one of the eight tiles round it, as far as the edge or the
    /// corner it shares with that tile, where the other tile's arm begins.
    /// </summary>
    /// <param name="x">The x of the tile.</param>
    /// <param name="y">The y of the tile.</param>
    /// <param name="direction">Which of <see cref="WallShapes.Directions"/> the arm goes.</param>
    /// <param name="color">Its color.</param>
    public void Arm(int x, int y, int direction, Color color)
    {
        var (dx, dy) = WallShapes.Directions[direction];
        var diagonal = dx != 0 && dy != 0;
        var way = diagonal ? new Vector2(dx, dy) * Diagonal : new Vector2(dx, dy);
        var reach = diagonal ? Diagonal : 0.5f;
        Block(new Vector2(x + 0.5f, y + 0.5f) + (way * (reach / 2f)), way, reach, ArmWidth, 0f, ArmHeight, color);
    }

    /// <summary>A cross lying on a tile: no wall here.</summary>
    public void Cross(int x, int y, Color color)
    {
        var middle = new Vector2(x + 0.5f, y + 0.5f);
        Block(middle, new Vector2(Diagonal, Diagonal), CrossLength, CrossWidth, CrossLift, CrossLift + 0.04f, color);
        Block(middle, new Vector2(Diagonal, -Diagonal), CrossLength, CrossWidth, CrossLift, CrossLift + 0.04f, color);
    }

    // A block lying along a way on the ground, with its middle over a point of the map.
    private void Block(Vector2 middle, Vector2 way, float length, float width, float from, float to, Color color)
    {
        var basis = new Basis(
            new Vector3(way.X, 0f, way.Y) * length,
            Vector3.Up * (to - from),
            new Vector3(-way.Y, 0f, way.X) * width);
        _blocks.Add(new Transform3D(basis, new Vector3(middle.X, (from + to) / 2f, middle.Y)), color);
    }
}
