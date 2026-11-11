namespace Net;

/// <summary>
/// Why a session ended for a peer, or why a peer left one. The numbers are part of the protocol:
/// never change or reuse one.
/// </summary>
public enum SessionEndReason
{
    /// <summary>Not ended.</summary>
    None = 0,

    /// <summary>The host ended the match.</summary>
    HostEnded = 1,

    /// <summary>Two peers reported different state hashes for the same tick.</summary>
    Desync = 2,

    /// <summary>The connection closed without a word: the other side quit, crashed or lost its network.</summary>
    ConnectionLost = 3,

    /// <summary>The peer fell further behind the host than the host allows and was dropped.</summary>
    FellBehind = 4,

    /// <summary>The peer sent something the protocol does not allow at that point.</summary>
    ProtocolError = 5,

    /// <summary>The peer speaks another version of the protocol.</summary>
    VersionMismatch = 6,

    /// <summary>The peer left on purpose.</summary>
    Left = 7,

    /// <summary>
    /// The peer cannot build the match the host described: it runs another build of the game, has
    /// other data, or lacks the map.
    /// </summary>
    SetupMismatch = 8,
}
