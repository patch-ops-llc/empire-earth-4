using System.Globalization;
using Godot;
using Sim;
using Sim.Setup;

namespace Game;

/// <summary>
/// Readout of frame time, the simulation tick, the camera and the number of selected units, in
/// the bottom left corner, away from the top bar and the message line under it. The
/// debug_toggle_overlay action shows and hides it. It lies under the frame of the interface, so
/// a full-screen panel and the pages of the menu cover it.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    private const double RefreshSeconds = 0.25;

    private Label _label = null!;
    private double _sinceRefresh;
    private double _worstFrame;
    private int _frames;

    /// <summary>The match whose tick is shown. Until it is set the readout has no tick.</summary>
    public Match? Match { get; set; }

    /// <summary>The player's selection, whose size is shown. Optional.</summary>
    public Selection? Selection { get; set; }

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

    private void Refresh(double averageFrame, double worstFrame)
    {
        var framesPerSecond = averageFrame > 0.0 ? 1.0 / averageFrame : 0.0;
        var text = string.Create(CultureInfo.InvariantCulture,
            $"Frame {averageFrame * 1000.0:F1} ms avg, {worstFrame * 1000.0:F1} ms worst ({framesPerSecond:F0} fps)\n" +
            $"Tick {Match?.World.Tick.ToString(CultureInfo.InvariantCulture) ?? "-"} ({SimInfo.TicksPerSecond} per second)");

        if (Camera is not null)
        {
            text += string.Create(CultureInfo.InvariantCulture,
                $"\nCamera at tile {Camera.Focus.X:F1}, {Camera.Focus.Y:F1}, distance {Camera.Distance:F1}");
        }

        if (Selection is not null)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"\nSelected {Selection.Count}");
        }

        _label.Text = text;
    }
}
