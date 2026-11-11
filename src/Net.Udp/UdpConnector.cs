using System.Net;
using System.Net.Sockets;

namespace Net.Udp;

/// <summary>
/// A client's side of the real network: turns the host's address and port into a connection for a
/// <see cref="ClientSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Poll"/> once at the start of every update, before the session's own
/// <c>Advance</c>. Messages arrive and the connection ends only inside it, never in between and
/// never on another thread. Keep polling after the session has stopped, until the connector is
/// disposed: a connection the client closed is still delivering what was sent before the close.
/// </para>
/// <para>Use it from one thread.</para>
/// </remarks>
public sealed class UdpConnector : IDisposable
{
    private readonly UdpEndpoint _endpoint;

    /// <summary>Opens a socket on any local address, to reach a host on another machine.</summary>
    /// <param name="options">Timeouts. Null for the defaults.</param>
    public UdpConnector(UdpOptions? options = null)
        : this(IPAddress.Any, options)
    {
    }

    /// <summary>Opens a socket on the given local address, on a port the system picks.</summary>
    /// <param name="localAddress">
    /// <see cref="IPAddress.Any"/> to reach a host on another machine.
    /// <see cref="IPAddress.Loopback"/> reaches only a host on this machine, and no firewall asks about it.
    /// </param>
    /// <param name="options">Timeouts. Null for the defaults.</param>
    /// <exception cref="IOException">No port could be bound.</exception>
    public UdpConnector(IPAddress localAddress, UdpOptions? options = null)
    {
        _endpoint = new UdpEndpoint(localAddress, 0, options);
    }

    /// <summary>
    /// Starts connecting and returns at once. The connection can be given to a
    /// <see cref="ClientSession"/> straight away: what is sent before the host answers is kept
    /// and goes out, in order, when it does. If the host never answers or refuses, the connection
    /// stops being open and <see cref="UdpConnection.CloseReason"/> says which.
    /// </summary>
    /// <exception cref="InvalidOperationException">This connector already has a connection with that host.</exception>
    public UdpConnection Connect(IPEndPoint host) => _endpoint.Connect(host);

    /// <summary>
    /// Looks the host's name up and starts connecting. An address in text, such as "192.168.1.20",
    /// is used as it is. The lookup of a name waits for the answer, so it can hold the caller up.
    /// </summary>
    /// <exception cref="SocketException">The name could not be found.</exception>
    /// <exception cref="InvalidOperationException">This connector already has a connection with that host.</exception>
    public UdpConnection Connect(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (!IPAddress.TryParse(host, out var address))
        {
            var addresses = Dns.GetHostAddresses(host);
            address = Array.Find(addresses, candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                ?? Array.Find(addresses, candidate => candidate.AddressFamily == AddressFamily.InterNetworkV6)
                ?? throw new SocketException((int)SocketError.HostNotFound);
        }

        return Connect(new IPEndPoint(address, port));
    }

    /// <summary>
    /// How many connections still have their link up, those that are closing included. Once it is
    /// zero, <see cref="Dispose"/> has nothing to wait for.
    /// </summary>
    public int ConnectionCount => _endpoint.ConnectionCount;

    /// <summary>Takes in everything that has happened on the network since the last call.</summary>
    public void Poll() => _endpoint.Poll();

    /// <summary>
    /// Closes every connection in order and releases the socket. It waits, for at most
    /// <see cref="UdpOptions.CloseTimeout"/>, until the host has what was sent to it.
    /// </summary>
    public void Dispose() => _endpoint.Dispose();
}
