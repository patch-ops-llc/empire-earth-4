using Godot;

namespace Game;

/// <summary>
/// Many copies of one shape, each with a place and a color of its own, drawn as this one node. The
/// layers of the combat view put every bar, shot and effect of a frame into one of these and
/// say again in the next frame what there is, so that nothing of the last frame has to be found
/// and taken away.
/// </summary>
public partial class InstancedShapes : MultiMeshInstance3D
{
    private readonly List<(Transform3D Place, Color Color)> _frame = [];

    /// <summary>Makes the node for the copies of one shape and puts it under a layer.</summary>
    /// <param name="layer">The layer the shapes are drawn under.</param>
    /// <param name="name">The name of the node.</param>
    /// <param name="mesh">The shape. Its own material is not used.</param>
    /// <param name="material">How every copy is drawn. It must take its color from the vertex color.</param>
    /// <param name="area">The part of the world the copies can be in, which Godot must be told because they move.</param>
    public static InstancedShapes Under(Node3D layer, string name, Mesh mesh, Material material, Aabb area)
    {
        var shapes = new InstancedShapes
        {
            Name = name,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = 32,
                VisibleInstanceCount = 0,
                CustomAabb = area,
            },
            MaterialOverride = material,
            CastShadow = ShadowCastingSetting.Off,
        };
        layer.AddChild(shapes);
        return shapes;
    }

    /// <summary>The number of copies added since <see cref="Begin"/>.</summary>
    public int Count => _frame.Count;

    /// <summary>Starts a frame with no copy in it.</summary>
    public void Begin() => _frame.Clear();

    /// <summary>Adds a copy to the frame. Copies are drawn in the order they are added.</summary>
    public void Add(Transform3D place, Color color) => _frame.Add((place, color));

    /// <summary>Shows the copies of the frame, and no others.</summary>
    public void End()
    {
        var shapes = Multimesh;
        if (_frame.Count > shapes.InstanceCount)
        {
            // Changing the count throws the old copies away, which is fine: all are placed again below.
            shapes.VisibleInstanceCount = 0;
            shapes.InstanceCount = Math.Max(_frame.Count, shapes.InstanceCount * 2);
        }

        for (var i = 0; i < _frame.Count; i++)
        {
            shapes.SetInstanceTransform(i, _frame[i].Place);
            shapes.SetInstanceColor(i, _frame[i].Color);
        }

        shapes.VisibleInstanceCount = _frame.Count;
    }

    /// <summary>The whole map, from the ground to well above the units, as the area the copies can be in.</summary>
    public static Aabb MapArea(Sim.Data.MapDef map) => new(new Vector3(0f, -1f, 0f), new Vector3(map.Width, 6f, map.Height));

    /// <summary>
    /// A material that draws a copy in its own color, without light, so that a bar or a flash
    /// reads the same in shade and in sun.
    /// </summary>
    /// <param name="seeThrough">True for shapes that fade: their color's alpha is how solid they are.</param>
    /// <param name="overEverything">True for what must never be hidden behind a unit or a tree, as a health bar.</param>
    public static StandardMaterial3D Material(bool seeThrough, bool overEverything = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = seeThrough ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        NoDepthTest = overEverything,
        RenderPriority = overEverything ? 10 : 0,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
