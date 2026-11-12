using Sim.Core;
using Sim.Numerics;

namespace Net.Tests;

/// <summary>
/// The session running the real game: matches built by <c>MatchSetup.Create</c> on the map
/// <c>ridge</c> from the description the host sends, with move orders, between a host and its
/// clients over a loopback with latency and jitter.
/// </summary>
public class RealMatchTests
{
    private static readonly LinkConditions Internet = new(TimeSpan.FromMilliseconds(45), TimeSpan.FromMilliseconds(30));

    [Fact]
    public void AHostAndTwoClientsPlayTheSameMatch()
    {
        var match = new RealMatch(clients: 2, conditions: Internet);
        match.Start();
        var first = match.Clients[0];
        var second = match.Clients[1];
        var startOf = match.Clients.Select(client => PositionsOf(client, client.Seat)).ToArray();

        match.Run(seconds: 0.5);
        Assert.True(first.Move(40, 30));
        match.Run(seconds: 0.5);
        Assert.True(second.Move(52, 60));
        match.Run(seconds: 3);

        // Orders from both players in the same frames, to parts of each group, across the ridge.
        Assert.True(first.Move(80, 80, first: 0, count: 3));
        Assert.True(second.Move(12, 14, first: 2, count: 4));
        Assert.True(first.Move(20, 70, first: 3, count: 3));
        match.Run(seconds: 16);

        var finalTick = match.Finish();

        Assert.Equal(SessionEndReason.HostEnded, match.Host.EndInfo!.Reason);
        Assert.InRange(finalTick, 380, 420);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(SessionEndReason.HostEnded, client.Player.Session.EndInfo!.Reason);
            Assert.Equal(finalTick, client.Tick);
            Assert.Equal(0, client.Match.Simulation.DroppedCommandCount);
        });
        Assert.Equal(first.Hash, second.Hash);

        // The orders did something: every unit of both players is somewhere else.
        Assert.All(match.Clients, client =>
            Assert.All(PositionsOf(first, client.Seat).Zip(startOf[client.Seat]), pair => Assert.NotEqual(pair.Second, pair.First)));

        // With 45 to 75 ms each way the host chose a delay that covers the trip.
        Assert.InRange(match.Host.CommandDelay, 3, 4);
    }

    [Fact]
    public void EveryPeerRecordsAReplayThatReproducesTheMatch()
    {
        var match = new RealMatch(clients: 2, conditions: Internet, options: new HostOptions());
        match.Start();
        match.Run(seconds: 1);
        Assert.True(match.Clients[0].Move(40, 30));
        Assert.True(match.Clients[1].Move(60, 40, first: 1, count: 2));
        match.Run(seconds: 6);
        var finalTick = match.Finish();

        Assert.All(match.Clients, client =>
        {
            Assert.True(client.Recording!.IsComplete);
            Assert.Null(client.Recording.Failure);

            var replay = client.ReadReplay();
            Assert.True(replay.IsComplete);
            Assert.Equal(finalTick, replay.TickCount);
            Assert.Equal(2, replay.Commands.Count);
            Assert.Equal(client.Hash, replay.End!.Value.Hash);

            var again = replay.Run(TestData.Data);
            Assert.True(again.Reproduced);
            Assert.Equal(finalTick, again.TicksRun);
            Assert.Equal(client.Hash, again.FinalHash);
        });

        // Both peers were given the same commands for the same ticks.
        Assert.Equal(
            match.Clients[0].ReadReplay().Commands.Select(entry => entry.Tick),
            match.Clients[1].ReadReplay().Commands.Select(entry => entry.Tick));
    }

    [Fact]
    public void AStateThatDiffersOnOnePeerStopsTheMatchForBothAtTheSameTick()
    {
        var match = new RealMatch(clients: 2, conditions: Internet, options: new HostOptions());
        match.Start();
        match.Run(seconds: 1);
        match.Clients[0].Move(40, 30);
        match.Run(seconds: 1);

        // Something that is in one world and not the other: what a bug in the simulation does.
        var odd = match.Clients[1];
        RealMatch.Corrupt(odd);
        match.Until(() => match.Clients.All(client => client.Player.Session.Phase == SessionPhase.Stopped), "the desync is found");

        var end = match.Host.EndInfo!;
        Assert.Equal(SessionEndReason.Desync, end.Reason);
        Assert.NotNull(end.Desync);
        Assert.All(match.Clients, client =>
        {
            Assert.Equal(SessionEndReason.Desync, client.Player.Session.EndInfo!.Reason);
            Assert.Equal(end.DesyncTick, client.Player.Session.EndInfo.DesyncTick);
            Assert.Equal(end.FinalTick, client.Tick);
            Assert.True(client.Recording!.IsComplete);
        });
        Assert.NotEqual(match.Clients[0].Hash, match.Clients[1].Hash);

        // The replay of the peer that went wrong does not reproduce, and says from where.
        var wrong = odd.ReadReplay().Run(TestData.Data);
        Assert.False(wrong.Reproduced);
        Assert.True(match.Clients[0].ReadReplay().Run(TestData.Data).Reproduced);
    }

    [Fact]
    public void APlayerWhoLeavesIsToldApartFromOneWhoIsLost()
    {
        var match = new RealMatch(clients: 3, conditions: Internet);
        match.Start();
        match.Run(seconds: 1);

        match.Clients[2].Player.Leave();
        match.Until(() => match.HostDepartures.Count == 1 && match.Clients.Take(2).All(client => client.Departures.Count == 1), "the leave is known");
        Assert.Equal([(2, SessionEndReason.Left)], match.HostDepartures);
        Assert.Equal([(2, SessionEndReason.Left)], match.Clients[0].Departures);
        Assert.Equal([(2, SessionEndReason.Left)], match.Clients[1].Departures);

        // The match goes on for the two who stay.
        var tick = match.Clients[0].Tick;
        match.Run(seconds: 1);
        Assert.InRange(match.Clients[0].Tick, tick + 19, tick + 21);
        match.Finish();
        Assert.Equal(match.Clients[0].Hash, match.Clients[1].Hash);
    }

    private static FixedVector2[] PositionsOf(RealClient view, int owner)
    {
        var positions = new List<FixedVector2>();
        for (var i = 0; i < view.Match.Bodies.Count; i++)
        {
            if (view.Match.Bodies.ValueAt(i).Owner == owner)
            {
                positions.Add(view.Match.Bodies.ValueAt(i).Position);
            }
        }

        Assert.NotEmpty(positions);
        return [.. positions];
    }
}
