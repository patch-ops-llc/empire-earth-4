using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Net;
using Net.Udp;
using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Game;

/// <summary>
/// One match on this machine, from the lobby to its end, however it is driven: alone, as the
/// host of a network match, or as a guest in someone else's. The rest of the game sees a match
/// to read and a <see cref="ClientSession"/> to give orders through, and calls
/// <see cref="Advance"/> once a frame.
/// </summary>
public abstract class MatchRun : IDisposable
{
    /// <summary>The local player's side of the session.</summary>
    public abstract ClientSession Client { get; }

    /// <summary>The match. A guest has none until the host has described it.</summary>
    public abstract Match? Match { get; }

    public abstract MatchDescription? Description { get; }

    /// <summary>The files of this match, once the match is built.</summary>
    public MatchFiles? Files { get; protected set; }

    /// <summary>
    /// The seats the computer plays, on the machine that hosts the match or plays it alone. Null
    /// on a guest's machine, which is not told what sits at a seat.
    /// </summary>
    public ComputerSeats? Computers { get; protected set; }

    /// <summary>
    /// True when this machine sets the pace of the match: when it hosts it or plays it alone
    /// (design: pause and speed are the host's). A guest is told the pace and cannot change it.
    /// </summary>
    public bool CanSetPace => PaceHost is not null;

    /// <summary>The host's side of the session when it is on this machine, which is where the pace is set.</summary>
    protected abstract HostSession? PaceHost { get; }

    /// <summary>Pauses the match for everyone, or lets it go on. Returns false when this machine cannot, or the match is not running.</summary>
    public bool SetPaused(bool paused) => PaceHost?.SetPaused(paused) ?? false;

    /// <summary>Sets the time between two ticks for everyone. Returns false when this machine cannot, or the match is not running.</summary>
    public bool SetTickInterval(TimeSpan interval) => PaceHost?.SetTickInterval(interval) ?? false;

    /// <summary>True once the match has started, and from then on.</summary>
    public bool HasStarted => Client.Phase == SessionPhase.Running || (Client.Phase == SessionPhase.Stopped && Match is { World.Tick: > 0 });

    /// <summary>
    /// One frame: takes in what the network brought, then lets the time pass for the session.
    /// Returns the number of ticks that ran.
    /// </summary>
    public abstract int Advance(TimeSpan elapsed);

    /// <summary>
    /// Ends this machine's part in the match, tells the other side, and closes the files. A match
    /// that never started leaves no replay.
    /// </summary>
    public void Dispose()
    {
        var started = HasStarted;
        Close();
        if (started)
        {
            Files?.Dispose();
        }
        else
        {
            Files?.Discard();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Leaves or ends the session and releases the network.</summary>
    protected abstract void Close();
}

/// <summary>A match with nobody else in it: the host and the player in one process, with no network.</summary>
public sealed class LocalRun : MatchRun
{
    private readonly Match _match;

    /// <param name="description">The match: its map, seed and starts.</param>
    /// <param name="data">The game data the match is built from.</param>
    /// <param name="localPlayer">The seat of the player at this machine.</param>
    /// <param name="keepFiles">False for a match that leaves no replay, as the smoke test's.</param>
    /// <param name="seats">What takes each seat, as the settings of the match have it. Left out, nobody sits at the other seats.</param>
    public LocalRun(MatchDescription description, GameData data, int localPlayer, bool keepFiles = true, IReadOnlyList<SeatSetting>? seats = null)
    {
        Description = description;
        _match = description.CreateMatch(data);
        Session = new LocalSession(_match.Simulation, description.Players.Count, localPlayer);
        Computers = new ComputerSeats(_match, Session.Host, seats ?? []);

        // The computer thinks after every tick this machine has run, before the view reads it.
        Session.Client.TickCompleted += _ => Computers.Think();
        if (keepFiles)
        {
            Files = new MatchFiles(Session.Client, description, _match, localPlayer);
        }
    }

    public LocalSession Session { get; }

    public override ClientSession Client => Session.Client;

    public override Match Match => _match;

    public override MatchDescription Description { get; }

    protected override HostSession PaceHost => Session.Host;

    public override int Advance(TimeSpan elapsed) => Session.Advance(elapsed);

    protected override void Close()
    {
        Session.Host.EndSession();
        for (var i = 0; i < 100 && Client.Phase != SessionPhase.Stopped; i++)
        {
            Session.Advance(TimeSpan.Zero);
        }
    }
}

/// <summary>A network match this machine hosts: a port others connect to, and the host's own player.</summary>
public sealed class HostRun : MatchRun
{
    private readonly UdpListener _listener;

    /// <param name="seats">What takes each seat, as the settings of the match have it. Left out, every seat is a person's.</param>
    /// <exception cref="IOException">The port could not be opened.</exception>
    public HostRun(MatchDescription description, GameData data, int port, bool localOnly, IReadOnlyList<SeatSetting>? seats = null)
    {
        Host = new MatchHost(data, description, seats: seats is null ? null : [.. seats.Select(seat => seat.Kind)]);
        Computers = new ComputerSeats(Host.Local.Match!, Host.Session, seats ?? []);

        // The computer thinks after every tick the host's own match has run.
        Host.Local.Session.TickCompleted += _ => Computers.Think();
        _listener = new UdpListener(localOnly ? IPAddress.Loopback : IPAddress.Any, port);
        Files = new MatchFiles(Host.Local.Session, description, Host.Local.Match!, Host.LocalPlayer);
    }

    public MatchHost Host { get; }

    public int Port => _listener.Port;

    public override ClientSession Client => Host.Local.Session;

    public override Match Match => Host.Local.Match!;

    public override MatchDescription Description => Host.Description;

    protected override HostSession PaceHost => Host.Session;

    public override int Advance(TimeSpan elapsed)
    {
        // The network first: what arrived is then there for the session to take in.
        _listener.Poll();
        while (_listener.TryAccept(out var connection))
        {
            if (!Host.TryAttach(connection))
            {
                connection.Close();
            }
        }

        return Host.Advance(elapsed);
    }

    /// <summary>Starts the match and stops taking connections. <see cref="MatchHost.CanStart"/> must be true.</summary>
    public void Start()
    {
        _listener.Accepting = false;
        Host.Start();
    }

    protected override void Close()
    {
        // The host's leaving ends the match for everyone. If it is not decided yet, the host's
        // player concedes in its last tick, so that every machine ends with the same result.
        Host.Leave();

        // Disposing waits, for a moment, until the guests have what was sent: the end of the match.
        _listener.Dispose();
    }
}

/// <summary>A network match this machine joins: a connection to the host, which describes the match.</summary>
public sealed class JoinRun : MatchRun
{
    private readonly UdpConnector _connector;

    /// <exception cref="SocketException">The host's name could not be found.</exception>
    /// <exception cref="IOException">No port could be opened.</exception>
    public JoinRun(GameData data, string host, int port, bool localOnly, bool readyWhenBuilt)
    {
        HostAddress = string.Create(CultureInfo.InvariantCulture, $"{host}, port {port}");
        _connector = localOnly ? new UdpConnector(IPAddress.Loopback) : new UdpConnector();
        try
        {
            Connection = _connector.Connect(host, port);
        }
        catch
        {
            _connector.Dispose();
            throw;
        }

        Player = new MatchClient(Connection, data, GameInfo.Version, readyWhenBuilt: readyWhenBuilt);

        // The replay starts when the match is built, since the first ticks can run in the very
        // update that brings the start.
        Player.MatchBuilt += () => Files = new MatchFiles(Player.Session, Player.Description!, Player.Match!, Player.Session.Player);
    }

    public MatchClient Player { get; }

    /// <summary>Where the host is, as a sentence names it: its address and its port.</summary>
    public string HostAddress { get; }

    /// <summary>The connection to the host, which knows why it closed when it has.</summary>
    public UdpConnection Connection { get; }

    public override ClientSession Client => Player.Session;

    public override Match? Match => Player.Match;

    public override MatchDescription? Description => Player.Description;

    protected override HostSession? PaceHost => null;

    public override int Advance(TimeSpan elapsed)
    {
        _connector.Poll();
        return Player.Advance(elapsed);
    }

    protected override void Close()
    {
        Player.Leave();

        // Disposing waits, for a moment, until the host has what was sent: that this player left.
        _connector.Dispose();
    }
}
