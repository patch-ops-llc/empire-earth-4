using Sim.Core;
using Sim.Data;
using Sim.Serialization;

namespace Net;

/// <summary>
/// The hosting machine's side of a match: the <see cref="HostSession"/>, and the host's own player
/// as a <see cref="MatchClient"/> joined over the loopback, as <see cref="LocalSession"/> does it.
/// The other seats are given to connections from the real network with <see cref="TryAttach"/>.
/// </summary>
/// <remarks>
/// <para>
/// The command delay is left for the host session to choose from the round trips it measures, so
/// <see cref="CanStart"/> waits until every remote player has answered a ping.
/// </para>
/// <para>
/// A seat that is not a person's (<see cref="SeatKind"/>) gets no connection, and the match does
/// not wait for it. Its player's orders, if it gives any, go in with
/// <see cref="HostSession.Submit"/>: that is where a computer opponent gives its orders.
/// </para>
/// <para>
/// A player who is gone has conceded (design 5.1). When a player leaves a running match, loses
/// its connection or is dropped, the host gives the order to concede in its name, so that the
/// match is decided for those who stay instead of leaving them a Capitol to hunt down. The
/// host's own player concedes the same way with <see cref="Leave"/>.
/// </para>
/// </remarks>
public sealed class MatchHost
{
    private readonly LoopbackNetwork _network = new();
    private readonly SeatKind[] _seats;

    /// <param name="data">This machine's game data, which the description must fit.</param>
    /// <param name="description">The match, with one player for each seat. The host draws its seed and its starts.</param>
    /// <param name="localPlayer">The seat of the person at this machine.</param>
    /// <param name="hostOptions">The seat count, the first tick and the match are always taken from the other arguments.</param>
    /// <param name="clientOptions">The options of the local player's session.</param>
    /// <param name="seats">
    /// What takes each seat, in the order of the players (<see cref="MatchSettings.Kinds"/>). Left
    /// out, every seat is a person's. The local player's is always a person's.
    /// </param>
    /// <exception cref="ArgumentException">The match cannot be built from the data. The message says why.</exception>
    public MatchHost(
        GameData data,
        MatchDescription description,
        int localPlayer = 0,
        HostOptions? hostOptions = null,
        ClientOptions? clientOptions = null,
        IReadOnlyList<SeatKind>? seats = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentOutOfRangeException.ThrowIfNegative(localPlayer);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(localPlayer, description.Players.Count);
        if (description.FindProblem(data) is { } problem)
        {
            throw new ArgumentException(problem, nameof(description));
        }

        _seats = seats is null ? new SeatKind[description.Players.Count] : [.. seats];
        if (_seats.Length != description.Players.Count)
        {
            throw new ArgumentException($"The match has {description.Players.Count} players and {_seats.Length} seats are named.", nameof(seats));
        }

        if (_seats[localPlayer] != SeatKind.Person)
        {
            throw new ArgumentException($"Seat {localPlayer} is the seat of the person at this machine.", nameof(seats));
        }

        Description = description;
        LocalPlayer = localPlayer;
        Session = new HostSession((hostOptions ?? new HostOptions()) with
        {
            PlayerCount = description.Players.Count,
            FirstTick = 0,
            Match = description,
        });

        Session.PlayerLeft += ConcedeFor;
        var link = _network.Connect();
        Session.Attach(localPlayer, link.HostEnd);
        Local = new MatchClient(link.ClientEnd, data, description.GameVersion, clientOptions, readyWhenBuilt: true);

        // The loopback delivers at once: the join and its answer with the description, then the
        // ready notice.
        Session.Advance(TimeSpan.Zero);
        Local.Advance(TimeSpan.Zero);
        Session.Advance(TimeSpan.Zero);
        if (!Session.IsReady(localPlayer))
        {
            throw new ArgumentException(Local.Refusal ?? "The host's own player could not join.", nameof(description));
        }
    }

    public MatchDescription Description { get; }

    public HostSession Session { get; }

    /// <summary>The player at this machine, with its match built.</summary>
    public MatchClient Local { get; }

    public int LocalPlayer { get; }

    public int PlayerCount => Description.Players.Count;

    /// <summary>What takes a seat.</summary>
    public SeatKind SeatOf(int player) => _seats[player];

    /// <summary>
    /// True when the match can start: every seat of a person has a player who is ready, and every
    /// remote player has answered a ping, so that the command delay fits the network between them.
    /// </summary>
    public bool CanStart
    {
        get
        {
            if (!Session.CanStart)
            {
                return false;
            }

            for (var player = 0; player < PlayerCount; player++)
            {
                if (_seats[player] == SeatKind.Person
                    && (!Session.IsReady(player) || (player != LocalPlayer && Session.GetRoundTrip(player) is null)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Gives the lowest free seat of a person to a connection from the network. Returns false,
    /// having done nothing, when every such seat is taken or the match has started; the caller
    /// then closes the connection.
    /// </summary>
    public bool TryAttach(IConnection connection) => TryAttach(connection, out _);

    /// <inheritdoc cref="TryAttach(IConnection)"/>
    public bool TryAttach(IConnection connection, out int player)
    {
        ArgumentNullException.ThrowIfNull(connection);

        player = -1;
        if (Session.Phase != SessionPhase.Joining)
        {
            return false;
        }

        for (var seat = 0; seat < PlayerCount; seat++)
        {
            if (_seats[seat] == SeatKind.Person && !Session.IsAttached(seat))
            {
                Session.Attach(seat, connection);
                player = seat;
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts the match. <see cref="CanStart"/> must be true.</summary>
    public void Start()
    {
        if (!CanStart)
        {
            throw new InvalidOperationException("Not every seat of a person has a player who is ready.");
        }

        Session.Start();
    }

    /// <summary>
    /// Lets time pass for the host and then for its own player, so that a tick the host issues
    /// runs here in the same update. Poll the real network before calling it. Returns the number
    /// of ticks that ran.
    /// </summary>
    public int Advance(TimeSpan elapsed)
    {
        _network.Advance(elapsed);
        Session.Advance(elapsed);
        return Local.Advance(elapsed);
    }

    /// <summary>
    /// Ends the match for everyone and runs the host's own player to the final tick, so that its
    /// record of the match is complete. The remote players run to the same tick as they receive
    /// the end.
    /// </summary>
    public void End()
    {
        Session.EndSession();
        for (var i = 0; i < 1000 && Local.Session.Phase != SessionPhase.Stopped; i++)
        {
            Local.Advance(TimeSpan.Zero);
        }
    }

    /// <summary>
    /// The person at this machine leaves, which ends the match for everyone as <see cref="End"/>
    /// does. If the match is running and not yet decided, the host's player concedes first, in
    /// the last tick of the match, so that every machine ends with the same result: the host
    /// gave up, and whoever is left has won.
    /// </summary>
    public void Leave()
    {
        if (Session.Phase == SessionPhase.Running
            && Local.Match is { } match
            && !match.Standings.IsDecided
            && Local.Session.Submit(new Command(CommandType.Concede, LocalPlayer)))
        {
            // The loopback delivers at once: the host takes the order in before it ends the match.
            Session.Advance(TimeSpan.Zero);
        }

        End();
    }

    /// <summary>A player who is gone from a running match has conceded: the host gives the order in its name.</summary>
    private void ConcedeFor(int player, SessionEndReason reason)
    {
        if (Session.Phase == SessionPhase.Running)
        {
            Session.Submit(new Command(CommandType.Concede, player));
        }
    }
}
