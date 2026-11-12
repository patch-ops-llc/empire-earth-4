using System.Globalization;
using Godot;
using Net;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// What the game does when it is started with <see cref="StartRequest.NetTestArgument"/> beside
/// <c>--host</c> or <c>--join</c>: with nobody at the keyboard it plays two short network matches,
/// one after the other, prints one line that says how the first stood at its end, and quits. Two
/// games started this way, one hosting and one joining, must print the same tick and the same
/// state hash.
/// </summary>
/// <remarks>
/// <para>
/// The first match is one that goes well. The host starts it as soon as every seat is ready.
/// Each game then orders its own units to walk to a point near the middle of the map, through
/// the session as a player's orders go. When the match has run the number of ticks asked for,
/// the host ends it, and every game runs to the same last tick.
/// </para>
/// <para>
/// The second match is one that goes wrong on purpose. Each game goes back to the first page of
/// the menu, as a player does between two matches, and hosts or joins again. The game that joins
/// then moves one of its own units by the smallest step there is, on its own machine only, as a
/// fault in the simulation would. Both games must stop because they no longer agree, and each
/// must have written its desync bundle. Each game reads its own bundle back: the host's must say
/// that the host repeated its own run, and the other's that it did not, from the tick after the
/// fault and in the store the fault was put in. Each prints what the end screen says about the
/// file and one line that starts with <see cref="DesyncMarker"/> and names the bundle.
/// </para>
/// <para>
/// The line that starts with <see cref="Marker"/> is printed last, and only when both matches
/// went as they should. The files of the second match go to a folder of their own, the folder
/// of match files with <see cref="DesyncFolderEnding"/> added to its name: the replay of the
/// game that was made to go wrong does not give the same match again.
/// </para>
/// </remarks>
public sealed class NetTest
{
    public const string Marker = "NET_TEST_OK";

    public const string DesyncMarker = "NET_TEST_DESYNC";

    /// <summary>Added to the name of the folder of match files for the match that is made to fail.</summary>
    public const string DesyncFolderEnding = "-desync";

    // Orders are given once the match has run a second, so that they travel while ticks do.
    private const int OrderAtTick = 20;

    // The fault is put in while the units walk, between two of the games' comparisons.
    private const int FaultAtTick = 30;

    // The games compare their states once a second, so a fault is noticed a second or two after
    // it is made. A match that runs this much longer has not noticed it.
    private const int NoticedWithinTicks = 400;

    // Far longer than a match of the test takes. Two games that never meet must not wait for ever.
    private const double TimeoutSeconds = 120.0;

    // The second match starts when the host has opened its port again and the other game has
    // found it there. A game that finds no match tries again after this long.
    private const ulong TryAgainAfterMsec = 250;

    // The first page of the menu is always a new scene, and so is the second match. This is what
    // the test knows from before it: the line about the first match, and when that match ended.
    private static string? s_firstResult;
    private static ulong s_firstEndedAt;

    private readonly int _ticks;
    private readonly ulong _startedAt = Time.GetTicksMsec();
    private bool _ordered;
    private int _faultAfter = -1;
    private ulong? _noMatchSince;
    private bool _leavingScene;

    /// <param name="ticks">The host ends the first match when it has run this many ticks.</param>
    public NetTest(int ticks)
    {
        _ticks = ticks;
    }

    /// <summary>
    /// Call once a frame, after the match has advanced. Returns the code to exit with once the
    /// test is over, and null until then.
    /// </summary>
    public int? Update(MatchRun? run, MatchMenu menu)
    {
        // The scene of the first match is on its way out, and the next one has a test of its own.
        if (_leavingScene)
        {
            return null;
        }

        var second = s_firstResult is not null;
        var waited = (Time.GetTicksMsec() - (second ? s_firstEndedAt : _startedAt)) / 1000.0;
        if (waited > TimeoutSeconds)
        {
            GD.PrintErr(run?.Match is { } late
                ? $"Net test: no result after {TimeoutSeconds} seconds, at tick {late.World.Tick} of the {(second ? "second" : "first")} match."
                : $"Net test: no {(second ? "second " : string.Empty)}match after {TimeoutSeconds} seconds. {menu.Notice}");
            return 1;
        }

        if (run is null)
        {
            if (!second)
            {
                GD.PrintErr($"Net test: there is no match. {menu.Notice}");
                return 1;
            }

            return TryAgain(menu);
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

            if (!second)
            {
                if (run is HostRun && match.World.Tick >= _ticks)
                {
                    // Closing ends the match for everyone and runs this game to its last tick.
                    menu.CloseRun();
                    return EndFirst(run, match, "host", menu);
                }

                return null;
            }

            if (run is JoinRun && _faultAfter < 0 && match.World.Tick >= FaultAtTick && MakeFault(match, client))
            {
                _faultAfter = match.World.Tick;
            }

            if (match.World.Tick >= FaultAtTick + NoticedWithinTicks)
            {
                GD.PrintErr($"Net test: the second match has run {match.World.Tick} ticks, and the games have not noticed the fault that was put into one of them after {FaultAtTick}.");
                menu.CloseRun();
                return 1;
            }

            return null;
        }

        if (client.Phase != SessionPhase.Stopped)
        {
            return null;
        }

        var end = client.EndInfo!;
        if (second)
        {
            return EndSecond(run, match, end, menu);
        }

        if (end.Reason != SessionEndReason.HostEnded)
        {
            GD.PrintErr($"Net test: the match stopped at tick {match.World.Tick}: {end.Reason}. {(run as JoinRun)?.Player.Refusal}");
            menu.CloseRun();
            return 1;
        }

        menu.CloseRun();
        return EndFirst(run, match, "guest", menu);
    }

    // The first match is over. Its line is kept, and the game goes on to the second.
    private int? EndFirst(MatchRun run, Match match, string role, MatchMenu menu)
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
        var result = string.Create(CultureInfo.InvariantCulture,
            $"role={role} player={run.Client.Player} version={GameInfo.Version} map={match.Map.Id} " +
            $"tick={match.World.Tick} entities={match.Bodies.Count} dropped={match.Simulation.DroppedCommandCount} " +
            $"content_hash=sha256:{match.Data.ContentHash} state_hash={stateHash} replay={files.ReplayPath}");
        GD.Print($"Net test: the first match is over: {result}. Now a second one, in which the games are made to disagree.");

        s_firstResult = $"{Marker} {result}";
        s_firstEndedAt = Time.GetTicksMsec();
        MatchFiles.FolderEnding = DesyncFolderEnding;
        _leavingScene = true;
        menu.FollowRequestAgain();
        return null;
    }

    // No second match yet: the host's port was not open yet, or not free yet. The menu is asked
    // again, a moment later.
    private int? TryAgain(MatchMenu menu)
    {
        var now = Time.GetTicksMsec();
        _noMatchSince ??= now;
        if (now - _noMatchSince < TryAgainAfterMsec)
        {
            return null;
        }

        GD.Print($"Net test: no second match yet, so this game asks for it again. {menu.Notice}");
        _leavingScene = true;
        menu.FollowRequestAgain();
        return null;
    }

    // The second match has stopped. It went as it should if it stopped because the games no
    // longer agreed, and this game's bundle says of it what this game knows to be true.
    private int EndSecond(MatchRun run, Match match, SessionEndInfo end, MatchMenu menu)
    {
        var files = run.Files;
        menu.CloseRun();
        if (end.Reason != SessionEndReason.Desync)
        {
            GD.PrintErr($"Net test: the second match stopped at tick {match.World.Tick} for another reason than the desync made on purpose: {end.Reason}.");
            return 1;
        }

        if (run is JoinRun && _faultAfter < 0)
        {
            GD.PrintErr($"Net test: the second match stopped at tick {match.World.Tick} because the games no longer agreed, before this game had put its fault in. That desync is a real one.");
            return 1;
        }

        if (files is not { BundlePath: { } path, Problem: null })
        {
            GD.PrintErr($"Net test: the desync bundle was not written. {files?.Problem}");
            return 1;
        }

        if (WhatIsWrongWith(path, run, match, end) is { } wrong)
        {
            GD.PrintErr($"Net test: the desync bundle {path} is not what it should be: {wrong}");
            return 1;
        }

        GD.Print(DesyncReport.Message(files));
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"{DesyncMarker} role={(run is HostRun ? "host" : "guest")} player={run.Client.Player} version={GameInfo.Version} " +
            $"tick={match.World.Tick} desync_tick={end.DesyncTick} bundle={path}"));
        GD.Print(s_firstResult);
        return 0;
    }

    // Reads the bundle as the desync-diff tool does and compares it with what this game knows:
    // which seat it had, how the match ended, and where the fault was put in, if it was here.
    private string? WhatIsWrongWith(string path, MatchRun run, Match match, SessionEndInfo end)
    {
        DesyncBundle bundle;
        try
        {
            bundle = DesyncBundle.Read(File.ReadAllBytes(path));
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"it cannot be read. {failed.Message}";
        }

        if (bundle.Player != run.Client.Player || bundle.DesyncTick != end.DesyncTick || bundle.FinalTick != end.FinalTick)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"it is of player {bundle.Player} and a match that failed its comparison after {bundle.DesyncTick} ticks and stopped after {bundle.FinalTick}, and this game is player {run.Client.Player}, failed after {end.DesyncTick} and stopped after {end.FinalTick}.");
        }

        if (bundle.MachineValue("game_build") != GameInfo.Version || string.IsNullOrEmpty(bundle.MachineValue("operating_system")))
        {
            return "it does not name this build and the operating system.";
        }

        if (bundle.AgreedSnapshot is null || bundle.AgreedTick > end.DesyncTick - bundle.HashInterval)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"it has no state from before the comparison that failed after {end.DesyncTick} ticks: {bundle.SecondRun.Detail}");
        }

        var again = bundle.SecondRun;
        if (run is HostRun)
        {
            // Nothing was done to this game, so run again it must give what it gave.
            return again.Outcome == SecondRunOutcome.Repeated
                ? null
                : string.Create(CultureInfo.InvariantCulture, $"it should say that this game repeated its own run, and says: {again.Outcome}, {again.PartedAt}, {again.Detail}");
        }

        // The states were the same up to the tick the fault was made after, and the unit that was
        // moved makes the next one differ, in the store that holds where units are.
        var first = _faultAfter + 1;
        var store = match.Bodies.Name;
        if (again.Outcome != SecondRunOutcome.Parted || again.PartedAt != first || again.Detail != store)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"it should say that this game did not repeat its own run, first after {first} ticks and in '{store}', and says: {again.Outcome}, {again.PartedAt}, {again.Detail}");
        }

        if (bundle.StateAt(first) is null || bundle.HashesAt(first) is null || bundle.HashesAt(_faultAfter) is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"it lacks the state or the hashes of the first tick that differs, {first}, or the hashes of the tick before.");
        }

        // The games compare their states at every multiple of the interval: the first after the fault fails.
        var interval = bundle.HashInterval;
        var expected = (first + interval - 1) / interval * interval;
        return end.DesyncTick == expected
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"the fault was made after {_faultAfter} ticks, so the comparison after {expected} should have failed, and the one after {end.DesyncTick} did.");
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
