namespace Net;

/// <summary>
/// One end of a connection between the host and a client, and the only thing the sessions know
/// about the network. It carries messages as bytes and knows nothing of what is in them. The
/// loopback implements it in one process; a real transport implements it over the wire.
/// </summary>
/// <remarks>
/// What an implementation must guarantee:
/// <list type="bullet">
/// <item>
/// Messages arrive complete, once each, unchanged and in the order they were sent, each as the
/// same run of bytes that was handed to <see cref="Send"/>, up to
/// <see cref="ProtocolInfo.MaxMessageSize"/> bytes long.
/// </item>
/// <item>Nothing here blocks or runs on another thread. The session polls it from its own update.</item>
/// <item>
/// Closing is orderly: everything sent before <see cref="Close"/> still reaches the other end, which
/// sees <see cref="IsOpen"/> turn false only after it has received all of it.
/// </item>
/// </list>
/// </remarks>
public interface IConnection
{
    /// <summary>
    /// False once this end was closed, or once the other end closed or was lost and everything it
    /// had sent has been received.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Sends one message to the other end. The bytes are copied before this returns. Does nothing
    /// once the connection is closed.
    /// </summary>
    void Send(ReadOnlySpan<byte> message);

    /// <summary>
    /// Takes the next message that has arrived, if there is one. The bytes are only valid until
    /// the next call.
    /// </summary>
    bool TryReceive(out ReadOnlyMemory<byte> message);

    /// <summary>Closes the connection from this end. Messages still on their way to it are lost.</summary>
    void Close();
}
