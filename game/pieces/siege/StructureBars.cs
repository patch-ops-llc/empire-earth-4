using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// A bar of hit points above every structure that has lost some, and above a selected one. It
/// faces the camera and is never hidden behind a structure, a unit or a tree.
/// </summary>
/// <remarks>
/// <para>
/// A bar is drawn over what the structure layer draws and shows what that layer knows, so that
/// the two never disagree. Under the fog of war (design 5.3) that is what the player knows:
/// another player's structure has a bar only once the player has seen it damaged, and a
/// remembered one keeps the bar it had when last seen, dimmed with it, however it has fared
/// since.
/// </para>
/// <para>
/// The bar is that of a unit (<see cref="HealthBars"/>), wider for a larger footprint: a dark
/// back, the hit points the structure has, and between them, in a pale color, those it has
/// just lost, which run out over a moment. A structure at full hit points has no bar unless it
/// is selected. Over one that is still building itself the bar stands above the bar of its
/// progress.
/// </para>
/// </remarks>
public partial class StructureBars : WorldLayer
{
    /// <summary>How tall a bar is, in tiles.</summary>
    public const float Height = 0.12f;

    // The dark edge round the bar, how far above the top of the structure it floats, how much
    // further above one that has a bar of its progress, and how much of a whole bar the pale
    // part gives up in a second.
    private const float Edge = 0.03f;
    private const float Lift = 0.7f;
    private const float AboveProgress = 0.22f;
    private const float TrailPerSecond = 0.6f;

    private static readonly Color Back = new(0.05f, 0.05f, 0.06f);
    private static readonly Color Trail = new(1f, 0.93f, 0.8f);

    private readonly StructureLayer _structures;
    private readonly List<DrawnStructure> _drawnNow = [];

    // For every structure with a bar, how full its bar was drawn in the last frame, the pale part included.
    private readonly Dictionary<EntityId, float> _drawn = [];
    private readonly HashSet<EntityId> _barred = [];
    private InstancedShapes _quads = null!;

    /// <summary>Makes the bars over the structures this layer draws.</summary>
    public StructureBars(StructureLayer structures) => _structures = structures;

    /// <summary>The number of bars drawn in the last frame.</summary>
    public int BarCount { get; private set; }

    /// <summary>
    /// True when this structure has a bar as it is drawn now: it is drawn, and it has lost hit
    /// points as far as the player knows or is selected.
    /// </summary>
    public bool Shows(EntityId entity)
    {
        _drawnNow.Clear();
        _structures.CollectDrawn(_drawnNow);
        foreach (var structure in _drawnNow)
        {
            if (structure.Entity == entity)
            {
                return Shows(in structure);
            }
        }

        return false;
    }

    /// <summary>How wide the bar of a structure is, in tiles, by the tiles it covers along each side.</summary>
    public static float WidthOf(int size) => 0.4f + (0.4f * size);

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
        _barred.Clear();
        _drawnNow.Clear();
        _structures.CollectDrawn(_drawnNow);
        if (GetViewport().GetCamera3D() is { } camera)
        {
            var basis = camera.GlobalBasis;
            foreach (var structure in _drawnNow)
            {
                Draw(in structure, basis, (float)delta);
            }
        }

        // A structure that has no bar any more starts without a pale part when it next has one.
        if (_drawn.Count != _barred.Count)
        {
            foreach (var entity in _drawn.Keys.Where(entity => !_barred.Contains(entity)).ToArray())
            {
                _drawn.Remove(entity);
            }
        }

        BarCount = _barred.Count;
        _quads.End();
    }

    private void Draw(in DrawnStructure structure, Basis camera, float delta)
    {
        var fraction = structure.Health;
        if (!Shows(in structure))
        {
            return;
        }

        // What a memory shows does not move: it is as the structure was when last seen.
        var drawn = !structure.IsRemembered && _drawn.TryGetValue(structure.Entity, out var before)
            ? Mathf.Max(fraction, before - (TrailPerSecond * delta))
            : fraction;
        _drawn[structure.Entity] = drawn;
        _barred.Add(structure.Entity);

        var width = WidthOf(structure.Size);
        var lift = structure.Height + Lift + (structure.IsFinished ? 0f : AboveProgress);
        var left = structure.Middle + (Vector3.Up * lift) - (camera.X * (width / 2f));
        AddQuad(camera, left, -Edge, width + Edge, Height + (2f * Edge), Back);
        if (drawn > fraction)
        {
            AddQuad(camera, left, 0f, width * drawn, Height, Trail);
        }

        if (fraction > 0f)
        {
            AddQuad(camera, left, 0f, width * fraction, Height, StructureLayer.Dim(HealthBars.ColorAt(fraction), structure.Dimmed));
        }
    }

    private bool Shows(in DrawnStructure structure) => structure.Health < 1f || View.Selection.Contains(structure.Entity);

    // A quad that faces the camera, from one distance to another along the bar, measured from its left end.
    private void AddQuad(Basis camera, Vector3 left, float from, float to, float height, Color color)
    {
        var center = left + (camera.X * ((from + to) / 2f));
        _quads.Add(new Transform3D(new Basis(camera.X * (to - from), camera.Y * height, camera.Z), center), color);
    }
}
