namespace Net.Udp;

/// <summary>The settings of a <see cref="UdpListener"/> or a <see cref="UdpConnector"/>.</summary>
public sealed record UdpOptions
{
    /// <summary>
    /// How long nothing at all may arrive from the other end, the library's own keep-alive packets
    /// included, before the connection counts as lost.
    /// </summary>
    public TimeSpan DisconnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a connector keeps asking a host that does not answer.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a closed connection waits for the other end to confirm that it has everything
    /// that was sent before the close. After that the connection is cut regardless.
    /// </summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The most connections a listener holds at once. More are refused.</summary>
    public int MaxConnections { get; init; } = 16;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(DisconnectTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DisconnectTimeout, UdpInfo.LongestTime);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ConnectTimeout, UdpInfo.LongestTime);
        ArgumentOutOfRangeException.ThrowIfLessThan(CloseTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(CloseTimeout, UdpInfo.LongestTime);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConnections, 1);
    }
}

/// <summary>Where a <see cref="UdpConnection"/> is in its life.</summary>
public enum UdpConnectionState
{
    /// <summary>A connector has asked the host and the host has not answered yet.</summary>
    Connecting,

    /// <summary>Both ends are there.</summary>
    Connected,

    /// <summary>Over, for the reason <see cref="UdpConnection.CloseReason"/> gives.</summary>
    Closed,
}

/// <summary>Why a <see cref="UdpConnection"/> is over: the first thing that ended it.</summary>
public enum UdpCloseReason
{
    /// <summary>It is not over.</summary>
    None,

    /// <summary>This end closed it.</summary>
    ClosedHere,

    /// <summary>The other end closed it.</summary>
    ClosedByRemote,

    /// <summary>Nothing arrived from the other end for the time the options allow.</summary>
    TimedOut,

    /// <summary>The host never answered: wrong address or port, nobody hosting, or a firewall in between.</summary>
    ConnectFailed,

    /// <summary>The host answered and refused: it takes no more connections, or it is another program or version.</summary>
    Refused,

    /// <summary>The network reported that the other end cannot be reached.</summary>
    Unreachable,
}

/// <summary>Fixed facts both ends of a UDP connection agree on.</summary>
public static class UdpInfo
{
    /// <summary>
    /// What a connector says when it asks to connect. A listener refuses anything else, so another
    /// program on the same port, or a version of this one that frames its messages differently,
    /// is turned away before any message passes. Change the number when the framing changes.
    /// </summary>
    public const string ConnectKey = "empire-earth-4/udp/1";

    /// <summary>The longest time an option may be set to.</summary>
    internal static readonly TimeSpan LongestTime = TimeSpan.FromHours(1);
}
