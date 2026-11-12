using Godot;

namespace Game;

/// <summary>
/// The shapes that stand in for a gate until there is art: two pillars, taller than the wall,
/// one at each end of the tile along the wall, with a paved way between them and a door in it.
/// While the gate is shut the door is a block of its owner's color that fills the way; while
/// it is open, two thin leaves stand back against the pillars and the paving shows. The camera
/// looks from the south, so a gate in a wall that runs up and down the map is seen from above
/// only: the block, or the bare paving, between the tops of the two pillars is what tells shut
/// from open there.
/// </summary>
/// <remarks>
/// <para>
/// A gate is a wall tile, and its shape is a wall tile's number (<see cref="WallShapes"/>)
/// with three things more: that it is a gate, which way the wall runs through it, and whether
/// its door is open. Its joins and how worn it is are read from the number as a wall tile's
/// are.
/// </para>
/// <para>
/// The pillars stand where the arms of the wall tiles beside the gate end, so the line goes on
/// through the gate. A wall that meets the gate from another side, across the way or from a
/// corner, ends at the edge of the gate's tile, where its own arm ends: the gate draws no arm
/// of its own, so nothing stands in the way.
/// </para>
/// <para>
/// The mesh has the two surfaces of <see cref="StructureShapes"/>: the pillars and the paving
/// are stone, and the door and a small crest on each pillar take the owner's color. The top
/// of a wall is its owner's color from end to end, so the bare stone of the two pillars is
/// what tells a gate from the wall it stands in.
/// </para>
/// </remarks>
public static class GateShapes
{
    /// <summary>How tall a whole gate is at its pillars, in tiles: what <see cref="StructureShapes.HeightOf"/> says of the gate.</summary>
    public const float Height = 1.10f;

    private const int IsGateBit = 1 << 17;
    private const int OpenBit = 1 << 18;
    private const int AlongYBit = 1 << 19;

    // The joins to the tiles above and below on the map, and to those on either side.
    private const int UpAndDown = (1 << 0) | (1 << 4);
    private const int LeftAndRight = (1 << 2) | (1 << 6);

    // Half of the way between the pillars, half of how far a pillar reaches across the wall, and
    // half of how wide the paving is.
    private const float WayHalf = 0.28f;
    private const float PillarHalfWidth = 0.36f;
    private const float PavingHalf = 0.5f;
    private const float PavingTop = 0.035f;

    // How thick the shut door is, by half, and how thick a leaf of the open one.
    private const float ShutHalf = 0.17f;
    private const float LeafHalf = 0.05f;

    // Half of how long the crest on a pillar is along the wall, and of how wide it is across.
    private const float CrestHalfLength = 0.06f;
    private const float CrestHalfWidth = 0.16f;

    // What a gate looks like at each stage of wear: how tall its pillars and their crests are,
    // how many crests are left, and how tall the door is.
    private static readonly Look[] Looks =
    [
        new(PillarTop: 1.02f, CapTop: Height, Caps: 2, DoorTop: 0.62f),
        new(PillarTop: 1.02f, CapTop: Height, Caps: 1, DoorTop: 0.60f),
        new(PillarTop: 0.74f, CapTop: 0.82f, Caps: 1, DoorTop: 0.46f),
        new(PillarTop: 0.40f, CapTop: 0.40f, Caps: 0, DoorTop: 0.22f),
    ];

    /// <summary>The shape of a gate.</summary>
    /// <param name="joins">Its joins, as a wall tile's.</param>
    /// <param name="stage">How worn it is, as a wall tile's.</param>
    /// <param name="alongY">True when the wall runs through it up and down the map, false when from side to side.</param>
    /// <param name="open">True when its door is drawn open.</param>
    public static int ShapeOf(int joins, int stage, bool alongY, bool open) =>
        WallShapes.ShapeOf(joins, stage) | IsGateBit | (alongY ? AlongYBit : 0) | (open ? OpenBit : 0);

    /// <summary>True for the shape of a gate.</summary>
    public static bool IsGate(int shape) => (shape & IsGateBit) != 0;

    /// <summary>True for the shape of a gate with its door open.</summary>
    public static bool IsOpen(int shape) => (shape & OpenBit) != 0;

    /// <summary>True when the wall runs through the gate of this shape up and down the map.</summary>
    public static bool RunsAlongY(int shape) => (shape & AlongYBit) != 0;

    /// <summary>
    /// Which way the wall runs through a gate with these joins: true for up and down the map,
    /// false for from side to side, and null when the joins do not say, as for a gate that
    /// stands alone or in a run along a diagonal.
    /// </summary>
    public static bool? RunOf(int joins) =>
        (joins & LeftAndRight) != 0 ? false
        : (joins & UpAndDown) != 0 ? true
        : null;

    /// <summary>The mesh of a gate's shape, standing on the ground with the middle of its tile at the origin.</summary>
    public static ArrayMesh MeshOf(int shape)
    {
        var look = Looks[WallShapes.StageOf(shape)];
        var along = RunsAlongY(shape) ? Vector2.Down : Vector2.Right;
        var across = new Vector2(-along.Y, along.X);
        var pillarMiddle = (WayHalf + 0.5f) / 2f;
        var pillarHalf = (0.5f - WayHalf) / 2f;
        var mesh = new ArrayMesh();

        var stone = new SurfaceTool();
        stone.Begin(Mesh.PrimitiveType.Triangles);
        WallShapes.AddBox(stone, along * pillarMiddle, along, pillarHalf, PillarHalfWidth, 0f, look.PillarTop);
        WallShapes.AddBox(stone, along * -pillarMiddle, along, pillarHalf, PillarHalfWidth, 0f, look.PillarTop);
        WallShapes.AddBox(stone, Vector2.Zero, across, PavingHalf, WayHalf, 0f, PavingTop);
        stone.Commit(mesh);

        var color = new SurfaceTool();
        color.Begin(Mesh.PrimitiveType.Triangles);
        if (look.Caps > 0)
        {
            WallShapes.AddBox(color, along * pillarMiddle, along, CrestHalfLength, CrestHalfWidth, look.PillarTop, look.CapTop);
        }

        if (look.Caps > 1)
        {
            WallShapes.AddBox(color, along * -pillarMiddle, along, CrestHalfLength, CrestHalfWidth, look.PillarTop, look.CapTop);
        }

        if (IsOpen(shape))
        {
            // Back against the pillars, one leaf on each side of the way.
            WallShapes.AddBox(color, along * (WayHalf - LeafHalf), across, PillarHalfWidth, LeafHalf, PavingTop, look.DoorTop);
            WallShapes.AddBox(color, along * -(WayHalf - LeafHalf), across, PillarHalfWidth, LeafHalf, PavingTop, look.DoorTop);
        }
        else
        {
            WallShapes.AddBox(color, Vector2.Zero, along, WayHalf, ShutHalf, PavingTop, look.DoorTop);
        }

        color.Commit(mesh);
        return mesh;
    }

    // The sizes of one stage, in tiles.
    private readonly record struct Look(float PillarTop, float CapTop, int Caps, float DoorTop);
}
