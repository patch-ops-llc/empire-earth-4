using Godot;

namespace Game;

/// <summary>
/// The mark on the ground that answers an order: a ring that closes on the point the player
/// ordered and then goes away. It stays at that point. Where each unit ends up can differ from
/// it, since each unit of a group has a destination of its own.
/// </summary>
public partial class OrderMarker : WorldLayer
{
    private const double Seconds = 1.2;
    private const float Lift = 0.05f;

    private MeshInstance3D _ring = null!;
    private StandardMaterial3D _material = null!;
    private double _left;

    protected override void Attach()
    {
        var mesh = PlaceholderMeshes.Ring(0.5f, Colors.White);
        _material = (StandardMaterial3D)mesh.Material;
        _ring = new MeshInstance3D { Name = "Ring", Mesh = mesh, Visible = false };
        AddChild(_ring);
    }

    /// <summary>Shows the mark at a point on the ground, from its beginning, in the color of the order.</summary>
    public void ShowAt(Vector3 point, Color color)
    {
        _material.AlbedoColor = color;
        _ring.Position = point + (Vector3.Up * Lift);
        _left = Seconds;
        _ring.Visible = true;
        Shrink();
    }

    public override void EveryFrame(double delta)
    {
        if (!_ring.Visible)
        {
            return;
        }

        _left -= delta;
        if (_left <= 0.0)
        {
            _ring.Visible = false;
            return;
        }

        Shrink();
    }

    // From twice its size to half of it, and flat all the way.
    private void Shrink()
    {
        var size = Mathf.Lerp(0.5f, 2f, (float)(_left / Seconds));
        _ring.Scale = new Vector3(size, 0.25f, size);
    }
}
