using System.Diagnostics;
using System.Net;
using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using GameClient = Net.MatchClient;

namespace Net.Udp.Tests;

/// <summary>
/// The real game over the real transport, as the game wires it: a hosting machine with its own
/// player, and a guest that connects by address and port, is sent the description of the match,
/// builds it and plays. Both ends are in this process, each with its own socket.
/// </summary>
public class UdpRealMatchTests
{
    [Fact]
    public void AHostAndAGuestPlayTheSameMatchAndRecordTheSameReplay()
    {
        var clock = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        var listener = new UdpListener(IPAddress.Loopback);
        var connector = new UdpConnector(IPAddress.Loopback);
        var host = new MatchHost(
            TestData.Data,
            TestData.Description(seed: 2026),
            hostOptions: new HostOptions { TickInterval = TimeSpan.FromMilliseconds(20), MaxLagTicks = 100_000 });
        var guest = new GameClient(connector.Connect(new IPEndPoint(IPAddress.Loopback, listener.Port)), TestData.Data, TestData.GameVersion, readyWhenBuilt: true);
        var replays = new[] { new MemoryStream(), new MemoryStream() };
        var hostRecording = new MatchRecording(host.Local.Session, host.Description, host.Local.Match!, replays[0]);
        MatchRecording? guestRecording = null;
        guest.MatchBuilt += () => guestRecording = new MatchRecording(guest.Session, guest.Description!, guest.Match!, replays[1]);

        // One turn of each program's loop: the network first, then the session.
        void Step()
        {
            var now = clock.Elapsed;
            var elapsed = now - last;
            last = now;

            listener.Poll();
            while (listener.TryAccept(out var connection))
            {
                Assert.True(host.TryAttach(connection));
            }

            host.Advance(elapsed);
            connector.Poll();
            guest.Advance(elapsed);
        }

        try
        {
            Wait.Until(() => host.CanStart, Step, "the guest has joined, built the match and answered a ping");
            listener.Accepting = false;
            host.Start();
            Wait.Until(() => guest.Session.Phase == SessionPhase.Running, Step, "the guest hears of the start");

            var ordered = new bool[4];
            Wait.Until(
                () =>
                {
                    var tick = guest.Match!.World.Tick;
                    Order(0, tick >= 5, host.Local, 0, 40, 30);
                    Order(1, tick >= 20, guest, 1, 52, 60);
                    Order(2, tick >= 60, host.Local, 0, 80, 80);
                    Order(3, tick >= 60, guest, 1, 12, 14);
                    return tick >= 150;
                },
                Step,
                "150 ticks have run");

            host.End();
            Wait.Until(() => guest.Session.Phase == SessionPhase.Stopped, Step, "the guest has run its last tick");

            var finalTick = host.Session.EndInfo!.FinalTick;
            var hostWorld = host.Local.Match!.World;
            var guestWorld = guest.Match!.World;
            Assert.Equal(SessionEndReason.HostEnded, guest.Session.EndInfo!.Reason);
            Assert.Equal((finalTick, finalTick), (hostWorld.Tick, guestWorld.Tick));
            Assert.Equal(hostWorld.ComputeHash(), guestWorld.ComputeHash());
            Assert.Equal(0, host.Local.Match.Simulation.DroppedCommandCount);

            // The orders did something: the world is not as it was built.
            Assert.NotEqual(TestData.Description(seed: 2026).CreateMatch(TestData.Data).World.ComputeHash(), hostWorld.ComputeHash());

            // Each wrote a replay, and either one gives the match back.
            Assert.True(hostRecording.IsComplete);
            Assert.True(guestRecording!.IsComplete);
            foreach (var file in replays)
            {
                var replay = Replay.Read(new ByteReader(file.ToArray()));
                Assert.Equal(4, replay.Commands.Count);
                var again = replay.Run(TestData.Data);
                Assert.True(again.Reproduced);
                Assert.Equal(hostWorld.ComputeHash(), again.FinalHash);
            }

            void Order(int index, bool due, GameClient player, int seat, int x, int y)
            {
                if (due && !ordered[index])
                {
                    ordered[index] = player.Session.Submit(new Command(CommandType.Move, seat, UnitsOf(player.Match!, seat), Fixed.FromInt(x), Fixed.FromInt(y)));
                }
            }
        }
        finally
        {
            Wait.DisposeWhilePolling(listener, connector.Poll);
            connector.Dispose();
        }
    }

    [Fact]
    public void AGuestOfAnotherBuildIsToldWhyAndTheHostHearsOfIt()
    {
        var clock = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        var listener = new UdpListener(IPAddress.Loopback);
        var connector = new UdpConnector(IPAddress.Loopback);
        var host = new MatchHost(TestData.Data, TestData.Description());
        var departures = new List<(int Player, SessionEndReason Reason)>();
        host.Session.PlayerLeft += (player, reason) => departures.Add((player, reason));
        var guest = new GameClient(connector.Connect(new IPEndPoint(IPAddress.Loopback, listener.Port)), TestData.Data, "another-build");

        void Step()
        {
            var now = clock.Elapsed;
            var elapsed = now - last;
            last = now;
            listener.Poll();
            while (listener.TryAccept(out var connection))
            {
                Assert.True(host.TryAttach(connection));
            }

            host.Advance(elapsed);
            connector.Poll();
            guest.Advance(elapsed);
        }

        try
        {
            Wait.Until(() => departures.Count > 0, Step, "the host hears that the guest has left");

            Assert.Equal([(1, SessionEndReason.SetupMismatch)], departures);
            Assert.Equal(SessionEndReason.SetupMismatch, guest.Session.EndInfo!.Reason);
            Assert.Contains("'another-build'", guest.Refusal);
            Assert.False(host.Session.IsAttached(1));
        }
        finally
        {
            Wait.DisposeWhilePolling(listener, connector.Poll);
            connector.Dispose();
        }
    }

    private static EntityId[] UnitsOf(Match match, int player)
    {
        var units = new List<EntityId>();
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            if (match.Bodies.ValueAt(i).Owner == player)
            {
                units.Add(match.Bodies.EntityAt(i));
            }
        }

        return [.. units];
    }
}
