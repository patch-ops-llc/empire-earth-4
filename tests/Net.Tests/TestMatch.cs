using Sim.Core;

namespace Net.Tests;

/// <summary>
/// A host and some clients in one process, each client with its own <see cref="ToyGame"/>, joined
/// over a <see cref="LoopbackNetwork"/>. The test moves time itself, a frame at a time.
/// </summary>
internal sealed class TestMatch
{
    /// <summary>One update of a game running at 60 frames a second.</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    /// <param name="clients">How many seats get a client, from seat 0 up. The rest are run by the host.</param>
    /// <param name="options">Left out: two seats, a command delay of 2 and a hash report every tick.</param>
    /// <param name="seedOf">The simulation seed of each seat's client, to build worlds that differ.</param>
    public TestMatch(
        int clients = 2,
        HostOptions? options = null,
        LinkConditions conditions = default,
        ulong networkSeed = 1,
        ClientOptions? clientOptions = null,
        Func<int, ulong>? seedOf = null)
    {
        Network = new LoopbackNetwork(networkSeed);
        Host = new HostSession(options ?? new HostOptions { PlayerCount = Math.Max(clients, 2), CommandDelay = 2, HashInterval = 1 });
        Host.PlayerLeft += (player, reason) => HostDepartures.Add((player, reason));

        for (var seat = 0; seat < clients; seat++)
        {
            var link = Network.Connect(conditions);
            Host.Attach(seat, link.HostEnd);
            Clients.Add(new TestClient(this, seat, link, new ToyGame(seedOf?.Invoke(seat) ?? ToyGame.DefaultSeed), clientOptions));
        }
    }

    public LoopbackNetwork Network { get; }

    public HostSession Host { get; }

    public List<TestClient> Clients { get; } = [];

    public List<(int Player, SessionEndReason Reason)> HostDepartures { get; } = [];

    /// <summary>Lets frames pass until every client has joined and is ready.</summary>
    public void Join()
    {
        for (var i = 0; i < 600 && !(Host.CanStart && Clients.All(client => client.Session.Player >= 0 && Host.IsReady(client.Seat))); i++)
        {
            Step();
        }

        Assert.True(Host.CanStart, "The clients did not all join.");
    }

    /// <summary>Joins every client and starts the match.</summary>
    public void Start()
    {
        Join();
        Host.Start();
    }

    /// <summary>One frame: the network, then the host, then every client that is not frozen.</summary>
    public void Step(TimeSpan? elapsed = null)
    {
        var time = elapsed ?? Frame;
        Network.Advance(time);
        Host.Advance(time);
        foreach (var client in Clients)
        {
            if (!client.Frozen)
            {
                client.TicksPerAdvance.Add(client.Session.Advance(time));
            }
        }
    }

    /// <summary>Lets frames pass for a length of time.</summary>
    public void Run(TimeSpan duration)
    {
        for (var passed = TimeSpan.Zero; passed < duration; passed += Frame)
        {
            Step();
        }
    }

    public void Run(double seconds) => Run(TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// Has the host end the match and lets frames pass until every client still in it has run its
    /// last tick. Returns the final tick.
    /// </summary>
    public int Finish()
    {
        Host.EndSession();
        for (var i = 0; i < 6000 && Clients.Any(client => client.Session.Phase != SessionPhase.Stopped); i++)
        {
            Step();
        }

        Assert.All(Clients, client => Assert.Equal(SessionPhase.Stopped, client.Session.Phase));
        return Host.EndInfo!.FinalTick;
    }
}

internal sealed class TestClient
{
    public TestClient(TestMatch match, int seat, LoopbackLink link, ToyGame game, ClientOptions? options)
    {
        Seat = seat;
        Link = link;
        Game = game;
        Session = new ClientSession(new RecordingConnection(link.ClientEnd, Sent), options);
        Session.CommandsScheduled += (tick, commands) => Bundles.Add(new TickBundle(tick, commands));
        Session.TickCompleted += tick => TickTimes.Add((match.Network.Now, tick));
        Session.PlayerLeft += (player, reason) => Departures.Add((player, reason));
        Session.ReportReady(game.Simulation);
    }

    public int Seat { get; }

    public LoopbackLink Link { get; }

    public ToyGame Game { get; }

    public ClientSession Session { get; }

    /// <summary>A frozen client gets no updates, like a program that hangs.</summary>
    public bool Frozen { get; set; }

    /// <summary>Every bundle the session queued, in order.</summary>
    public List<TickBundle> Bundles { get; } = [];

    /// <summary>When each tick ran, on the network's clock.</summary>
    public List<(TimeSpan At, int Tick)> TickTimes { get; } = [];

    /// <summary>How many ticks each update ran.</summary>
    public List<int> TicksPerAdvance { get; } = [];

    public List<(int Player, SessionEndReason Reason)> Departures { get; } = [];

    /// <summary>Everything the session sent to the host.</summary>
    public List<NetMessage> Sent { get; } = [];

    public int Tick => Game.World.Tick;

    public bool Spawn(int contentId) => Session.Submit(new Command(ToyGame.Spawn, Seat, contentId: contentId));

    public bool Cull() => Session.Submit(new Command(ToyGame.Cull, Seat));

    private sealed class RecordingConnection(IConnection inner, List<NetMessage> sent) : IConnection
    {
        public bool IsOpen => inner.IsOpen;

        public void Send(ReadOnlySpan<byte> message)
        {
            sent.Add(MessageCodec.Read(message.ToArray()));
            inner.Send(message);
        }

        public bool TryReceive(out ReadOnlyMemory<byte> message) => inner.TryReceive(out message);

        public void Close() => inner.Close();
    }
}
