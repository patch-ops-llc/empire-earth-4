using Sim.Core;
using Sim.Outcome;
using Sim.Serialization;
using Sim.Setup;

namespace Net.Tests;

/// <summary>The hosting machine: the host session, its own player over the loopback, and seats for the network.</summary>
public class MatchHostTests
{
    private static readonly TimeSpan Frame = TestMatch.Frame;

    [Fact]
    public void TheHostsOwnPlayerIsBuiltAndReadyAtOnce()
    {
        var host = new MatchHost(TestData.Data, TestData.Description(), localPlayer: 1);

        Assert.Equal(1, host.LocalPlayer);
        Assert.Equal(1, host.Local.Session.Player);
        Assert.Equal(2, host.PlayerCount);
        Assert.NotNull(host.Local.Match);
        Assert.True(host.Local.IsReady);
        Assert.True(host.Session.IsReady(1));
        Assert.Same(host.Description, host.Session.Options.Match);
    }

    [Fact]
    public void TheMatchCannotStartWhileASeatIsEmptyOrItsPlayerIsNotReady()
    {
        var table = new Table();
        Assert.False(table.Host.CanStart);
        Assert.Throws<InvalidOperationException>(table.Host.Start);

        var guest = table.Join(readyWhenBuilt: false);
        table.Run(0.2);
        Assert.NotNull(guest.Match);
        Assert.False(table.Host.CanStart);

        guest.Ready();
        table.Run(0.2);
        Assert.True(table.Host.CanStart);
    }

    [Fact]
    public void TheMatchWaitsForARemotePlayerToAnswerAPing()
    {
        var table = new Table(new LinkConditions(TimeSpan.FromMilliseconds(80), TimeSpan.Zero));
        var guest = table.Join();

        // Ready reaches the host with the join's second leg; the pong with the first ping is behind it.
        for (var i = 0; i < 600 && !table.Host.Session.IsReady(1); i++)
        {
            table.Step();
        }

        Assert.True(table.Host.Session.CanStart);
        Assert.Equal(table.Host.Session.GetRoundTrip(1) is not null, table.Host.CanStart);

        table.Run(1);
        Assert.True(table.Host.CanStart);
        table.Host.Start();
        table.Run(1);

        // 160 ms there and back, and the frames it waits for: a delay of 4 covers it.
        Assert.Equal(4, table.Host.Session.CommandDelay);
        Assert.Equal(guest.Match!.World.Tick, table.Host.Local.Match!.World.Tick, 6.0);
    }

    [Fact]
    public void SeatsAreGivenInOrderAndNoMoreThanThereAre()
    {
        var host = new MatchHost(TestData.Data, TestData.Description(players: 3), localPlayer: 1);
        var network = new LoopbackNetwork();

        Assert.True(host.TryAttach(network.Connect().HostEnd, out var first));
        Assert.True(host.TryAttach(network.Connect().HostEnd, out var second));
        Assert.False(host.TryAttach(network.Connect().HostEnd, out var third));

        Assert.Equal((0, 2, -1), (first, second, third));
    }

    [Fact]
    public void ASeatThatIsLeftCanBeTakenAgainUntilTheMatchStarts()
    {
        var table = new Table();
        var first = table.Join();
        table.Run(0.2);

        first.Leave();
        table.Run(0.2);
        Assert.False(table.Host.Session.IsAttached(1));
        Assert.False(table.Host.CanStart);

        var second = table.Join();
        table.Run(0.2);
        Assert.Equal(1, second.Session.Player);
        table.Host.Start();

        Assert.False(table.Host.TryAttach(table.Network.Connect().HostEnd));
    }

    [Fact]
    public void TheHostAndAGuestPlayAndEndTogether()
    {
        var table = new Table(new LinkConditions(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10)));
        var guest = table.Join();
        table.Run(0.5);
        table.Host.Start();
        table.Run(5);

        table.Host.End();
        Assert.Equal(SessionPhase.Stopped, table.Host.Local.Session.Phase);
        table.Run(1);

        var finalTick = table.Host.Session.EndInfo!.FinalTick;
        Assert.InRange(finalTick, 95, 105);
        Assert.Equal(new SessionEndInfo(SessionEndReason.HostEnded, finalTick), guest.Session.EndInfo);
        Assert.Equal(finalTick, table.Host.Local.Match!.World.Tick);
        Assert.Equal(finalTick, guest.Match!.World.Tick);
        Assert.Equal(table.Host.Local.Match.World.ComputeHash(), guest.Match.World.ComputeHash());
    }

    [Fact]
    public void AMatchTheHostCannotBuildIsRefusedWhenItIsMade()
    {
        var elsewhere = new MatchDescription(TestData.GameVersion, TestData.Data.ContentHash, "atoll", 1, [new PlayerSetup(0), new PlayerSetup(1)]);
        var otherData = new MatchDescription(TestData.GameVersion, new string('0', 64), "ridge", 1, [new PlayerSetup(0), new PlayerSetup(1)]);

        Assert.Contains("no map 'atoll'", Assert.Throws<ArgumentException>(() => new MatchHost(TestData.Data, elsewhere)).Message);
        Assert.Contains("other data", Assert.Throws<ArgumentException>(() => new MatchHost(TestData.Data, otherData)).Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchHost(TestData.Data, TestData.Description(), localPlayer: 2));
    }

    /// <summary>Design 5.1: a player may concede at any time, and the order runs on every machine.</summary>
    [Fact]
    public void APlayerWhoConcedesIsOutOnEveryMachineAndTheMatchGoesOnToBeWatched()
    {
        var (table, guest) = Playing();

        Assert.True(guest.Session.Submit(new Command(CommandType.Concede, 1)));
        table.Run(0.5);

        Assert.All(new[] { table.Host.Local.Match!, guest.Match! }, match =>
        {
            Assert.Equal((PlayerStanding.Playing, PlayerStanding.Conceded), (match.Standings.Of(0), match.Standings.Of(1)));
            Assert.Equal(0, match.Standings.Winner);
        });
        Assert.Equal(SessionPhase.Running, table.Host.Session.Phase);
        Assert.Equal(SessionPhase.Running, guest.Session.Phase);
        var tick = guest.Match!.World.Tick;
        table.Run(0.5);
        Assert.InRange(guest.Match.World.Tick - tick, 8, 12);
    }

    /// <summary>A player who is gone has conceded: the host gives the order in its name, whatever took the player away.</summary>
    [Theory]
    [InlineData("leaves")]
    [InlineData("loses the connection")]
    [InlineData("breaks the protocol")]
    public void APlayerWhoIsGoneFromARunningMatchHasConceded(string how)
    {
        var (table, guest) = Playing();
        switch (how)
        {
            case "leaves":
                guest.Leave();
                break;
            case "loses the connection":
                table.GuestLinks[0].ClientEnd.Close();
                break;
            default:
                table.GuestLinks[0].ClientEnd.Send(MessageCodec.Encode(new ReadyNotice()));
                break;
        }

        table.Run(0.5);

        var match = table.Host.Local.Match!;
        Assert.Equal((PlayerStanding.Playing, PlayerStanding.Conceded), (match.Standings.Of(0), match.Standings.Of(1)));
        Assert.Equal(0, match.Standings.Winner);
        Assert.Equal(SessionPhase.Running, table.Host.Session.Phase);
        Assert.False(table.Host.Session.IsAttached(1));
    }

    [Fact]
    public void APlayerWhoLeavesBeforeTheMatchStartsConcedesNothing()
    {
        var table = new Table();
        var first = table.Join();
        table.Run(0.2);
        first.Leave();
        table.Run(0.2);
        table.Join();
        table.Run(0.5);
        table.Host.Start();
        table.Run(1);

        Assert.False(table.Host.Local.Match!.Standings.IsDecided);
    }

    /// <summary>The host's leaving ends the match for everyone, and every machine ends it with the same result: the host gave up.</summary>
    [Fact]
    public void WhenTheHostLeavesItsPlayerConcedesInTheLastTickOfTheMatch()
    {
        var (table, guest) = Playing(new LinkConditions(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10)));
        var issued = table.Host.Session.NextTick;

        table.Host.Leave();
        table.Run(1);

        var finalTick = table.Host.Session.EndInfo!.FinalTick;
        Assert.Equal(issued + 1, finalTick);
        Assert.Equal(new SessionEndInfo(SessionEndReason.HostEnded, finalTick), guest.Session.EndInfo);
        Assert.All(new[] { table.Host.Local.Match!, guest.Match! }, match =>
        {
            Assert.Equal(finalTick, match.World.Tick);
            Assert.Equal((PlayerStanding.Conceded, PlayerStanding.Playing), (match.Standings.Of(0), match.Standings.Of(1)));
            Assert.Equal((1, finalTick - 1), (match.Standings.Winner, match.Standings.DecidedTick));
        });
        Assert.Equal(table.Host.Local.Match!.World.ComputeHash(), guest.Match!.World.ComputeHash());
    }

    [Fact]
    public void TheHostLeavesAPausedMatchTheSameWay()
    {
        var (table, guest) = Playing();
        table.Host.Session.SetPaused(true);
        table.Run(0.5);
        Assert.True(guest.Session.IsPaused);

        table.Host.Leave();
        table.Run(0.5);

        Assert.Equal(SessionPhase.Stopped, guest.Session.Phase);
        Assert.Equal(1, guest.Match!.Standings.Winner);
        Assert.Equal(table.Host.Local.Match!.World.ComputeHash(), guest.Match.World.ComputeHash());
    }

    [Fact]
    public void AHostWhoLeavesAMatchThatIsDecidedConcedesNothing()
    {
        var (table, guest) = Playing();
        guest.Session.Submit(new Command(CommandType.Concede, 1));
        table.Run(0.5);

        table.Host.Leave();
        table.Run(0.5);

        Assert.All(new[] { table.Host.Local.Match!, guest.Match! }, match =>
            Assert.Equal((PlayerStanding.Playing, PlayerStanding.Conceded, 0), (match.Standings.Of(0), match.Standings.Of(1), match.Standings.Winner)));
    }

    [Fact]
    public void AHostWhoLeavesBeforeTheMatchStartsJustEndsIt()
    {
        var table = new Table();
        var guest = table.Join();
        table.Run(0.5);

        table.Host.Leave();
        table.Run(0.5);

        Assert.Equal(SessionPhase.Stopped, table.Host.Session.Phase);
        Assert.Equal(SessionEndReason.HostEnded, guest.Session.EndInfo!.Reason);
        Assert.Equal(0, table.Host.Local.Match!.World.Tick);
    }

    /// <summary>
    /// A seat nobody takes needs no connection and nobody's word to start. Its player's orders
    /// come from the hosting machine, which is where the computer opponent gives them.
    /// </summary>
    [Theory]
    [InlineData(SeatKind.Nobody)]
    [InlineData(SeatKind.Computer)]
    public void ASeatThatIsNoPersonsIsNotWaitedForAndTakesOrdersFromTheHost(SeatKind kind)
    {
        var host = new MatchHost(TestData.Data, TestData.Description(), seats: [SeatKind.Person, kind]);
        var network = new LoopbackNetwork();

        Assert.Equal((SeatKind.Person, kind), (host.SeatOf(0), host.SeatOf(1)));
        Assert.False(host.TryAttach(network.Connect().HostEnd));
        Assert.True(host.CanStart);
        host.Start();
        for (var i = 0; i < 30; i++)
        {
            host.Advance(Frame);
        }

        var match = host.Local.Match!;
        Assert.InRange(match.World.Tick, 8, 11);
        Assert.False(match.Standings.IsDecided);

        Assert.True(host.Session.Submit(new Command(CommandType.Concede, 1)));
        for (var i = 0; i < 30; i++)
        {
            host.Advance(Frame);
        }

        Assert.Equal((PlayerStanding.Conceded, 0), (match.Standings.Of(1), match.Standings.Winner));
    }

    [Fact]
    public void AMatchOfThreeGivesItsFreeSeatsOfPersonsInOrder()
    {
        var host = new MatchHost(TestData.Data, TestData.Description(players: 4), localPlayer: 2, seats: [SeatKind.Person, SeatKind.Nobody, SeatKind.Person, SeatKind.Person]);
        var network = new LoopbackNetwork();

        Assert.True(host.TryAttach(network.Connect().HostEnd, out var first));
        Assert.True(host.TryAttach(network.Connect().HostEnd, out var second));
        Assert.False(host.TryAttach(network.Connect().HostEnd, out _));

        Assert.Equal((0, 3), (first, second));
    }

    [Fact]
    public void SeatsThatDoNotFitTheMatchAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new MatchHost(TestData.Data, TestData.Description(), seats: [SeatKind.Person]));
        Assert.Throws<ArgumentException>(() => new MatchHost(TestData.Data, TestData.Description(), seats: [SeatKind.Nobody, SeatKind.Person]));
    }

    /// <summary>A match of the host and one guest that has run for a second.</summary>
    private static (Table Table, MatchClient Guest) Playing(LinkConditions conditions = default)
    {
        var table = new Table(conditions);
        var guest = table.Join();
        table.Run(0.5);
        table.Host.Start();
        table.Run(1);
        Assert.InRange(guest.Match!.World.Tick, 10, 21);
        return (table, guest);
    }

    /// <summary>A hosting machine and the loopback its guests arrive over.</summary>
    private sealed class Table(LinkConditions conditions = default)
    {
        private readonly List<MatchClient> _guests = [];

        public LoopbackNetwork Network { get; } = new(seed: 5);

        public MatchHost Host { get; } = new(TestData.Data, TestData.Description());

        public List<LoopbackLink> GuestLinks { get; } = [];

        public MatchClient Join(bool readyWhenBuilt = true)
        {
            var link = Network.Connect(conditions);
            GuestLinks.Add(link);
            Assert.True(Host.TryAttach(link.HostEnd));
            var guest = new MatchClient(link.ClientEnd, TestData.Data, TestData.GameVersion, readyWhenBuilt: readyWhenBuilt);
            _guests.Add(guest);
            return guest;
        }

        public void Step()
        {
            Network.Advance(Frame);
            Host.Advance(Frame);
            foreach (var guest in _guests)
            {
                guest.Advance(Frame);
            }
        }

        public void Run(double seconds)
        {
            for (var passed = TimeSpan.Zero; passed < TimeSpan.FromSeconds(seconds); passed += Frame)
            {
                Step();
            }
        }
    }
}
