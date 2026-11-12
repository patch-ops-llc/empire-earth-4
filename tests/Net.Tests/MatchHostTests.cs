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

    /// <summary>A hosting machine and the loopback its guests arrive over.</summary>
    private sealed class Table(LinkConditions conditions = default)
    {
        private readonly List<MatchClient> _guests = [];

        public LoopbackNetwork Network { get; } = new(seed: 5);

        public MatchHost Host { get; } = new(TestData.Data, TestData.Description());

        public MatchClient Join(bool readyWhenBuilt = true)
        {
            var link = Network.Connect(conditions);
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
