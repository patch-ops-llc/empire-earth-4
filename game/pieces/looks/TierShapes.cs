using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// The stand-in for a structure's look by tier until there is art: a turret that rises through
/// the middle of the structure's shape, in the color of the track the structure shows, with one
/// storey for each tier and as many dots on its top, set out as on a die. A taller turret is a
/// higher tier, and the dots say which, also from far above.
/// </summary>
/// <remarks>
/// The turret is a shape of its own that stands in the structure's shape and changes nothing of
/// it, so whoever draws the structure itself, finished, unfinished or remembered, need not know
/// of it. Its shaft starts on the ground, so it stands in every shape, whatever the roof.
/// </remarks>
public static class TierShapes
{
    /// <summary>How tall one storey is, in tiles.</summary>
    public const float StoreyHeight = 0.38f;

    // How far under the top of the structure's shape the first storey starts, so that the top is inside it.
    private const float Sink = 0.1f;

    // The turret's width as a share of the footprint's side, and the least and the most it is, in tiles.
    private const float WidthShare = 0.4f;
    private const float LeastWidth = 0.5f;
    private const float MostWidth = 1.4f;

    private const float CollarHeight = 0.1f;
    private const float CollarOverhang = 0.07f;

    // Every other storey is darker, so that storeys of one color can be told apart from the side.
    private const float EvenStoreyShade = 0.42f;

    // The dots lie in three rows of three on the top, each this share of its cell wide, and
    // above the top by enough not to flicker against it.
    private const int DotsInARow = 3;
    private const float DotShare = 0.62f;
    private const float DotLift = 0.012f;

    private static readonly Color Shaft = new(0.36f, 0.35f, 0.34f);
    private static readonly Color Collar = new(0.11f, 0.12f, 0.15f);
    private static readonly Color Dot = new(0.07f, 0.07f, 0.09f);

    // Which of the nine cells carry a dot at each tier, as the faces of a die and on from them:
    // a bit for each cell, row by row from the far side, the cell at the west first.
    private static readonly int[] Dots =
    [
        0b000_000_000,
        0b000_010_000,
        0b001_000_100,
        0b001_010_100,
        0b101_000_101,
        0b101_010_101,
        0b101_101_101,
        0b101_111_101,
        0b111_101_111,
        0b111_111_111,
    ];

    /// <summary>The most tiers the dots can say. A higher tier shows this many.</summary>
    public static int MostDots => Dots.Length - 1;

    /// <summary>How wide the turret of a structure is, in tiles.</summary>
    public static float WidthOf(StructureDef structure) =>
        Math.Clamp(WidthShare * structure.Footprint, LeastWidth, MostWidth);

    /// <summary>How tall a structure is drawn with its turret at a tier, in tiles.</summary>
    public static float HeightOf(StructureDef structure, int tier) =>
        StructureShapes.HeightOf(structure) - Sink + CollarHeight + (Math.Max(0, tier) * StoreyHeight);

    /// <summary>
    /// The turret of a structure at a tier, in one color for the storeys and the top, standing on the ground
    /// with the middle of the footprint at the origin. It has no material: its colors are those
    /// of its corners.
    /// </summary>
    public static ArrayMesh Turret(StructureDef structure, int tier, Color track)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        var half = WidthOf(structure) / 2f;
        var from = StructureShapes.HeightOf(structure) - Sink;

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(tool, half - 0.02f, 0f, from, Shaft);
        AddBox(tool, half + CollarOverhang, from, from + CollarHeight, Collar);

        var height = from + CollarHeight;
        for (var storey = 1; storey <= tier; storey++)
        {
            // The top of the last storey is what is seen from above, so it is always the track's own color.
            AddBox(tool, half, height, height + StoreyHeight, storey % 2 == 1 ? track : track.Darkened(EvenStoreyShade), track);
            height += StoreyHeight;
        }

        AddDots(tool, half, height + DotLift, Dots[Math.Min(tier, MostDots)]);
        return tool.Commit();
    }

    // The dots of a tier, lying on a square top.
    private static void AddDots(SurfaceTool tool, float half, float height, int cells)
    {
        var cell = 2f * half / DotsInARow;
        var dot = cell * DotShare / 2f;
        tool.SetColor(Dot);
        tool.SetNormal(Vector3.Up);
        for (var row = 0; row < DotsInARow; row++)
        {
            for (var column = 0; column < DotsInARow; column++)
            {
                var bit = (DotsInARow * DotsInARow) - 1 - ((row * DotsInARow) + column);
                if ((cells & (1 << bit)) == 0)
                {
                    continue;
                }

                var x = -half + ((column + 0.5f) * cell);
                var z = -half + ((row + 0.5f) * cell);
                tool.AddVertex(new Vector3(x - dot, height, z - dot));
                tool.AddVertex(new Vector3(x + dot, height, z - dot));
                tool.AddVertex(new Vector3(x + dot, height, z + dot));

                tool.AddVertex(new Vector3(x - dot, height, z - dot));
                tool.AddVertex(new Vector3(x + dot, height, z + dot));
                tool.AddVertex(new Vector3(x - dot, height, z + dot));
            }
        }
    }

    // An upright box round the middle, with a top and no underside: nothing sees it from below.
    private static void AddBox(SurfaceTool tool, float half, float from, float to, Color color) =>
        AddBox(tool, half, from, to, color, color);

    private static void AddBox(SurfaceTool tool, float half, float from, float to, Color sides, Color top)
    {
        Vector2[] corners = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];

        tool.SetColor(top);
        tool.SetNormal(Vector3.Up);
        tool.AddVertex(At(0, to));
        tool.AddVertex(At(1, to));
        tool.AddVertex(At(2, to));
        tool.AddVertex(At(0, to));
        tool.AddVertex(At(2, to));
        tool.AddVertex(At(3, to));

        tool.SetColor(sides);
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

        Vector3 At(int corner, float height) => new(corners[corner].X * half, height, corners[corner].Y * half);
    }
}
