using System.Diagnostics;
using System.Net;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Net.Udp.Tests;

/// <summary>
/// These tests run on real sockets and real time, so they wait for things instead of stepping a
/// clock. Every wait has a generous limit: a slow machine makes a test slower, not red. The tests
/// run one at a time, so that one test's load cannot run another's timeouts out.
/// </summary>
internal static class Wait
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>Polls until the condition holds, and fails the test if it never does.</summary>
    public static void Until(Func<bool> condition, Action poll, string what)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            poll();
            if (condition())
            {
                return;
            }

            if (clock.Elapsed > Limit)
            {
                Assert.Fail($"Gave up after {Limit.TotalSeconds:0} seconds waiting until {what}.");
            }

            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Disposes a listener or a connector as a program of its own would: on another thread,
    /// while this one goes on polling the other end, until the dispose has returned. Two ends in
    /// one thread would otherwise wait for each other until the close timeout.
    /// </summary>
    public static void DisposeWhilePolling(IDisposable disposable, Action poll)
    {
        var thread = new Thread(disposable.Dispose) { IsBackground = true, Name = "Dispose" };
        thread.Start();
        Until(() => !thread.IsAlive, poll, "the dispose returns");
    }

    /// <summary>Keeps polling for a length of time, for a test that says something must not happen.</summary>
    public static void For(TimeSpan time, Action poll)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < time)
        {
            poll();
            Thread.Sleep(1);
        }
    }
}

/// <summary>
/// A listener, a connector and one connection between them over the local address, both ends in
/// this process, with a <see cref="UdpRelay"/> in between when the test asks for a bad network.
/// </summary>
internal sealed class UdpLink : IDisposable
{
    public UdpLink(RelayConditions? conditions = null, UdpOptions? options = null, int seed = 1)
    {
        Listener = new UdpListener(IPAddress.Loopback, 0, options);
        Relay = conditions is null ? null : new UdpRelay(Listener.Port, conditions, seed);
        Connector = new UdpConnector(IPAddress.Loopback, options);
        ClientEnd = Connector.Connect(new IPEndPoint(IPAddress.Loopback, Relay?.Port ?? Listener.Port));

        UdpConnection? hostEnd = null;
        Until(() => hostEnd is not null || Listener.TryAccept(out hostEnd), "the listener has the connection");
        HostEnd = hostEnd!;
        Until(() => ClientEnd.State == UdpConnectionState.Connected, "the connector hears the answer");
    }

    public UdpListener Listener { get; }

    public UdpConnector Connector { get; }

    public UdpRelay? Relay { get; }

    public UdpConnection HostEnd { get; }

    public UdpConnection ClientEnd { get; }

    /// <summary>Set by a test that disposes the listener itself, so that it is polled no more.</summary>
    public bool ListenerGone { get; set; }

    /// <summary>Set by a test that disposes the connector itself, so that it is polled no more.</summary>
    public bool ConnectorGone { get; set; }

    public void Poll()
    {
        if (!ListenerGone)
        {
            Listener.Poll();
        }

        if (!ConnectorGone)
        {
            Connector.Poll();
        }
    }

    public void Until(Func<bool> condition, string what) => Wait.Until(condition, Poll, what);

    public void For(int milliseconds) => Wait.For(TimeSpan.FromMilliseconds(milliseconds), Poll);

    /// <summary>Waits for one message and returns it.</summary>
    public ReadOnlyMemory<byte> Receive(UdpConnection connection)
    {
        ReadOnlyMemory<byte> message = default;
        Until(() => connection.TryReceive(out message), "a message arrives");
        return message;
    }

    /// <summary>Waits for one message and decodes it.</summary>
    public NetMessage ReceiveMessage(UdpConnection connection) => MessageCodec.Read(Receive(connection));

    /// <summary>
    /// Closes both ends and polls both until the links are gone, so that neither dispose has
    /// anything to wait for. Two programs would each be polling on their own; here one thread
    /// has to do it for both.
    /// </summary>
    public void Dispose()
    {
        if (Relay is not null)
        {
            Relay.Conditions = new RelayConditions();
        }

        ClientEnd.Close();
        HostEnd.Close();
        var clock = Stopwatch.StartNew();
        while ((Listener.ConnectionCount > 0 || Connector.ConnectionCount > 0) && clock.Elapsed < TimeSpan.FromSeconds(2))
        {
            Poll();
            Thread.Sleep(1);
        }

        Connector.Dispose();
        Listener.Dispose();
        Relay?.Dispose();
    }
}
