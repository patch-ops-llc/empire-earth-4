namespace Net;

/// <summary>Fixed facts about the session protocol that the host and every client agree on.</summary>
public static class ProtocolInfo
{
    /// <summary>
    /// The version of the session protocol. A client states it when it joins and the host turns
    /// away any other. Raise it whenever a message changes its meaning or its layout
    /// (<see cref="MessageCodec"/>).
    /// </summary>
    /// <remarks>
    /// Version 2 added <see cref="MatchNotice"/> and <see cref="LeaveNotice"/>, and the reason
    /// <see cref="SessionEndReason.SetupMismatch"/>. Version 3 added <see cref="PauseNotice"/> and
    /// <see cref="SpeedNotice"/>, the host's pause and speed.
    /// </remarks>
    public const int Version = 3;

    /// <summary>The longest command delay a host may set, in ticks.</summary>
    public const int MaxCommandDelay = 20;

    /// <summary>
    /// The most bytes one encoded command may take: room for about 2,000 entities, several times
    /// what one player can own. The encoding itself sets no limit, so the protocol does: a client
    /// cannot submit a larger command and the host does not accept one.
    /// </summary>
    public const int MaxCommandSize = 8 * 1024;

    /// <summary>
    /// The most bytes one message may take. A transport must carry messages of this size; a
    /// longer one is treated as a fault of the sender. The host never issues a larger bundle:
    /// commands that do not fit run one tick later.
    /// </summary>
    public const int MaxMessageSize = 64 * 1024;
}
