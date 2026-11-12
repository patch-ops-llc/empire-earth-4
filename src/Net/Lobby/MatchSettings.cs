using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Net;

/// <summary>What takes a seat of a match.</summary>
/// <remarks>
/// The host keeps a seat of any kind but <see cref="Person"/> free of connections and does not
/// wait for it (<see cref="MatchHost"/>). The orders of such a seat's player go in with
/// <see cref="HostSession.Submit"/> on the hosting machine, which is the only one that knows
/// what sits there: the other machines get the commands and nothing else.
/// </remarks>
public enum SeatKind
{
    /// <summary>A person: in the host's own seat the one at the hosting machine, in any other one who joins over the network.</summary>
    Person = 0,

    /// <summary>
    /// Nobody: the seat's player is in the match with its start and never gives an order. This is
    /// the other player of a match played alone.
    /// </summary>
    Nobody = 1,

    /// <summary>
    /// The computer: the hosting machine plays the seat, with the personality the seat's setting
    /// names (design 5.5). This library does not know the computer player. Whoever runs the match
    /// on the hosting machine makes it and hands its orders to <see cref="HostSession.Submit"/>.
    /// </summary>
    Computer = 2,
}

/// <summary>What the host sets for one seat of a match.</summary>
/// <param name="Kind">What takes the seat.</param>
/// <param name="Start">
/// The index of the seat's start among the map's, or null to have it drawn from the starts no
/// seat names (design 5.2), which is the default.
/// </param>
/// <param name="Personality">
/// For a seat the computer takes, the ID of its personality, one of the data's
/// (<see cref="GameData.Personalities"/>). It says nothing for a seat of another kind.
/// </param>
public sealed record SeatSetting(SeatKind Kind = SeatKind.Person, int? Start = null, string? Personality = null);

/// <summary>
/// The settings of a match, as the host chooses them before it begins: the map and, for every
/// seat, what takes it and which start it gets. Design 5.2: "Which start each player gets is a
/// setting of the match. By default the host draws it before the match begins."
/// </summary>
/// <remarks>
/// The settings stay on the hosting machine. What travels is the <see cref="MatchDescription"/>
/// made from them, with every start decided, so chance never has to be the same on two machines.
/// The start epoch and the epoch cap (design 1.2) are settings to add here when the lobby gets
/// them.
/// </remarks>
public sealed record MatchSettings
{
    /// <param name="mapId">The ID of the map.</param>
    /// <param name="seats">One entry for each seat, in the order of the players.</param>
    public MatchSettings(string mapId, IReadOnlyList<SeatSetting> seats)
    {
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(seats);
        MapId = mapId;
        Seats = [.. seats];
    }

    public string MapId { get; }

    public IReadOnlyList<SeatSetting> Seats { get; }

    /// <summary>What takes each seat, in the order of the players, as <see cref="MatchHost"/> takes it.</summary>
    public IReadOnlyList<SeatKind> Kinds => [.. Seats.Select(seat => seat.Kind)];

    /// <summary>The settings a match has when nobody changes any: every seat a person's, every start drawn.</summary>
    public static MatchSettings Default(string mapId, int seatCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seatCount, 1);
        return new MatchSettings(mapId, [.. Enumerable.Repeat(new SeatSetting(), seatCount)]);
    }

    /// <summary>These settings with one seat changed.</summary>
    public MatchSettings WithSeat(int seat, SeatSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentOutOfRangeException.ThrowIfNegative(seat);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, Seats.Count);
        var seats = Seats.ToArray();
        seats[seat] = setting;
        return new MatchSettings(MapId, seats);
    }

    /// <summary>
    /// Why no match can be made from these settings with this data, as a sentence for the host,
    /// or null when one can.
    /// </summary>
    public string? FindProblem(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var map = data.Maps.FirstOrDefault(candidate => string.Equals(candidate.Id, MapId, StringComparison.Ordinal));
        if (map is null)
        {
            return $"The data has no map '{MapId}'.";
        }

        if (Seats.Count is < 1 or > MatchSetup.MaxPlayers)
        {
            return $"A match has 1 to {MatchSetup.MaxPlayers} players, and these settings have {Seats.Count} seats.";
        }

        if (Seats.Count > map.Starts.Count)
        {
            return $"The map '{MapId}' has {map.Starts.Count} starts, which is too few for {Seats.Count} players.";
        }

        for (var seat = 0; seat < Seats.Count; seat++)
        {
            if (!Enum.IsDefined(Seats[seat].Kind))
            {
                return $"Seat {seat + 1} is set to something this build does not know.";
            }

            if (Seats[seat].Kind == SeatKind.Computer)
            {
                if (Seats[seat].Personality is not { } personality)
                {
                    return $"Seat {seat + 1} is set to the computer, which needs a personality.";
                }

                if (data.PersonalityOf(personality) is null)
                {
                    return $"Seat {seat + 1} is set to the computer with the personality '{personality}', which the data does not have.";
                }
            }

            if (Seats[seat].Start is not { } start)
            {
                continue;
            }

            if (start < 0 || start >= map.Starts.Count)
            {
                return $"Seat {seat + 1} is set to start {start + 1}, and the map '{MapId}' has {map.Starts.Count} starts.";
            }

            for (var other = 0; other < seat; other++)
            {
                if (Seats[other].Start == start)
                {
                    return $"Seats {other + 1} and {seat + 1} are both set to start {start + 1}. Each player needs a start of its own.";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The match these settings give: every seat that names a start gets it, and the others are
    /// drawn evenly from the starts that are left (<see cref="StartDraw.Assign"/>).
    /// </summary>
    /// <param name="data">The host's game data.</param>
    /// <param name="gameVersion">The text that names the host's build.</param>
    /// <param name="seed">The seed of the match, which the host draws.</param>
    /// <param name="pick">The source of chance for the draw of the starts: see <see cref="StartDraw.Draw"/>.</param>
    /// <exception cref="ArgumentException">The settings give no match: <see cref="FindProblem"/> says why.</exception>
    public MatchDescription Describe(GameData data, string gameVersion, ulong seed, Func<int, int> pick)
    {
        ArgumentNullException.ThrowIfNull(gameVersion);
        ArgumentNullException.ThrowIfNull(pick);
        if (FindProblem(data) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        var map = data.Maps.First(candidate => string.Equals(candidate.Id, MapId, StringComparison.Ordinal));
        var players = StartDraw.Assign([.. Seats.Select(seat => seat.Start)], map.Starts.Count, pick);
        return new MatchDescription(gameVersion, data.ContentHash, MapId, seed, players);
    }
}
