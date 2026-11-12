using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// How a structure that is still building itself is drawn: its shape stands as a plan, see-through,
/// and fills from the ground up as it is built, in a duller color than it will have, so that an
/// unfinished structure is not taken for one that works.
/// </summary>
/// <remarks>
/// The structure layer makes one of these for a structure the player knows to be unfinished,
/// tells it after every tick how far the player knows it to be built and how far it is a
/// memory, and puts the finished colors on the structure itself once the player knows it to be
/// finished.
/// </remarks>
public sealed class FoundationLook
{
    // The built part takes this much of the color of bare timber, and the plan above it is this solid.
    private const float BareShare = 0.55f;
    private const float PlanAlpha = 0.24f;

    // A foundation that has just been laid shows this much of its height, so that it is seen at all.
    private const float LeastShown = 0.12f;

    private static readonly Color Bare = new(0.62f, 0.52f, 0.38f);

    private const string Code = """
        shader_type spatial;
        render_mode depth_draw_always;

        uniform vec4 built_color : source_color;
        uniform vec4 plan_color : source_color;
        uniform float built_to = 0.0;

        varying float height;

        void vertex() {
            height = VERTEX.y;
        }

        void fragment() {
            bool built = height <= built_to;
            ALBEDO = built ? built_color.rgb : plan_color.rgb;
            ALPHA = built ? 1.0 : plan_color.a;
            ROUGHNESS = 1.0;
        }
        """;

    private static readonly StringName BuiltColor = "built_color";
    private static readonly StringName PlanColor = "plan_color";
    private static readonly StringName BuiltTo = "built_to";

    private static Shader? _shader;

    private readonly ShaderMaterial _walls;
    private readonly ShaderMaterial _roof;
    private readonly Color _wallColor;
    private readonly Color _roofColor;
    private readonly float _height;
    private readonly int _buildTicks;

    private FoundationLook(ShaderMaterial walls, ShaderMaterial roof, Color wallColor, Color roofColor, StructureDef structure)
    {
        _walls = walls;
        _roof = roof;
        _wallColor = wallColor;
        _roofColor = roofColor;
        _height = StructureShapes.HeightOf(structure);
        _buildTicks = Math.Max(structure.BuildTicks, 1);
    }

    /// <summary>How tall the structure's shape is, in tiles.</summary>
    public float Height => _height;

    /// <summary>How much of the structure is drawn as built, from 0 to 1.</summary>
    public float Shown { get; private set; }

    /// <summary>Puts the unfinished look on a structure's shape.</summary>
    /// <param name="body">The node that draws the structure, with the mesh of <see cref="StructureShapes.MeshOf"/>.</param>
    /// <param name="structure">The structure.</param>
    /// <param name="walls">The color its walls will have.</param>
    /// <param name="roof">The color its roof will have.</param>
    public static FoundationLook On(MeshInstance3D body, StructureDef structure, Color walls, Color roof)
    {
        _shader ??= new Shader { Code = Code };
        var look = new FoundationLook(new ShaderMaterial { Shader = _shader }, new ShaderMaterial { Shader = _shader }, walls, roof, structure);
        body.SetSurfaceOverrideMaterial(StructureShapes.WallsSurface, look._walls);
        body.SetSurfaceOverrideMaterial(StructureShapes.RoofSurface, look._roof);
        body.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        look.Dim(0f);
        look.Show(0);
        return look;
    }

    /// <summary>
    /// Puts an unseen shape with the unfinished look under a node, so that the renderer has what
    /// it needs to draw the look before the first foundation is laid, and that one appears at
    /// once and without a pause.
    /// </summary>
    public static void Prepare(Node3D under, StructureDef structure)
    {
        var shape = new MeshInstance3D { Name = "UnfinishedLook", Mesh = StructureShapes.MeshOf(structure), Visible = false };
        On(shape, structure, Bare, Bare);
        under.AddChild(shape);
    }

    /// <summary>Draws the structure as built for this many ticks of its build time.</summary>
    public void Show(int built)
    {
        Shown = Math.Clamp(built, 0, _buildTicks) / (float)_buildTicks;
        var to = _height * Mathf.Max(Shown, LeastShown);
        _walls.SetShaderParameter(BuiltTo, to);
        _roof.SetShaderParameter(BuiltTo, to);
    }

    /// <summary>
    /// Draws the structure as far a memory as this, from 0 for a structure the player sees now
    /// to 1 for one the player remembers: its colors go the way a finished structure's do.
    /// </summary>
    public void Dim(float dimmed)
    {
        Paint(_walls, _wallColor, dimmed);
        Paint(_roof, _roofColor, dimmed);
    }

    private static void Paint(ShaderMaterial material, Color finished, float dimmed)
    {
        var built = StructureLayer.Dim(finished.Lerp(Bare, BareShare), dimmed);
        var plan = StructureLayer.Dim(finished.Lerp(Colors.White, 0.4f), dimmed);
        material.SetShaderParameter(BuiltColor, built);
        material.SetShaderParameter(PlanColor, new Color(plan, PlanAlpha));
    }
}
