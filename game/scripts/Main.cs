using Godot;
using Net;
using Sim.Data;
using Sim.Setup;

namespace Game;

/// <summary>
/// Root of <c>scenes/main.tscn</c>, the scene the game starts in. It loads the game data and opens
/// the menu, where a match is started alone, hosted or joined. Once there is a match it hands it,
/// with the local player's side of the session, to the world view, the camera, the unit control
/// and the overlay.
/// </summary>
/// <remarks>
/// Code under <c>game/</c> reads the match and never changes it. Only commands change the
/// simulation, and only the session runs its ticks: once a frame, here, the match's
/// <see cref="MatchRun"/> is given the time the frame took. It takes in what the network brought
/// before it lets the session advance, and it is disposed before the game exits, so that the
/// other players are told and the replay is closed.
/// </remarks>
public partial class Main : Node3D
{
    /// <summary>
    /// Passed after <c>--</c> on the command line, this makes the game run the smoke test and quit.
    /// Exported builds refuse a scene path on the command line, so this is how an export is tested.
    /// </summary>
    public const string SmokeTestArgument = "--smoke-test";

    /// <summary>The map every match is played on, until the lobby has a way to choose one.</summary>
    public const string MapId = "ridge";

    /// <summary>The number of players in every match, until the lobby has seats to fill.</summary>
    public const int PlayerCount = 2;

    // The seat of the local player in the smoke test's match.
    private const int SmokeTestPlayer = 0;

    // The smoke test runs this many ticks in a frame, so that the view follows the match as it
    // does in play, and lets the view draw this many frames at the end before it reports.
    private const int SmokeTestTicksPerFrame = 20;
    private const int SmokeTestLastFrames = 3;

    private LocalSession? _smokeSession;
    private SmokeTest? _smokeTest;
    private MatchMenu? _menu;
    private NetTest? _netTest;
    private Match? _match;
    private WorldView _world = null!;
    private RtsCamera _camera = null!;
    private UnitControl _units = null!;
    private int _lastFrames;
    private bool _quitting;

    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        var smokeTest = arguments.Contains(SmokeTestArgument);
        _world = GetNode<WorldView>("World");
        _camera = GetNode<RtsCamera>("CameraRig");
        _units = GetNode<UnitControl>("UnitControl");

        var data = LoadData();
        if (data is null)
        {
            // Without its data there is no game to show, and the smoke test fails for lack of its marker.
            GetTree().Quit(1);
            return;
        }

        if (smokeTest)
        {
            StartSmokeTest(data);
            return;
        }

        if (!StartRequest.TryRead(arguments, out var request, out var problem))
        {
            GD.PrintErr($"Command line: {problem}");
            GetTree().Quit(1);
            return;
        }

        _netTest = request.NetTestTicks > 0 ? new NetTest(request.NetTestTicks) : null;
        _menu = new MatchMenu { Name = "MatchMenu" };
        _menu.Open(data, request);
        AddChild(_menu);
    }

    public override void _Process(double delta)
    {
        if (_quitting)
        {
            return;
        }

        if (_smokeTest is not null)
        {
            RunSmokeTest(_smokeTest);
            return;
        }

        if (_menu is null)
        {
            return;
        }

        // While a page of the menu is up, the pointer and the keys are the menu's.
        var playing = !_menu.IsOpen;
        _camera.ProcessMode = playing ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _units.ProcessMode = playing ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;

        var run = _menu.Run;
        if (run is not null)
        {
            Advance(run, delta);
        }

        if (_netTest?.Update(run, _menu) is { } exitCode)
        {
            Quit(exitCode);
        }
    }

    private void Advance(MatchRun run, double delta)
    {
        // The run takes in what the network brought and then advances the session.
        run.Advance(TimeSpan.FromSeconds(delta));

        // A guest has no match until the host has described it, which is before the first tick.
        if (_match is null && run.Match is { } match)
        {
            Show(run, match);
        }

        // The world view draws after this, in the same frame, this far between the last two ticks.
        var client = run.Client;
        _world.TickFraction = client.Phase == SessionPhase.Running && client.TickInterval > TimeSpan.Zero
            ? (float)(client.TimeIntoTick / client.TickInterval)
            : 1f;
    }

    public override void _Notification(int what)
    {
        // Closing the window ends the game without leaving the tree first.
        if (what == NotificationWMCloseRequest)
        {
            _menu?.CloseRun();
        }
    }

    public override void _ExitTree() => _menu?.CloseRun();

    // Hands a match to everything that draws it or gives orders in it. The view reads the match
    // again after every tick, before anything else hears of the tick.
    private void Show(MatchRun run, Match match)
    {
        _match = match;
        ShowMap(match, run.Client.Player);
        run.Client.TickCompleted += _ => _world.AfterTick();

        _units.Attach(match, run.Client, _world);
        GetNode<DebugOverlay>("DebugOverlay").Selection = _units.Selection;
    }

    private void ShowMap(Match match, int localPlayer)
    {
        var map = match.Map;
        var size = new Vector2(map.Width, map.Height);

        // The map's corner is the world origin, so a tile's coordinates are its world position.
        var ground = GetNode<MeshInstance3D>("Ground");
        ground.Mesh = new PlaneMesh { Size = size };
        ground.Position = new Vector3(size.X / 2f, 0f, size.Y / 2f);

        // The view opens on the player's own start: the middle of where the Capitol will stand.
        _camera.SetBounds(new Rect2(Vector2.Zero, size));
        var setup = match.Players[localPlayer];
        if (setup.HasStart)
        {
            var start = map.Starts[setup.Start];
            var footprint = match.Data.Rules.Map.StartFootprint;
            _camera.CenterOn(new Vector2(start.X + (footprint / 2f), start.Y + (footprint / 2f)));
        }

        _world.Attach(match);
        GetNode<DebugOverlay>("DebugOverlay").Match = match;
    }

    private void StartSmokeTest(GameData data)
    {
        var map = data.Maps.First(candidate => candidate.Id == MapId);

        // The smoke test fixes the seed and the starts so that every machine builds the same match.
        var players = new PlayerSetup[PlayerCount];
        for (var player = 0; player < PlayerCount; player++)
        {
            var offset = (player - SmokeTestPlayer + PlayerCount) % PlayerCount;
            players[player] = new PlayerSetup((SmokeTest.LocalStart + offset) % map.Starts.Count);
        }

        GD.Print($"Match on '{map.Id}' with seed {SmokeTest.Seed}. Player {SmokeTestPlayer} has start {SmokeTest.LocalStart}. Data sha256:{data.ContentHash}.");
        var match = MatchSetup.Create(data, map, SmokeTest.Seed, players);
        _match = match;
        ShowMap(match, SmokeTestPlayer);

        _smokeSession = new LocalSession(match.Simulation, PlayerCount, SmokeTestPlayer);
        _smokeSession.Client.TickCompleted += _ => _world.AfterTick();
        _smokeSession.Client.Stopped += end => GD.PrintErr($"The session stopped: {end.Reason}.");

        // The smoke test plays its own script, so it takes no input.
        _smokeTest = new SmokeTest(match, _smokeSession);
    }

    private void RunSmokeTest(SmokeTest smokeTest)
    {
        _world.TickFraction = 1f;
        if (!smokeTest.Run(SmokeTestTicksPerFrame))
        {
            Quit(1);
        }
        else if (smokeTest.IsDone && ++_lastFrames >= SmokeTestLastFrames)
        {
            Quit(smokeTest.Report(_world.UnitCount) ? 0 : 1);
        }
    }

    private void Quit(int exitCode)
    {
        _quitting = true;
        _menu?.CloseRun();
        GetTree().Quit(exitCode);
    }

    private static GameData? LoadData()
    {
        DataLoadResult loaded;
        try
        {
            loaded = DataLoader.Load(new PackedDataFiles());
        }
        catch (IOException exception)
        {
            GD.PrintErr($"The game data could not be read. {exception.Message}");
            return null;
        }

        if (loaded.Data is null)
        {
            foreach (var error in loaded.Errors)
            {
                GD.PrintErr($"Game data: {error}");
            }

            return null;
        }

        var data = loaded.Data;
        var map = data.Maps.FirstOrDefault(candidate => candidate.Id == MapId);
        if (map is null || map.Starts.Count < PlayerCount)
        {
            GD.PrintErr($"Game data: there is no map '{MapId}' with {PlayerCount} starts.");
            return null;
        }

        return data;
    }
}
