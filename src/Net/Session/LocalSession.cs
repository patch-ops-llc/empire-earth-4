using Sim.Core;

namespace Net;

/// <summary>
/// A whole session in one process: a host and one client joined over the loopback, with the match
/// already started. This is single-player, and it is the same code a network match runs. The
/// client is the person at the machine; every other seat is run by the host, so an AI player
/// gives its commands with <see cref="HostSession.Submit"/>.
/// </summary>
public sealed class LocalSession
{
    private readonly LoopbackNetwork _network = new();

    /// <param name="simulation">The simulation of the match, built and not yet running.</param>
    /// <param name="playerCount">The number of seats, the local player's included.</param>
    /// <param name="localPlayer">The seat of the person at the machine.</param>
    /// <param name="hostOptions">
    /// Left out, the command delay is 1 tick and the rest are the defaults. The seat count and the
    /// first tick are always taken from the other arguments, the local player is never dropped for
    /// falling behind, and the host issues as many ticks in one update as the client may run.
    /// </param>
    /// <param name="clientOptions">
    /// <see cref="ClientOptions.MaxTicksPerAdvance"/> is the most ticks one update runs. Time
    /// beyond that is dropped, so the game slows down when the machine cannot keep up and never
    /// has to make up for a long stall. Raise it to run the game faster than real time.
    /// </param>
    public LocalSession(
        Simulation simulation,
        int playerCount = 2,
        int localPlayer = 0,
        HostOptions? hostOptions = null,
        ClientOptions? clientOptions = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        hostOptions ??= new HostOptions();
        clientOptions ??= new ClientOptions();
        Host = new HostSession(hostOptions with
        {
            PlayerCount = playerCount,
            FirstTick = simulation.World.Tick,
            CommandDelay = hostOptions.CommandDelay ?? CommandDelayPolicy.SinglePlayer,
            MaxLagTicks = int.MaxValue,
            MaxTicksPerAdvance = clientOptions.MaxTicksPerAdvance,
        });

        var link = _network.Connect();
        Host.Attach(localPlayer, link.HostEnd);
        Client = new ClientSession(link.ClientEnd, clientOptions);
        Client.ReportReady(simulation);

        // The loopback delivers at once, so the handshake needs no time to pass: the join request
        // and its answer, then the ready notice, then the start.
        Host.Advance(TimeSpan.Zero);
        Client.Advance(TimeSpan.Zero);
        Host.Advance(TimeSpan.Zero);
        Host.Start();
        Client.Advance(TimeSpan.Zero);
    }

    public HostSession Host { get; }

    public ClientSession Client { get; }

    /// <summary>
    /// Lets time pass for the host and then the client, so that a tick the host issues runs in the
    /// same update. Returns the number of ticks that ran.
    /// </summary>
    public int Advance(TimeSpan elapsed)
    {
        _network.Advance(elapsed);
        Host.Advance(elapsed);
        return Client.Advance(elapsed);
    }
}
