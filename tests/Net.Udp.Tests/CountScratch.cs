using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Sim.Core;

namespace Net.Udp.Tests;

/// <summary>
/// Temporary. Measures, on whatever system runs it, why the number of commands that run in 120
/// ticks over the bad network varies, and fails on purpose so that the numbers show in the log.
/// Removed again before this branch is ready.
/// </summary>
public sealed class CountScratch
{
    [Fact]
    public void MeasureCommands()
    {
        var sleep = int.Parse(Environment.GetEnvironmentVariable("COUNT_SLEEP") ?? "1", CultureInfo.InvariantCulture);
        var runs = int.Parse(Environment.GetEnvironmentVariable("COUNT_RUNS") ?? "10", CultureInfo.InvariantCulture);
        var text = new StringBuilder();

        var clock = Stopwatch.StartNew();
        for (var index = 0; index < 50; index++)
        {
            Thread.Sleep(1);
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"cores={Environment.ProcessorCount} sleep1={clock.Elapsed.TotalMilliseconds / 50:F1}ms");
        for (var run = 0; run < runs; run++)
        {
            text.AppendLine(One(sleep, byCount: false));
        }

        for (var run = 0; run < Math.Max(1, runs * 3 / 5); run++)
        {
            text.AppendLine(One(sleep, byCount: true));
        }

        var output = Environment.GetEnvironmentVariable("COUNT_OUT");
        if (output is null)
        {
            Assert.Fail("MEASURED\n" + text);
        }

        File.AppendAllText(output, text.ToString());
    }

    private static string One(int sleep, bool byCount)
    {
        var options = new HostOptions { PlayerCount = 2, HashInterval = 1, TickInterval = TimeSpan.FromMilliseconds(20), MaxLagTicks = 100_000 };
        using var match = new UdpMatch(2, options, RelayConditions.Milliseconds(20, jitter: 20, loss: 0.05));
        match.Start();

        var lastCommandTick = new int[2];
        var submitted = new int[2];
        var lastTick = new int[2];
        var lastMoved = new TimeSpan[2];
        var jumps = new int[2];
        var stalls = new int[2];
        var stalled = new TimeSpan[2];
        var longest = new TimeSpan[2];
        var submitTick = new List<int>();
        var commands = 0;
        var steps = 0;
        var slowestStep = TimeSpan.Zero;
        var clock = Stopwatch.StartNew();
        var before = clock.Elapsed;
        while (true)
        {
            match.Step();
            steps++;
            var now = clock.Elapsed;
            slowestStep = now - before > slowestStep ? now - before : slowestStep;
            before = now;
            foreach (var client in match.Clients)
            {
                var seat = client.Seat;
                var moved = client.Tick - lastTick[seat];
                if (moved > 0)
                {
                    var waited = now - lastMoved[seat];
                    if (lastTick[seat] > 0 && waited > TimeSpan.FromMilliseconds(100))
                    {
                        stalls[seat]++;
                        stalled[seat] += waited;
                        longest[seat] = waited > longest[seat] ? waited : longest[seat];
                    }

                    if (moved >= 4)
                    {
                        jumps[seat]++;
                    }

                    lastTick[seat] = client.Tick;
                    lastMoved[seat] = now;
                }

                if (client.Session.Phase == SessionPhase.Running && client.Tick >= lastCommandTick[seat] + 4)
                {
                    lastCommandTick[seat] = client.Tick;
                    Assert.True(client.Session.Submit(new Command(commands % 3 == 2 ? ToyGame.Cull : ToyGame.Spawn, seat, contentId: commands)));
                    submitTick.Add(client.Tick);
                    commands++;
                    submitted[seat]++;
                }
            }

            var done = match.Clients.All(client => client.Tick >= 120
                && (!byCount || (client.Game.Log.Count(executed => executed.Player == 0) >= 20 && client.Game.Log.Count(executed => executed.Player == 1) >= 20)));
            if (done)
            {
                break;
            }

            Assert.True(now < TimeSpan.FromSeconds(30));
            Thread.Sleep(sleep);
        }

        var took = clock.Elapsed;
        var endTicks = string.Join(",", match.Clients.Select(client => client.Tick));
        var libraryRoundTrips = string.Join(",", Enumerable.Range(0, 2).Select(seat => LibraryNumbers(match.HostConnections[seat])));
        var sessionRoundTrips = string.Join(",", Enumerable.Range(0, 2).Select(seat => (match.Host.GetRoundTrip(seat) ?? TimeSpan.Zero).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)));
        var finalTick = match.Finish();
        var log = match.Clients[0].Game.Log;
        var ranIds = log.Select(executed => executed.ContentId).ToHashSet();
        var lostAt = string.Join(",", Enumerable.Range(0, commands).Where(id => !ranIds.Contains(id)).Select(id => submitTick[id]));
        var waits = log.Select(executed => executed.Tick - submitTick[executed.ContentId]).Order().ToArray();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(byCount ? "NEW" : "OLD")} ran={log.Count(executed => executed.Player == 0)}+{log.Count(executed => executed.Player == 1)}={log.Count} submitted={submitted[0]}+{submitted[1]} final={finalTick} endTicks={endTicks} ms={took.TotalMilliseconds:F0} steps={steps} slowestStepMs={slowestStep.TotalMilliseconds:F0} jumps={jumps[0]}+{jumps[1]} stalls={stalls[0]}+{stalls[1]} stalledMs={stalled[0].TotalMilliseconds:F0}+{stalled[1].TotalMilliseconds:F0} longestMs={longest[0].TotalMilliseconds:F0},{longest[1].TotalMilliseconds:F0} dropped={match.Clients[0].Relay!.Dropped}+{match.Clients[1].Relay!.Dropped} notRunSubmittedAt=[{lostAt}] waitTicks med={waits[waits.Length / 2]} p90={waits[waits.Length * 9 / 10]} max={waits[^1]} libRttResend={libraryRoundTrips} sessionRtt={sessionRoundTrips}");
    }

    /// <summary>The library's own round trip estimate and the resend delay it derives from it, in milliseconds.</summary>
    private static string LibraryNumbers(UdpConnection connection)
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var peer = typeof(UdpConnection).GetProperty("Peer", Hidden)!.GetValue(connection)!;
        var roundTrip = peer.GetType().GetProperty("RoundTripTime", Hidden)!.GetValue(peer);
        var resend = peer.GetType().GetProperty("ResendDelay", Hidden)!.GetValue(peer);
        return string.Create(CultureInfo.InvariantCulture, $"{roundTrip}/{resend:F0}");
    }
}
