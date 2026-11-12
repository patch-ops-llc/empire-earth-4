using Godot;

namespace Game;

/// <summary>
/// The epoch announcement (design 1.4): when a player's epoch rises, every player is told, in a
/// banner under the top bar that names the epoch and goes away by itself. The local player's own
/// banner also says what the epoch changes: how high a track may now go.
/// </summary>
/// <remarks>
/// It finds a new epoch by comparing each player's epoch with that of the tick before, so it
/// works the same alone, hosting and joined, and needs nothing from the simulation but the tiers.
/// The banner is drawn over a full-screen panel, because the research panel is where the player
/// is likely to be when a research raises the epoch. It takes no click. It stands in the middle
/// of the window, under the messages, and is no wider than the development aids at the edges
/// leave free (<see cref="InterfaceFrame.MiddleWidth"/>): a banner that is wider on one line is
/// written on more. The messages give up their oldest when the banners would otherwise reach
/// the bottom panel (<see cref="InterfaceFrame.RoomUnderMessages"/>).
/// </remarks>
public partial class EpochAnnouncement : InterfacePart
{
    private const double ShownSeconds = 8.0;
    private const double FadeSeconds = 1.5;

    // Between the message line, which a research that finishes writes to in the same tick, and the banner.
    private const float Gap = 8f;

    private readonly List<(PanelContainer Banner, double Left, float Width)> _banners = [];
    private int[] _epochs = [];
    private VBoxContainer _place = null!;
    private float _top;

    /// <summary>The number of announcements made since the match was shown.</summary>
    public int Count { get; private set; }

    /// <summary>The banners that are shown now, the oldest first.</summary>
    public IEnumerable<PanelContainer> Shown => _banners.Select(shown => shown.Banner);

    protected override void Attach()
    {
        var match = View.Match;
        _epochs = new int[match.Players.Count];
        for (var player = 0; player < _epochs.Length; player++)
        {
            _epochs[player] = match.Research.Tech(player).Epoch;
        }

        _place = UiStyle.Column(6);
        _place.Name = "EpochBanners";
        _place.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_place);
        _place.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
    }

    public override void AfterTick()
    {
        var match = View.Match;
        for (var player = 0; player < _epochs.Length; player++)
        {
            var epoch = match.Research.Tech(player).Epoch;
            if (epoch > _epochs[player])
            {
                Announce(player, epoch);
            }

            _epochs[player] = epoch;
        }
    }

    public override void EveryFrame(double delta)
    {
        if (_banners.Count == 0)
        {
            if (_top > 0f)
            {
                Frame.RoomUnderMessages = 0f;
            }

            _top = 0f;
            return;
        }

        // Under the messages, however many there are. It moves down for a new message and does
        // not jump back up when one goes away, unless it has to: the messages leave the banners
        // their room above the bottom panel, and the banners move up into it.
        var height = _place.GetCombinedMinimumSize().Y;
        Frame.RoomUnderMessages = Gap + height;
        _top = Math.Max(Frame.MessagesEnd + Gap, Math.Min(_top, Frame.MiddleEnd - height));
        _place.OffsetTop = _top;

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

        if (_banners.Count == 0)
        {
            _top = Frame.MessagesEnd + Gap;
            _place.OffsetTop = _top;
        }

        _place.AddChild(banner);
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
