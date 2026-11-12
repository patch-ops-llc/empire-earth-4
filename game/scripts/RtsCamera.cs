using Godot;

namespace Game;

/// <summary>
/// Fixed-pitch strategy camera. This node sits on the ground at the point the camera looks at, and
/// its child <see cref="Camera3D"/> hangs behind and above it. Panning moves this node, zooming
/// changes the distance, and the pitch and heading never change.
/// </summary>
/// <remarks>
/// One world unit is one simulation tile. Simulation x is world X and simulation y is world Z, so
/// the top of the screen is the low-y side of the map.
/// </remarks>
public partial class RtsCamera : Node3D
{
    private Camera3D? _camera;
    private Vector2 _focus;
    private float _distance;
    private float _targetDistance;
    private bool _pointerInWindow;

    /// <summary>Angle between the ground and the line of sight, in degrees.</summary>
    [Export(PropertyHint.Range, "20,89,1")]
    public float PitchDegrees { get; set; } = 55f;

    [Export(PropertyHint.Range, "1,200,0.5")]
    public float MinDistance { get; set; } = 12f;

    [Export(PropertyHint.Range, "1,200,0.5")]
    public float MaxDistance { get; set; } = 80f;

    [Export(PropertyHint.Range, "1,200,0.5")]
    public float StartDistance { get; set; } = 36f;

    /// <summary>Pan speed in tiles per second for each tile of camera distance, so that the ground
    /// crosses the screen at the same rate at every zoom level.</summary>
    [Export]
    public float PanSpeedPerDistance { get; set; } = 0.7f;

    /// <summary>Distance multiplier for one notch of the mouse wheel.</summary>
    [Export(PropertyHint.Range, "1.01,2,0.01")]
    public float ZoomStep { get; set; } = 1.15f;

    /// <summary>Distance multiplier per second while a zoom key is held.</summary>
    [Export(PropertyHint.Range, "1.1,8,0.1")]
    public float KeyZoomRate { get; set; } = 2.5f;

    /// <summary>How quickly the distance follows its target. Higher is snappier.</summary>
    [Export]
    public float ZoomSmoothing { get; set; } = 12f;

    [Export]
    public bool EdgeScrollEnabled { get; set; } = true;

    /// <summary>Width in viewport pixels of the border that scrolls the camera.</summary>
    [Export]
    public float EdgeScrollMargin { get; set; } = 16f;

    /// <summary>Area the focus point must stay inside, in tiles.</summary>
    public Rect2 Bounds { get; private set; } = new(0f, 0f, 96f, 96f);

    /// <summary>The ground point at the center of the view, in tiles.</summary>
    public Vector2 Focus => _focus;

    /// <summary>Current distance from the camera to the focus point, in tiles.</summary>
    public float Distance => _distance;

    /// <summary>Sets the area the camera may look at and centers the view on it.</summary>
    public void SetBounds(Rect2 bounds)
    {
        Bounds = bounds;
        _focus = bounds.GetCenter();
        ApplyTransform();
    }

    /// <summary>Moves the view so that a ground point, in tiles, is at its center.</summary>
    public void CenterOn(Vector2 point)
    {
        _focus = point.Clamp(Bounds.Position, Bounds.End);
        ApplyTransform();
    }

    /// <summary>Puts the camera at a distance from the focus point at once, with no glide.</summary>
    public void ZoomTo(float distance)
    {
        _distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        _targetDistance = _distance;
        ApplyTransform();
    }

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera");
        _focus = Bounds.GetCenter();
        _distance = Mathf.Clamp(StartDistance, MinDistance, MaxDistance);
        _targetDistance = _distance;
        ApplyTransform();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMMouseExit || what == NotificationApplicationFocusOut)
        {
            _pointerInWindow = false;
        }
    }

    public override void _Input(InputEvent @event)
    {
        // The pointer counts as inside the window only after it has moved there. A position left
        // over from before it entered, or from after it left, must not scroll the camera.
        if (@event is InputEventMouseMotion)
        {
            _pointerInWindow = true;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _targetDistance /= ZoomStep;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _targetDistance *= ZoomStep;
                break;
            // Trackpads report a pinch and a two-finger scroll as gestures, not as wheel buttons.
            case InputEventMagnifyGesture magnify when magnify.Factor > 0f:
                _targetDistance /= magnify.Factor;
                break;
            case InputEventPanGesture pan:
                _targetDistance *= Mathf.Pow(ZoomStep, pan.Delta.Y);
                break;
            default:
                return;
        }

        _targetDistance = Mathf.Clamp(_targetDistance, MinDistance, MaxDistance);
    }

    public override void _Process(double delta)
    {
        var seconds = (float)delta;

        var zoomKeys = Input.GetAxis("camera_zoom_in", "camera_zoom_out");
        _targetDistance = Mathf.Clamp(_targetDistance * Mathf.Pow(KeyZoomRate, zoomKeys * seconds), MinDistance, MaxDistance);
        _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-ZoomSmoothing * seconds));

        var direction = Input.GetVector("camera_pan_left", "camera_pan_right", "camera_pan_up", "camera_pan_down")
            + EdgeScrollDirection();
        _focus += direction.LimitLength(1f) * PanSpeedPerDistance * _distance * seconds;
        _focus = _focus.Clamp(Bounds.Position, Bounds.End);

        ApplyTransform();
    }

    private Vector2 EdgeScrollDirection()
    {
        if (!EdgeScrollEnabled || !_pointerInWindow)
        {
            return Vector2.Zero;
        }

        var viewport = GetViewport();
        var size = viewport.GetVisibleRect().Size;
        var pointer = viewport.GetMousePosition();
        var direction = Vector2.Zero;

        if (pointer.X < EdgeScrollMargin)
        {
            direction.X = -1f;
        }
        else if (pointer.X > size.X - EdgeScrollMargin)
        {
            direction.X = 1f;
        }

        if (pointer.Y < EdgeScrollMargin)
        {
            direction.Y = -1f;
        }
        else if (pointer.Y > size.Y - EdgeScrollMargin)
        {
            direction.Y = 1f;
        }

        return direction;
    }

    private void ApplyTransform()
    {
        if (_camera is null)
        {
            return;
        }

        var pitch = Mathf.DegToRad(PitchDegrees);
        Position = new Vector3(_focus.X, 0f, _focus.Y);
        _camera.Position = new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)) * _distance;
        _camera.Rotation = new Vector3(-pitch, 0f, 0f);
    }
}
