using Godot;
using Sim.Data;
using Sim.Setup;
using Entropy = System.Security.Cryptography.RandomNumberGenerator;

namespace Game;

/// <summary>
/// Root of <c>scenes/main.tscn</c>, the scene the game starts in. It loads the game data, builds one
/// match on the map <see cref="MapId"/> and hands it to the world view, the camera and the overlay.
/// </summary>
/// <remarks>
/// Code under <c>game/</c> reads the match and never changes it. Only commands change the
/// simulation, and only the session runs its ticks.
/// </remarks>
public partial class Main : Node3D
{
    /// <summary>
    /// Passed after <c>--</c> on the command line, this makes the game run the smoke test and quit.
    /// Exported builds refuse a scene path on the command line, so this is how an export is tested.
    /// </summary>
    public const string SmokeTestArgument = "--smoke-test";

    /// <summary>The map every match is played on, until there is a lobby to choose one in.</summary>
    public const string MapId = "ridge";

    /// <summary>The seat of the person at this machine.</summary>
    public const int LocalPlayer = 0;

    private const int PlayerCount = 2;

    // The smoke test lets the view draw this many frames before it reports.
    private const int SmokeTestFrames = 3;

    private Match? _match;
    private WorldView _world = null!;
    private bool _smokeTest;
    private int _frames;

    public override void _Ready()
    {
        _smokeTest = OS.GetCmdlineUserArgs().Contains(SmokeTestArgument);
        _world = GetNode<WorldView>("World");

        _match = CreateMatch(_smokeTest, out var localStart);
        if (_match is null)
        {
            // Without its data there is no game to show, and the smoke test fails for lack of its marker.
            GetTree().Quit(1);
            return;
        }

        var map = _match.Map;
        var size = new Vector2(map.Width, map.Height);

        // The map's corner is the world origin, so a tile's coordinates are its world position.
        var ground = GetNode<MeshInstance3D>("Ground");
        ground.Mesh = new PlaneMesh { Size = size };
        ground.Position = new Vector3(size.X / 2f, 0f, size.Y / 2f);

        // The view opens on the player's own start: the middle of where the Capitol will stand.
        var start = map.Starts[localStart];
        var footprint = _match.Data.Rules.Map.StartFootprint;
        var camera = GetNode<RtsCamera>("CameraRig");
        camera.SetBounds(new Rect2(Vector2.Zero, size));
        camera.CenterOn(new Vector2(start.X + (footprint / 2f), start.Y + (footprint / 2f)));

        _world.Attach(_match);
        GetNode<DebugOverlay>("DebugOverlay").Match = _match;
    }

    public override void _Process(double delta)
    {
        if (!_smokeTest || _match is null || ++_frames < SmokeTestFrames)
        {
            return;
        }

        _smokeTest = false;
        GetTree().Quit(SmokeTest.Report(_match, _world.UnitCount) ? 0 : 1);
    }

    // The seed and the start each player gets are chosen here, outside the simulation, which rolls
    // nothing by itself (design 5.2). The smoke test fixes both so that every machine builds the
    // same match.
    private static Match? CreateMatch(bool smokeTest, out int localStart)
    {
        localStart = 0;

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

        var seed = smokeTest ? SmokeTest.Seed : BitConverter.ToUInt64(Entropy.GetBytes(sizeof(ulong)));
        localStart = smokeTest ? SmokeTest.LocalStart : Entropy.GetInt32(map.Starts.Count);

        // The local player takes the drawn start and the other players the ones after it.
        var players = new PlayerSetup[PlayerCount];
        for (var player = 0; player < PlayerCount; player++)
        {
            var offset = (player - LocalPlayer + PlayerCount) % PlayerCount;
            players[player] = new PlayerSetup((localStart + offset) % map.Starts.Count);
        }

        GD.Print($"Match on '{map.Id}' with seed {seed}. Player {LocalPlayer} has start {localStart}. Data sha256:{data.ContentHash}.");
        return MatchSetup.Create(data, map, seed, players);
    }
}
