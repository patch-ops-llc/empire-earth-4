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
/// joined; the page of the settings of a match the player hosts; the page that waits for the
/// other player; and the page that says how a match ended and why.
/// It creates the <see cref="MatchRun"/> and closes it. Whoever owns the frame advances
/// <see cref="Run"/>. During a match the menu shows nothing: the interface round the match is
/// the <see cref="InterfaceFrame"/>.
/// </summary>
/// <remarks>
/// Which start each player gets is a setting of the match (design 5.2), which the host chooses
/// on the page of the settings: a start it names, or one drawn from those no player is set to,
/// which is what every seat has until the host says otherwise. The seed and the starts that are
/// drawn come from the system's source of chance, here, on the host, outside the simulation,
/// which rolls nothing by itself. A guest is given them in the host's description of the match.
/// </remarks>
public partial class MatchMenu : CanvasLayer
{
    // What outlives the scene, which is loaded again whenever the game returns to the first page.
    private static string? s_notice;
    private static string s_lastAddress = string.Empty;
    private static string s_lastPort = GameInfo.DefaultPort.ToString(CultureInfo.InvariantCulture);
    private static bool s_requestUsed;
    private static MatchSettings? s_settings;
    private static string? s_personality;

    private GameData _data = null!;
    private StartRequest _request = StartRequest.Menu;
    private Page _page = Page.Start;
    private string _joinedAddress = string.Empty;

    private Control _screen = null!;
    private Control _startPage = null!;
    private Control _waitingPage = null!;
    private Control _endedPage = null!;
    private Control _setupPage = null!;
    private Label _noticeLabel = null!;
    private Label _waitingTitle = null!;
    private Label _waitingStatus = null!;
    private Label _endedTitle = null!;
    private Label _endedText = null!;
    private Label _setupMap = null!;
    private Label _setupNote = null!;
    private Label _setupProblem = null!;
    private Button _setupGo = null!;
    private readonly List<OptionButton> _setupWho = [];
    private readonly List<OptionButton> _setupStart = [];
    private IReadOnlyList<SeatChoice> _seatChoices = [];
    private MatchSettings _settings = null!;
    private int _setupPort = GameInfo.DefaultPort;
    private Button _startButton = null!;
    private LineEdit _hostPort = null!;
    private LineEdit _joinAddress = null!;

    private enum Page
    {
        Start,
        Setup,
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
        _seatChoices = SeatChoices.For(data);
        _settings = s_settings ?? MatchSettings.Default(Main.MapId, Main.PlayerCount);
    }

    /// <summary>The settings of the match the player is about to host, as the page of the settings shows them.</summary>
    public MatchSettings Settings => _settings;

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
                case StartKind.Alone when _request.Computer.Length > 0:
                    PlayAgainstComputer(_request.Computer);
                    break;
                case StartKind.Alone:
                    PlayAlone();
                    break;
                case StartKind.Host:
                    Host(_request.Port, localOnly: _request.NetTestTicks > 0, MatchSettings.Default(Main.MapId, Main.PlayerCount));
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

    /// <summary>
    /// Leaves the match, or the page that waits for one, and goes back to the first page. The
    /// other players are told, and the replay is closed.
    /// </summary>
    public void LeaveMatch() => BackToStart(null);

    // One click, with every start drawn: nobody sits at the other seats.
    private void PlayAlone() => PlayHere(AloneSettings());

    /// <summary>
    /// Starts a match alone against the computer, as the button of the first page does: the
    /// player in the first seat, the computer with this personality in the second, every start
    /// drawn. A personality the data does not have starts nothing, and the page says so.
    /// </summary>
    public void PlayAgainstComputer(string personality)
    {
        var settings = AloneSettings().WithSeat(1, new SeatSetting(SeatKind.Computer, Personality: personality));
        if (settings.FindProblem(_data) is not null)
        {
            var known = string.Join(", ", _data.Personalities.Select(candidate => candidate.Id));
            ShowNotice($"The computer has no personality '{personality}'. It has: {known}.");
            return;
        }

        s_personality = personality;
        PlayHere(settings);
    }

    // A match nobody joins has no network in it: no port is opened and nothing is sent, so the
    // system has nothing to ask the player about.
    private void PlayHere(MatchSettings settings)
    {
        var run = new LocalRun(Describe(settings), _data, localPlayer: 0, seats: settings.Seats);
        Begin(run);
        ShowPage(Page.Playing);
    }

    /// <summary>The settings of a match alone: the player in the first seat and nobody in the others, every start drawn.</summary>
    private static MatchSettings AloneSettings()
    {
        var settings = MatchSettings.Default(Main.MapId, Main.PlayerCount);
        for (var seat = 1; seat < settings.Seats.Count; seat++)
        {
            settings = settings.WithSeat(seat, new SeatSetting(SeatKind.Nobody));
        }

        return settings;
    }

    private void Host(int port, bool localOnly, MatchSettings settings)
    {
        HostRun run;
        try
        {
            run = new HostRun(Describe(settings), _data, port, localOnly, settings.Seats);
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

        _joinedAddress = run.HostAddress;
        Begin(run);
        _waitingTitle.Text = "Joining a match";
        _startButton.Visible = false;
        ShowPage(Page.Waiting);
        GD.Print($"Joining {_joinedAddress}.");
    }

    private void Begin(MatchRun run)
    {
        Run = run;
        run.Client.Started += Announce;

        // A match alone has started by the time it is handed over.
        if (run.Client.Phase == SessionPhase.Running)
        {
            Announce();
        }

        void Announce()
        {
            var description = run.Description!;
            var seat = run.Client.Player;
            GD.Print($"Match on '{description.MapId}' with seed {description.Seed}. This is player {seat + 1} of {description.Players.Count}, at start {description.Players[seat].Start}. Data sha256:{description.ContentHash}.");
            for (var other = 0; other < description.Players.Count; other++)
            {
                if (run.Computers?.PersonalityOf(other) is { } personality)
                {
                    GD.Print($"The computer plays player {other + 1} with the personality '{personality.Id}', at start {description.Players[other].Start}.");
                }
            }
        }
    }

    // The host draws the seed, and the starts its settings leave to chance, from the system's
    // source of chance.
    private MatchDescription Describe(MatchSettings settings)
    {
        var seed = BitConverter.ToUInt64(Entropy.GetBytes(sizeof(ulong)));
        return settings.Describe(_data, GameInfo.Version, seed, Entropy.GetInt32);
    }

    private void OnHostPressed()
    {
        if (!StartRequest.TryParsePort(_hostPort.Text, out var port))
        {
            ShowNotice("The port is a number from 1 to 65535.");
            return;
        }

        s_lastPort = _hostPort.Text.Trim();
        _setupPort = port;
        ShowNotice(null);
        ShowSetup();
        ShowPage(Page.Setup);
    }

    /// <summary>
    /// Sets one seat of the match the player is about to host, as a choice on the page of the
    /// settings does: what takes the seat, and its start, or null to have the start drawn.
    /// </summary>
    public void SetSeat(int seat, SeatSetting setting)
    {
        _settings = _settings.WithSeat(seat, setting);
        s_settings = _settings;
        ShowSetup();
    }

    /// <summary>
    /// Begins the match of the page of the settings, as its button does: a match somebody is to
    /// join is opened to the network and waits, and any other starts at once on this machine.
    /// Does nothing while the settings give no match.
    /// </summary>
    public void BeginFromSettings()
    {
        if (_settings.FindProblem(_data) is not null)
        {
            return;
        }

        if (SomebodyJoins(_settings))
        {
            Host(_setupPort, localOnly: false, _settings);
            if (Run is null)
            {
                // The port could not be opened, which the first page says.
                ShowPage(Page.Start);
            }
        }
        else
        {
            PlayHere(_settings);
        }
    }

    private static bool SomebodyJoins(MatchSettings settings) =>
        settings.Seats.Skip(1).Any(seat => seat.Kind == SeatKind.Person);

    // The page of the settings as the settings stand.
    private void ShowSetup()
    {
        var map = _data.Maps.First(candidate => candidate.Id == _settings.MapId);
        _setupMap.Text = string.Create(CultureInfo.InvariantCulture,
            $"Map '{map.Id}', {map.Width} by {map.Height} tiles, with {map.Starts.Count} starts. A match has {_settings.Seats.Count} players.");

        for (var seat = 0; seat < _settings.Seats.Count; seat++)
        {
            var setting = _settings.Seats[seat];
            if (seat > 0)
            {
                _setupWho[seat - 1].Selected = SeatChoices.IndexOf(_seatChoices, setting);
            }

            _setupStart[seat].Selected = setting.Start is { } start ? start + 1 : 0;
        }

        var problem = _settings.FindProblem(_data);
        _setupProblem.Text = problem ?? string.Empty;
        _setupProblem.Visible = problem is not null;
        _setupGo.Disabled = problem is not null;

        var joins = SomebodyJoins(_settings);
        _setupGo.Text = joins
            ? string.Create(CultureInfo.InvariantCulture, $"Open the match on port {_setupPort}")
            : "Start the match";
        var note = joins
            ? "A start that is drawn is one of those no player is set to, each as likely as the next. The other player sees the starts when the match begins."
            : "A start that is drawn is one of those no player is set to, each as likely as the next. Nobody joins this match, so it is played on this machine only and nothing is opened to the network.";
        if (_settings.Seats.Any(seat => seat.Kind == SeatKind.Computer))
        {
            note += $" {SeatChoices.ComputerNote}";
        }

        _setupNote.Text = note;
    }

    // A start as the page names it: its number, and where on the map it lies.
    private static string StartName(MapDef map, int start)
    {
        var place = map.Starts[start];
        var dx = place.X - (map.Width / 2.0);
        var dy = place.Y - (map.Height / 2.0);
        var across = dx < 0 ? "west" : "east";
        var along = dy < 0 ? "north" : "south";
        var where = Math.Abs(dx) > 2 * Math.Abs(dy) ? across
            : Math.Abs(dy) > 2 * Math.Abs(dx) ? along
            : $"{along}-{across}";
        return string.Create(CultureInfo.InvariantCulture, $"Start {start + 1}, in the {where}");
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
        var player = run.Client.Player;
        CloseRun();

        // How the match stood when it stopped (design 5.1): a host who leaves concedes in the
        // last tick, so a match the host walks out of has its result on every machine.
        var standings = end?.Reason == SessionEndReason.Desync ? null : run.Match?.Standings;
        var headline = standings is null ? null : OutcomeWords.Headline(standings, player);
        _endedTitle.Text = headline ?? "The match is over";
        var result = headline is null ? string.Empty : $"{OutcomeWords.Why(standings!, player)} {OutcomeWords.Length(standings!, tick)}\n\n";

        var text = result + end?.Reason switch
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
                    : host.Computers?.PersonalityOf(seat) is { } personality ? SeatChoices.ComputerLabel(personality).ToLowerInvariant()
                    : host.Host.SeatOf(seat) != SeatKind.Person ? "nobody"
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

    private static string PlayerName(int seat) => MatchPanel.PlayerName(seat);

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
        _setupPage.Visible = page == Page.Setup;
        _waitingPage.Visible = page == Page.Waiting;
        _endedPage.Visible = page == Page.Ended;
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
        pages.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(18f));
        center.AddChild(pages);

        var stack = new VBoxContainer();
        pages.AddChild(stack);
        _startPage = BuildStartPage();
        _setupPage = BuildSetupPage();
        _waitingPage = BuildWaitingPage();
        _endedPage = BuildEndedPage();
        stack.AddChild(_startPage);
        stack.AddChild(_setupPage);
        stack.AddChild(_waitingPage);
        stack.AddChild(_endedPage);
    }

    private VBoxContainer BuildStartPage()
    {
        var page = UiStyle.Column(12);
        page.AddChild(UiStyle.Title(ProjectSettings.GetSetting("application/config/name").AsString()));
        page.AddChild(UiStyle.Text($"Version {GameInfo.Version}. Data {_data.ContentHash[..12]}."));

        _noticeLabel = UiStyle.Text(string.Empty);
        _noticeLabel.AddThemeColorOverride("font_color", UiStyle.Warning);
        page.AddChild(_noticeLabel);

        var alone = UiStyle.PushButton("Play alone");
        alone.Pressed += PlayAlone;
        page.AddChild(alone);

        // A match alone against the computer, with the personality chosen beside the button. The
        // personalities are those of the data.
        if (_data.Personalities.Count > 0)
        {
            var computerRow = UiStyle.Row();
            var against = UiStyle.PushButton("Play against the computer");
            against.Name = "AgainstComputer";
            against.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            computerRow.AddChild(against);
            var personality = Options("Personality");
            personality.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            personality.CustomMinimumSize = new Vector2(150f, 0f);
            personality.TooltipText = "The personality of the computer opponent";
            for (var i = 0; i < _data.Personalities.Count; i++)
            {
                personality.AddItem(_data.Personalities[i].Name);
                if (_data.Personalities[i].Id == s_personality)
                {
                    personality.Selected = i;
                }
            }

            against.Pressed += () => PlayAgainstComputer(_data.Personalities[Math.Max(0, personality.Selected)].Id);
            computerRow.AddChild(personality);
            page.AddChild(computerRow);
            page.AddChild(UiStyle.Text(SeatChoices.ComputerNote));
        }

        page.AddChild(new HSeparator());

        page.AddChild(UiStyle.Text("Host a match for another player to join, and choose its settings first. The port is the number they need beside this machine's address."));
        var hostRow = UiStyle.Row();
        hostRow.AddChild(new Label { Text = "Port" });
        _hostPort = UiStyle.Field(s_lastPort);
        _hostPort.CustomMinimumSize = new Vector2(100f, 0f);
        _hostPort.MaxLength = 5;
        _hostPort.TextSubmitted += _ => OnHostPressed();
        hostRow.AddChild(_hostPort);
        var host = UiStyle.PushButton("Host a match");
        host.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        host.Pressed += OnHostPressed;
        hostRow.AddChild(host);
        page.AddChild(hostRow);
        page.AddChild(new HSeparator());

        page.AddChild(UiStyle.Text("Join a match another player hosts. Type the address their game shows."));
        var joinRow = UiStyle.Row();
        _joinAddress = UiStyle.Field(s_lastAddress);
        _joinAddress.PlaceholderText = $"192.168.1.20 or 192.168.1.20:{GameInfo.DefaultPort}";
        _joinAddress.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _joinAddress.TextSubmitted += _ => OnJoinPressed();
        joinRow.AddChild(_joinAddress);
        var join = UiStyle.PushButton("Join");
        join.Pressed += OnJoinPressed;
        joinRow.AddChild(join);
        page.AddChild(joinRow);
        return page;
    }

    // The settings of a match the player hosts: a row for each seat, with what takes it and the
    // start it gets.
    private VBoxContainer BuildSetupPage()
    {
        var page = UiStyle.Column(12);
        page.AddChild(UiStyle.Title("Match settings"));
        _setupMap = UiStyle.Text(string.Empty);
        page.AddChild(_setupMap);

        var map = _data.Maps.First(candidate => candidate.Id == _settings.MapId);
        var rows = new GridContainer { Name = "Seats", Columns = 3 };
        rows.AddThemeConstantOverride("h_separation", 12);
        rows.AddThemeConstantOverride("v_separation", 8);
        page.AddChild(rows);
        foreach (var heading in new[] { "Seat", "Played by", "Start" })
        {
            var label = new Label { Text = heading };
            label.AddThemeColorOverride("font_color", new Color(0.65f, 0.68f, 0.74f));
            rows.AddChild(label);
        }

        for (var seat = 0; seat < _settings.Seats.Count; seat++)
        {
            var index = seat;
            var name = new Label { Text = $"{PlayerName(seat)}, {PlayerColors.NameOf(seat)}" };
            name.AddThemeColorOverride("font_color", PlayerColors.Of(seat).Lightened(0.25f));
            rows.AddChild(name);

            if (seat == 0)
            {
                rows.AddChild(new Label { Text = "You" });
            }
            else
            {
                var who = Options($"Seat{seat + 1}Who");
                for (var i = 0; i < _seatChoices.Count; i++)
                {
                    who.AddItem(_seatChoices[i].Label);
                    who.SetItemDisabled(i, !_seatChoices[i].Offered);
                }

                who.ItemSelected += chosen => SetSeat(index, SeatChoices.Apply(_seatChoices[(int)chosen], _settings.Seats[index]));
                _setupWho.Add(who);
                rows.AddChild(who);
            }

            var start = Options($"Seat{seat + 1}Start");
            start.AddItem("Drawn");
            for (var i = 0; i < map.Starts.Count; i++)
            {
                start.AddItem(StartName(map, i));
            }

            start.ItemSelected += chosen => SetSeat(index, _settings.Seats[index] with { Start = chosen == 0 ? null : (int)chosen - 1 });
            _setupStart.Add(start);
            rows.AddChild(start);
        }

        _setupNote = UiStyle.Text(string.Empty);
        page.AddChild(_setupNote);
        _setupProblem = UiStyle.Text(string.Empty);
        _setupProblem.AddThemeColorOverride("font_color", UiStyle.Warning);
        page.AddChild(_setupProblem);

        var row = UiStyle.Row();
        _setupGo = UiStyle.PushButton(string.Empty);
        _setupGo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _setupGo.Pressed += BeginFromSettings;
        row.AddChild(_setupGo);
        var back = UiStyle.PushButton("Back");
        back.Pressed += () => ShowPage(Page.Start);
        row.AddChild(back);
        page.AddChild(row);
        return page;
    }

    private static OptionButton Options(string name)
    {
        var options = new OptionButton { Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        options.AddThemeStyleboxOverride("normal", UiStyle.BoxStyle(new Color(0.2f, 0.26f, 0.36f)));
        options.AddThemeStyleboxOverride("hover", UiStyle.BoxStyle(new Color(0.27f, 0.35f, 0.48f)));
        options.AddThemeStyleboxOverride("pressed", UiStyle.BoxStyle(new Color(0.15f, 0.2f, 0.28f)));
        options.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return options;
    }

    private VBoxContainer BuildWaitingPage()
    {
        var page = UiStyle.Column(12);
        _waitingTitle = UiStyle.Title(string.Empty);
        page.AddChild(_waitingTitle);
        _waitingStatus = UiStyle.Text(string.Empty);
        page.AddChild(_waitingStatus);

        var row = UiStyle.Row();
        _startButton = UiStyle.PushButton("Start the match");
        _startButton.Disabled = true;
        _startButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _startButton.Pressed += StartMatch;
        row.AddChild(_startButton);
        var cancel = UiStyle.PushButton("Cancel");
        cancel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cancel.Pressed += () => BackToStart(null);
        row.AddChild(cancel);
        page.AddChild(row);
        return page;
    }

    private VBoxContainer BuildEndedPage()
    {
        var page = UiStyle.Column(12);
        _endedTitle = UiStyle.Title("The match is over");
        page.AddChild(_endedTitle);
        _endedText = UiStyle.Text(string.Empty);
        page.AddChild(_endedText);
        var back = UiStyle.PushButton("Back to the menu");
        back.Pressed += () => BackToStart(null);
        page.AddChild(back);
        return page;
    }
}
