using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Net.Udp;

/// <summary>
/// One UDP socket and the connections that run over it: what <see cref="UdpListener"/> and
/// <see cref="UdpConnector"/> share. It owns the LiteNetLib manager and is the only code that
/// talks to it.
/// </summary>
/// <remarks>
/// <para>
/// LiteNetLib receives and resends on two threads of its own and queues what happened. That queue
/// is emptied only in <see cref="Poll"/>, on the caller's thread, so no connection changes while
/// the session is in the middle of an update.
/// </para>
/// <para>
/// Every payload starts with one byte: <see cref="DataFrame"/> before a session's message, or
/// <see cref="CloseFrame"/> alone for "this end sends nothing more". The close notice exists
/// because LiteNetLib's own disconnect drops whatever is still queued or unacknowledged, so a
/// message sent just before it can be lost. The notice travels in line behind the messages, and
/// the end that receives it is the one that disconnects.
/// </para>
/// </remarks>
internal sealed class UdpEndpoint : ILiteNetEventListener, IDisposable
{
    private const byte DataFrame = 0;
    private const byte CloseFrame = 1;

    /// <summary>
    /// The most pieces one message may arrive in. A whole message with its frame byte takes 65
    /// at the smallest packet size the library uses; anything that claims more is not ours, and
    /// is dropped before memory is set aside for it.
    /// </summary>
    private const ushort MaxFragments = 128;

    private static readonly byte[] CloseNotice = [CloseFrame];

    private readonly LiteNetManager _manager;
    private readonly UdpOptions _options;
    private readonly List<UdpConnection> _connections = [];
    private readonly Queue<UdpConnection> _incoming = new();
    private readonly byte[] _frame = new byte[ProtocolInfo.MaxMessageSize + 1];
    private bool _disposed;

    internal UdpEndpoint(IPAddress address, int port, UdpOptions? options)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        _options = options ?? new UdpOptions();
        _options.Validate();

        var (addressV4, addressV6) = Families(address);
        var connectTimeout = (int)_options.ConnectTimeout.TotalMilliseconds;
        var reconnectDelay = Math.Clamp(connectTimeout / 10, 50, 500);
        var disconnectTimeout = Math.Max(1, (int)_options.DisconnectTimeout.TotalMilliseconds);
        _manager = new LiteNetManager(this)
        {
            AutoRecycle = true,
            UnsyncedEvents = false,
            IPv6Enabled = addressV6 is not null,
            DisconnectTimeout = disconnectTimeout,

            // The library's keep-alive. A quiet connection must hear several of them within the
            // timeout, or it would be given up on for being quiet.
            PingInterval = Math.Clamp(disconnectTimeout / 4, 1, 1000),
            ReconnectDelay = reconnectDelay,
            MaxConnectAttempts = Math.Max(0, ((connectTimeout + reconnectDelay - 1) / reconnectDelay) - 1),
            MaxFragmentsCount = MaxFragments,
        };

        if (!_manager.Start(addressV4, addressV6 ?? IPAddress.IPv6None, port))
        {
            throw new IOException($"UDP port {port} on {address} could not be bound. Another program may be using it.");
        }
    }

    /// <summary>The port the socket is bound to.</summary>
    internal int Port => _manager.LocalPort;

    /// <summary>Whether a request to connect is taken. A connector takes none.</summary>
    internal bool AcceptsConnections { get; set; }

    /// <summary>The time on a clock that only goes forward. Where it started does not matter.</summary>
    internal static TimeSpan Now => Stopwatch.GetElapsedTime(0);

    internal TimeSpan CloseTimeout => _options.CloseTimeout;

    /// <summary>The connections whose link is still up, those that are closing included.</summary>
    internal int ConnectionCount => _connections.Count;

    /// <summary>
    /// Takes in everything that has happened on the socket since the last call, and gives up on
    /// closes the other end has not confirmed in time.
    /// </summary>
    internal void Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Pump();
    }

    internal UdpConnection Connect(IPEndPoint remote)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(remote);

        var peer = _manager.Connect(remote, UdpInfo.ConnectKey);
        if (peer is null || peer.Tag is not null)
        {
            throw new InvalidOperationException($"There is already a connection with {remote}.");
        }

        var connection = new UdpConnection(this, peer, connected: false);
        peer.Tag = connection;
        _connections.Add(connection);
        return connection;
    }

    internal bool TryAccept(out UdpConnection connection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _incoming.TryDequeue(out connection!);
    }

    internal void SendData(LiteNetPeer peer, ReadOnlySpan<byte> message)
    {
        _frame[0] = DataFrame;
        message.CopyTo(_frame.AsSpan(1));
        peer.Send(_frame.AsSpan(0, message.Length + 1), DeliveryMethod.ReliableOrdered);

        // Wake the library's thread rather than wait out its interval, which would add up to
        // fifteen milliseconds to every message.
        _manager.TriggerUpdate();
    }

    internal void SendCloseNotice(LiteNetPeer peer)
    {
        peer.Send(CloseNotice, DeliveryMethod.ReliableOrdered);
        _manager.TriggerUpdate();
    }

    /// <summary>Cuts the link now. What has not reached the other end is lost.</summary>
    internal void Cut(UdpConnection connection)
    {
        if (connection.LinkGone)
        {
            return;
        }

        _manager.DisconnectPeer(connection.Peer);
        Forget(connection, UdpCloseReason.ClosedHere);
    }

    /// <summary>
    /// Closes every connection in order, waits for the other ends to confirm for at most
    /// <see cref="UdpOptions.CloseTimeout"/>, and lets go of the socket and the library's threads.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var connection in _connections.ToArray())
        {
            connection.Close();
        }

        var deadline = Now + CloseTimeout;
        while (_connections.Exists(connection => connection.IsClosing) && Now < deadline)
        {
            Thread.Sleep(1);
            Pump();
        }

        _disposed = true;
        _manager.Stop();
        foreach (var connection in _connections.ToArray())
        {
            Forget(connection, UdpCloseReason.ClosedHere);
        }
    }

    void ILiteNetEventListener.OnConnectionRequest(LiteConnectionRequest request)
    {
        if (!AcceptsConnections || _connections.Count >= _options.MaxConnections)
        {
            request.Reject();
            return;
        }

        // Refuses by itself when the key is not ours, and returns nothing.
        if (request.AcceptIfKey(UdpInfo.ConnectKey) is { Tag: null } peer)
        {
            var connection = new UdpConnection(this, peer, connected: true);
            peer.Tag = connection;
            _connections.Add(connection);
            _incoming.Enqueue(connection);
        }
    }

    void ILiteNetEventListener.OnPeerConnected(LiteNetPeer peer)
    {
        // The library also reports a peer the listener accepted, which is connected already.
        if (peer.Tag is UdpConnection { LinkGone: false } connection)
        {
            connection.OnConnected();
        }
    }

    void ILiteNetEventListener.OnPeerDisconnected(LiteNetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (peer.Tag is UdpConnection connection)
        {
            Forget(connection, ReasonFor(disconnectInfo.Reason));
        }
    }

    void ILiteNetEventListener.OnNetworkReceive(LiteNetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        if (peer.Tag is not UdpConnection connection || connection.LinkGone)
        {
            return;
        }

        var kind = reader.AvailableBytes > 0 && deliveryMethod == DeliveryMethod.ReliableOrdered
            ? reader.GetByte()
            : byte.MaxValue;
        if (kind == DataFrame && reader.AvailableBytes <= ProtocolInfo.MaxMessageSize)
        {
            connection.OnData(reader.GetRemainingBytes());
        }
        else
        {
            // The close notice, after which the other end waits for this one to cut the link. Or
            // something this code never sends, from an end that cannot be trusted further.
            if (kind == CloseFrame && reader.AvailableBytes == 0)
            {
                connection.OnCloseNotice();
            }

            Cut(connection);
        }
    }

    private static (IPAddress V4, IPAddress? V6) Families(IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return (IPAddress.Any, IPAddress.IPv6Any);
        }

        if (IPAddress.IsLoopback(address))
        {
            return (IPAddress.Loopback, IPAddress.IPv6Loopback);
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return (address, null);
        }

        throw new ArgumentException(
            "Only an IPv4 address, a loopback address or an 'any' address can be bound. To take IPv6 connections, bind IPAddress.Any, which binds both families.",
            nameof(address));
    }

    private static UdpCloseReason ReasonFor(DisconnectReason reason) => reason switch
    {
        DisconnectReason.DisconnectPeerCalled => UdpCloseReason.ClosedHere,
        DisconnectReason.Timeout => UdpCloseReason.TimedOut,
        DisconnectReason.ConnectionFailed or DisconnectReason.UnknownHost => UdpCloseReason.ConnectFailed,
        DisconnectReason.ConnectionRejected or DisconnectReason.InvalidProtocol => UdpCloseReason.Refused,
        DisconnectReason.HostUnreachable or DisconnectReason.NetworkUnreachable => UdpCloseReason.Unreachable,
        _ => UdpCloseReason.ClosedByRemote,
    };

    private void Pump()
    {
        _manager.PollEvents();

        var now = Now;
        for (var index = _connections.Count - 1; index >= 0; index--)
        {
            var connection = _connections[index];
            if (connection.LingerUntil is { } until && now >= until)
            {
                Cut(connection);
            }
        }
    }

    private void Forget(UdpConnection connection, UdpCloseReason reason)
    {
        if (!connection.LinkGone)
        {
            connection.OnLinkGone(reason);
            _connections.Remove(connection);
        }
    }
}
