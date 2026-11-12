using Godot;

namespace Game;

/// <summary>
/// The shapes that stand in for a wall tile until there is art: a post on the middle of the
/// tile, and from it an arm to every neighbouring tile of the same wall, so that tiles in a
/// line, round a corner or along a diagonal read as one wall and a tile alone reads as a post.
/// </summary>
/// <remarks>
/// <para>
/// A shape is a number: the joins of the tile, a bit for each of the eight tiles round it that
/// its wall goes on to (<see cref="WallJoins"/>), and how worn it is, from
/// <see cref="StageOf(long, long)"/>. A wall that has lost hit points stands lower and loses the
/// cap in its owner's color piece by piece, so that the tile an attack is breaking is seen in
/// the line, also from far above.
/// </para>
/// <para>
/// The mesh has the two surfaces of <see cref="StructureShapes"/>, stone and what takes the
/// owner's color, and no material, so whatever draws structures draws these the same way. The
/// cap runs along the whole wall, which is what tells whose it is at the widest zoom.
/// </para>
/// </remarks>
public static class WallShapes
{
    /// <summary>How tall a whole wall tile is at its post, in tiles: what <see cref="StructureShapes.HeightOf"/> says of the wall.</summary>
    public const float Height = 0.78f;

    /// <summary>The stage of a wall with all its hit points.</summary>
    public const int Whole = 0;

    /// <summary>The stage of a wall that is nearly through.</summary>
    public const int Rubble = 3;

    // Set in every shape of a wall, so that none is the 0 that stands for a structure with one shape.
    private const int IsWall = 1 << 16;
    private const int JoinBits = 0xFF;
    private const int StageShift = 8;
    private const int StageBits = 3;

    private const float Diagonal = 0.70710678f;

    /// <summary>
    /// The eight tiles round a tile, in the order of the bits of a join: the one above on the
    /// map first, then round as the map is drawn, so the even ones share an edge with the tile
    /// and the odd ones a corner.
    /// </summary>
    public static readonly (int X, int Y)[] Directions =
    [
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1),
    ];

    // What a wall looks like at each stage: the post and its cap, an arm and its cap, and which
    // pieces of the caps are left.
    private static readonly Look[] Looks =
    [
        new(PostHalf: 0.34f, PostTop: 0.66f, CapHalf: 0.38f, CapTop: Height, ArmHalf: 0.24f, ArmTop: 0.48f, ArmCapTop: 0.57f),
        new(PostHalf: 0.34f, PostTop: 0.66f, CapHalf: 0.38f, CapTop: Height, ArmHalf: 0.24f, ArmTop: 0.46f, ArmCapTop: 0.55f),
        new(PostHalf: 0.35f, PostTop: 0.50f, CapHalf: 0.37f, CapTop: 0.60f, ArmHalf: 0.25f, ArmTop: 0.34f, ArmCapTop: 0.41f),
        new(PostHalf: 0.37f, PostTop: 0.30f, CapHalf: 0.37f, CapTop: 0.37f, ArmHalf: 0.27f, ArmTop: 0.18f, ArmCapTop: 0.18f),
    ];

    /// <summary>The shape of a wall tile with these joins at this stage.</summary>
    public static int ShapeOf(int joins, int stage) => IsWall | (Math.Clamp(stage, Whole, Rubble) << StageShift) | (joins & JoinBits);

    /// <summary>The joins of a shape: bit i is set when the wall goes on to the tile at <see cref="Directions"/>[i].</summary>
    public static int JoinsOf(int shape) => shape & JoinBits;

    /// <summary>How worn a shape is, from <see cref="Whole"/> to <see cref="Rubble"/>.</summary>
    public static int StageOf(int shape) => (shape >> StageShift) & StageBits;

    /// <summary>
    /// How worn a wall with these hit points is drawn: <see cref="Whole"/> only with all of
    /// them, then a stage for each third it has lost, so that the first hit already shows.
    /// </summary>
    public static int StageOf(long hitPoints, long maximum) =>
        maximum <= 0 || hitPoints >= maximum ? Whole
        : 3 * hitPoints >= 2 * maximum ? 1
        : 3 * hitPoints >= maximum ? 2
        : Rubble;

    /// <summary>
    /// The mesh of a shape, standing on the ground with the middle of its tile at the origin.
    /// The shape of a gate is a wall tile's with more said of it, and <see cref="GateShapes"/> has its mesh.
    /// </summary>
    public static ArrayMesh MeshOf(int shape)
    {
        if (GateShapes.IsGate(shape))
        {
            return GateShapes.MeshOf(shape);
        }

        var joins = JoinsOf(shape);
        var stage = StageOf(shape);
        var look = Looks[stage];
        var mesh = new ArrayMesh();

        var stone = new SurfaceTool();
        stone.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(stone, Vector2.Zero, Vector2.Right, look.PostHalf, look.PostHalf, 0f, look.PostTop);
        for (var i = 0; i < Directions.Length; i++)
        {
            if ((joins & (1 << i)) != 0)
            {
                var (way, reach) = Arm(i);
                AddBox(stone, way * (reach / 2f), way, reach / 2f, look.ArmHalf, 0f, look.ArmTop);
            }
        }

        stone.Commit(mesh);

        var cap = new SurfaceTool();
        cap.Begin(Mesh.PrimitiveType.Triangles);
        AddPostCap(cap, look, stage);
        for (var i = 0; i < Directions.Length; i++)
        {
            if ((joins & (1 << i)) != 0)
            {
                AddArmCap(cap, look, stage, i);
            }
        }

        cap.Commit(mesh);
        return mesh;
    }

    // The way an arm goes on the ground, and how far it is from the middle of the tile to where
    // it meets the arm of the next tile: the edge, or the corner for a diagonal.
    private static (Vector2 Way, float Reach) Arm(int direction)
    {
        var (x, y) = Directions[direction];
        return x != 0 && y != 0 ? (new Vector2(x, y) * Diagonal, Diagonal) : (new Vector2(x, y), 0.5f);
    }

    // The cap of the post: whole, then without a quarter, then half of it, then one quarter.
    private static void AddPostCap(SurfaceTool tool, Look look, int stage)
    {
        var half = look.CapHalf;
        var quarter = half / 2f;
        switch (stage)
        {
            case Whole:
                AddBox(tool, Vector2.Zero, Vector2.Right, half, half, look.PostTop, look.CapTop);
                break;
            case 1:
                AddBox(tool, new Vector2(0f, -quarter), Vector2.Right, half, quarter, look.PostTop, look.CapTop);
                AddBox(tool, new Vector2(-quarter, quarter), Vector2.Right, quarter, quarter, look.PostTop, look.CapTop);
                break;
            case 2:
                AddBox(tool, new Vector2(0f, -quarter), Vector2.Right, half, quarter, look.PostTop, look.CapTop);
                break;
            default:
                AddBox(tool, new Vector2(-quarter, -quarter), Vector2.Right, quarter, quarter, look.PostTop, look.CapTop);
                break;
        }
    }

    // The cap of an arm: all along it, then with a gap, then only by the post, then none.
    private static void AddArmCap(SurfaceTool tool, Look look, int stage, int direction)
    {
        var (way, reach) = Arm(direction);
        var half = look.ArmHalf + 0.03f;
        switch (stage)
        {
            case Whole:
                Piece(0f, 1f);
                break;
            case 1:
                Piece(0f, 0.62f);
                Piece(0.8f, 1f);
                break;
            case 2:
                Piece(0f, 0.7f);
                break;
            default:
                break;
        }

        void Piece(float from, float to)
        {
            var middle = reach * (from + to) / 2f;
            AddBox(tool, way * middle, way, reach * (to - from) / 2f, half, look.ArmTop, look.ArmCapTop);
        }
    }

    /// <summary>
    /// Adds a block with upright sides and a top, lying along a way on the ground. There is no
    /// underside, since nothing sees it. The sides are flat, so that it reads as built.
    /// </summary>
    internal static void AddBox(SurfaceTool tool, Vector2 middle, Vector2 way, float halfLength, float halfWidth, float from, float to)
    {
        var across = new Vector2(-way.Y, way.X);
        Vector2[] corners = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];

        tool.SetNormal(Vector3.Up);
        tool.AddVertex(At(0, to));
        tool.AddVertex(At(1, to));
        tool.AddVertex(At(2, to));
        tool.AddVertex(At(0, to));
        tool.AddVertex(At(2, to));
        tool.AddVertex(At(3, to));

        for (var i = 0; i < corners.Length; i++)
        {
            var next = (i + 1) % corners.Length;
            var along = At(next, from) - At(i, from);
            tool.SetNormal(Vector3.Up.Cross(along).Normalized());

            tool.AddVertex(At(next, to));
            tool.AddVertex(At(i, to));
            tool.AddVertex(At(i, from));

            tool.AddVertex(At(next, to));
            tool.AddVertex(At(i, from));
            tool.AddVertex(At(next, from));
        }

        Vector3 At(int corner, float height)
        {
            var ground = middle + (way * (corners[corner].X * halfLength)) + (across * (corners[corner].Y * halfWidth));
            return new Vector3(ground.X, height, ground.Y);
        }
    }

    // The sizes of one stage, in tiles: half the side of the post and of its cap with how high
    // each reaches, and half the width of an arm with how high it and its cap reach.
    private readonly record struct Look(float PostHalf, float PostTop, float CapHalf, float CapTop, float ArmHalf, float ArmTop, float ArmCapTop);
}
