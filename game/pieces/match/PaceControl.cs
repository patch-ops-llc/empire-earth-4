using Godot;

namespace Game;

/// <summary>
/// Pause and speed (design: they are the host's). On the machine that sets the pace, the one
/// that hosts the match or plays it alone, a row of buttons under the top bar at the right
/// pauses the match and sets its speed. On every machine a banner says while the match is
/// paused, and a line says when its speed changes.
/// </summary>
/// <remarks>
/// It changes nothing in the match: the pace is the time between two ticks, which the host's
/// side of the session sets and tells every client (<see cref="MatchRun.SetPaused"/>,
/// <see cref="MatchRun.SetTickInterval"/>). What it shows it reads from the local player's side,
/// <c>View.Client</c>, so the host sees what every guest sees.
/// </remarks>
public partial class PaceControl : InterfacePart
{
    /// <summary>Pauses the match and lets it go on. Named in the input map of project.godot.</summary>
    public static readonly StringName PauseAction = "match_pause";

    /// <summary>Sets the next speed up. Named in the input map of project.godot.</summary>
    public static readonly StringName FasterAction = "match_faster";

    /// <summary>Sets the next speed down. Named in the input map of project.godot.</summary>
    public static readonly StringName SlowerAction = "match_slower";

    private const float Margin = 8f;

    private readonly List<Button> _speeds = [];
    private PanelContainer _box = null!;
    private Button _pause = null!;
    private Label _speed = null!;
    private Label _banner = null!;
    private TimeSpan _interval;
    private bool _paused;

    /// <summary>True while the banner says that the match is paused.</summary>
    public bool ShowsPaused => _banner.Visible;

    /// <summary>The words of the banner.</summary>
    public string BannerText => _banner.Text;

    /// <summary>The speed as this part words it: "normal speed", "double speed".</summary>
    public string SpeedText => MatchSpeed.NameOf(View.Client.TickInterval);

    /// <summary>Pauses the match or lets it go on, as the button does. Only the host's machine can.</summary>
    public void TogglePause()
    {
        if (!View.Run.CanSetPace)
        {
            Frame.Say("Only the host can pause the match.");
            return;
        }

        if (!View.Run.SetPaused(!View.Client.IsPaused))
        {
            Frame.Say("The match is over, so it cannot be paused.");
        }
    }

    /// <summary>Sets the speed of the match, as a button does. Only the host's machine can.</summary>
    /// <param name="factor">How many times normal speed.</param>
    public void SetSpeed(double factor)
    {
        if (!View.Run.CanSetPace)
        {
            Frame.Say("Only the host can set the speed of the match.");
            return;
        }

        View.Run.SetTickInterval(MatchSpeed.IntervalOf(factor));
    }

    protected override void Attach()
    {
        Build();
        _interval = View.Client.TickInterval;
        _paused = View.Client.IsPaused;
        View.Client.PaceChanged += OnPaceChanged;

        // A guest is shown the match before it starts, and learns the pace when it does.
        View.Client.Started += () =>
        {
            _interval = View.Client.TickInterval;
            Refresh();
        };
        Refresh();
    }

    public override void EveryFrame(double delta)
    {
        // Under the message line, so that neither covers the other.
        var top = Frame.MessagesEnd + Margin;
        _box.Position = new Vector2(_box.GetViewportRect().Size.X - _box.Size.X - Margin, top);
        _banner.Position = new Vector2((_banner.GetViewportRect().Size.X - _banner.Size.X) / 2f, top);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(PauseAction))
        {
            TogglePause();
        }
        else if (@event.IsActionPressed(FasterAction) && View.Run.CanSetPace)
        {
            Step(1);
        }
        else if (@event.IsActionPressed(SlowerAction) && View.Run.CanSetPace)
        {
            Step(-1);
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    // The next speed on offer above or below the one the match has.
    private void Step(int by)
    {
        var now = MatchSpeed.FactorOf(View.Client.TickInterval);
        var speeds = MatchSpeed.All;
        if (by > 0)
        {
            foreach (var (factor, _) in speeds)
            {
                if (factor > now)
                {
                    SetSpeed(factor);
                    return;
                }
            }
        }
        else
        {
            for (var i = speeds.Count - 1; i >= 0; i--)
            {
                if (speeds[i].Factor < now)
                {
                    SetSpeed(speeds[i].Factor);
                    return;
                }
            }
        }
    }

    private void OnPaceChanged()
    {
        var client = View.Client;
        if (client.TickInterval != _interval)
        {
            _interval = client.TickInterval;
            Frame.Say(View.Run.CanSetPace
                ? $"The match runs at {SpeedText}."
                : $"The host set the match to {SpeedText}.");
        }

        if (client.IsPaused != _paused)
        {
            _paused = client.IsPaused;
            if (!_paused)
            {
                Frame.Say("The match goes on.", seconds: 3.0);
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        var client = View.Client;
        var host = View.Run.CanSetPace;
        var key = InterfaceFrame.KeyOf(PauseAction);

        _banner.Visible = client.IsPaused;
        _banner.Text = host
            ? (key is null ? "Paused" : $"Paused. Press {key} to go on.")
            : "The host has paused the match.";

        _pause.Text = (client.IsPaused ? "Go on" : "Pause") + (key is null ? string.Empty : $" ({key})");
        for (var i = 0; i < _speeds.Count; i++)
        {
            // The speed the match has is the button that cannot be pressed.
            _speeds[i].Disabled = MatchSpeed.Is(client.TickInterval, MatchSpeed.All[i].Factor);
        }

        // A guest is shown the speed while it is not the normal one, and has no buttons.
        var normal = client.TickInterval <= TimeSpan.Zero || MatchSpeed.Is(client.TickInterval, 1.0);
        _speed.Text = host ? "Speed" : char.ToUpperInvariant(SpeedText[0]) + SpeedText[1..];
        _box.Visible = host || !normal;
        _box.ResetSize();
    }

    private void Build()
    {
        // Controls of this part's own, over the world: the frame has no place under the bar.
        var root = new Control { Name = "Pace", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _box = new PanelContainer { Name = "PaceBox" };
        _box.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(6f));
        root.AddChild(_box);
        var row = UiStyle.Row(6);
        _box.AddChild(row);

        var host = View.Run.CanSetPace;
        _pause = UiStyle.FrameButton("Pause");
        _pause.Name = "PauseButton";
        _pause.TooltipText = "Pause the match for everyone, and let it go on";
        _pause.Visible = host;
        _pause.Pressed += TogglePause;
        row.AddChild(_pause);

        _speed = new Label { Name = "SpeedText" };
        row.AddChild(_speed);
        foreach (var (factor, name) in MatchSpeed.All)
        {
            var button = UiStyle.FrameButton(MatchSpeed.Mark(factor));
            button.TooltipText = char.ToUpperInvariant(name[0]) + name[1..];
            button.Visible = host;
            button.Pressed += () => SetSpeed(factor);
            _speeds.Add(button);
            row.AddChild(button);
        }

        _banner = new Label { Name = "PausedBanner", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _banner.AddThemeFontSizeOverride("font_size", 22);
        _banner.AddThemeStyleboxOverride("normal", UiStyle.PanelStyle(10f));
        root.AddChild(_banner);
    }
}
