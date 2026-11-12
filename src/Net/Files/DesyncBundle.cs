using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Net;

/// <summary>The hash of every part of the state when <paramref name="Tick"/> ticks had run, in the order of <see cref="DesyncBundle.Parts"/>.</summary>
public readonly record struct PartHashes(int Tick, ImmutableArray<ulong> Hashes);

/// <summary>The whole state when <paramref name="Tick"/> ticks had run, as <c>Snapshot.Write</c> writes it.</summary>
public sealed record TickState(int Tick, byte[] Snapshot);

/// <summary>What became of a machine's second run of the last stretch of its match.</summary>
public enum SecondRunOutcome
{
    /// <summary>There was no second run. <see cref="SecondRun.Detail"/> says why.</summary>
    NotRun = 0,

    /// <summary>The second run gave the state of the first in every tick.</summary>
    Repeated = 1,

    /// <summary>The second run gave another state than the first, from <see cref="SecondRun.PartedAt"/> on.</summary>
    Parted = 2,
}

/// <summary>
/// The machine's own check of itself. When a match stops on a desync, the machine goes back to
/// the last state it has on which everyone agreed, runs the same commands again in a match built
/// afresh, and compares each tick with what it had the first time.
/// </summary>
/// <param name="Outcome">How that went.</param>
/// <param name="From">The tick of the state it started from, or -1.</param>
/// <param name="To">The last tick it compared, or -1.</param>
/// <param name="PartedAt">The first tick whose state was not the first run's, or -1.</param>
/// <param name="Detail">The parts that differed in that tick, or why there was no second run.</param>
public sealed record SecondRun(SecondRunOutcome Outcome, int From = -1, int To = -1, int PartedAt = -1, string Detail = "");

/// <summary>
/// Everything one machine knows about a match that stopped because the machines no longer agreed
/// on its state, as one file a tester can send: the replay so far, the last state everyone agreed
/// on, the state the match stopped in, the hash of every part of the state for each tick in
/// between, and the build and the system it ran on. <see cref="DesyncWatch"/> collects it.
/// </summary>
/// <remarks>
/// <para>
/// The file is a zip archive, so that anyone can look inside:
/// </para>
/// <code>
/// about.txt            what the file is, in words, then one "key: value" line for each fact
/// match.replay         the replay, as Replay reads it
/// agreed.snapshot      the last state the machines agreed on, as Snapshot.Restore reads it
/// final.snapshot       the state the match stopped in
/// hashes.txt           a line of part names, then for each tick its number and one hash for each part
/// states/tick-N.snapshot  the state after N ticks, for each tick the fault may have happened in
/// </code>
/// <para>
/// Reading takes nothing on trust: a file that is not such an archive, or lacks a part, or holds
/// more than a bundle can, is refused with <see cref="InvalidDataException"/>. The replay and the
/// snapshots are checked by whoever reads them.
/// </para>
/// </remarks>
public sealed class DesyncBundle
{
    /// <summary>The ending of a bundle's file name.</summary>
    public const string FileEnding = ".desync.zip";

    /// <summary>
    /// The key under which the game says, in <see cref="Machine"/>, when it wrote the file. It is
    /// the one fact about a machine that differs between any two and means nothing by it.
    /// </summary>
    public const string WrittenAtKey = "written_at";

    /// <summary>The version of the layout above.</summary>
    public const int FormatVersion = 1;

    /// <summary>The most a bundle holds when it is unpacked, all entries together. A file that claims more is refused.</summary>
    public const long MaxUnpackedBytes = 256L * 1024 * 1024;

    private const string AboutEntry = "about.txt";
    private const string ReplayEntry = "match.replay";
    private const string AgreedEntry = "agreed.snapshot";
    private const string FinalEntry = "final.snapshot";
    private const string HashesEntry = "hashes.txt";
    private const string StatePrefix = "states/tick-";
    private const string StateSuffix = ".snapshot";
    private const int MaxEntries = 4096;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly string[] OwnKeys =
    [
        "format", "player", "players", "game_version", "content_hash", "map", "seed", "hash_interval", "desync_tick",
        "final_tick", "agreed_tick", "parts", "second_run", "second_run_from", "second_run_to", "second_run_parted_at",
        "second_run_detail", "states",
    ];

    /// <summary>The seat of the player whose machine wrote this.</summary>
    public required int Player { get; init; }

    /// <summary>The number of seats in the match.</summary>
    public int PlayerCount { get; init; }

    /// <summary>The version of the game, as the match description has it. Empty when there is no replay to take it from.</summary>
    public string GameVersion { get; init; } = "";

    /// <summary>The hash of the game data, as the match description has it.</summary>
    public string ContentHash { get; init; } = "";

    public string MapId { get; init; } = "";

    public ulong Seed { get; init; }

    /// <summary>The session compared state hashes before every tick this divides.</summary>
    public required int HashInterval { get; init; }

    /// <summary>The tick whose hashes the host found to differ, or -1 if the session did not say.</summary>
    public required int DesyncTick { get; init; }

    /// <summary>The number of ticks this machine had run when the match stopped.</summary>
    public required int FinalTick { get; init; }

    /// <summary>The replay of the match up to its stop. Empty when the machine could not record one.</summary>
    public required byte[] Replay { get; init; }

    /// <summary>The state the match stopped in.</summary>
    public required byte[] FinalSnapshot { get; init; }

    /// <summary>
    /// The number of ticks that had run in <see cref="AgreedSnapshot"/>, or -1 when there is
    /// none: the machines differed before their first comparison, or the state was too long ago
    /// to have been kept.
    /// </summary>
    public int AgreedTick { get; init; } = -1;

    /// <summary>The last state this machine kept from before the last comparison that every machine passed.</summary>
    public byte[]? AgreedSnapshot { get; init; }

    /// <summary>The names of the parts of the state: <c>world</c>, then every store the match registered.</summary>
    public required ImmutableArray<string> Parts { get; init; }

    /// <summary>
    /// The hash of every part as the match ran, for each tick from the agreed state to the stop,
    /// in ascending order. These are from the match itself and not from the second run.
    /// </summary>
    public required ImmutableArray<PartHashes> Hashes { get; init; }

    public SecondRun SecondRun { get; init; } = new(SecondRunOutcome.NotRun, Detail: "not asked for");

    /// <summary>
    /// The whole state for each tick in which the machines may first have differed: those after
    /// the last comparison they passed, up to the one they failed. Each is the state the match
    /// had after that tick: as it was kept while the match ran, or, for a tick too long ago for
    /// that, as the second run gave it while its hashes were those of the first.
    /// </summary>
    public ImmutableArray<TickState> States { get; init; } = [];

    /// <summary>The build and the system the game ran on, as the game describes them. No key is one of the bundle's own.</summary>
    public ImmutableArray<KeyValuePair<string, string>> Machine { get; init; } = [];

    /// <summary>The hashes of the parts when this many ticks had run, or null when the bundle has none for it.</summary>
    public PartHashes? HashesAt(int tick)
    {
        foreach (var hashes in Hashes)
        {
            if (hashes.Tick == tick)
            {
                return hashes;
            }
        }

        return null;
    }

    /// <summary>The state the second run wrote for this many ticks, or null.</summary>
    public TickState? StateAt(int tick) => States.FirstOrDefault(state => state.Tick == tick);

    /// <summary>What the game says of its machine under a key, or null.</summary>
    public string? MachineValue(string key)
    {
        foreach (var (name, value) in Machine)
        {
            if (string.Equals(name, key, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Writes the bundle as a zip archive.</summary>
    /// <param name="output">Receives the archive. It stays open.</param>
    /// <exception cref="ArgumentException">A part's name or a machine key cannot be written as a line of text.</exception>
    public void Write(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var about = AboutText();
        var hashes = HashesText();
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Add(archive, AboutEntry, Utf8.GetBytes(about));
        Add(archive, ReplayEntry, Replay);
        if (AgreedSnapshot is not null)
        {
            Add(archive, AgreedEntry, AgreedSnapshot);
        }

        Add(archive, FinalEntry, FinalSnapshot);
        Add(archive, HashesEntry, Utf8.GetBytes(hashes));
        foreach (var state in States)
        {
            Add(archive, string.Create(CultureInfo.InvariantCulture, $"{StatePrefix}{state.Tick}{StateSuffix}"), state.Snapshot);
        }
    }

    /// <summary>The bundle as the bytes of its file.</summary>
    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        Write(stream);
        return stream.ToArray();
    }

    /// <summary>True when the data begins as a zip archive does, which a replay and a snapshot never do.</summary>
    public static bool LooksLikeOne(ReadOnlySpan<byte> start) => start.Length >= 4 && start[0] == 'P' && start[1] == 'K' && start[2] == 3 && start[3] == 4;

    /// <summary>Reads a bundle.</summary>
    /// <param name="input">The archive. It must be able to seek.</param>
    /// <param name="maxUnpackedBytes">The most the archive may unpack to, all entries together.</param>
    /// <exception cref="InvalidDataException">The data is not a bundle this build reads.</exception>
    public static DesyncBundle Read(Stream input, long maxUnpackedBytes = MaxUnpackedBytes)
    {
        ArgumentNullException.ThrowIfNull(input);

        Dictionary<string, byte[]> entries;
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            entries = ReadEntries(archive, maxUnpackedBytes);
        }
        catch (Exception refused) when (refused is InvalidDataException or NotSupportedException or EndOfStreamException or IOException)
        {
            throw new InvalidDataException($"The file is not a desync bundle: it cannot be read as a zip archive. {refused.Message}", refused);
        }

        var facts = ReadAbout(Entry(entries, AboutEntry));
        var format = Number(facts, "format");
        if (format != FormatVersion)
        {
            throw new InvalidDataException($"The bundle has layout version {format} and this build reads version {FormatVersion}.");
        }

        var parts = Text(facts, "parts").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToImmutableArray();
        var agreedTick = Number(facts, "agreed_tick", minimum: -1);
        var hasAgreed = entries.TryGetValue(AgreedEntry, out var agreed);
        if (hasAgreed != (agreedTick >= 0))
        {
            throw new InvalidDataException(agreedTick >= 0
                ? $"The bundle names an agreed state after {agreedTick} ticks and has no {AgreedEntry}."
                : $"The bundle has {AgreedEntry} and names no tick for it.");
        }

        var states = ImmutableArray.CreateBuilder<TickState>();
        foreach (var (name, bytes) in entries)
        {
            if (name.StartsWith(StatePrefix, StringComparison.Ordinal) && name.EndsWith(StateSuffix, StringComparison.Ordinal))
            {
                var number = name[StatePrefix.Length..^StateSuffix.Length];
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var tick))
                {
                    throw new InvalidDataException($"The bundle has a state named '{name}', which names no tick.");
                }

                states.Add(new TickState(tick, bytes));
            }
        }

        states.Sort((first, second) => first.Tick.CompareTo(second.Tick));

        var outcome = Text(facts, "second_run") switch
        {
            "repeated" => SecondRunOutcome.Repeated,
            "parted" => SecondRunOutcome.Parted,
            "not run" => SecondRunOutcome.NotRun,
            var other => throw new InvalidDataException($"The bundle says of its second run '{other}', which is not an outcome."),
        };

        return new DesyncBundle
        {
            Player = Number(facts, "player"),
            PlayerCount = Number(facts, "players"),
            GameVersion = Text(facts, "game_version"),
            ContentHash = Text(facts, "content_hash"),
            MapId = Text(facts, "map"),
            Seed = ulong.TryParse(Text(facts, "seed"), NumberStyles.None, CultureInfo.InvariantCulture, out var seed)
                ? seed
                : throw new InvalidDataException($"The bundle's seed is '{Text(facts, "seed")}', which is not a number."),
            HashInterval = Number(facts, "hash_interval", minimum: 1),
            DesyncTick = Number(facts, "desync_tick", minimum: -1),
            FinalTick = Number(facts, "final_tick"),
            Replay = Entry(entries, ReplayEntry),
            FinalSnapshot = Entry(entries, FinalEntry),
            AgreedTick = agreedTick,
            AgreedSnapshot = agreed,
            Parts = parts,
            Hashes = ReadHashes(Entry(entries, HashesEntry), parts),
            SecondRun = new SecondRun(
                outcome,
                Number(facts, "second_run_from", minimum: -1),
                Number(facts, "second_run_to", minimum: -1),
                Number(facts, "second_run_parted_at", minimum: -1),
                Text(facts, "second_run_detail")),
            States = states.ToImmutable(),
            Machine = [.. facts.Where(fact => !OwnKeys.Contains(fact.Key, StringComparer.Ordinal))],
        };
    }

    /// <summary>Reads a bundle from the bytes of its file.</summary>
    /// <exception cref="InvalidDataException">The data is not a bundle this build reads.</exception>
    public static DesyncBundle Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var stream = new MemoryStream(file, writable: false);
        return Read(stream);
    }

    private static void Add(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private string AboutText()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"This match was stopped because the players' games no longer agreed on what had happened in it.\n");
        text.Append("That is a fault in the game, not something a player did.\n\n");
        text.Append(CultureInfo.InvariantCulture, $"This file is what the game of player {Player + 1} kept for finding the fault. Please send it, as it is,\n");
        text.Append("to whoever gave you the build, and ask every other player of the match to send theirs.\n");
        text.Append("One file alone seldom shows which game went wrong.\n\n");
        text.Append("For developers: `desync-diff` in src/Tools reads this file, alone or with another player's.\n\n");

        Fact(text, "format", FormatVersion);
        Fact(text, "player", Player);
        Fact(text, "players", PlayerCount);
        Fact(text, "game_version", GameVersion);
        Fact(text, "content_hash", ContentHash);
        Fact(text, "map", MapId);
        Fact(text, "seed", Seed);
        Fact(text, "hash_interval", HashInterval);
        Fact(text, "desync_tick", DesyncTick);
        Fact(text, "final_tick", FinalTick);
        Fact(text, "agreed_tick", AgreedTick);
        foreach (var part in Parts)
        {
            if (part.Length == 0 || part.Any(letter => char.IsWhiteSpace(letter) || char.IsControl(letter) || letter == ','))
            {
                throw new ArgumentException($"The part '{part}' has a name that cannot stand in a list: it is empty or has a space or a comma in it.");
            }
        }

        Fact(text, "parts", string.Join(", ", Parts));
        Fact(text, "second_run", SecondRun.Outcome switch
        {
            SecondRunOutcome.Repeated => "repeated",
            SecondRunOutcome.Parted => "parted",
            _ => "not run",
        });
        Fact(text, "second_run_from", SecondRun.From);
        Fact(text, "second_run_to", SecondRun.To);
        Fact(text, "second_run_parted_at", SecondRun.PartedAt);
        Fact(text, "second_run_detail", SecondRun.Detail);
        Fact(text, "states", States.Length);

        text.Append('\n');
        foreach (var (key, value) in Machine)
        {
            if (key.Length == 0 || OwnKeys.Contains(key, StringComparer.Ordinal) || key.Any(letter => char.IsWhiteSpace(letter) || char.IsControl(letter) || letter == ':'))
            {
                throw new ArgumentException($"'{key}' cannot be the key of a fact about the machine: it is empty, one of the bundle's own, or has a space or a colon in it.");
            }

            Fact(text, key, value);
        }

        return text.ToString();
    }

    private static void Fact<T>(StringBuilder text, string key, T value)
    {
        // One line each, whatever the value holds.
        var line = string.Create(CultureInfo.InvariantCulture, $"{value}");
        var clean = new StringBuilder(line.Length);
        foreach (var letter in line)
        {
            clean.Append(char.IsControl(letter) ? ' ' : letter);
        }

        text.Append(CultureInfo.InvariantCulture, $"{key}: {clean.ToString().Trim()}\n");
    }

    private string HashesText()
    {
        var text = new StringBuilder();
        text.Append("tick");
        foreach (var part in Parts)
        {
            text.Append('\t').Append(part);
        }

        text.Append('\n');
        foreach (var hashes in Hashes)
        {
            if (hashes.Hashes.Length != Parts.Length)
            {
                throw new ArgumentException($"Tick {hashes.Tick} has {hashes.Hashes.Length} hashes and the state has {Parts.Length} parts.");
            }

            text.Append(CultureInfo.InvariantCulture, $"{hashes.Tick}");
            foreach (var hash in hashes.Hashes)
            {
                text.Append(CultureInfo.InvariantCulture, $"\t{hash:x16}");
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static Dictionary<string, byte[]> ReadEntries(ZipArchive archive, long maxUnpackedBytes)
    {
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException($"The archive has {archive.Entries.Count} entries, more than a bundle can.");
        }

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var left = maxUnpackedBytes;
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            // The length an entry announces is not to be believed: what counts is what comes out.
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                left -= read;
                if (left < 0)
                {
                    throw new InvalidDataException($"The archive unpacks to more than {maxUnpackedBytes} bytes, more than a bundle can.");
                }

                bytes.Write(buffer, 0, read);
            }

            if (!entries.TryAdd(entry.FullName, bytes.ToArray()))
            {
                throw new InvalidDataException($"The archive has two entries named '{entry.FullName}'.");
            }
        }

        return entries;
    }

    private static byte[] Entry(Dictionary<string, byte[]> entries, string name) =>
        entries.TryGetValue(name, out var bytes) ? bytes : throw new InvalidDataException($"The file is not a desync bundle: it has no {name}.");

    private static string Decode(byte[] bytes, string name)
    {
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException($"The bundle's {name} is not text.");
        }
    }

    /// <summary>The "key: value" lines, in order. Any other line is for the reader and is passed over.</summary>
    private static List<KeyValuePair<string, string>> ReadAbout(byte[] bytes)
    {
        var facts = new List<KeyValuePair<string, string>>();
        foreach (var line in Decode(bytes, AboutEntry).Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || line[..colon].Any(char.IsWhiteSpace))
            {
                continue;
            }

            facts.Add(new KeyValuePair<string, string>(line[..colon], line[(colon + 1)..].Trim()));
        }

        return facts;
    }

    private static string Text(List<KeyValuePair<string, string>> facts, string key)
    {
        foreach (var (name, value) in facts)
        {
            if (string.Equals(name, key, StringComparison.Ordinal))
            {
                return value;
            }
        }

        throw new InvalidDataException($"The bundle's {AboutEntry} has no line '{key}'.");
    }

    private static int Number(List<KeyValuePair<string, string>> facts, string key, int minimum = 0)
    {
        var text = Text(facts, key);
        return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) && number >= minimum
            ? number
            : throw new InvalidDataException($"The bundle's {AboutEntry} says '{key}: {text}', which is not a number it can be.");
    }

    private static ImmutableArray<PartHashes> ReadHashes(byte[] bytes, ImmutableArray<string> parts)
    {
        var lines = Decode(bytes, HashesEntry).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var expected = "tick\t" + string.Join('\t', parts);
        if (lines.Length == 0 || !string.Equals(lines[0], expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The bundle's {HashesEntry} does not begin with the names of the parts {AboutEntry} lists.");
        }

        var table = ImmutableArray.CreateBuilder<PartHashes>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var cells = lines[i].Split('\t');
            if (cells.Length != parts.Length + 1
                || !int.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tick)
                || (table.Count > 0 && tick <= table[^1].Tick))
            {
                throw new InvalidDataException($"The bundle's {HashesEntry} has a line that is not a tick and one hash for each part, in ascending order: line {i + 1}.");
            }

            var hashes = ImmutableArray.CreateBuilder<ulong>(parts.Length);
            for (var part = 0; part < parts.Length; part++)
            {
                if (cells[part + 1].Length != 16 || !ulong.TryParse(cells[part + 1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hash))
                {
                    throw new InvalidDataException($"The bundle's {HashesEntry} has '{cells[part + 1]}' where a hash should be: line {i + 1}.");
                }

                hashes.Add(hash);
            }

            table.Add(new PartHashes(tick, hashes.MoveToImmutable()));
        }

        return table.MoveToImmutable();
    }
}
