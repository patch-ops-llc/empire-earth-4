using System.Globalization;
using Godot;
using Net;

namespace Game;

/// <summary>
/// The right of the top bar: which player this is, how the match is played, who has left it, and
/// the button that leaves it.
/// </summary>
public partial class MatchPanel : InterfacePart
{
    private readonly List<string> _left = [];
    private Label _text = null!;
    private float _textWidth;

    protected override void Attach()
    {
        // The sentence is cut short with an ellipsis when it is given less than its width.
        _text = new Label
        {
            Name = "MatchText",
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Frame.TopRight.AddChild(_text);

        if (View.Leave is { } leave)
        {
            var button = UiStyle.FrameButton("Leave the match");
            button.Name = "LeaveButton";
            button.Pressed += leave;
            Frame.TopRight.AddChild(button);
        }

        View.Client.PlayerLeft += (player, reason) =>
        {
            var sentence = $"{PlayerName(player)} {HowLeft(reason)}.";
            _left.Add(sentence);
            Frame.Say(sentence, seconds: 12.0);
            Refresh();
        };
        Refresh();
    }

    // The sentence gives way to everything else in the top bar: it has its whole width while
    // the bar has the room, and what is left when the other parts need it, so that the button
    // beside it never leaves the window.
    public override void EveryFrame(double delta)
    {
        var width = Mathf.Min(_textWidth, Mathf.Floor(Frame.TopBarRoomFor(_text)));
        if (!Mathf.IsEqualApprox(width, _text.CustomMinimumSize.X))
        {
            _text.CustomMinimumSize = new Vector2(width, 0f);
        }
    }

    /// <summary>A player as a sentence names it: seats count from 1.</summary>
    public static string PlayerName(int seat) => string.Create(CultureInfo.InvariantCulture, $"Player {seat + 1}");

    private void Refresh()
    {
        var seat = View.Player;
        var who = $"{PlayerName(seat)}, {PlayerColors.NameOf(seat)}.";
        var how = View.Run switch
        {
            HostRun host => string.Create(CultureInfo.InvariantCulture, $"Hosting on port {host.Port}."),
            JoinRun join => $"Joined {join.HostAddress}.",
            _ => "Playing alone.",
        };

        _text.Text = string.Join(' ', [who, how, .. _left]);
        _text.TooltipText = _text.Text;
        _textWidth = Mathf.Ceil(_text.GetThemeFont("font").GetStringSize(_text.Text, fontSize: _text.GetThemeFontSize("font_size")).X);
    }

    private static string HowLeft(SessionEndReason reason) => reason switch
    {
        SessionEndReason.Left => "left the match",
        SessionEndReason.ConnectionLost => "lost the connection",
        SessionEndReason.FellBehind => "fell too far behind and is out of the match",
        _ => "is out of the match",
    };
}
