using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;

namespace Net.Tests;

/// <summary>
/// A host and some players in one process, each player building the real game's match from the
/// description the host sends, joined over a <see cref="LoopbackNetwork"/>. The test moves time
/// itself, a frame at a time.
/// </summary>
internal sealed class RealMatch
{
    /// <param name="dataOf">The game data of each seat's machine. Left out: the repository's.</param>
    /// <param name="versionOf">The game version of each seat's machine. Left out: the host's.</param>
    public RealMatch(
        int clients = 2,
        LinkConditions conditions = default,
        HostOptions? options = null,
        MatchDescription? description = null,
        Func<int, GameData>? dataOf = null,
        Func<int, string>? versionOf = null,
        ulong networkSeed = 1)
    {
        Description = description ?? TestData.Description(players: Math.Max(clients, 2));
        Network = new LoopbackNetwork(networkSeed);
        Host = new HostSession((options ?? new HostOptions { HashInterval = 1 }) with
        {
            PlayerCount = Description.Players.Count,
            Match = Description,
        });
        Host.PlayerLeft += (player, reason) => HostDepartures.Add((player, reason));

        for (var seat = 0; seat < clients; seat++)
        {
            var link = Network.Connect(conditions);
            Host.Attach(seat, link.HostEnd);
            Clients.Add(new RealClient(seat, link.ClientEnd, dataOf?.Invoke(seat) ?? TestData.Data, versionOf?.Invoke(seat) ?? Description.GameVersion));
        }
    }

    public MatchDescription Description { get; }

    public LoopbackNetwork Network { get; }

    public HostSession Host { get; }

    public List<RealClient> Clients { get; } = [];

    public List<(int Player, SessionEndReason Reason)> HostDepartures { get; } = [];

    /// <summary>One frame: the network, then the host, then every client.</summary>
    public void Step()
    {
        Network.Advance(TestMatch.Frame);
        Host.Advance(TestMatch.Frame);
        foreach (var client in Clients)
        {
            client.Player.Advance(TestMatch.Frame);
        }
    }

    public void Run(double seconds)
    {
        for (var passed = TimeSpan.Zero; passed < TimeSpan.FromSeconds(seconds); passed += TestMatch.Frame)
        {
            Step();
        }
    }

    /// <summary>Lets frames pass until something is true, and fails if it never is.</summary>
    public void Until(Func<bool> condition, string what)
    {
        for (var i = 0; i < 6000 && !condition(); i++)
        {
            Step();
        }

        Assert.True(condition(), $"Still waiting after 100 seconds: {what}.");
    }

    /// <summary>Lets frames pass until every client is ready and has answered a ping, then starts the match.</summary>
    public void Start()
    {
        Until(
            () => Host.CanStart && Clients.All(client => Host.IsReady(client.Seat) && Host.GetRoundTrip(client.Seat) is not null),
            "every client is ready");
        Host.Start();
        Until(() => Clients.All(client => client.Player.Session.Phase == SessionPhase.Running), "every client hears of the start");
    }

    /// <summary>Puts something in one client's world that no other has, as a fault in the simulation would.</summary>
    public static void Corrupt(RealClient client)
    {
        var where = client.Match.Bodies.ValueAt(0).Position;
        client.Match.SpawnUnit(client.Seat, where, Fixed.One / 8);
    }

    /// <summary>Has the host end the match and lets every client still in it run its last tick. Returns the final tick.</summary>
    public int Finish()
    {
        Host.EndSession();
        Until(() => Clients.All(client => client.Player.Session.Phase == SessionPhase.Stopped), "every client stops");
        return Host.EndInfo!.FinalTick;
    }
}

internal sealed class RealClient
{
    public RealClient(int seat, IConnection connection, GameData data, string gameVersion)
    {
        Seat = seat;
        Player = new MatchClient(connection, data, gameVersion, readyWhenBuilt: true);
        Player.MatchBuilt += () => Recording = new MatchRecording(Player.Session, Player.Description!, Player.Match!, ReplayFile);
        Player.Session.PlayerLeft += (player, reason) => Departures.Add((player, reason));
        Player.Session.CommandsScheduled += (tick, commands) => Scheduled.AddRange(commands.Select(command => (tick, command)));
    }

    public int Seat { get; }

    public MatchClient Player { get; }

    public Match Match => Player.Match ?? throw new InvalidOperationException($"Player {Seat} has no match.");

    public MemoryStream ReplayFile { get; } = new();

    public MatchRecording? Recording { get; private set; }

    public List<(int Player, SessionEndReason Reason)> Departures { get; } = [];

    /// <summary>Every command the session scheduled, with its tick, in order.</summary>
    public List<(int Tick, Command Command)> Scheduled { get; } = [];

    public int Tick => Match.World.Tick;

    public ulong Hash => Match.World.ComputeHash();

    /// <summary>The entities this player owns, in ascending order.</summary>
    public EntityId[] Units()
    {
        var units = new List<EntityId>();
        for (var i = 0; i < Match.Bodies.Count; i++)
        {
            if (Match.Bodies.ValueAt(i).Owner == Seat)
            {
                units.Add(Match.Bodies.EntityAt(i));
            }
        }

        return [.. units];
    }

    /// <summary>Orders some of this player's units to a tile. Returns false when the match is not running.</summary>
    public bool Move(int tileX, int tileY, int first = 0, int count = int.MaxValue)
    {
        var units = Units().Skip(first).Take(count).ToArray();
        return Player.Session.Submit(new Command(CommandType.Move, Seat, units, Fixed.FromInt(tileX), Fixed.FromInt(tileY)));
    }

    /// <summary>The replay this client wrote, read back.</summary>
    public Replay ReadReplay() => Replay.Read(new ByteReader(ReplayFile.ToArray()));
}
