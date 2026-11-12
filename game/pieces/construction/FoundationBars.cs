using Godot;

namespace Game;

/// <summary>
/// A bar above every structure that is building itself, which fills as it is built. It faces the
/// camera and is never hidden behind a structure, a unit or a tree.
/// </summary>
/// <remarks>
/// The bar is blue, so that it is not read as hit points, which are green, yellow and red: a
/// foundation has all its hit points from the moment it is laid (design 2.5).
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

    private InstancedShapes _quads = null!;

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
    }

    public override void EveryFrame(double delta)
    {
        _quads.Begin();
        BarCount = 0;
        if (GetViewport().GetCamera3D() is { } camera)
        {
            var match = View.Match;
            var basis = camera.GlobalBasis;
            var foundations = match.Foundations;
            for (var i = 0; i < foundations.Count; i++)
            {
                var entity = foundations.EntityAt(i);
                if (!match.World.Entities.IsAlive(entity)
                    || !match.Structures.TryGet(entity, out var placed)
                    || match.Data.StructureOf(placed.Type) is not { } structure)
                {
                    continue;
                }

                var fraction = Mathf.Clamp(foundations.ValueAt(i).Built / (float)Math.Max(structure.BuildTicks, 1), 0f, 1f);
                var middle = new Vector3(
                    placed.X + (placed.Size / 2f),
                    StructureShapes.HeightOf(structure) + Lift,
                    placed.Y + (placed.Size / 2f));
                AddQuad(basis, middle, -Edge, Width + Edge, Height + (2f * Edge), Back);
                if (fraction > 0f)
                {
                    AddQuad(basis, middle, 0f, Width * fraction, Height, Built);
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
