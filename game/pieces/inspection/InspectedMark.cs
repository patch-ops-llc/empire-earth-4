using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// A ring on the ground under the unit of another player that the inspection panel is about, so
/// that the player sees which unit the panel describes. The player's own units have the rings of
/// the selection.
/// </summary>
public partial class InspectedMark : WorldLayer
{
    // Above the ground by enough not to flicker against it.
    private const float Lift = 0.04f;

    private static readonly Basis Flat = Basis.FromScale(new Vector3(1f, 0.25f, 1f));

    private MeshInstance3D _ring = null!;

    /// <summary>The unit the ring is under, or <see cref="EntityId.None"/> for no ring.</summary>
    public EntityId Unit { get; set; }

    protected override void Attach()
    {
        _ring = new MeshInstance3D
        {
            Name = "Ring",
            Mesh = PlaceholderMeshes.Ring(PlaceholderMeshes.UnitRadius + 0.1f, new Color(1f, 0.95f, 0.85f)),
            Visible = false,
        };
        AddChild(_ring);
    }

    public override void EveryFrame(double delta)
    {
        if (Unit != EntityId.None && View.World.TryGetDrawnPosition(Unit, out var position))
        {
            _ring.Transform = new Transform3D(Flat, position + (Vector3.Up * Lift));
            _ring.Visible = true;
        }
        else
        {
            _ring.Visible = false;
        }
    }
}
