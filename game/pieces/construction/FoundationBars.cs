using Godot;

namespace Game;

/// <summary>
/// A bar above every structure that is building itself, which fills as it is built. It faces the
/// camera and is never hidden behind a structure, a unit or a tree.
/// </summary>
/// <remarks>
/// <para>
/// A bar is drawn over what the structure layer draws unfinished and shows what that layer
/// shows, so that the two never disagree, and a view that hides a structure hides its bar. Under
/// the fog of war that layer draws what the player knows, so another player's foundation has a
/// bar only once the player has seen it, and a remembered one keeps the bar it had then.
/// </para>
/// <para>
/// The bar is blue, so that it is not read as hit points, which are green, yellow and red: a
/// foundation has all its hit points from the moment it is laid (design 2.5).
/// </para>
/// </remarks>
public partial class FoundationBars : WorldLayer
{
    /// <summary>How wide a bar is, in tiles.</summary>
    public const float Width = 1.3f;

    /// <summary>How tall a bar is, in tiles.</summary>
    public const float Height = 0.12f;

    // The dark edge round the bar, and how far above the top of the structure it floats.
    private const float Edge = 0.03f;
    private const float Lift = 0.4f;

    private static readonly Color Back = new(0.05f, 0.05f, 0.06f);
    private static readonly Color Built = new(0.42f, 0.74f, 1f);

    private readonly StructureLayer _structures;
    private readonly List<UnfinishedStructure> _unfinished = [];
    private InstancedShapes _quads = null!;

    /// <summary>Makes the bars over the structures this layer draws unfinished.</summary>
    public FoundationBars(StructureLayer structures) => _structures = structures;

    /// <summary>The number of bars drawn in the last frame.</summary>
    public int BarCount { get; private set; }

    protected override void Attach()
    {
        _quads = InstancedShapes.Under(
            this,
            "Bars",
            new QuadMesh { Size = Vector2.One },
            InstancedShapes.Material(seeThrough: false, overEverything: true),
            InstancedShapes.MapArea(View.Match.Map));
        foreach (var structure in View.Match.Data.Structures)
        {
            FoundationLook.Prepare(this, structure);
            break;
        }
    }

    public override void EveryFrame(double delta)
    {
        _quads.Begin();
        BarCount = 0;
        _unfinished.Clear();
        _structures.CollectUnfinished(_unfinished);
        if (GetViewport().GetCamera3D() is { } camera)
        {
            var basis = camera.GlobalBasis;
            foreach (var structure in _unfinished)
            {
                var middle = structure.Middle + (Vector3.Up * (structure.Height + Lift));
                AddQuad(basis, middle, -Edge, Width + Edge, Height + (2f * Edge), Back);
                if (structure.Built > 0f)
                {
                    // A remembered foundation keeps the bar it had when last seen, dimmed with it.
                    AddQuad(basis, middle, 0f, Width * Mathf.Min(structure.Built, 1f), Height, StructureLayer.Dim(Built, structure.Dimmed));
                }

                BarCount++;
            }
        }

        _quads.End();
    }

    // A quad that faces the camera, from one distance to another along the bar, measured from its left end.
    private void AddQuad(Basis camera, Vector3 middle, float from, float to, float height, Color color)
    {
        var left = middle - (camera.X * (Width / 2f));
        var center = left + (camera.X * ((from + to) / 2f));
        _quads.Add(new Transform3D(new Basis(camera.X * (to - from), camera.Y * height, camera.Z), center), color);
    }
}
