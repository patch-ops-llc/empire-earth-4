using System.Globalization;
using Godot;
using Net;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// What the game does when it is started with <see cref="StartRequest.NetTestArgument"/> beside
/// <c>--host</c> or <c>--join</c>: it plays a short network match with nobody at the keyboard,
/// prints one line that says how the match stood at its end and quits. Two games started this
/// way, one hosting and one joining, must print the same tick and the same state hash.
/// </summary>
/// <remarks>
/// The host starts the match as soon as every seat is ready. Each game then orders its own units
/// to walk to a point near the middle of the map, through the session as a player's orders go.
/// When the match has run the number of ticks asked for, the host ends it, and every game runs to
/// the same last tick.
/// <para>
/// With <see cref="StartRequest.NetTestDesyncArgument"/> as well, the test is of a desync made on
/// purpose. The game that joins moves one of its own units by the smallest step there is, on its
/// own machine only, as a fault in the simulation would. Both games must then stop because they
/// no longer agree, long before the ticks asked for have run, and each must have written its
/// desync bundle. Each prints what the end screen says about the file and one line that starts
/// with <see cref="DesyncMarker"/> and names the bundle.
/// </para>
/// </remarks>
public sealed class NetTest
{
    public const string Marker = "NET_TEST_OK";

    public const string DesyncMarker = "NET_TEST_DESYNC";

    // Orders are given once the match has run a second, so that they travel while ticks do.
    private const int OrderAtTick = 20;

    // Far longer than the test takes. Two games that never meet must not wait for ever.
    private const double TimeoutSeconds = 120.0;

    private readonly int _ticks;
    private readonly int _desyncTick;
    private readonly ulong _startedAt = Time.GetTicksMsec();
    private bool _ordered;
    private bool _faultMade;

    /// <param name="ticks">The host ends the match when it has run this many ticks.</param>
    /// <param name="desyncTick">Above 0, the game that joins puts a fault into its state once it has run this many ticks.</param>
    public NetTest(int ticks, int desyncTick = 0)
    {
        _ticks = ticks;
        _desyncTick = desyncTick;
    }

    /// <summary>
    /// Call once a frame, after the match has advanced. Returns the code to exit with once the
    /// test is over, and null until then.
    /// </summary>
    public int? Update(MatchRun? run, MatchMenu menu)
    {
        if (run is null)
        {
            GD.PrintErr($"Net test: there is no match. {menu.Notice}");
            return 1;
        }

        if ((Time.GetTicksMsec() - _startedAt) / 1000.0 > TimeoutSeconds)
        {
            GD.PrintErr($"Net test: no result after {TimeoutSeconds} seconds, at tick {run.Match?.World.Tick ?? 0}.");
            return 1;
        }

        var client = run.Client;
        if (run is HostRun { HasStarted: false, Host.CanStart: true })
        {
            menu.StartMatch();
        }

        if (run.Match is not { } match)
        {
            return null;
        }

        if (client.Phase == SessionPhase.Running)
        {
            if (!_ordered && match.World.Tick >= OrderAtTick)
            {
                _ordered = Order(match, client);
            }

            if (_desyncTick > 0 && !_faultMade && run is JoinRun && match.World.Tick >= _desyncTick)
            {
                _faultMade = MakeFault(match, client);
            }

            if (run is HostRun && match.World.Tick >= _ticks)
            {
                // Closing ends the match for everyone and runs this game to its last tick.
                menu.CloseRun();
                return Report(run, match, "host");
            }

            return null;
        }

        if (client.Phase != SessionPhase.Stopped)
        {
            return null;
        }

        var end = client.EndInfo!;
        if (_desyncTick > 0)
        {
            return ReportDesync(run, match, end, menu);
        }

        if (end.Reason != SessionEndReason.HostEnded)
        {
            GD.PrintErr($"Net test: the match stopped at tick {match.World.Tick}: {end.Reason}. {(run as JoinRun)?.Player.Refusal}");
            menu.CloseRun();
            return 1;
        }

        menu.CloseRun();
        return Report(run, match, "guest");
    }

    private int Report(MatchRun run, Match match, string role)
    {
        if (!_ordered)
        {
            GD.PrintErr($"Net test: the match ended at tick {match.World.Tick}, before this game gave its order.");
            return 1;
        }

        if (run.Files is not { ReplayPath: not null, Problem: null } files)
        {
            GD.PrintErr($"Net test: the replay was not written. {run.Files?.Problem}");
            return 1;
        }

        var stateHash = match.World.ComputeHash().ToString("x16", CultureInfo.InvariantCulture);
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"{Marker} role={role} player={run.Client.Player} version={GameInfo.Version} map={match.Map.Id} " +
            $"tick={match.World.Tick} entities={match.Bodies.Count} dropped={match.Simulation.DroppedCommandCount} " +
            $"content_hash=sha256:{match.Data.ContentHash} state_hash={stateHash} replay={files.ReplayPath}"));
        return 0;
    }

    private static int ReportDesync(MatchRun run, Match match, SessionEndInfo end, MatchMenu menu)
    {
        var files = run.Files;
        menu.CloseRun();
        if (end.Reason != SessionEndReason.Desync)
        {
            GD.PrintErr($"Net test: the match stopped at tick {match.World.Tick} for another reason than the desync made on purpose: {end.Reason}.");
            return 1;
        }

        if (files is not { BundlePath: not null, Problem: null })
        {
            GD.PrintErr($"Net test: the desync bundle was not written. {files?.Problem}");
            return 1;
        }

        GD.Print(DesyncReport.Message(files));
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"{DesyncMarker} role={(run is HostRun ? "host" : "guest")} player={run.Client.Player} version={GameInfo.Version} " +
            $"tick={match.World.Tick} desync_tick={end.DesyncTick} bundle={files.BundlePath}"));
        return 0;
    }

    // One unit of this player is, on this machine only, the smallest step from where it should be.
    private static bool MakeFault(Match match, ClientSession client)
    {
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            ref var body = ref match.Bodies.ValueAt(i);
            if (body.Owner == client.Player)
            {
                body.Position += new FixedVector2(Fixed.Epsilon, Fixed.Zero);
                return true;
            }
        }

        return false;
    }

    // Every unit of this player walks to a point beside the middle of the map, a different one
    // for each seat.
    private static bool Order(Match match, ClientSession client)
    {
        var units = new List<EntityId>();
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            if (match.Bodies.ValueAt(i).Owner == client.Player)
            {
                units.Add(match.Bodies.EntityAt(i));
            }
        }

        var x = Fixed.FromRaw(((match.Map.Width / 2) + (client.Player * 3) - 1) * Fixed.RawOne);
        var y = Fixed.FromRaw(match.Map.Height / 2 * Fixed.RawOne);
        return client.Submit(new Command(CommandType.Move, client.Player, units.ToArray(), x, y));
    }
}
