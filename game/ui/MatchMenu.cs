using System.Globalization;
using System.Net.Sockets;
using Godot;
using Net;
using Net.Udp;
using Sim.Data;
using Sim.Serialization;
using Entropy = System.Security.Cryptography.RandomNumberGenerator;

namespace Game;

/// <summary>
/// The menu and the lobby: the page the game opens on, where a match is started alone, hosted or
/// joined; the page that waits for the other player; the small panel shown during a match; and
/// the page that says why a match ended. It creates the <see cref="MatchRun"/> and closes it.
/// Whoever owns the frame advances <see cref="Run"/>.
/// </summary>
/// <remarks>
/// The seed and the start each player gets are drawn here, on the host, outside the simulation,
/// which rolls nothing by itself (design 5.2). A guest is given them in the host's description of
/// the match.
/// </remarks>
public partial class MatchMenu : CanvasLayer
{
    // The names of the colors the world view gives the first seats.
    private static readonly string[] SeatColors = ["blue", "red"];

    // What outlives the scene, which is loaded again whenever the game returns to the first page.
    private static string? s_notice;
    private static string s_lastAddress = string.Empty;
    private static string s_lastPort = GameInfo.DefaultPort.ToString(CultureInfo.InvariantCulture);
    private static bool s_requestUsed;

    private readonly List<string> _events = [];

    private GameData _data = null!;
    private StartRequest _request = StartRequest.Menu;
    private Page _page = Page.Start;
    private string _joinedAddress = string.Empty;

    private Control _screen = null!;
    private Control _startPage = null!;
    private Control _waitingPage = null!;
    private Control _endedPage = null!;
    private Control _hud = null!;
    private Label _noticeLabel = null!;
    private Label _waitingTitle = null!;
    private Label _waitingStatus = null!;
    private Label _endedText = null!;
    private Label _hudText = null!;
    private Button _startButton = null!;
    private LineEdit _hostPort = null!;
    private LineEdit _joinAddress = null!;

    private enum Page
    {
        Start,
        Waiting,
        Playing,
        Ended,
    }

    /// <summary>The match the menu has started, hosts or has joined, until it is closed.</summary>
    public MatchRun? Run { get; private set; }

    /// <summary>True while a page covers the game, so that the pointer and the keys are the menu's.</summary>
    public bool IsOpen => _page != Page.Playing;

    /// <summary>What the first page last told the player: why a match could not be started or joined.</summary>
    public string? Notice { get; private set; }

    /// <summary>Gives the menu the game data and what the command line asked for. Call it before the menu enters the tree.</summary>
    public void Open(GameData data, StartRequest request)
    {
        _data = data;
        _request = request;
    }

    public override void _Ready()
    {
        Layer = 50;
        Build();
        ShowPage(Page.Start);
        ShowNotice(s_notice);
        s_notice = null;

        // The command line is followed once. A game that comes back to the first page stays there.
        if (!s_requestUsed)
        {
            s_requestUsed = true;
            switch (_request.Kind)
            {
                case StartKind.Host:
                    Host(_request.Port, localOnly: _request.NetTestTicks > 0);
                    break;
                case StartKind.Join:
                    Join(_request.Address, _request.Port, localOnly: _request.NetTestTicks > 0);
                    break;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (Run is not { } run)
        {
            return;
        }

        switch (_page)
        {
            case Page.Waiting when run.HasStarted:
                ShowPage(Page.Playing);
                break;

            case Page.Waiting when run.Client.Phase == SessionPhase.Stopped:
                BackToStart(WhyNotJoined(run));
                break;

            case Page.Waiting:
                _waitingStatus.Text = WaitingText(run);
                _startButton.Disabled = run is not HostRun { Host.CanStart: true };
                break;

            case Page.Playing when run.Client.Phase == SessionPhase.Stopped:
                ShowEnded(run);
                break;

            case Page.Playing:
                _hudText.Text = HudText(run);
                break;
        }
    }

    /// <summary>Starts the hosted match, as the Start button does. Does nothing unless every seat is ready.</summary>
    public void StartMatch()
    {
        if (Run is HostRun { HasStarted: false, Host.CanStart: true } host)
        {
            host.Start();
        }
    }

    /// <summary>
    /// Ends this machine's part in the match: the other players are told, the network is released
    /// and the replay is closed. Call it before the game exits. It does nothing the second time.
    /// </summary>
    public void CloseRun()
    {
        var run = Run;
        Run = null;
        run?.Dispose();
    }

    private void PlayAlone()
    {
        var run = new LocalRun(Describe(), _data, localPlayer: 0);
        Begin(run);
        ShowPage(Page.Playing);
    }

    private void Host(int port, bool localOnly)
    {
        HostRun run;
        try
        {
            run = new HostRun(Describe(), _data, port, localOnly);
        }
        catch (Exception failed) when (failed is IOException or SocketException)
        {
            ShowNotice($"Port {port} could not be opened. Another program, or another copy of the game, may be using it. {failed.Message}");
            return;
        }

        Begin(run);
        _waitingTitle.Text = "Hosting a match";
        _startButton.Visible = true;
        ShowPage(Page.Waiting);
        GD.Print($"Hosting on port {run.Port}.");
    }

    private void Join(string address, int port, bool localOnly)
    {
        JoinRun run;
        try
        {
            run = new JoinRun(_data, address, port, localOnly, readyWhenBuilt: true);
        }
        catch (Exception failed) when (failed is IOException or SocketException or ArgumentException)
        {
            ShowNotice($"'{address}' could not be reached. {failed.Message}");
            return;
        }

        _joinedAddress = string.Create(CultureInfo.InvariantCulture, $"{address}, port {port}");
        Begin(run);
        _waitingTitle.Text = "Joining a match";
        _startButton.Visible = false;
        ShowPage(Page.Waiting);
        GD.Print($"Joining {_joinedAddress}.");
    }

    private void Begin(MatchRun run)
    {
        Run = run;
        _events.Clear();
        run.Client.PlayerLeft += (player, reason) => _events.Add($"{PlayerName(player)} {HowLeft(reason)}.");
        run.Client.Started += () =>
        {
            var description = run.Description!;
            var seat = run.Client.Player;
            GD.Print($"Match on '{description.MapId}' with seed {description.Seed}. This is player {seat + 1} of {description.Players.Count}, at start {description.Players[seat].Start}. Data sha256:{description.ContentHash}.");
        };
    }

    // The host draws the seed and the starts from the system's source of chance.
    private MatchDescription Describe()
    {
        var map = _data.Maps.First(candidate => candidate.Id == Main.MapId);
        var seed = BitConverter.ToUInt64(Entropy.GetBytes(sizeof(ulong)));
        var players = StartDraw.Draw(Main.PlayerCount, map.Starts.Count, Entropy.GetInt32);
        return new MatchDescription(GameInfo.Version, _data.ContentHash, map.Id, seed, players);
    }

    private void OnHostPressed()
    {
        if (!StartRequest.TryParsePort(_hostPort.Text, out var port))
        {
            ShowNotice("The port is a number from 1 to 65535.");
            return;
        }

        s_lastPort = _hostPort.Text.Trim();
        Host(port, localOnly: false);
    }

    private void OnJoinPressed()
    {
        if (!StartRequest.TryParseAddress(_joinAddress.Text, out var address, out var port))
        {
            ShowNotice($"Type the host's address, with or without a port: 192.168.1.20 or 192.168.1.20:{GameInfo.DefaultPort}.");
            return;
        }

        s_lastAddress = _joinAddress.Text.Trim();
        Join(address, port, localOnly: false);
    }

    // The first page is always a new scene, so that the next match finds a world view without one.
    private void BackToStart(string? notice)
    {
        CloseRun();
        s_notice = notice;
        GetTree().ReloadCurrentScene();
    }

    private void ShowEnded(MatchRun run)
    {
        var end = run.Client.EndInfo;
        var tick = run.Match?.World.Tick ?? 0;
        CloseRun();

        var text = end?.Reason switch
        {
            SessionEndReason.HostEnded => "The host ended the match.",
            SessionEndReason.Desync => string.Create(CultureInfo.InvariantCulture,
                $"The two games no longer agree, so the match was stopped at tick {tick}. This is a fault in the game, not something you did."),
            SessionEndReason.ConnectionLost => "The connection to the host was lost.",
            SessionEndReason.FellBehind => "This machine fell too far behind and was taken out of the match.",
            _ => $"The match stopped: {end?.Reason}.",
        };

        if (run.Files is { } files)
        {
            if (files.SnapshotPath is not null)
            {
                text += $"\n\nTo find the fault, both players' files are needed. Yours are:\n{files.ReplayPath}\n{files.SnapshotPath}";
            }
            else if (files.ReplayPath is not null)
            {
                text += $"\n\nReplay: {files.ReplayPath}";
            }

            if (files.Problem is not null)
            {
                text += $"\n\n{files.Problem}";
            }
        }

        GD.Print(text);
        _endedText.Text = text;
        ShowPage(Page.Ended);
    }

    private string WaitingText(MatchRun run)
    {
        if (run is HostRun host)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"Listening on port {host.Port}.\n{LocalAddresses()}\n");
            for (var seat = 0; seat < host.Host.PlayerCount; seat++)
            {
                var state = seat == host.Host.LocalPlayer ? "you"
                    : !host.Host.Session.IsAttached(seat) ? "nobody yet"
                    : !host.Host.Session.IsReady(seat) ? "joining"
                    : "ready";
                text += $"\n{PlayerName(seat)}: {state}";
            }

            return text;
        }

        var client = run.Client;
        var address = $"{_joinedAddress}\n\n";
        if (client.Player < 0)
        {
            return address + "Waiting for the host to answer.";
        }

        return run.Match is null
            ? address + "Connected. Waiting for the host to describe the match."
            : address + $"You are {PlayerName(client.Player).ToLowerInvariant()} on the map '{run.Match.Map.Id}'. Waiting for the host to start the match.";
    }

    private string HudText(MatchRun run)
    {
        var seat = run.Client.Player;
        var color = seat >= 0 && seat < SeatColors.Length ? $", {SeatColors[seat]}" : string.Empty;
        var text = run switch
        {
            HostRun host => string.Create(CultureInfo.InvariantCulture, $"{PlayerName(seat)}{color}. Hosting on port {host.Port}."),
            JoinRun => $"{PlayerName(seat)}{color}. Joined {_joinedAddress}.",
            _ => $"{PlayerName(seat)}{color}. Playing alone.",
        };

        foreach (var line in _events)
        {
            text += "\n" + line;
        }

        return text;
    }

    // Why a match that never started is over, as a sentence for the player.
    private string WhyNotJoined(MatchRun run)
    {
        if (run is not JoinRun join)
        {
            return $"The match could not be started: {run.Client.EndInfo?.Reason}.";
        }

        if (join.Player.Refusal is { } refusal)
        {
            return refusal;
        }

        return (run.Client.EndInfo?.Reason, join.Connection.CloseReason) switch
        {
            (SessionEndReason.HostEnded, _) => "The host closed the match before it started.",
            (SessionEndReason.VersionMismatch, _) => "The host runs another version of the game. Both players need the same build.",
            (_, UdpCloseReason.Refused) => $"The game at {_joinedAddress} has no free seat, or its match has already started.",
            (_, UdpCloseReason.ConnectFailed or UdpCloseReason.Unreachable or UdpCloseReason.TimedOut) =>
                $"No game answered at {_joinedAddress}. Check the address and the port, that the host is on its waiting page, and that the host's firewall lets the game through.",
            (var reason, _) => $"The connection to {_joinedAddress} closed before the match started ({reason}).",
        };
    }

    private static string PlayerName(int seat) => string.Create(CultureInfo.InvariantCulture, $"Player {seat + 1}");

    private static string HowLeft(SessionEndReason reason) => reason switch
    {
        SessionEndReason.Left => "left the match",
        SessionEndReason.ConnectionLost => "lost the connection",
        SessionEndReason.FellBehind => "fell too far behind and is out of the match",
        _ => "is out of the match",
    };

    // The addresses another machine on the same network can reach this one at.
    private static string LocalAddresses()
    {
        var addresses = IP.GetLocalAddresses()
            .Where(address => address.Contains('.', StringComparison.Ordinal)
                && !address.StartsWith("127.", StringComparison.Ordinal)
                && !address.StartsWith("169.254.", StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        return addresses.Length == 0
            ? "This machine has no network address."
            : $"This machine's address on its network: {string.Join(" or ", addresses)}.";
    }

    private void ShowNotice(string? notice)
    {
        Notice = notice;
        _noticeLabel.Text = notice ?? string.Empty;
        _noticeLabel.Visible = !string.IsNullOrEmpty(notice);
        if (!string.IsNullOrEmpty(notice))
        {
            GD.Print(notice);
        }
    }

    private void ShowPage(Page page)
    {
        _page = page;
        _screen.Visible = page != Page.Playing;
        _startPage.Visible = page == Page.Start;
        _waitingPage.Visible = page == Page.Waiting;
        _endedPage.Visible = page == Page.Ended;
        _hud.Visible = page == Page.Playing;
    }

    private void Build()
    {
        // The shade takes every click, so that nothing behind a page can be selected or ordered.
        _screen = new ColorRect { Name = "Screen", Color = new Color(0f, 0f, 0f, 0.55f) };
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.AddChild(center);

        var pages = new PanelContainer { CustomMinimumSize = new Vector2(520f, 0f) };
        pages.AddThemeStyleboxOverride("panel", PanelStyle(18f));
        center.AddChild(pages);

        var stack = new VBoxContainer();
        pages.AddChild(stack);
        _startPage = BuildStartPage();
        _waitingPage = BuildWaitingPage();
        _endedPage = BuildEndedPage();
        stack.AddChild(_startPage);
        stack.AddChild(_waitingPage);
        stack.AddChild(_endedPage);

        var hud = new PanelContainer { Name = "MatchPanel" };
        hud.AddThemeStyleboxOverride("panel", PanelStyle(8f));
        var hudStack = Column(6);
        _hudText = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        hudStack.AddChild(_hudText);
        var leave = PushButton("Leave the match");
        leave.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        leave.FocusMode = Control.FocusModeEnum.None;
        leave.Pressed += () => BackToStart(null);
        hudStack.AddChild(leave);
        hud.AddChild(hudStack);
        AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.Minsize, 8);
        hud.GrowHorizontal = Control.GrowDirection.Begin;
        _hud = hud;
    }

    private VBoxContainer BuildStartPage()
    {
        var page = Column(12);
        page.AddChild(Title(ProjectSettings.GetSetting("application/config/name").AsString()));
        page.AddChild(Text($"Version {GameInfo.Version}. Data {_data.ContentHash[..12]}."));

        _noticeLabel = Text(string.Empty);
        _noticeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.45f));
        page.AddChild(_noticeLabel);

        var alone = PushButton("Play alone");
        alone.Pressed += PlayAlone;
        page.AddChild(alone);
        page.AddChild(new HSeparator());

        page.AddChild(Text("Host a match for another player to join. The port is the number they need beside this machine's address."));
        var hostRow = Row();
        hostRow.AddChild(new Label { Text = "Port" });
        _hostPort = Field(s_lastPort);
        _hostPort.CustomMinimumSize = new Vector2(100f, 0f);
        _hostPort.MaxLength = 5;
        _hostPort.TextSubmitted += _ => OnHostPressed();
        hostRow.AddChild(_hostPort);
        var host = PushButton("Host a match");
        host.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        host.Pressed += OnHostPressed;
        hostRow.AddChild(host);
        page.AddChild(hostRow);
        page.AddChild(new HSeparator());

        page.AddChild(Text("Join a match another player hosts. Type the address their game shows."));
        var joinRow = Row();
        _joinAddress = Field(s_lastAddress);
        _joinAddress.PlaceholderText = $"192.168.1.20 or 192.168.1.20:{GameInfo.DefaultPort}";
        _joinAddress.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _joinAddress.TextSubmitted += _ => OnJoinPressed();
        joinRow.AddChild(_joinAddress);
        var join = PushButton("Join");
        join.Pressed += OnJoinPressed;
        joinRow.AddChild(join);
        page.AddChild(joinRow);
        return page;
    }

    private VBoxContainer BuildWaitingPage()
    {
        var page = Column(12);
        _waitingTitle = Title(string.Empty);
        page.AddChild(_waitingTitle);
        _waitingStatus = Text(string.Empty);
        page.AddChild(_waitingStatus);

        var row = Row();
        _startButton = PushButton("Start the match");
        _startButton.Disabled = true;
        _startButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _startButton.Pressed += StartMatch;
        row.AddChild(_startButton);
        var cancel = PushButton("Cancel");
        cancel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cancel.Pressed += () => BackToStart(null);
        row.AddChild(cancel);
        page.AddChild(row);
        return page;
    }

    private VBoxContainer BuildEndedPage()
    {
        var page = Column(12);
        page.AddChild(Title("The match is over"));
        _endedText = Text(string.Empty);
        page.AddChild(_endedText);
        var back = PushButton("Back to the menu");
        back.Pressed += () => BackToStart(null);
        page.AddChild(back);
        return page;
    }

    private static Button PushButton(string text)
    {
        var button = new Button { Text = text };
        button.AddThemeStyleboxOverride("normal", BoxStyle(new Color(0.2f, 0.26f, 0.36f)));
        button.AddThemeStyleboxOverride("hover", BoxStyle(new Color(0.27f, 0.35f, 0.48f)));
        button.AddThemeStyleboxOverride("pressed", BoxStyle(new Color(0.15f, 0.2f, 0.28f)));
        button.AddThemeStyleboxOverride("disabled", BoxStyle(new Color(0.16f, 0.17f, 0.2f)));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    private static LineEdit Field(string text)
    {
        var field = new LineEdit { Text = text };
        field.AddThemeStyleboxOverride("normal", BoxStyle(new Color(0.03f, 0.035f, 0.05f)));
        field.AddThemeStyleboxOverride("focus", BoxStyle(new Color(0.03f, 0.035f, 0.05f), new Color(0.45f, 0.6f, 0.85f)));
        return field;
    }

    private static StyleBoxFlat BoxStyle(Color color, Color? border = null)
    {
        var style = new StyleBoxFlat { BgColor = color, BorderColor = border ?? color.Lightened(0.15f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = 12f;
        style.ContentMarginRight = 12f;
        style.ContentMarginTop = 7f;
        style.ContentMarginBottom = 7f;
        return style;
    }

    private static VBoxContainer Column(int separation)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    private static HBoxContainer Row()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        return row;
    }

    private static Label Title(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        return label;
    }

    private static Label Text(string text) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(480f, 0f),
    };

    private static StyleBoxFlat PanelStyle(float margin)
    {
        var style = new StyleBoxFlat { BgColor = new Color(0.09f, 0.1f, 0.13f, 0.94f) };
        style.SetCornerRadiusAll(6);
        style.SetContentMarginAll(margin);
        return style;
    }
}
