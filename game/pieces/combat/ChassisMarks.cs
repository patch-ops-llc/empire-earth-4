using Godot;
using Sim.Numerics;

namespace Game;

/// <summary>
/// A pale mark on top of every unit that can fight, which says how it fights while all units
/// share one stand-in shape: a cross for a unit that fights hand to hand, a ball for one that
/// shoots, and a ring for artillery, which cannot fire at what stands close to it. The mark is
/// larger on a unit of a line that keeps more room round it, as Cavalry does. A worker has none.
/// </summary>
/// <remarks>
/// <para>
/// The three marks differ in outline when seen from above, which is how the camera sees them.
/// </para>
/// <para>
/// A chassis that has a look of its own (<see cref="ChassisLookSet"/>) is not drawn in the
/// stand-in shape and carries no mark: its look says more. The marks stay for every chassis
/// the presentation data does not name yet.
/// </para>
/// </remarks>
public partial class ChassisMarks : WorldLayer
{
    private const float LargeScale = 1.4f;

    // How far above the top of the unit the middle of a mark is.
    private const float Lift = 0.09f;

    private static readonly Color Pale = new(0.93f, 0.92f, 0.86f);

    // The two arms of a cross: a bar along X, turned an eighth of a turn either way.
    private static readonly Vector3 Arm = new(0.6f, 0.1f, 0.12f);
    private static readonly Basis FirstArm = new Basis(Vector3.Up, Mathf.Pi / 4f) * Basis.FromScale(Arm);
    private static readonly Basis SecondArm = new Basis(Vector3.Up, -Mathf.Pi / 4f) * Basis.FromScale(Arm);

    private InstancedShapes _bars = null!;
    private InstancedShapes _balls = null!;
    private InstancedShapes _rings = null!;

    // For every line of the roster, whether its units keep more than a tile between their centers.
    private bool[] _large = [];
    private ChassisLookSet _looks = null!;

    /// <summary>The number of units that carried a mark in the last frame.</summary>
    public int Count { get; private set; }

    protected override void Attach()
    {
        var data = View.Match.Data;
        _looks = ChassisLookSet.For(data);
        _large = new bool[data.Rules.Lines.Count];
        foreach (var line in data.Lines)
        {
            _large[line.Index] = line.Base.Spacing.ToFraction() > Fraction.One;
        }

        var area = InstancedShapes.MapArea(View.Match.Map);
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f };

        _bars = InstancedShapes.Under(this, "Crosses", new BoxMesh { Size = Vector3.One }, material, area);
        _balls = InstancedShapes.Under(this, "Balls", new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 12, Rings = 6 }, material, area);
        _rings = InstancedShapes.Under(this, "Rings", new TorusMesh { InnerRadius = 0.13f, OuterRadius = 0.27f, Rings = 16, RingSegments = 6 }, material, area);
    }

    public override void EveryFrame(double delta)
    {
        _bars.Begin();
        _balls.Begin();
        _rings.Begin();
        Count = 0;

        var match = View.Match;
        foreach (var entity in View.World.Units)
        {
            if (!match.Combatants.TryGet(entity, out var unit)
                || _looks.Of(unit.Chassis) is not null
                || !match.Bodies.TryGet(entity, out var body)
                || !View.World.TryGetDrawnPosition(entity, out var position))
            {
                continue;
            }

            Count++;
            var profile = match.Combat.ProfileOf(body.Owner, unit.Chassis);
            var scale = Basis.FromScale(Vector3.One * (_large[profile.Line] ? LargeScale : 1f));
            var center = position + (Vector3.Up * (PlaceholderMeshes.UnitHeight + Lift));
            if (profile.Melee)
            {
                _bars.Add(new Transform3D(scale * FirstArm, center), Pale);
                _bars.Add(new Transform3D(scale * SecondArm, center), Pale);
            }
            else
            {
                var shapes = profile.MinRangeSquared > 0 ? _rings : _balls;
                shapes.Add(new Transform3D(scale, center), Pale);
            }
        }

        _bars.End();
        _balls.End();
        _rings.End();
    }
}
