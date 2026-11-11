using Godot;

namespace Game;

/// <summary>
/// The mark on the ground that answers a move order: a ring that closes on the place the units
/// were sent to and then goes away.
/// </summary>
public partial class OrderMarker : Node3D
{
    private const double Seconds = 1.2;
    private const float Lift = 0.05f;

    private double _left;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D { Mesh = PlaceholderMeshes.Ring(0.5f, new Color(1f, 0.95f, 0.4f)) });
        Visible = false;
    }

    /// <summary>Shows the mark at a point on the ground, from its beginning.</summary>
    public void ShowAt(Vector3 point)
    {
        MoveTo(point);
        _left = Seconds;
        Visible = true;
        Shrink();
    }

    /// <summary>Moves the mark, as far as it has got, to another point.</summary>
    public void MoveTo(Vector3 point) => Position = point + (Vector3.Up * Lift);

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _left -= delta;
        if (_left <= 0.0)
        {
            Visible = false;
            return;
        }

        Shrink();
    }

    // From twice its size to half of it, and flat all the way.
    private void Shrink()
    {
        var size = Mathf.Lerp(0.5f, 2f, (float)(_left / Seconds));
        Scale = new Vector3(size, 0.25f, size);
    }
}
