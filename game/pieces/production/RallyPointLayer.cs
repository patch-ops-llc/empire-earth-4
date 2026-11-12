using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// Draws the rally point of every selected structure that has one set: a flag where new units
/// walk to, and a line to it from the structure. A structure whose rally point was never set
/// has none drawn, since its units stay beside it.
/// </summary>
/// <remarks>
/// The flag stands where the simulation has the point, so it appears once the order has been
/// carried out, a tick or more after the click. The mark of the order shows the click at once.
/// </remarks>
public partial class RallyPointLayer : WorldLayer
{
    private const float PoleHeight = 1.7f;
    private const float PoleWidth = 0.09f;
    private const float PennantLength = 0.85f;
    private const float PennantHeight = 0.55f;
    private const float LineWidth = 0.1f;

    // Above the ground by enough not to flicker against it or against the territory.
    private const float LineLift = 0.07f;

    private readonly List<Flag> _flags = [];
    private Mesh _flagMesh = null!;
    private Mesh _lineMesh = null!;

    /// <summary>The number of rally points drawn.</summary>
    public int FlagCount { get; private set; }

    protected override void Attach()
    {
        _flagMesh = FlagMesh();
        _lineMesh = LineMesh();
        View.Selection.Changed += Refresh;
    }

    public override void AfterTick() => Refresh();

    private void Refresh()
    {
        var match = View.Match;
        var producers = match.Production.Producers;
        var shown = 0;
        foreach (var entity in View.Selection.Entities)
        {
            if (!ProductionOffer.IsProducerOf(match, View.Player, entity))
            {
                continue;
            }

            var producer = producers.Get(entity);
            if (!producer.RallySet)
            {
                continue;
            }

            if (shown == _flags.Count)
            {
                _flags.Add(CreateFlag());
            }

            var from = WorldView.ToWorld(match.Bodies.Get(entity).Position);
            var to = WorldView.ToWorld(producer.RallyPoint);
            Place(_flags[shown++], from, to);
        }

        for (var i = 0; i < _flags.Count; i++)
        {
            _flags[i].Pole.Visible = i < shown;
            _flags[i].Line.Visible = i < shown && _flags[i].HasLine;
        }

        FlagCount = shown;
    }

    private static void Place(Flag flag, Vector3 from, Vector3 to)
    {
        flag.Pole.Position = to;

        var along = to - from;
        var length = along.Length();
        flag.HasLine = length > 0.01f;
        if (flag.HasLine)
        {
            // The line's mesh runs one tile toward -Z, which is turned to the flag and stretched to it.
            var basis = Basis.LookingAt(along / length, Vector3.Up) * Basis.FromScale(new Vector3(1f, 1f, length));
            flag.Line.Transform = new Transform3D(basis, from + (Vector3.Up * LineLift));
        }
    }

    private Flag CreateFlag()
    {
        var pole = new MeshInstance3D
        {
            Name = $"RallyFlag{_flags.Count}",
            Mesh = _flagMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var line = new MeshInstance3D
        {
            Name = $"RallyLine{_flags.Count}",
            Mesh = _lineMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(pole);
        AddChild(line);
        return new Flag(pole, line);
    }

    // A pole with a pennant, standing on the origin, seen from both sides. It takes no light.
    private static ArrayMesh FlagMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Up);

        // The pole, as two upright rectangles that cross.
        var half = PoleWidth / 2f;
        tool.SetColor(Colors.White);
        AddQuad(tool, new(-half, 0f, 0f), new(half, 0f, 0f), new(half, PoleHeight, 0f), new(-half, PoleHeight, 0f));
        AddQuad(tool, new(0f, 0f, -half), new(0f, 0f, half), new(0f, PoleHeight, half), new(0f, PoleHeight, -half));

        tool.SetColor(RallyOrder.Mark);
        tool.AddVertex(new Vector3(0f, PoleHeight, 0f));
        tool.AddVertex(new Vector3(PennantLength, PoleHeight - (PennantHeight / 2f), 0f));
        tool.AddVertex(new Vector3(0f, PoleHeight - PennantHeight, 0f));

        var mesh = tool.Commit();
        mesh.SurfaceSetMaterial(0, Paint(1f));
        return mesh;
    }

    // A strip on the ground from the origin one tile toward -Z.
    private static ArrayMesh LineMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Up);
        tool.SetColor(RallyOrder.Mark);
        var half = LineWidth / 2f;
        AddQuad(tool, new(-half, 0f, 0f), new(half, 0f, 0f), new(half, 0f, -1f), new(-half, 0f, -1f));

        var mesh = tool.Commit();
        mesh.SurfaceSetMaterial(0, Paint(0.7f));
        return mesh;
    }

    private static void AddQuad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        tool.AddVertex(a);
        tool.AddVertex(b);
        tool.AddVertex(c);

        tool.AddVertex(a);
        tool.AddVertex(c);
        tool.AddVertex(d);
    }

    private static StandardMaterial3D Paint(float alpha) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        AlbedoColor = new Color(1f, 1f, 1f, alpha),
        Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private sealed class Flag(MeshInstance3D pole, MeshInstance3D line)
    {
        public MeshInstance3D Pole { get; } = pole;

        public MeshInstance3D Line { get; } = line;

        public bool HasLine { get; set; }
    }
}
