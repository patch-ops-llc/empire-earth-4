using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// A bar of hit points above every unit that has lost some, and above every selected unit. The
/// bar faces the camera and is never hidden behind a unit or a tree.
/// </summary>
/// <remarks>
/// A bar has three parts: a dark back, the hit points the unit has, and between them, in a pale
/// color, the hit points it has just lost, which run out over a moment so that a hit can be seen
/// after it has landed. A unit at full hit points has no bar unless it is selected, so that an
/// army that has not fought is not a wall of bars.
/// </remarks>
public partial class HealthBars : WorldLayer
{
    /// <summary>How wide a bar is, in tiles.</summary>
    public const float Width = 0.9f;

    /// <summary>How tall a bar is, in tiles.</summary>
    public const float Height = 0.1f;

    /// <summary>How wide the dark edge round a bar is, in tiles.</summary>
    public const float Edge = 0.025f;

    /// <summary>How far above the top of its unit the middle of a bar floats, in tiles.</summary>
    public const float Lift = 0.62f;

    // How much of a whole bar the pale part gives up in a second.
    private const float TrailPerSecond = 0.6f;

    private static readonly Color Back = new(0.05f, 0.05f, 0.06f);
    private static readonly Color Trail = new(1f, 0.93f, 0.8f);
    private static readonly Color Healthy = new(0.33f, 0.86f, 0.36f);
    private static readonly Color Hurt = new(0.96f, 0.8f, 0.22f);
    private static readonly Color Failing = new(0.93f, 0.26f, 0.2f);

    // For every unit with a bar, how full its bar was drawn in the last frame, the pale part included.
    private readonly Dictionary<EntityId, float> _drawn = [];
    private readonly List<EntityId> _gone = [];
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

    public override void AfterTick()
    {
        var health = View.Match.Health;
        foreach (var entity in _drawn.Keys)
        {
            if (!health.Contains(entity))
            {
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _drawn.Remove(entity);
        }

        _gone.Clear();
    }

    public override void EveryFrame(double delta)
    {
        _quads.Begin();
        BarCount = 0;

        if (GetViewport().GetCamera3D() is { } camera)
        {
            var basis = camera.GlobalBasis;
            foreach (var entity in View.World.Units)
            {
                Draw(entity, basis, (float)delta);
            }
        }

        _quads.End();
    }

    /// <summary>The color of a bar that is this full, from 0 to 1.</summary>
    public static Color ColorAt(float fraction) => fraction > 0.5f ? Healthy : fraction > 0.25f ? Hurt : Failing;

    private void Draw(EntityId entity, Basis camera, float delta)
    {
        if (!View.Match.Health.TryGet(entity, out var health)
            || health.Maximum <= 0
            || !View.World.TryGetDrawnPosition(entity, out var position))
        {
            return;
        }

        var fraction = Mathf.Clamp(health.HitPoints / (float)health.Maximum, 0f, 1f);
        var drawn = _drawn.TryGetValue(entity, out var before) ? Mathf.Max(fraction, before - (TrailPerSecond * delta)) : fraction;
        if (fraction >= 1f && !View.Selection.Contains(entity))
        {
            _drawn.Remove(entity);
            return;
        }

        _drawn[entity] = drawn;
        BarCount++;

        var middle = position + (Vector3.Up * (PlaceholderMeshes.UnitHeight + Lift));
        AddQuad(camera, middle, -Edge, Width + Edge, Height + (2f * Edge), Back);
        if (drawn > fraction)
        {
            AddQuad(camera, middle, 0f, Width * drawn, Height, Trail);
        }

        if (fraction > 0f)
        {
            AddQuad(camera, middle, 0f, Width * fraction, Height, ColorAt(fraction));
        }
    }

    // A quad that faces the camera, from one distance to another along the bar, measured from its left end.
    private void AddQuad(Basis camera, Vector3 middle, float from, float to, float height, Color color)
    {
        var left = middle - (camera.X * (Width / 2f));
        var center = left + (camera.X * ((from + to) / 2f));
        _quads.Add(new Transform3D(new Basis(camera.X * (to - from), camera.Y * height, camera.Z), center), color);
    }
}
