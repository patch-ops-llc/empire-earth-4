using System.Net;
using LiteNetLib;

namespace Net.Udp;

/// <summary>
/// One end of a connection over UDP: an <see cref="IConnection"/> for a session, made by a
/// <see cref="UdpListener"/> on the host or a <see cref="UdpConnector"/> on a client.
/// </summary>
/// <remarks>
/// <para>
/// It keeps what <see cref="IConnection"/> promises. Messages arrive whole, once each and in the
/// order they were sent, up to <see cref="ProtocolInfo.MaxMessageSize"/> bytes, however many
/// datagrams that takes and however many of them the network loses. Nothing arrives, and nothing
/// about the connection changes, except inside the <c>Poll</c> of the listener or connector that
/// made it, so the session sees it change only between its own updates.
/// </para>
/// <para>
/// A close is orderly. The end that closes first tells the other that nothing more will come, in
/// line behind everything it sent. The other end answers by cutting the link once that notice has
/// reached it, which is after all the messages before it. Until then, or until
/// <see cref="UdpOptions.CloseTimeout"/> has passed, the closing end keeps the link up so that
/// lost datagrams can be sent again. The listener or connector must keep being polled meanwhile,
/// or be disposed, which waits.
/// </para>
/// <para>Use it from the thread that polls. It is not safe to use from two threads at once.</para>
/// </remarks>
public sealed class UdpConnection : IConnection
{
    private readonly UdpEndpoint _endpoint;
    private readonly Queue<ReadOnlyMemory<byte>> _inbox = new();

    /// <summary>What was sent before the host answered. Null once it has.</summary>
    private Queue<byte[]>? _unsent;

    private bool _closedHere;

    /// <summary>The other end said it sends nothing more, or the link is gone.</summary>
    private bool _remoteDone;

    internal UdpConnection(UdpEndpoint endpoint, LiteNetPeer peer, bool connected)
    {
        _endpoint = endpoint;
        Peer = peer;
        RemoteEndPoint = new IPEndPoint(peer.Address, peer.Port);
        _unsent = connected ? null : new Queue<byte[]>();
    }

    /// <summary>The address and port of the other end.</summary>
    public IPEndPoint RemoteEndPoint { get; }

    /// <summary>
    /// Where the link is. It can be <see cref="UdpConnectionState.Closed"/> while
    /// <see cref="IsOpen"/> is still true: the other end has closed and messages it sent before
    /// that are still waiting to be received.
    /// </summary>
    public UdpConnectionState State =>
        CloseReason != UdpCloseReason.None ? UdpConnectionState.Closed
        : _unsent is null ? UdpConnectionState.Connected
        : UdpConnectionState.Connecting;

    /// <summary>Why the connection is over, for a message to the player. None while it is not.</summary>
    public UdpCloseReason CloseReason { get; private set; }

    /// <inheritdoc/>
    /// <remarks>True while a connector is still waiting for the host's answer.</remarks>
    public bool IsOpen => !_closedHere && !(_remoteDone && _inbox.Count == 0);

    internal LiteNetPeer Peer { get; }

    /// <summary>
    /// Set while this end waits for the other to confirm a close: the time on the endpoint's
    /// clock at which it stops waiting.
    /// </summary>
    internal TimeSpan? LingerUntil { get; set; }

    /// <summary>True once the library has let go of the link. Nothing more can pass.</summary>
    internal bool LinkGone { get; private set; }

    /// <summary>True while a close is still on its way to the other end.</summary>
    internal bool IsClosing => !LinkGone && LingerUntil is not null;

    /// <inheritdoc/>
    /// <remarks>
    /// Before the host has answered, messages are kept and sent, in order, as soon as it does.
    /// </remarks>
    /// <exception cref="ArgumentException">The message is longer than <see cref="ProtocolInfo.MaxMessageSize"/>.</exception>
    public void Send(ReadOnlySpan<byte> message)
    {
        if (message.Length > ProtocolInfo.MaxMessageSize)
        {
            throw new ArgumentException(
                $"A message of {message.Length} bytes is longer than the {ProtocolInfo.MaxMessageSize} a connection carries.",
                nameof(message));
        }

        if (_closedHere || _remoteDone)
        {
            return;
        }

        if (_unsent is not null)
        {
            _unsent.Enqueue(message.ToArray());
        }
        else
        {
            _endpoint.SendData(Peer, message);
        }
    }

    /// <inheritdoc/>
    public bool TryReceive(out ReadOnlyMemory<byte> message)
    {
        if (_closedHere || _inbox.Count == 0)
        {
            message = default;
            return false;
        }

        message = _inbox.Dequeue();
        return true;
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (_closedHere)
        {
            return;
        }

        _closedHere = true;
        _inbox.Clear();
        if (CloseReason == UdpCloseReason.None)
        {
            CloseReason = UdpCloseReason.ClosedHere;
        }

        if (LinkGone || _remoteDone)
        {
            // The other end closed first and the link was cut when its notice came.
            return;
        }

        if (_unsent is null)
        {
            SendCloseNotice();
        }
        else
        {
            // The host has not answered yet, and the request cannot be called back: if this end
            // just went silent, the host would take the connection and keep a place for it until
            // it timed out. So wait for the answer. What was sent goes out then, and the notice
            // behind it.
            LingerUntil = UdpEndpoint.Now + _endpoint.CloseTimeout;
        }
    }

    /// <summary>The host answered.</summary>
    internal void OnConnected()
    {
        if (_unsent is not { } unsent)
        {
            return;
        }

        _unsent = null;
        foreach (var message in unsent)
        {
            _endpoint.SendData(Peer, message);
        }

        if (_closedHere)
        {
            SendCloseNotice();
        }
    }

    /// <summary>A message arrived.</summary>
    internal void OnData(ReadOnlyMemory<byte> message)
    {
        if (!_closedHere && !_remoteDone)
        {
            _inbox.Enqueue(message);
        }
    }

    /// <summary>The other end says it sends nothing more. Everything it sent has arrived before this.</summary>
    internal void OnCloseNotice()
    {
        _remoteDone = true;
        if (CloseReason == UdpCloseReason.None)
        {
            CloseReason = UdpCloseReason.ClosedByRemote;
        }
    }

    /// <summary>The link is gone, whichever way.</summary>
    internal void OnLinkGone(UdpCloseReason reason)
    {
        LinkGone = true;
        LingerUntil = null;
        _unsent = null;
        _remoteDone = true;
        if (CloseReason == UdpCloseReason.None)
        {
            CloseReason = reason;
        }
    }

    private void SendCloseNotice()
    {
        _endpoint.SendCloseNotice(Peer);
        LingerUntil = UdpEndpoint.Now + _endpoint.CloseTimeout;
    }
}
