using Godot;

namespace Game;

/// <summary>
/// A ring on the ground under every selected unit. All rings are one node. This is a layer of the
/// world view, which calls it once the units have been placed for the frame, so the rings go
/// exactly under them.
/// </summary>
public partial class SelectionRings : WorldLayer
{
    // Above the ground by enough not to flicker against it.
    private const float Lift = 0.04f;

    private static readonly Basis Flat = Basis.FromScale(new Vector3(1f, 0.25f, 1f));

    private MultiMesh _rings = null!;

    protected override void Attach()
    {
        _rings = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = PlaceholderMeshes.Ring(PlaceholderMeshes.UnitRadius, new Color(0.55f, 1f, 0.55f)),
            InstanceCount = 16,
            VisibleInstanceCount = 0,
        };

        // Godot must be told the area the rings can be in, because they move every frame.
        var map = View.Match.Map;
        _rings.CustomAabb = new Aabb(Vector3.Zero, new Vector3(map.Width, 1f, map.Height));
        AddChild(new MultiMeshInstance3D { Name = "Rings", Multimesh = _rings });
    }

    public override void EveryFrame(double delta)
    {
        var selected = View.Selection.Entities;
        if (selected.Count > _rings.InstanceCount)
        {
            // Changing the count throws the old rings away, which is fine: all are placed again below.
            _rings.InstanceCount = Math.Max(selected.Count, _rings.InstanceCount * 2);
        }

        var placed = 0;
        foreach (var entity in selected)
        {
            if (View.World.TryGetDrawnPosition(entity, out var position))
            {
                _rings.SetInstanceTransform(placed++, new Transform3D(Flat, position + (Vector3.Up * Lift)));
            }
        }

        _rings.VisibleInstanceCount = placed;
    }
}
