using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Godot;
using Net;
using Sim;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Serialization;
using Sim.Setup;

namespace Game;

/// <summary>
/// The frame test: a match with armies made by the development spawn, shown as any match is and
/// played at the pace of play, in which the <see cref="FrameClock"/> times every part of the
/// game's own code in every frame. At the end it prints what the parts took, holds the sum
/// against the frame budget, and the game quits. A person runs it; CI does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The budget</b> (technical plan, section 6). At 60 frames a second a frame has 16.7 ms.
/// With 500 units, all of the game's code outside the tick may take 4 ms of a frame on average
/// and 8 ms at the 99th percentile. The tick has its own budget, 8 ms on average and 25 ms at
/// worst. The frame test prints the tick as the game runs it, which includes the first seconds
/// after the game has started, when the runtime has not yet compiled the code well.
/// </para>
/// <para>
/// <b>What it cannot tell.</b> It times the game's code, not the engine's and not the drawing.
/// What the renderer is asked for in a frame is printed, for a run in a window. A time over the
/// budget does not fail the test, because times depend on the machine: read the line that says
/// so. The test fails when the match could not be played as scripted.
/// </para>
/// <para>
/// <b>The pace.</b> Every frame gives the session a sixtieth of a second, however long the frame
/// really took, so a tick runs in every third frame, as at 60 frames a second, and the same
/// frames are measured on a fast machine, on a slow one, in a window and headless.
/// </para>
/// <para>
/// <b>The match.</b> The map, the seed and the starts are the smoke test's. At tick 1 the armies
/// are spawned around the middle of the map, half of them melee, three in ten ranged and two in
/// ten firing shells. At tick 20 they get their orders, the player's units are selected and the
/// camera looks at the middle from as far as it goes. The timing begins at tick 40 and lasts
/// 45 seconds unless told otherwise. Every three seconds the selection is dropped and a box is
/// drawn over the whole window, which is what a player does many times in a match. The match
/// writes its replay as a match played alone does, since that is part of what a frame costs.
/// </para>
/// </remarks>
public sealed class FrameTest : IDisposable
{
    /// <summary>After <c>--</c> on the command line, with the scenario after it: runs the frame test and quits.</summary>
    public const string Argument = "--frame-test";

    /// <summary>The number of units, in all. 500 if left out.</summary>
    public const string UnitsArgument = "--frame-units";

    /// <summary>The seconds of play that are timed. 45 if left out.</summary>
    public const string SecondsArgument = "--frame-seconds";

    /// <summary>The last line the test prints. Check for it: Godot exits with 0 even when a script fails to load.</summary>
    public const string Marker = "FRAME_TEST_DONE";

    /// <summary>The number of units the budget is set for.</summary>
    public const int BudgetUnits = 500;

    /// <summary>The budget of the game's code outside the tick, in milliseconds a frame: on average and at the 99th percentile.</summary>
    public const double BudgetAverage = 4.0;
    public const double BudgetP99 = 8.0;

    /// <summary>The budget of the tick, in milliseconds: on average and at worst.</summary>
    public const double TickBudgetAverage = 8.0;
    public const double TickBudgetWorst = 25.0;

    private const int FramesPerSecond = 60;
    private const int DefaultSeconds = 45;
    private const int LocalPlayer = 0;

    private const int SpawnAt = 1;
    private const int OrdersAt = 20;
    private const int TimeFrom = 40;

    // The selection is dropped and boxed again this often, first half this long after the timing began.
    private const int FramesBetweenSelections = 3 * FramesPerSecond;

    // Tiles between the middle of the map and the nearest unit of either army.
    private const int GapToMiddle = 9;

    private static readonly string[] Scenarios = ["fight", "own", "march"];

    // A sixtieth of a second, rounded up, so that three of them are never short of a tick.
    private static readonly TimeSpan FrameTime = TimeSpan.FromTicks((TimeSpan.TicksPerSecond + FramesPerSecond - 1) / FramesPerSecond);

    private readonly string _scenario;
    private readonly int _units;
    private readonly int _frames;
    private readonly Match _match;
    private readonly MatchFiles _files;
    private readonly SmokeLog _log = new();
    private readonly List<(double Milliseconds, int Tick)> _ticks = [];
    private readonly List<double> _boxes = [];
    private MatchView? _view;
    private RtsCamera? _camera;
    private UnitControl? _control;
    private FrameSpan _tick;
    private FrameSpan _heard;
    private bool _spawned;
    private bool _ordered;
    private bool _timing;
    private bool _failed;
    private bool _disposed;
    private int _unitsAtStart;
    private int _mostDrawn;
    private int _mostSelected;
    private int _mostBoxed;
    private long _allocatedAtStart;
    private int _collectionsAtStart;
    private int _fullCollectionsAtStart;
    private double _sumObjects;
    private double _sumCalls;
    private double _sumPrimitives;
    private double _mostObjects;
    private double _mostCalls;
    private double _mostPrimitives;

    private FrameTest(GameData data, MapDef map, string scenario, int units, int seconds)
    {
        _scenario = scenario;
        _units = units;
        _frames = seconds * FramesPerSecond;

        var players = new PlayerSetup[Main.PlayerCount];
        for (var player = 0; player < players.Length; player++)
        {
            players[player] = new PlayerSetup((SmokeTest.LocalStart + player) % map.Starts.Count);
        }

        var description = new MatchDescription(GameInfo.Version, data.ContentHash, map.Id, SmokeTest.Seed, players);

        // The clock must hear of a tick before anything else does, to tell the tick from what
        // follows it. So the run is made without its files, the clock listens, and then the files
        // are opened here: the replay is written exactly as a match played alone writes it.
        Run = new LocalRun(description, data, LocalPlayer, keepFiles: false);
        _match = Run.Match;
        Run.Client.TickCompleted += TickEnded;
        Run.Client.Stopped += end =>
        {
            if (!_disposed)
            {
                Fail($"the session stopped: {end.Reason}.");
            }
        };
        _files = new MatchFiles(Run.Client, description, _match, LocalPlayer);

        // As in the smoke test, an error the engine logs fails the test: among them every
        // exception that Godot catches and logs, after which the game would go on without what threw it.
        OS.AddLogger(_log);
    }

    /// <summary>The test's match, to be shown as any match is.</summary>
    public LocalRun Run { get; }

    /// <summary>How far the match is between its last two ticks, for the world view.</summary>
    public float TickFraction
    {
        get
        {
            var client = Run.Client;
            return client.Phase == SessionPhase.Running && client.TickInterval > TimeSpan.Zero
                ? (float)(client.TimeIntoTick / client.TickInterval)
                : 1f;
        }
    }

    /// <summary>
    /// Reads the test's arguments and builds its match. False, with what is wrong, when an
    /// argument makes no sense or the data does not allow the development spawn.
    /// </summary>
    public static bool TryRead(string[] arguments, GameData data, out FrameTest? test, out string problem)
    {
        test = null;
        var scenario = After(arguments, Argument) ?? Scenarios[0];
        if (!Scenarios.Contains(scenario))
        {
            problem = $"there is no scenario '{scenario}'. There are: {string.Join(", ", Scenarios)}.";
            return false;
        }

        var units = BudgetUnits;
        if (After(arguments, UnitsArgument) is { } unitsText
            && (!int.TryParse(unitsText, NumberStyles.None, CultureInfo.InvariantCulture, out units) || units < 2))
        {
            problem = $"{UnitsArgument} takes a number of units, 2 or more, not '{unitsText}'.";
            return false;
        }

        var seconds = DefaultSeconds;
        if (After(arguments, SecondsArgument) is { } secondsText
            && (!int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds < 1))
        {
            problem = $"{SecondsArgument} takes a whole number of seconds, 1 or more, not '{secondsText}'.";
            return false;
        }

        if (!data.Rules.Debug.Spawn)
        {
            problem = "the data does not allow the development spawn, which makes the test's armies.";
            return false;
        }

        var map = data.Maps.First(candidate => candidate.Id == Main.MapId);
        test = new FrameTest(data, map, scenario, units, seconds);
        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// Gives the test the view of its match. Call it once the match is shown, so that the clock
    /// is also the last to hear of a tick.
    /// </summary>
    public void Attach(MatchView view, RtsCamera camera, UnitControl control)
    {
        _view = view;
        _camera = camera;
        _control = control;
        Run.Client.TickCompleted += AllHeard;
        GD.Print($"Frame test '{_scenario}' with {_units} units, {_frames / FramesPerSecond} seconds of play.");
    }

    /// <summary>
    /// Runs one frame of the test. Call it first thing in every frame, in place of advancing the
    /// match. False once the test is over: then <see cref="Report"/> is due.
    /// </summary>
    public bool RunFrame()
    {
        FrameClock.NextFrame();
        if (_view is not { } view || _control is not { } control)
        {
            Fail("the match could not be shown.");
            return false;
        }

        if (_log.Errors > 0)
        {
            Fail($"the engine logged an error while the match was shown, {_log.Errors} so far. The first: {_log.First}");
        }

        if (_failed)
        {
            return false;
        }

        var tick = _match.World.Tick;
        if (!_spawned && tick >= SpawnAt)
        {
            _spawned = true;
            Spawn(view);
        }

        if (!_ordered && tick >= OrdersAt)
        {
            _ordered = true;
            GiveOrders(view);
        }

        if (!_timing && tick >= TimeFrom)
        {
            _timing = true;
            _unitsAtStart = _match.Movers.Count;
            _allocatedAtStart = GC.GetTotalAllocatedBytes();
            _collectionsAtStart = GC.CollectionCount(0);
            _fullCollectionsAtStart = GC.CollectionCount(2);
            FrameClock.Start();
        }

        if (_timing)
        {
            if (FrameClock.FrameCount >= _frames)
            {
                return false;
            }

            ReadTheRenderer(view);
            if (FrameClock.FrameCount % FramesBetweenSelections == FramesBetweenSelections / 2)
            {
                SelectAgain(view, control);
            }
        }

        // The tick is timed from here until the clock hears of it. That includes what the session
        // does before it runs the tick, which with no network is next to nothing.
        var session = FrameClock.Begin();
        _tick = FrameClock.Begin();
        Run.Advance(FrameTime);
        FrameClock.End("session: the advance outside the tick", session);
        return !_failed;
    }

    /// <summary>Prints what the frames took and how that stands to the budget. False when the test failed.</summary>
    public bool Report()
    {
        FrameClock.Stop();
        if (_failed || !_timing || FrameClock.FrameCount == 0)
        {
            if (!_failed)
            {
                Fail($"the match stopped short, at tick {_match.World.Tick}.");
            }

            return false;
        }

        var dropped = _match.Simulation.DroppedCommandCount;
        if (dropped != 0)
        {
            Fail($"the simulation dropped {dropped} of the test's orders.");
            return false;
        }

        var frames = FrameClock.FrameCount;
        var optimized = typeof(SimInfo).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };
        Print($"Frame test '{_scenario}': {_units} units asked for, {_unitsAtStart} in the match when the timing began and {_match.Movers.Count} at tick {_match.World.Tick}. At most {_mostDrawn} were drawn and {_mostSelected} selected. {frames} frames of a sixtieth of a second, a tick in every third. Simulation optimized: {(optimized ? "true" : "false")}.");
        GD.Print(FrameClock.Report());

        var ordered = _ticks.OrderBy(tick => tick.Milliseconds).ToList();
        var tickAverage = ordered.Count > 0 ? ordered.Average(tick => tick.Milliseconds) : 0.0;
        var tickP99 = ordered.Count > 0 ? ordered[Math.Max(0, (int)Math.Ceiling(ordered.Count * 0.99) - 1)].Milliseconds : 0.0;
        var tickWorst = ordered.Count > 0 ? ordered[^1] : default;
        Print($"Ticks as the game ran them: {ordered.Count}, average {tickAverage:F3} ms, p99 {tickP99:F3} ms, worst {tickWorst.Milliseconds:F3} ms at tick {tickWorst.Tick}.");
        if (_boxes.Count > 0)
        {
            Print($"Box selections: {_boxes.Count}, taking at most {_mostBoxed} units, average {_boxes.Average():F3} ms, worst {_boxes.Max():F3} ms each, with all that hears of the selection.");
        }

        Print($"Memory: {(GC.GetTotalAllocatedBytes() - _allocatedAtStart) / 1024.0 / frames:F1} KB allocated a frame by the whole process, {GC.CollectionCount(0) - _collectionsAtStart} collections, {GC.CollectionCount(2) - _fullCollectionsAtStart} of them full.");
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("Asked of the renderer: nothing, in a headless run. Run it in a window to see what is drawn.");
        }
        else
        {
            Print($"Asked of the renderer a frame ({RenderingServer.GetCurrentRenderingMethod()}, {RenderingServer.GetVideoAdapterName()}): draw calls average {_sumCalls / frames:F0} most {_mostCalls:F0}, primitives average {_sumPrimitives / frames:F0} most {_mostPrimitives:F0}, objects average {_sumObjects / frames:F0} most {_mostObjects:F0}.");
        }
        Print($"State hash at tick {_match.World.Tick}: {_match.World.ComputeHash():x16}.");

        var own = FrameClock.Of(FrameClock.IsOwnCode);
        Print($"frame test: the game's code outside the tick took {own.Average:F2} ms of a frame on average (budget {BudgetAverage:F0}) and {own.P99:F2} ms at the 99th percentile (budget {BudgetP99:F0}); the tick {tickAverage:F2} ms on average (budget {TickBudgetAverage:F0}) and {tickWorst.Milliseconds:F2} ms at worst (budget {TickBudgetWorst:F0}).");

        var over = new List<string>();
        if (own.Average > BudgetAverage)
        {
            over.Add("the game's code outside the tick on average");
        }

        if (own.P99 > BudgetP99)
        {
            over.Add("the game's code outside the tick at the 99th percentile");
        }

        if (tickAverage > TickBudgetAverage)
        {
            over.Add("the tick on average");
        }

        if (tickWorst.Milliseconds > TickBudgetWorst)
        {
            over.Add("the tick at worst");
        }

        if (!optimized)
        {
            GD.Print("frame test: measured, not held. This build's simulation is not optimized, as the editor builds it, so these are not the game's times.");
        }
        else if (_units != BudgetUnits)
        {
            Print($"frame test: measured, not held. The budget is for {BudgetUnits} units and this run asked for {_units}.");
        }
        else if (over.Count > 0)
        {
            GD.Print($"frame test: over the budget on this machine: {string.Join(", ", over)}.");
        }
        else
        {
            GD.Print("frame test: within the budget on this machine.");
        }

        GD.Print(Marker);
        return true;
    }

    /// <summary>Closes the replay and the match.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FrameClock.Stop();
        OS.RemoveLogger(_log);
        _files.Dispose();
        Run.Dispose();
    }

    private static string? After(string[] arguments, string name)
    {
        var at = Array.IndexOf(arguments, name);
        return at >= 0 && at + 1 < arguments.Length && !arguments[at + 1].StartsWith("--", StringComparison.Ordinal)
            ? arguments[at + 1]
            : null;
    }

    private static void Print(FormattableString text) => GD.Print(text.ToString(CultureInfo.InvariantCulture));

    // The first to hear of a tick: the tick is over, and what hears of it begins.
    private void TickEnded(int tick)
    {
        if (!FrameClock.On)
        {
            return;
        }

        if (_tick.Started != 0)
        {
            _ticks.Add(((Stopwatch.GetTimestamp() - _tick.Started) * 1000.0 / Stopwatch.Frequency, tick));
        }

        FrameClock.End(FrameClock.TickPart, _tick);
        _heard = FrameClock.Begin();
    }

    // The last to hear of a tick. The view has timed its own parts in between. What is left is
    // the replay's recording and whatever else listens to the session.
    private void AllHeard(int tick)
    {
        if (!FrameClock.On)
        {
            return;
        }

        FrameClock.End("after tick: the replay and whatever else hears of a tick", _heard);
        _heard = default;

        // Should the session run another tick in this frame, it is timed from here.
        _tick = FrameClock.Begin();
    }

    private void ReadTheRenderer(MatchView view)
    {
        var objects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        var calls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        _sumObjects += objects;
        _sumCalls += calls;
        _sumPrimitives += primitives;
        _mostObjects = Math.Max(_mostObjects, objects);
        _mostCalls = Math.Max(_mostCalls, calls);
        _mostPrimitives = Math.Max(_mostPrimitives, primitives);
        _mostDrawn = Math.Max(_mostDrawn, view.World.UnitCount);
        _mostSelected = Math.Max(_mostSelected, view.Selection.Count);
    }

    // What a player does many times in a match: lets go of the army and boxes it again.
    private void SelectAgain(MatchView view, UnitControl control)
    {
        var started = Stopwatch.GetTimestamp();
        var span = FrameClock.Begin();
        view.Selection.Clear();
        control.SelectWholeWindow();
        FrameClock.End("input: the selection dropped and boxed again", span);
        _boxes.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        _mostBoxed = Math.Max(_mostBoxed, view.Selection.Count);
    }

    private void Spawn(MatchView view)
    {
        var chassis = _match.Data.Chassis;
        var melee = chassis.FirstOrDefault(candidate => candidate.Attack.Kind == AttackKind.Melee);
        var ranged = chassis.FirstOrDefault(candidate => candidate.Attack is { Kind: AttackKind.Ranged, MinRange: null, BlastRadius: null });
        var shells = chassis.FirstOrDefault(candidate => candidate.Attack is { Kind: AttackKind.Ranged, BlastRadius: not null });
        ChassisDef?[] wanted = [melee, melee, melee, melee, melee, ranged, ranged, ranged, shells, shells];
        ChassisDef[] mix = [.. wanted.OfType<ChassisDef>()];
        if (mix.Length == 0)
        {
            Fail("the data has no chassis that fights.");
            return;
        }

        // Each army stands on the tiles nearest the middle of the map, on its own side of it.
        var grid = _match.Grid;
        var (middleX, middleY) = (grid.Width / 2, grid.Height / 2);
        var sides = _scenario == "own" ? 1 : Main.PlayerCount;
        for (var side = 0; side < sides; side++)
        {
            var player = (view.Player + side) % Main.PlayerCount;
            var tiles = new List<(int X, int Y)>();
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var onItsSide = sides == 1 || (side == 0 ? x <= middleX - GapToMiddle : x >= middleX + GapToMiddle);
                    if (onItsSide && grid.IsPassable(x, y))
                    {
                        tiles.Add((x, y));
                    }
                }
            }

            tiles = [.. tiles
                .OrderBy(tile => Math.Max(Math.Abs(tile.X - middleX), Math.Abs(tile.Y - middleY) * 2))
                .ThenBy(tile => tile.Y)
                .ThenBy(tile => tile.X)];
            var count = Math.Min(_units / sides, tiles.Count);
            for (var i = 0; i < count; i++)
            {
                var point = MapGrid.TileCenter(tiles[i].X, tiles[i].Y);
                Submit(view, new Command(CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: mix[i % mix.Length].Index));
            }
        }
    }

    private void GiveOrders(MatchView view)
    {
        var own = new List<EntityId>();
        var other = new List<EntityId>();
        for (var i = 0; i < _match.Combatants.Count; i++)
        {
            var entity = _match.Combatants.EntityAt(i);
            if (_match.Movers.Contains(entity) && _match.Bodies.TryGet(entity, out var body))
            {
                (body.Owner == view.Player ? own : other).Add(entity);
            }
        }

        if (own.Count == 0)
        {
            Fail("the development spawn made no unit for the player.");
            return;
        }

        // The whole of the player's army is selected, as for one order to all of it, and the
        // camera looks at the field from as far as it goes.
        var grid = _match.Grid;
        view.Selection.Set(own);
        _camera?.CenterOn(new Vector2(grid.Width / 2f, grid.Height / 2f));
        _camera?.ZoomTo(_camera.MaxDistance);

        var middle = MapGrid.TileCenter(grid.Width / 2, grid.Height / 2);
        var north = MapGrid.TileCenter(grid.Width / 2, grid.Height / 6);
        var south = MapGrid.TileCenter(grid.Width / 2, grid.Height * 5 / 6);
        var enemy = (view.Player + 1) % Main.PlayerCount;
        switch (_scenario)
        {
            case "fight":
                // Both armies attack toward the middle and meet there.
                Submit(view, new Command(CommandType.AttackMove, view.Player, [.. own], middle.X, middle.Y));
                Submit(view, new Command(CommandType.AttackMove, enemy, [.. other], middle.X, middle.Y));
                break;
            case "own":
                // One army, all of it the player's and all of it selected, on the move.
                Submit(view, new Command(CommandType.Move, view.Player, [.. own], north.X, north.Y));
                break;
            default:
                // Two armies that move past each other and do not fight.
                Submit(view, new Command(CommandType.Move, view.Player, [.. own], north.X, north.Y));
                Submit(view, new Command(CommandType.Move, enemy, [.. other], south.X, south.Y));
                break;
        }
    }

    // The player's orders leave through the player's side of the session, as in play. The other
    // army has nobody at its seat, so its orders are given to the host directly.
    private void Submit(MatchView view, in Command command)
    {
        var taken = command.Player == view.Player ? Run.Client.Submit(command) : Run.Session.Host.Submit(command);
        if (!taken)
        {
            Fail($"the session refused a {command.Type} order at tick {_match.World.Tick}.");
        }
    }

    private void Fail(string reason)
    {
        if (!_failed)
        {
            _failed = true;
            GD.PrintErr($"Frame test: {reason}");
        }
    }
}
