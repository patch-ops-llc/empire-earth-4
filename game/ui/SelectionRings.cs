using Godot;

namespace Game;

/// <summary>A ring on the ground under every selected unit. All rings are one node.</summary>
public partial class SelectionRings : MultiMeshInstance3D
{
    // Above the ground by enough not to flicker against it.
    private const float Lift = 0.04f;

    private static readonly Basis Flat = Basis.FromScale(new Vector3(1f, 0.25f, 1f));

    private MultiMesh _rings = null!;

    public override void _Ready()
    {
        _rings = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = PlaceholderMeshes.Ring(PlaceholderMeshes.UnitRadius, new Color(0.55f, 1f, 0.55f)),
            InstanceCount = 16,
            VisibleInstanceCount = 0,
        };
        Multimesh = _rings;
    }

    /// <summary>The area the rings can be in. Godot must be told, because the rings move every frame.</summary>
    public void SetArea(Vector2 size) => _rings.CustomAabb = new Aabb(Vector3.Zero, new Vector3(size.X, 1f, size.Y));

    /// <summary>Puts a ring at each of these points on the ground, and shows no other.</summary>
    public void Place(List<Vector3> points)
    {
        if (points.Count > _rings.InstanceCount)
        {
            // Changing the count throws the old rings away, which is fine: all are placed again below.
            _rings.InstanceCount = Math.Max(points.Count, _rings.InstanceCount * 2);
        }

        for (var i = 0; i < points.Count; i++)
        {
            _rings.SetInstanceTransform(i, new Transform3D(Flat, points[i] + (Vector3.Up * Lift)));
        }

        _rings.VisibleInstanceCount = points.Count;
    }
}
