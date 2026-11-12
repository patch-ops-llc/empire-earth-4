using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// The shapes that stand in for structures until there is art: for each structure of the roster
/// a few blocks one on another, as wide as its footprint, so that each can be told from the
/// others at a glance and none is mistaken for a unit.
/// </summary>
/// <remarks>
/// A mesh has two surfaces, <see cref="WallsSurface"/> and <see cref="RoofSurface"/>, and no
/// material: whoever draws it puts the owner's color on the roof. The placement preview of the
/// build menu draws the same mesh with a material of its own.
/// </remarks>
public static class StructureShapes
{
    public const int WallsSurface = 0;
    public const int RoofSurface = 1;

    // How wide the line round a selected structure is, in tiles, and how far inside the edge of
    // the footprint its outer side runs.
    private const float OutlineWidth = 0.14f;
    private const float OutlineInset = 0.02f;

    // A structure the table does not name: a plain block with a lid.
    private static readonly Tier[] Plain =
    [
        new(0.90f, 0.90f, 0f, 0.80f, Roof: false),
        new(0.94f, 0.94f, 0.80f, 0.92f, Roof: true),
    ];

    // The blocks of each structure, from the ground up, by the ID of its file. Widths are shares
    // of the footprint's side, heights are in tiles.
    private static readonly Dictionary<string, Tier[]> Tiers = new()
    {
        // A wide hall with a keep on it, and the tallest roof on the map.
        ["capitol"] =
        [
            new(0.92f, 0.88f, 0f, 0.90f, Roof: false),
            new(0.56f, 0.52f, 0.90f, 1.60f, Roof: false),
            new(0.66f, 0f, 1.60f, 2.40f, Roof: true),
        ],

        // A lower hall with a flat-topped lookout.
        ["outpost"] =
        [
            new(0.90f, 0.84f, 0f, 0.80f, Roof: false),
            new(0.46f, 0.42f, 0.80f, 1.50f, Roof: false),
            new(0.56f, 0.56f, 1.50f, 1.70f, Roof: true),
        ],

        // A long house under a hipped roof.
        ["barracks"] =
        [
            new(0.90f, 0.88f, 0f, 0.70f, Roof: false),
            new(0.94f, 0.50f, 0.70f, 1.15f, Roof: true),
        ],

        // A workshop with a chimney.
        ["works"] =
        [
            new(0.90f, 0.88f, 0f, 0.85f, Roof: false),
            new(0.94f, 0.94f, 0.85f, 0.97f, Roof: true),
            new(0.22f, 0.18f, 0.97f, 1.70f, Roof: false),
        ],

        // A small house under a steep roof.
        ["academy"] =
        [
            new(0.88f, 0.84f, 0f, 0.90f, Roof: false),
            new(0.94f, 0f, 0.90f, 1.70f, Roof: true),
        ],

        // A mound with a block of machinery on it.
        ["generator"] =
        [
            new(0.86f, 0.60f, 0f, 0.50f, Roof: false),
            new(0.50f, 0.50f, 0.50f, 1.10f, Roof: true),
        ],

        // Narrow and tall, with a platform wider than its top.
        ["tower"] =
        [
            new(0.60f, 0.50f, 0f, 1.70f, Roof: false),
            new(0.72f, 0.72f, 1.70f, 1.95f, Roof: true),
        ],

        // A post. A wall tile is drawn with the shapes of WallShapes, which join it to the
        // tiles round it. This is the tile alone, as tall as those, for whatever asks here.
        ["wall"] =
        [
            new(0.68f, 0.68f, 0f, 0.66f, Roof: false),
            new(0.76f, 0.76f, 0.66f, WallShapes.Height, Roof: true),
        ],

        // A block as tall as a gate's pillars. A gate is drawn with the shapes of GateShapes,
        // which turn it the way its wall runs. This is for whatever asks here.
        ["gate"] =
        [
            new(0.94f, 0.68f, 0f, 0.80f, Roof: false),
            new(0.98f, 0.74f, 0.80f, GateShapes.Height, Roof: true),
        ],
    };

    /// <summary>How tall the structure is drawn, in tiles, which is also how tall it is to the pointer.</summary>
    public static float HeightOf(StructureDef structure) => TiersOf(structure)[^1].To;

    /// <summary>
    /// The shape of a structure, standing on the ground with the middle of its footprint at the
    /// origin.
    /// </summary>
    public static ArrayMesh MeshOf(StructureDef structure)
    {
        var tiers = TiersOf(structure);
        var mesh = new ArrayMesh();
        AddSurface(mesh, tiers, structure.Footprint, roof: false);
        AddSurface(mesh, tiers, structure.Footprint, roof: true);
        return mesh;
    }

    /// <summary>
    /// A line of one color along the edge of a square of tiles, lying on the ground with the
    /// middle of the square at the origin. It takes no light.
    /// </summary>
    public static ArrayMesh Outline(int size, Color color)
    {
        var outer = (size / 2f) - OutlineInset;
        var inner = outer - OutlineWidth;

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Up);

        // Two bars across the whole side, and two between them.
        AddFlat(tool, -outer, -outer, outer, -inner);
        AddFlat(tool, -outer, inner, outer, outer);
        AddFlat(tool, -outer, -inner, -inner, inner);
        AddFlat(tool, inner, -inner, outer, inner);

        var mesh = tool.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        return mesh;
    }

    private static Tier[] TiersOf(StructureDef structure) => Tiers.GetValueOrDefault(structure.Id, Plain);

    // The walls or the roofs of a shape as one surface. Every shape has blocks of both.
    private static void AddSurface(ArrayMesh mesh, Tier[] tiers, int footprint, bool roof)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var any = false;
        foreach (var tier in tiers)
        {
            if (tier.Roof == roof)
            {
                AddBlock(tool, tier, footprint);
                any = true;
            }
        }

        if (!any)
        {
            throw new InvalidOperationException("Every structure shape needs a block of walls and a block of roof.");
        }

        tool.Commit(mesh);
    }

    // A block with four sloping or upright sides and a top. There is no underside, since nothing
    // sees it. The sides are flat, so that it reads as built.
    private static void AddBlock(SurfaceTool tool, Tier tier, int footprint)
    {
        var foot = tier.FootShare * footprint / 2f;
        var top = tier.TopShare * footprint / 2f;
        Vector2[] corners = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];

        if (top > 0f)
        {
            tool.SetNormal(Vector3.Up);
            tool.AddVertex(Top(0));
            tool.AddVertex(Top(1));
            tool.AddVertex(Top(2));
            tool.AddVertex(Top(0));
            tool.AddVertex(Top(2));
            tool.AddVertex(Top(3));
        }

        for (var i = 0; i < corners.Length; i++)
        {
            var next = (i + 1) % corners.Length;
            var along = Bottom(next) - Bottom(i);
            var up = Top(i) - Bottom(i);
            tool.SetNormal(up.Cross(along).Normalized());

            tool.AddVertex(Top(next));
            tool.AddVertex(Top(i));
            tool.AddVertex(Bottom(i));

            tool.AddVertex(Top(next));
            tool.AddVertex(Bottom(i));
            tool.AddVertex(Bottom(next));
        }

        Vector3 Top(int corner) => new(corners[corner].X * top, tier.To, corners[corner].Y * top);
        Vector3 Bottom(int corner) => new(corners[corner].X * foot, tier.From, corners[corner].Y * foot);
    }

    // A rectangle lying on the ground, seen from above.
    private static void AddFlat(SurfaceTool tool, float fromX, float fromZ, float toX, float toZ)
    {
        tool.AddVertex(new Vector3(fromX, 0f, fromZ));
        tool.AddVertex(new Vector3(toX, 0f, fromZ));
        tool.AddVertex(new Vector3(toX, 0f, toZ));

        tool.AddVertex(new Vector3(fromX, 0f, fromZ));
        tool.AddVertex(new Vector3(toX, 0f, toZ));
        tool.AddVertex(new Vector3(fromX, 0f, toZ));
    }

    // One block: how wide it is at its foot and at its top, as shares of the footprint's side,
    // from what height to what height it stands, and whether it is roof or wall.
    private readonly record struct Tier(float FootShare, float TopShare, float From, float To, bool Roof);
}
