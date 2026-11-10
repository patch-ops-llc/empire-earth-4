using System.Globalization;
using Godot;
using Sim;

namespace Game;

/// <summary>
/// Corner readout of frame time, the simulation tick and the camera. The debug_toggle_overlay
/// action shows and hides it.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    private const double RefreshSeconds = 0.25;

    private Label _label = null!;
    private double _sinceRefresh;
    private double _worstFrame;
    private int _frames;
    private double _sincePlaceholderTick;
    private long _placeholderTick;

    /// <summary>Camera whose focus point and distance are shown. Optional.</summary>
    [Export]
    public RtsCamera? Camera { get; set; }

    public override void _Ready()
    {
        _label = GetNode<Label>("%Readout");
        Refresh(0.0, 0.0);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("debug_toggle_overlay"))
        {
            Visible = !Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        AdvancePlaceholderTick(delta);

        _frames++;
        _sinceRefresh += delta;
        _worstFrame = Math.Max(_worstFrame, delta);
        if (_sinceRefresh < RefreshSeconds)
        {
            return;
        }

        Refresh(_sinceRefresh / _frames, _worstFrame);
        _frames = 0;
        _sinceRefresh = 0.0;
        _worstFrame = 0.0;
    }

    // No simulation runs yet. Until the world view drives real ticks, this counts at the
    // simulation's rate so the readout has its final shape.
    private void AdvancePlaceholderTick(double delta)
    {
        const double tickSeconds = 1.0 / SimInfo.TicksPerSecond;

        _sincePlaceholderTick += delta;
        while (_sincePlaceholderTick >= tickSeconds)
        {
            _sincePlaceholderTick -= tickSeconds;
            _placeholderTick++;
        }
    }

    private void Refresh(double averageFrame, double worstFrame)
    {
        var framesPerSecond = averageFrame > 0.0 ? 1.0 / averageFrame : 0.0;
        var text = string.Create(CultureInfo.InvariantCulture,
            $"Frame {averageFrame * 1000.0:F1} ms avg, {worstFrame * 1000.0:F1} ms worst ({framesPerSecond:F0} fps)\n" +
            $"Tick {_placeholderTick} (placeholder, {SimInfo.TicksPerSecond} per second)");

        if (Camera is not null)
        {
            text += string.Create(CultureInfo.InvariantCulture,
                $"\nCamera at tile {Camera.Focus.X:F1}, {Camera.Focus.Y:F1}, distance {Camera.Distance:F1}");
        }

        _label.Text = text;
    }
}
