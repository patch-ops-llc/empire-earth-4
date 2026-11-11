using System.Buffers;
using Sim.Data;
using Sim.Setup;

namespace Sim.Serialization;

/// <summary>
/// What every machine needs to build the same match: which build of the game, which data, which
/// map, the seed, and the players with their starts. A replay file begins with one, and a host
/// sends one to each client before the match starts. This is its one encoding.
/// </summary>
/// <remarks>
/// <para>
/// Layout, in the byte format of <see cref="ByteWriter"/>:
/// </para>
/// <code>
/// 2 bytes    Version
/// text       the game version
/// 32 bytes   the content hash of the data
/// text       the ID of the map
/// 8 bytes    the seed
/// a count    the number of players, 1 to 8
/// 4 bytes    for each player, the index of its start in the map's list, or -1 for none
/// </code>
/// <para>
/// The content hash and the map decide whether a match can be built at all:
/// <see cref="FindProblem"/> says so for the data a machine has. The game version is text that this
/// class only carries. Peers refuse each other when theirs differ; a replay is re-run by whatever
/// build reads it, because showing that a later build still gives the same match is what a
/// recorded replay is for.
/// </para>
/// <para>
/// A description has no value equality. Two are the same exactly when their bytes are.
/// </para>
/// </remarks>
public sealed class MatchDescription
{
    /// <summary>The version of the layout above.</summary>
    public const ushort Version = 1;

    /// <summary>The most characters the game version or the map ID may have.</summary>
    public const int MaxTextLength = 128;

    private const int ContentHashBytes = 32;

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdef");

    private readonly PlayerSetup[] _players;

    /// <param name="gameVersion">Text that names the build, at most <see cref="MaxTextLength"/> characters. May be empty.</param>
    /// <param name="contentHash">The content hash of the data, as <see cref="GameData.ContentHash"/> gives it: 64 lowercase hexadecimal digits.</param>
    /// <param name="mapId">The ID of the map, as <see cref="MapDef.Id"/> gives it.</param>
    /// <param name="seed">The seed of the world's random generator.</param>
    /// <param name="players">The players, in the order that numbers them from 0: 1 to <see cref="MatchSetup.MaxPlayers"/>, no two on the same start.</param>
    public MatchDescription(string gameVersion, string contentHash, string mapId, ulong seed, IReadOnlyList<PlayerSetup> players)
    {
        ArgumentNullException.ThrowIfNull(gameVersion);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(players);

        var copy = new PlayerSetup[players.Count];
        for (var player = 0; player < copy.Length; player++)
        {
            copy[player] = players[player] ?? throw new ArgumentException($"Player {player} is null.", nameof(players));
        }

        if (Check(gameVersion, contentHash, mapId, copy) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        GameVersion = gameVersion;
        ContentHash = contentHash;
        MapId = mapId;
        Seed = seed;
        _players = copy;
    }

    /// <summary>Text that names the build of the game the match was made by.</summary>
    public string GameVersion { get; }

    /// <summary>The content hash of the data, as 64 lowercase hexadecimal digits.</summary>
    public string ContentHash { get; }

    public string MapId { get; }

    public ulong Seed { get; }

    /// <summary>The players. A player's number is its position in this list.</summary>
    public IReadOnlyList<PlayerSetup> Players => _players;

    /// <summary>Reads a description and leaves the reader after it.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a description this build reads.</exception>
    public static MatchDescription Read(ByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var version = reader.ReadUInt16();
        if (version != Version)
        {
            throw new InvalidDataException($"The match description has layout version {version} and this build reads version {Version}.");
        }

        var gameVersion = reader.ReadString();
        var contentHash = Convert.ToHexStringLower(reader.ReadBytes(ContentHashBytes));
        var mapId = reader.ReadString();
        var seed = reader.ReadUInt64();
        var players = new PlayerSetup[reader.ReadCount(minimumBytesPerItem: sizeof(int))];
        for (var player = 0; player < players.Length; player++)
        {
            players[player] = new PlayerSetup(reader.ReadInt32());
        }

        if (Check(gameVersion, contentHash, mapId, players) is { } problem)
        {
            throw new InvalidDataException($"The match description is not valid. {problem}");
        }

        return new MatchDescription(gameVersion, contentHash, mapId, seed, players);
    }

    /// <summary>Appends the description to the writer.</summary>
    public void Write(ByteWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteUInt16(Version);
        writer.WriteString(GameVersion);
        writer.WriteBytes(Convert.FromHexString(ContentHash));
        writer.WriteString(MapId);
        writer.WriteUInt64(Seed);
        writer.WriteCount(_players.Length);
        foreach (var player in _players)
        {
            writer.WriteInt32(player.Start);
        }
    }

    /// <summary>The same match with other data: the description a recording gets when it is run again on data that has changed.</summary>
    public MatchDescription WithContentHash(string contentHash) => new(GameVersion, contentHash, MapId, Seed, _players);

    /// <summary>
    /// Why the match cannot be built from this data, as a sentence for a person, or null when it
    /// can: the data is the data the description names, it has the map, and the map has the starts.
    /// </summary>
    public string? FindProblem(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!string.Equals(data.ContentHash, ContentHash, StringComparison.Ordinal))
        {
            return $"The match was made with other data: its content hash is {ContentHash} and this data has {data.ContentHash}.";
        }

        var map = FindMap(data);
        if (map is null)
        {
            return $"The data has no map '{MapId}'.";
        }

        for (var player = 0; player < _players.Length; player++)
        {
            if (_players[player].HasStart && _players[player].Start >= map.Starts.Count)
            {
                return $"Player {player} has start {_players[player].Start}, but map '{MapId}' has {map.Starts.Count} starts.";
            }
        }

        return null;
    }

    /// <summary>Builds the match, ready for its first tick, with <see cref="MatchSetup.Create"/>.</summary>
    /// <exception cref="InvalidDataException">The match cannot be built from this data. The message is <see cref="FindProblem"/>'s.</exception>
    public Match CreateMatch(GameData data)
    {
        if (FindProblem(data) is { } problem)
        {
            throw new InvalidDataException(problem);
        }

        try
        {
            return MatchSetup.Create(data, FindMap(data)!, Seed, _players);
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException($"The match cannot be set up on map '{MapId}'. {refused.Message}", refused);
        }
    }

    private MapDef? FindMap(GameData data)
    {
        foreach (var map in data.Maps)
        {
            if (string.Equals(map.Id, MapId, StringComparison.Ordinal))
            {
                return map;
            }
        }

        return null;
    }

    private static string? Check(string gameVersion, string contentHash, string mapId, PlayerSetup[] players)
    {
        if (gameVersion.Length > MaxTextLength)
        {
            return $"The game version has {gameVersion.Length} characters and may have {MaxTextLength}.";
        }

        if (!IsUnicode(gameVersion) || !IsUnicode(mapId))
        {
            return "The game version and the map ID must be valid Unicode text.";
        }

        if (contentHash.Length != ContentHashBytes * 2 || contentHash.AsSpan().ContainsAnyExcept(HexDigits))
        {
            return $"A content hash is {ContentHashBytes * 2} lowercase hexadecimal digits, not '{contentHash}'.";
        }

        if (mapId.Length is 0 or > MaxTextLength)
        {
            return $"The map ID has {mapId.Length} characters and must have 1 to {MaxTextLength}.";
        }

        if (players.Length is < 1 or > MatchSetup.MaxPlayers)
        {
            return $"A match has 1 to {MatchSetup.MaxPlayers} players, not {players.Length}.";
        }

        for (var player = 0; player < players.Length; player++)
        {
            var start = players[player].Start;
            if (start < PlayerSetup.NoStart)
            {
                return $"Player {player} has start {start}. A start is 0 or more, or {PlayerSetup.NoStart} for none.";
            }

            for (var other = 0; other < player; other++)
            {
                if (start != PlayerSetup.NoStart && players[other].Start == start)
                {
                    return $"Players {other} and {player} share start {start}.";
                }
            }
        }

        return null;
    }

    /// <summary>False for text with half of a surrogate pair, which has no UTF-8 form and so cannot be written.</summary>
    private static bool IsUnicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
