using Godot;
using Net;
using Sim.Core;
using Sim.Outcome;

namespace Game;

/// <summary>
/// How a match ends, for the player (design 5.1): the screen that says victory or defeat and why,
/// and the question that is asked before the player leaves or concedes. The Leave button of the
/// top bar opens it.
/// </summary>
/// <remarks>
/// <para>
/// It reads the standings of the match after every tick, so it says the same on every machine
/// and whatever took a player out. The result comes up by itself once: when the local player is
/// out of the match, or has won it. The match goes on behind it for as long as the host keeps
/// it, and the player can close the screen and watch, and open it again with Leave.
/// </para>
/// <para>
/// While the player is still in an undecided match, Leave asks first, because leaving concedes.
/// Conceding is a command, <see cref="CommandType.Concede"/>, given through the session like
/// any order. Leaving needs none: the host concedes for a player who is gone.
/// </para>
/// <para>
/// The screen is drawn on a layer of its own over the interface, so that nothing a part shows
/// lies over it, and under the menu.
/// </para>
/// </remarks>
public partial class EndScreen : InterfacePart
{
    /// <summary>Opens the screen, as the Leave button does. Named in the input map of project.godot.</summary>
    public static readonly StringName OpenAction = "match_leave";

    private static readonly Color VictoryColor = new(0.55f, 0.9f, 0.6f);
    private static readonly Color NeutralColor = new(0.85f, 0.87f, 0.92f);

    private readonly Dictionary<int, SessionEndReason> _left = [];
    private PlayerStanding[] _before = [];
    private CanvasLayer _layer = null!;
    private Label _title = null!;
    private Label _text = null!;
    private VBoxContainer _players = null!;
    private Label _length = null!;
    private Button _concede = null!;
    private Button _leave = null!;
    private Button _stay = null!;

    // The headline the screen has come up for by itself, so that it does so once.
    private string? _announced;
    private bool _conceding;

    /// <summary>True while the screen is up.</summary>
    public bool IsOpen => _layer.Visible;

    /// <summary>What the screen says now, or would say if it were up.</summary>
    public EndReading Reading { get; private set; } = EndReading.Of(new Standings(1), 0, 0);

    /// <summary>Opens the screen: on the result when there is one for the player, and on the question otherwise.</summary>
    public void Open()
    {
        Refresh();
        _layer.Visible = true;
    }

    public void Close() => _layer.Visible = false;

    /// <summary>
    /// Concedes the match for the player, as the screen's button does. The order is carried out
    /// a few ticks later, like any other, and the result comes up then.
    /// </summary>
    public void Concede()
    {
        if (!View.Match.Standings.IsPlaying(View.Player) || View.Match.Standings.IsDecided)
        {
            return;
        }

        if (View.Client.Submit(new Command(CommandType.Concede, View.Player)))
        {
            _conceding = true;
            Close();
            if (View.Client.IsPaused)
            {
                Frame.Say("The match is paused. You concede as soon as it goes on.", seconds: 10.0);
            }
        }
        else
        {
            Frame.Say("The match is over, so there is nothing to concede.");
        }
    }

    protected override void Attach()
    {
        var standings = View.Match.Standings;
        _before = [.. Enumerable.Range(0, standings.PlayerCount).Select(standings.Of)];
        View.Client.PlayerLeft += (player, reason) => _left[player] = reason;
        Build();
    }

    public override void AfterTick()
    {
        var standings = View.Match.Standings;
        var changed = false;
        for (var seat = 0; seat < _before.Length; seat++)
        {
            var now = standings.Of(seat);
            if (now == _before[seat])
            {
                continue;
            }

            _before[seat] = now;
            changed = true;

            // In a match that goes on, another player's going out is a line under the top bar.
            // One who left has been named there already, and the last one is on the screen.
            if (seat != View.Player && !standings.IsDecided && !_left.ContainsKey(seat))
            {
                Frame.Say(OutcomeWords.WentOut(seat, now), seconds: 12.0);
            }
        }

        if (!changed && !IsOpen)
        {
            return;
        }

        Refresh();
        if (Reading.Headline is { } headline && headline != _announced)
        {
            _announced = headline;
            _conceding = false;
            _layer.Visible = true;
        }
    }

    public override void _Input(InputEvent @event)
    {
        // The screen takes the key that closes it before anything under it does.
        if (IsOpen && @event.IsActionPressed(InterfaceFrame.ClosePanelAction))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen && @event.IsActionPressed(OpenAction))
        {
            Open();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Refresh()
    {
        var standings = View.Match.Standings;
        Reading = EndReading.Of(standings, View.Player, View.Match.World.Tick, seat => _left.TryGetValue(seat, out var reason) ? reason : null);

        var decided = Reading.Headline is not null;
        _title.Text = Reading.Headline ?? "Leave the match?";
        _title.AddThemeColorOverride("font_color", Reading.Headline switch
        {
            OutcomeWords.Victory => VictoryColor,
            OutcomeWords.Defeat => UiStyle.Warning,
            _ => NeutralColor,
        });
        _text.Text = decided ? Reading.Why : Question();

        foreach (var child in _players.GetChildren())
        {
            _players.RemoveChild(child);
            child.QueueFree();
        }

        for (var seat = 0; seat < Reading.Players.Count; seat++)
        {
            var line = new Label { Text = Reading.Players[seat] };
            line.AddThemeColorOverride("font_color", PlayerColors.Of(seat).Lightened(0.3f));
            _players.AddChild(line);
        }

        _length.Text = Reading.Length;
        _concede.Visible = !decided;
        _concede.Disabled = _conceding;
        _concede.Text = _conceding ? "Conceding" : "Concede and watch";
        _leave.Visible = View.Leave is not null;
        _leave.Text = decided ? "Back to the menu" : "Concede and leave";
        _stay.Text = decided ? "Keep watching" : "Stay in the match";
    }

    private string Question()
    {
        var standings = View.Match.Standings;
        var others = Enumerable.Range(0, standings.PlayerCount).Where(seat => seat != View.Player && standings.IsPlaying(seat)).ToArray();
        var wins = others.Length == 1
            ? $"{MatchPanel.PlayerName(others[0])} wins"
            : "the others play on";
        var host = View.Run is HostRun
            ? " You host this match, so it ends for everyone when you leave."
            : string.Empty;
        return $"Leaving concedes the match: you give no more orders, and {wins}. You can concede and watch the rest, or leave now.{host}";
    }

    private void Build()
    {
        _layer = new CanvasLayer { Name = "EndScreenLayer", Layer = 20, Visible = false };
        AddChild(_layer);

        // The shade takes every click, so that nothing behind the screen is selected or ordered.
        var shade = new ColorRect { Name = "Shade", Color = new Color(0f, 0f, 0f, 0.5f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(shade);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shade.AddChild(center);

        var box = new PanelContainer { Name = "EndScreen", CustomMinimumSize = new Vector2(460f, 0f) };
        box.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(20f));
        center.AddChild(box);

        var stack = UiStyle.Column(12);
        box.AddChild(stack);
        _title = UiStyle.Title(string.Empty);
        _title.Name = "Headline";
        _title.AddThemeFontSizeOverride("font_size", 34);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.AddChild(_title);
        _text = UiStyle.Text(string.Empty);
        _text.CustomMinimumSize = new Vector2(420f, 0f);
        stack.AddChild(_text);
        stack.AddChild(new HSeparator());
        _players = UiStyle.Column(2);
        _players.Name = "Players";
        stack.AddChild(_players);
        _length = new Label { Name = "Length" };
        _length.AddThemeColorOverride("font_color", new Color(0.65f, 0.68f, 0.74f));
        stack.AddChild(_length);

        var row = UiStyle.Row();
        stack.AddChild(row);
        _concede = Wide(UiStyle.FrameButton(string.Empty), "ConcedeButton");
        _concede.TooltipText = "Give the match up and go on watching it";
        _concede.Pressed += Concede;
        row.AddChild(_concede);
        _leave = Wide(UiStyle.FrameButton(string.Empty), "LeaveButton");
        _leave.Pressed += () => View.Leave?.Invoke();
        row.AddChild(_leave);
        _stay = Wide(UiStyle.FrameButton(string.Empty), "StayButton");
        _stay.Pressed += Close;
        row.AddChild(_stay);
    }

    private static Button Wide(Button button, string name)
    {
        button.Name = name;
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return button;
    }
}

/// <summary>What the end screen says about a match for one player.</summary>
/// <param name="Headline">Victory, defeat or no winner, or null while the player is in an undecided match.</param>
/// <param name="Why">The sentence under the headline. Empty while there is no headline.</param>
/// <param name="Players">A line for each player, in the order of the seats: who it is and how it stands.</param>
/// <param name="Length">How long the match has run, or ran until it was decided.</param>
public sealed record EndReading(string? Headline, string Why, IReadOnlyList<string> Players, string Length)
{
    /// <summary>Reads the standings of a match for one player.</summary>
    /// <param name="standings">The standings.</param>
    /// <param name="player">The player the screen is for.</param>
    /// <param name="tick">The tick the match is at.</param>
    /// <param name="left">Why a seat's player is gone from the session, or null when it is not.</param>
    public static EndReading Of(Standings standings, int player, int tick, Func<int, SessionEndReason?>? left = null)
    {
        var players = new string[standings.PlayerCount];
        for (var seat = 0; seat < players.Length; seat++)
        {
            var you = seat == player ? " (you)" : string.Empty;
            players[seat] = $"{MatchPanel.PlayerName(seat)}, {PlayerColors.NameOf(seat)}{you}: {OutcomeWords.Standing(standings, seat)}";
        }

        return new EndReading(
            OutcomeWords.Headline(standings, player),
            OutcomeWords.Why(standings, player, left),
            players,
            OutcomeWords.Length(standings, tick));
    }
}
