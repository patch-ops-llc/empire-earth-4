using System.Globalization;

namespace Game;

/// <summary>What the game does when it has started.</summary>
public enum StartKind
{
    /// <summary>It opens the menu.</summary>
    Menu,

    /// <summary>It starts a match alone at once, as the menu's Play alone button does.</summary>
    Alone,

    /// <summary>It hosts a match at once, as the menu's Host button does.</summary>
    Host,

    /// <summary>It joins a match at once, as the menu's Join button does.</summary>
    Join,
}

/// <summary>
/// What the command line asks of the game, from the arguments after <c>--</c>. Without any, the
/// game opens its menu.
/// </summary>
/// <param name="Kind">Whether to open the menu, host or join.</param>
/// <param name="Address">The host to join: a name or an address.</param>
/// <param name="Port">The port to host on or to join at.</param>
/// <param name="NetTestTicks">Above 0, the game plays the network test for this many ticks and quits. See <see cref="NetTest"/>.</param>
/// <param name="Computer">The ID of the personality of the computer opponent of a match alone, or empty when nobody sits at the other seat.</param>
public sealed record StartRequest(StartKind Kind, string Address, int Port, int NetTestTicks, string Computer = "")
{
    /// <summary>This starts a match alone at once, with no network.</summary>
    public const string AloneArgument = "--alone";

    /// <summary>
    /// Followed by the ID of a personality, this starts a match alone against the computer at
    /// once, as the menu's button does: <c>--computer swarm</c>.
    /// </summary>
    public const string ComputerArgument = "--computer";

    /// <summary>Followed by an optional port, this hosts a match at once.</summary>
    public const string HostArgument = "--host";

    /// <summary>Followed by an address, with or without <c>:port</c>, this joins a match at once.</summary>
    public const string JoinArgument = "--join";

    /// <summary>Followed by a number of ticks, and with one of the other two, this plays the network test.</summary>
    public const string NetTestArgument = "--net-test";

    /// <summary>The request of a game started without arguments.</summary>
    public static StartRequest Menu { get; } = new(StartKind.Menu, string.Empty, GameInfo.DefaultPort, 0);

    /// <summary>Reads the arguments. Returns false, with the reason, when they make no sense.</summary>
    public static bool TryRead(IReadOnlyList<string> arguments, out StartRequest request, out string? problem)
    {
        request = Menu;
        problem = null;

        for (var i = 0; i < arguments.Count; i++)
        {
            var hasValue = i + 1 < arguments.Count && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal);
            switch (arguments[i])
            {
                case AloneArgument:
                    request = request with { Kind = StartKind.Alone };
                    break;

                case ComputerArgument:
                    if (!hasValue)
                    {
                        problem = $"{ComputerArgument} needs the ID of a personality after it, as in {ComputerArgument} swarm.";
                        return false;
                    }

                    request = request with { Computer = arguments[++i] };
                    break;

                case HostArgument:
                    var port = GameInfo.DefaultPort;
                    if (hasValue && !TryParsePort(arguments[++i], out port))
                    {
                        problem = $"'{arguments[i]}' is not a port. A port is a number from 1 to 65535.";
                        return false;
                    }

                    request = request with { Kind = StartKind.Host, Port = port };
                    break;

                case JoinArgument:
                    if (!hasValue || !TryParseAddress(arguments[++i], out var address, out port))
                    {
                        problem = $"{JoinArgument} needs the host's address after it, with or without a port: 192.168.1.20 or 192.168.1.20:{GameInfo.DefaultPort}.";
                        return false;
                    }

                    request = request with { Kind = StartKind.Join, Address = address, Port = port };
                    break;

                case NetTestArgument:
                    if (!hasValue || !int.TryParse(arguments[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks <= 0)
                    {
                        problem = $"{NetTestArgument} needs a number of ticks after it.";
                        return false;
                    }

                    request = request with { NetTestTicks = ticks };
                    break;

                case MatchFiles.FolderArgument:
                    if (!hasValue)
                    {
                        problem = $"{MatchFiles.FolderArgument} needs a folder after it.";
                        return false;
                    }

                    i++;
                    break;

                default:
                    problem = $"'{arguments[i]}' is not an argument the game knows.";
                    return false;
            }
        }

        if (request.Computer.Length > 0)
        {
            if (request.Kind is StartKind.Host or StartKind.Join)
            {
                problem = $"{ComputerArgument} starts a match alone, and cannot go with {HostArgument} or {JoinArgument}.";
                return false;
            }

            request = request with { Kind = StartKind.Alone };
        }

        if (request.NetTestTicks > 0 && request.Kind is not (StartKind.Host or StartKind.Join))
        {
            problem = $"{NetTestArgument} needs {HostArgument} or {JoinArgument} with it.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads what a player types to say where the host is: a name or an address, alone or followed
    /// by a colon and a port. An IPv6 address with a port is written in square brackets.
    /// </summary>
    public static bool TryParseAddress(string text, out string address, out int port)
    {
        address = text.Trim();
        port = GameInfo.DefaultPort;

        if (address.StartsWith('['))
        {
            var close = address.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            var rest = address[(close + 1)..];
            address = address[1..close];
            return address.Length > 0 && (rest.Length == 0 || (rest[0] == ':' && TryParsePort(rest[1..], out port)));
        }

        // One colon separates a port. More than one is an IPv6 address without a port.
        var colon = address.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0 && colon == address.LastIndexOf(':'))
        {
            var portText = address[(colon + 1)..];
            address = address[..colon];
            return address.Length > 0 && TryParsePort(portText, out port);
        }

        return address.Length > 0;
    }

    /// <summary>Reads a port: a whole number from 1 to 65535.</summary>
    public static bool TryParsePort(string text, out int port) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;
}
