using System.Diagnostics;
using System.Net;
using Sim.Core;

namespace Net.Udp.Tests;

/// <summary>
/// A host session and some client sessions, each client with its own <see cref="ToyGame"/> and
/// its own socket, joined over UDP on the local address. Time is the real clock: every step polls
/// the sockets and gives each session the time that has passed since its last step, as the game
/// loop will.
/// </summary>
internal sealed class UdpMatch : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _hostTime;

    /// <param name="seedOf">The simulation seed of each client, in the order they connect, to build worlds that differ.</param>
    public UdpMatch(int clients, HostOptions options, RelayConditions? conditions = null, UdpOptions? udpOptions = null, Func<int, ulong>? seedOf = null)
    {
        Listener = new UdpListener(IPAddress.Loopback, 0, udpOptions);
        Host = new HostSession(options);
        Host.PlayerLeft += (player, reason) => HostDepartures.Add((player, reason));

        for (var index = 0; index < clients; index++)
        {
            var relay = conditions is null ? null : new UdpRelay(Listener.Port, conditions, seed: 3 + index);
            var client = new MatchClient(this, new ToyGame(seedOf?.Invoke(index) ?? ToyGame.DefaultSeed), relay, udpOptions);
            Clients.Add(client);

            // One at a time, so that the order of connecting is the order of the seats.
            Until(() => Host.IsAttached(index), $"client {index} is given a seat");
        }
    }

    public UdpListener Listener { get; }

    public HostSession Host { get; }

    public List<MatchClient> Clients { get; } = [];

    public List<(int Player, SessionEndReason Reason)> HostDepartures { get; } = [];

    /// <summary>The host's end of each seat's connection.</summary>
    public Dictionary<int, UdpConnection> HostConnections { get; } = [];

    public TimeSpan Now => _clock.Elapsed;

    /// <summary>One turn of every program's loop: the host's, then each client's that is not frozen.</summary>
    public void Step()
    {
        Listener.Poll();
        while (Listener.TryAccept(out var connection))
        {
            var seat = Enumerable.Range(0, Host.Options.PlayerCount).First(candidate => !Host.IsAttached(candidate));
            Host.Attach(seat, connection);
            HostConnections[seat] = connection;
        }

        var now = Now;
        Host.Advance(now - _hostTime);
        _hostTime = now;

        foreach (var client in Clients)
        {
            client.Step();
        }
    }

    public void Until(Func<bool> condition, string what) => Wait.Until(condition, Step, what);

    public void For(int milliseconds) => Wait.For(TimeSpan.FromMilliseconds(milliseconds), Step);

    /// <summary>Lets every client join and report ready, and starts the match.</summary>
    public void Start()
    {
        Until(() => Host.CanStart, "every client has joined and is ready");
        Host.Start();
    }

    /// <summary>
    /// Has the host end the match and waits until every client still in it has run its last
    /// tick. Returns the final tick.
    /// </summary>
    public int Finish()
    {
        Host.EndSession();
        Until(() => Clients.All(client => client.Session.Phase == SessionPhase.Stopped), "every client has stopped");
        return Host.EndInfo!.FinalTick;
    }

    public void Dispose()
    {
        foreach (var client in Clients)
        {
            if (client.Relay is not null)
            {
                client.Relay.Conditions = new RelayConditions();
            }
        }

        // Each program closes what it still has open, and all are polled until the links are
        // gone, so that no dispose has anything to wait for.
        Wait.DisposeWhilePolling(Listener, () =>
        {
            foreach (var client in Clients)
            {
                client.Connector.Poll();
            }
        });

        foreach (var client in Clients)
        {
            client.Connector.Dispose();
            client.Relay?.Dispose();
        }
    }
}

internal sealed class MatchClient
{
    private readonly UdpMatch _match;
    private TimeSpan _time;

    public MatchClient(UdpMatch match, ToyGame game, UdpRelay? relay, UdpOptions? options)
    {
        _match = match;
        Game = game;
        Relay = relay;
        Connector = new UdpConnector(IPAddress.Loopback, options);
        Connection = Connector.Connect(new IPEndPoint(IPAddress.Loopback, relay?.Port ?? match.Listener.Port));
        Session = new ClientSession(Connection);
        Session.CommandsScheduled += (tick, commands) => Bundles.Add(new TickBundle(tick, commands));
        Session.PlayerLeft += (player, reason) => Departures.Add((player, reason));
        Session.ReportReady(game.Simulation);
        _time = match.Now;
    }

    /// <summary>The bad network between this client and the host, if the match has one. Each client has its own.</summary>
    public UdpRelay? Relay { get; }

    public UdpConnector Connector { get; }

    public UdpConnection Connection { get; }

    public ClientSession Session { get; }

    public ToyGame Game { get; }

    /// <summary>A frozen client gets no updates and does not poll, like a program that hangs.</summary>
    public bool Frozen { get; set; }

    /// <summary>Every bundle the session queued, in order.</summary>
    public List<TickBundle> Bundles { get; } = [];

    public List<(int Player, SessionEndReason Reason)> Departures { get; } = [];

    public int Seat => Session.Player;

    public int Tick => Game.World.Tick;

    public bool Spawn(int contentId) => Session.Submit(new Command(ToyGame.Spawn, Seat, contentId: contentId));

    /// <param name="contentId">The toy game ignores it for a cull. A test can tell its commands apart by it.</param>
    public bool Cull(int contentId = 0) => Session.Submit(new Command(ToyGame.Cull, Seat, contentId: contentId));

    public void Step()
    {
        var now = _match.Now;
        if (!Frozen)
        {
            Connector.Poll();
            Session.Advance(now - _time);
        }

        _time = now;
    }
}
