using System.Globalization;
using Godot;
using Sim;
using Sim.Setup;

namespace Game;

/// <summary>
/// Readout of frame time, the simulation tick, the camera and the number of selected units. The
/// debug_toggle_overlay action shows and hides it. Before there is a match it stands in the
/// bottom left corner, under the pages of the menu. In a match it stands where the frame of the
/// interface keeps room for what a developer reads, so that no panel of the game covers it:
/// <see cref="StandIn"/>.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    private const double RefreshSeconds = 0.25;

    // The lines are short enough for the column of the interface's frame that holds the readout.
    private const int FontSize = 13;

    private Control _panel = null!;
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
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _panel = _label.GetParent<Control>();
        Refresh(0.0, 0.0);
    }

    /// <summary>True while the readout is shown.</summary>
    public bool IsShown => _panel.Visible;

    /// <summary>
    /// Moves the readout into a place of the interface's frame, which then decides where it
    /// stands. The readout goes with that place when the match is left.
    /// </summary>
    public void StandIn(Container place)
    {
        _panel.GetParent().RemoveChild(_panel);
        place.AddChild(_panel);

        // A line that is longer than the place is wide is written on two.
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("debug_toggle_overlay"))
        {
            _panel.Visible = !_panel.Visible;
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
            $"{framesPerSecond:F0} fps: {averageFrame * 1000.0:F1} ms, worst {worstFrame * 1000.0:F1} ms\n" +
            $"Tick {Match?.World.Tick.ToString(CultureInfo.InvariantCulture) ?? "-"} ({SimInfo.TicksPerSecond} per second)");

        if (Camera is not null)
        {
            text += string.Create(CultureInfo.InvariantCulture,
                $"\nCamera at {Camera.Focus.X:F1}, {Camera.Focus.Y:F1}, distance {Camera.Distance:F1}");
        }

        if (Selection is not null)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"\nSelected {Selection.Count}");
        }

        _label.Text = text;
    }
}
