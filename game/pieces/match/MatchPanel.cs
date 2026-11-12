using System.Globalization;
using Godot;
using Net;

namespace Game;

/// <summary>
/// The right of the top bar: which player this is, how the match is played, who has left it, and
/// the button that leaves it, which opens the <see cref="EndScreen"/>.
/// </summary>
/// <remarks>
/// The sentences give way to everything else in the top bar, a whole sentence at a time and
/// never in the middle of one, so that the buttons of the bar stay in the window however many
/// parts fill it. What is said first, with the room there is: everything; then without how the
/// match is played; then who has left alone, in full and then in short; then who the player is;
/// then nothing. Having given way they do not come back until the window is wider or a player
/// leaves.
/// </remarks>
public partial class MatchPanel : InterfacePart
{
    private readonly List<(int Player, SessionEndReason Reason)> _left = [];

    // What the bar can say, from the longest to the shortest, each with its width.
    private readonly List<(string Text, float Width)> _forms = [];
    private Label _text = null!;

    // The first of the forms that has had room, in a window of this width.
    private int _shortest;
    private float _window;
    private bool _out;

    protected override void Attach()
    {
        _text = new Label { Name = "MatchText" };
        Frame.TopRight.AddChild(_text);

        if (View.Leave is { } leave)
        {
            // One word, because the bar is short of room. The sentence beside it names the match.
            var button = UiStyle.FrameButton("Leave");
            button.Name = "LeaveButton";
            button.TooltipText = "Leave the match and go back to the menu, or concede it. Leaving concedes, so it asks first";
            button.Pressed += () =>
            {
                // The end screen asks first, since leaving concedes (design 5.1). A match shown
                // without it is left at once.
                if (Frame.Parts.OfType<EndScreen>().FirstOrDefault() is { } screen)
                {
                    screen.Open();
                }
                else
                {
                    leave();
                }
            };
            Frame.TopRight.AddChild(button);
        }

        View.Client.PlayerLeft += (player, reason) =>
        {
            _left.Add((player, reason));
            Frame.Say(LeftSentence(player, reason), seconds: 12.0);
            Refresh();
        };
        Refresh();
    }

    public override void AfterTick()
    {
        // A player who is out of the match gives no more orders, and the bar says so.
        var isOut = !View.Match.Standings.IsPlaying(View.Player);
        if (isOut != _out)
        {
            _out = isOut;
            Refresh();
        }
    }

    public override void EveryFrame(double delta)
    {
        // Having given way, the sentences stay as short for as long as the window is as wide,
        // so that they do not come and go with what the other parts of the bar show.
        var window = _text.GetViewportRect().Size.X;
        if (!Mathf.IsEqualApprox(window, _window))
        {
            _window = window;
            _shortest = 0;
        }

        var room = Frame.TopBarRoomFor(_text);
        while (_shortest < _forms.Count && _forms[_shortest].Width > room)
        {
            _shortest++;
        }

        var text = _shortest < _forms.Count ? _forms[_shortest].Text : string.Empty;
        if (_text.Text != text)
        {
            _text.Text = text;
        }
    }

    /// <summary>A player as a sentence names it: seats count from 1.</summary>
    public static string PlayerName(int seat) => string.Create(CultureInfo.InvariantCulture, $"Player {seat + 1}");

    private void Refresh()
    {
        var seat = View.Player;
        var who = _out
            ? $"{PlayerName(seat)}, {PlayerColors.NameOf(seat)}, out of the match."
            : $"{PlayerName(seat)}, {PlayerColors.NameOf(seat)}.";
        var how = View.Run switch
        {
            HostRun host => string.Create(CultureInfo.InvariantCulture, $"Hosting on port {host.Port}."),
            JoinRun join => $"Joined {join.HostAddress}.",
            _ => "Playing alone.",
        };

        _forms.Clear();
        _shortest = 0;
        if (_left.Count == 0)
        {
            AddForm($"{who} {how}");
        }
        else
        {
            var left = string.Join(' ', _left.Select(one => LeftSentence(one.Player, one.Reason)));
            AddForm($"{who} {how} {left}");
            AddForm($"{who} {left}");
            AddForm(left);
            AddForm(_left.Count == 1
                ? $"{PlayerName(_left[0].Player)} is out."
                : string.Create(CultureInfo.InvariantCulture, $"{_left.Count} players are out."));
        }

        AddForm(who);
    }

    private void AddForm(string text)
    {
        var size = _text.GetThemeFont("font").GetStringSize(text, fontSize: _text.GetThemeFontSize("font_size"));
        _forms.Add((text, Mathf.Ceil(size.X)));
    }

    private static string LeftSentence(int player, SessionEndReason reason) => $"{PlayerName(player)} {HowLeft(reason)}.";

    private static string HowLeft(SessionEndReason reason) => reason switch
    {
        SessionEndReason.Left => "left the match",
        SessionEndReason.ConnectionLost => "lost the connection",
        SessionEndReason.FellBehind => "fell too far behind and is out of the match",
        _ => "is out of the match",
    };
}
