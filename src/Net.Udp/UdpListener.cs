using System.Net;

namespace Net.Udp;

/// <summary>
/// The host's side of the real network: one UDP port that takes connections and hands each to the
/// caller, which gives it a seat with <see cref="HostSession.Attach"/>.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Poll"/> once at the start of every update, before the session's own
/// <c>Advance</c>. Connections are taken, messages arrive and connections end only inside it,
/// never in between and never on another thread. Keep polling after the session has stopped,
/// until the listener is disposed: a connection the host closed is still delivering what was
/// sent before the close.
/// </para>
/// <para>Use it from one thread.</para>
/// </remarks>
public sealed class UdpListener : IDisposable
{
    private readonly UdpEndpoint _endpoint;

    /// <summary>Binds the port and starts taking connections.</summary>
    /// <param name="address">
    /// The local address to bind. <see cref="IPAddress.Any"/> takes connections from other
    /// machines, over IPv4 and, where the system has it, IPv6. <see cref="IPAddress.Loopback"/>
    /// takes them from this machine only, which no firewall asks about.
    /// </param>
    /// <param name="port">The port, or 0 for one the system picks; <see cref="Port"/> says which.</param>
    /// <param name="options">Timeouts and the connection limit. Null for the defaults.</param>
    /// <exception cref="IOException">The port could not be bound.</exception>
    public UdpListener(IPAddress address, int port = 0, UdpOptions? options = null)
    {
        _endpoint = new UdpEndpoint(address, port, options) { AcceptsConnections = true };
    }

    /// <summary>The port clients connect to.</summary>
    public int Port => _endpoint.Port;

    /// <summary>
    /// Whether new connections are taken. Turn it off once the match has started and no seat is
    /// left to give; a client that tries is refused at once instead of waiting.
    /// </summary>
    public bool Accepting
    {
        get => _endpoint.AcceptsConnections;
        set => _endpoint.AcceptsConnections = value;
    }

    /// <summary>
    /// The connections that count towards <see cref="UdpOptions.MaxConnections"/>: every one
    /// whose link is still up, whether or not the caller has taken it yet, those that are closing
    /// included. Once it is zero, <see cref="Dispose"/> has nothing to wait for.
    /// </summary>
    public int ConnectionCount => _endpoint.ConnectionCount;

    /// <summary>Takes in everything that has happened on the network since the last call.</summary>
    public void Poll() => _endpoint.Poll();

    /// <summary>Hands over the next connection a client has made, if there is one.</summary>
    public bool TryAccept(out UdpConnection connection) => _endpoint.TryAccept(out connection);

    /// <summary>
    /// Closes every connection in order and releases the port. It waits, for at most
    /// <see cref="UdpOptions.CloseTimeout"/>, until the clients have what was sent to them.
    /// </summary>
    public void Dispose() => _endpoint.Dispose();
}
