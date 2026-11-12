using Godot;
using Sim.Core;
using Sim.Research;

namespace Game;

/// <summary>
/// The epoch announcement (design 1.4): when a player's epoch rises, every player is told, in a
/// banner under the top bar that names the epoch and goes away by itself. The local player's own
/// banner also says what the epoch changes: how high a track may now go.
/// </summary>
/// <remarks>
/// It hears of a new epoch as the player does: from the events the local player may know of
/// (<see cref="Sim.Vision.PlayerView.CollectEvents"/>), which hold every player's change of
/// epoch, another player's with the epoch alone. It asks nothing of the true state of the
/// match, so it shows on a machine that joined what that player is told and no more. A player
/// whose epoch rises more than once in a tick gets one banner, for the epoch reached. Where
/// there is no room for every banner the frame hides the oldest for as long
/// (<see cref="InterfaceFrame.Announce"/>), so the local player's own is announced last.
/// The banner stands in the frame's place for announcements
/// (<see cref="InterfaceFrame.UnderMessages"/>): in the middle of the window, under the
/// messages, over a full-screen panel, because the research panel is where the player is likely
/// to be when a research raises the epoch, and above the bottom panel. It takes no click. It is
/// no wider than the development aids at the edges leave free
/// (<see cref="InterfaceFrame.MiddleWidth"/>): a banner that is wider on one line is written on
/// more.
/// </remarks>
public partial class EpochAnnouncement : InterfacePart
{
    private const double ShownSeconds = 8.0;
    private const double FadeSeconds = 1.5;

    private readonly List<(PanelContainer Banner, double Left, float Width)> _banners = [];
    private readonly List<SimEvent> _known = [];

    // The epoch each player reached in the tick that is looked at, or zero.
    private int[] _reached = [];
    private VBoxContainer _place = null!;

    /// <summary>The number of announcements made since the match was shown.</summary>
    public int Count { get; private set; }

    /// <summary>The banners that are shown now, the oldest first.</summary>
    public IEnumerable<PanelContainer> Shown => _banners.Select(shown => shown.Banner);

    protected override void Attach()
    {
        _reached = new int[View.Match.Players.Count];
        _place = Frame.UnderMessages;
    }

    public override void AfterTick()
    {
        View.Sight.Seen.CollectEvents(_known);
        var any = false;
        foreach (var happened in _known)
        {
            if (happened.Kind == SimEventKind.EpochChanged && ResearchEvent.TryRead(in happened, out var research))
            {
                _reached[research.Player] = Math.Max(_reached[research.Player], research.Epoch);
                any = true;
            }
        }

        if (!any)
        {
            return;
        }

        // The other players' first and the local player's own last: where there is room for
        // fewer, the frame keeps the newest, and the player's own epoch is the one to keep.
        for (var player = 0; player < _reached.Length; player++)
        {
            if (_reached[player] > 0 && player != View.Player)
            {
                Announce(player, _reached[player]);
                _reached[player] = 0;
            }
        }

        if (_reached[View.Player] > 0)
        {
            Announce(View.Player, _reached[View.Player]);
            _reached[View.Player] = 0;
        }
    }

    public override void EveryFrame(double delta)
    {
        if (_banners.Count == 0)
        {
            return;
        }

        var middle = Frame.MiddleWidth;
        for (var i = _banners.Count - 1; i >= 0; i--)
        {
            var (banner, left, width) = _banners[i];
            left -= delta;
            if (left <= 0.0)
            {
                _place.RemoveChild(banner);
                banner.QueueFree();
                _banners.RemoveAt(i);
                continue;
            }

            banner.Modulate = new Color(1f, 1f, 1f, (float)Math.Min(1.0, left / FadeSeconds));
            Fit(banner, width, middle);
            _banners[i] = (banner, left, width);
        }
    }

    // A banner is on one line while that fits in the middle of the window, and as wide as the
    // middle with its lines wrapped when it does not.
    private static void Fit(PanelContainer banner, float width, float limit)
    {
        var wraps = width > limit;
        var least = new Vector2(wraps ? limit : 0f, 0f);
        if (banner.CustomMinimumSize == least)
        {
            return;
        }

        banner.CustomMinimumSize = least;
        foreach (var line in banner.GetChild(0).GetChildren().OfType<Label>())
        {
            line.AutowrapMode = wraps ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        }
    }

    private void Announce(int player, int epoch)
    {
        var match = View.Match;
        var own = player == View.Player;
        var title = InspectionWords.EpochTitle(match.Data, epoch);
        var sentence = InspectionWords.EpochSentence(match.Data, player, View.Player, epoch, match.Research.EpochCap);

        // The player's own epoch is the headline of the match. Another player's is news, and smaller.
        var style = UiStyle.PanelStyle(own ? 14f : 9f);
        style.BorderColor = PlayerColors.Of(player).Lightened(0.2f);
        style.SetBorderWidthAll(own ? 2 : 1);
        style.ContentMarginLeft = own ? 28f : 18f;
        style.ContentMarginRight = own ? 28f : 18f;

        var banner = new PanelContainer
        {
            Name = "EpochBanner",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        banner.AddThemeStyleboxOverride("panel", style);
        var lines = UiStyle.Column(2);
        lines.MouseFilter = Control.MouseFilterEnum.Ignore;
        banner.AddChild(lines);
        var headline = InspectionUi.Text(title, ResearchColors.Ink, own ? 30 : 18);
        headline.HorizontalAlignment = HorizontalAlignment.Center;
        lines.AddChild(headline);
        var under = InspectionUi.Text(sentence, own ? ResearchColors.Ink : ResearchColors.Faint, own ? 14 : 13);
        under.HorizontalAlignment = HorizontalAlignment.Center;
        lines.AddChild(under);

        Frame.Announce(banner);
        var width = banner.GetCombinedMinimumSize().X;
        Fit(banner, width, Frame.MiddleWidth);
        _banners.Add((banner, ShownSeconds, width));
        Count++;
        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Epoch announcement: {title}. {sentence}");
        }
    }
}
