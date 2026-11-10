namespace Net.Tests;

/// <summary>
/// Desync detection: clients report <c>World.ComputeHash()</c>, the host compares the hashes of
/// the same tick, and a difference stops the match for everyone at a tick they all reach.
/// </summary>
public class DesyncTests
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void ADifferenceInOneWorldStopsTheMatchForEveryone(int hashInterval)
    {
        var options = new HostOptions { PlayerCount = 3, CommandDelay = 3, HashInterval = hashInterval, MaxLagTicks = 200 };
        var match = new TestMatch(clients: 3, options: options, conditions: new LinkConditions(20 * Millisecond, 15 * Millisecond));
        var hostStops = new List<SessionEndInfo>();
        match.Host.Stopped += hostStops.Add;
        match.Start();
        for (var frame = 0; frame < 130; frame++)
        {
            match.Clients[frame % 3].Spawn(frame);
            match.Step();
        }

        Assert.Equal(SessionPhase.Running, match.Host.Phase);

        // One world goes its own way: an entity the other two do not have.
        var odd = match.Clients[1];
        var changedAt = odd.Tick;
        odd.Game.World.Entities.Create();
        match.Run(seconds: 3);

        var end = Assert.Single(hostStops);
        Assert.Equal(SessionPhase.Stopped, match.Host.Phase);
        Assert.Same(end, match.Host.EndInfo);
        Assert.Equal(SessionEndReason.Desync, end.Reason);

        // The first hash that covers the change. The world was changed when it stood before tick
        // changedAt, after it had reported for that tick if a report was due, so it is the next.
        var expectedTick = ((changedAt / hashInterval) + 1) * hashInterval;
        Assert.Equal(expectedTick, end.DesyncTick);
        var desync = end.Desync!;
        Assert.Equal(expectedTick, desync.Tick);
        Assert.NotEqual(desync.FirstHash, desync.SecondHash);
        Assert.Contains(1, new[] { desync.FirstPlayer, desync.SecondPlayer });
        Assert.NotEqual(desync.FirstPlayer, desync.SecondPlayer);

        // Every peer stopped, at the same tick, which is the last one the host issued.
        Assert.Equal(match.Host.NextTick, end.FinalTick);
        Assert.True(end.FinalTick >= expectedTick);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(new SessionEndInfo(SessionEndReason.Desync, end.FinalTick, expectedTick), client.Session.EndInfo);
            Assert.Equal(end.FinalTick, client.Tick);
            Assert.False(client.Spawn(1));
        });

        // So their states can be compared like for like: two agree and one does not.
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), match.Clients[2].Game.World.ComputeHash());
        Assert.NotEqual(match.Clients[0].Game.World.ComputeHash(), odd.Game.World.ComputeHash());

        // Nothing more happens.
        var hostTick = match.Host.NextTick;
        match.Run(seconds: 1);
        Assert.Equal(hostTick, match.Host.NextTick);
        Assert.Single(hostStops);
    }

    /// <summary>
    /// Clients that built different worlds, from another seed or other data, differ before the
    /// first tick. The first report is made before any tick runs, so the match stops at once.
    /// </summary>
    [Fact]
    public void WorldsThatDifferFromTheStartAreCaughtBeforeTheFirstTick()
    {
        var options = new HostOptions { CommandDelay = 3, HashInterval = 20 };
        var match = new TestMatch(clients: 2, options: options, seedOf: seat => seat == 1 ? 999UL : ToyGame.DefaultSeed);
        match.Start();
        match.Run(seconds: 2);

        var end = match.Host.EndInfo!;
        Assert.Equal(SessionEndReason.Desync, end.Reason);
        Assert.Equal(0, end.DesyncTick);
        Assert.Equal(new ToyGame().World.ComputeHash(), end.Desync!.FirstPlayer == 0 ? end.Desync.FirstHash : end.Desync.SecondHash);

        // Only the head start was issued, and that is as far as anyone runs.
        Assert.Equal(2, end.FinalTick);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(new SessionEndInfo(SessionEndReason.Desync, 2, 0), client.Session.EndInfo);
            Assert.Equal(2, client.Tick);
        });
    }

    /// <summary>
    /// Peers compare the hash of the same tick, whenever each gets there. One that is far behind
    /// reports old ticks while the others report new ones, and that is no desync.
    /// </summary>
    [Fact]
    public void PeersThatAreAtDifferentTicksAreNotADesync()
    {
        var options = new HostOptions { PlayerCount = 3, CommandDelay = 2, HashInterval = 1 };
        var match = new TestMatch(clients: 3, options: options);
        match.Start();
        var late = match.Clients[2];

        for (var frame = 0; frame < 400; frame++)
        {
            late.Frozen = frame is > 60 and < 200;
            match.Clients[frame % 3].Spawn(frame);
            match.Step();
            if (frame == 199)
            {
                Assert.True(match.Clients[0].Tick - late.Tick > 40);
                Assert.Equal(late.Tick, match.Host.GetReportedTick(2));
            }
        }

        Assert.Equal(SessionPhase.Running, match.Host.Phase);
        Assert.Empty(match.HostDepartures);
        match.Finish();
        Assert.Equal(match.Clients[0].Game.World.ComputeHash(), late.Game.World.ComputeHash());
    }

    /// <summary>With one peer there is nothing to compare with, and its reports only show its progress.</summary>
    [Fact]
    public void ASinglePeerIsNeverADesync()
    {
        var match = new TestMatch(clients: 1);
        match.Start();
        match.Run(seconds: 1);
        match.Clients[0].Game.World.Entities.Create();
        match.Run(seconds: 1);

        Assert.Equal(SessionPhase.Running, match.Host.Phase);
        Assert.InRange(match.Host.GetReportedTick(0), 38, 41);
    }
}
