using System.Runtime.InteropServices;
using Sim.Core;
using Sim.Data;
using Sim.Setup;

namespace Sim.Serialization;

/// <summary>A state hash in a replay: the hash of the world when <paramref name="Tick"/> ticks had run.</summary>
public readonly record struct ReplayHash(int Tick, ulong Hash);

/// <summary>Where a match run from a replay first differed from the recording.</summary>
/// <param name="Tick">The number of ticks that had run.</param>
/// <param name="Recorded">The state hash in the replay.</param>
/// <param name="Computed">The state hash of the match run now.</param>
public readonly record struct ReplayMismatch(int Tick, ulong Recorded, ulong Computed);

/// <summary>What running a replay again gave.</summary>
/// <param name="TicksRun">The ticks that were run: all of the replay's, or those up to the first difference.</param>
/// <param name="FinalHash">The state hash after those ticks.</param>
/// <param name="HashesChecked">The recorded hashes that were compared, the differing one included.</param>
/// <param name="Mismatch">The first difference, or null when every recorded hash was met.</param>
public sealed record ReplayResult(int TicksRun, ulong FinalHash, int HashesChecked, ReplayMismatch? Mismatch)
{
    /// <summary>True when the match run now met every hash the replay recorded.</summary>
    public bool Reproduced => Mismatch is null;
}

/// <summary>
/// A recorded match: its description, the commands of every tick and the state hashes that were
/// recorded on the way. Running it again gives the same match, and the hashes prove it.
/// <see cref="ReplayRecorder"/> writes one.
/// </summary>
/// <remarks>
/// <para>
/// Layout, in the byte format of <see cref="ByteWriter"/>:
/// </para>
/// <code>
/// 4 bytes   the letters RPLY
/// 2 bytes   Version
/// 2 bytes   CommandEncoding.Version
/// 2 bytes   the hash interval: a state hash is recorded every so many ticks, at least 1
/// ...       the match, as MatchDescription writes it
/// then records, to the end of the data. Each starts with one byte that says which it is:
///   1  commands   4 bytes: the tick they run in. A count, at least 1, then each command as
///                 CommandEncoding writes it, in the order they run.
///   2  hash       4 bytes: a number of ticks. 8 bytes: the state hash when that many had run.
///   3  end        4 bytes: the number of ticks the match ran. 8 bytes: its final state hash.
///                 Nothing follows it.
/// </code>
/// <para>
/// The hash records are those of tick 0, the match as it was built, and of every multiple of the
/// hash interval the match reached, in that order and none left out. The end record comes after
/// the last of them and less than one interval later. Commands records come in ascending order of
/// their ticks. Commands and hashes are not sorted against each other: a session learns the
/// commands of a tick some ticks before that tick runs.
/// </para>
/// <para>
/// A replay without an end record is one whose recording stopped: the game was closed, or failed.
/// It is still a replay, of the ticks it has: see <see cref="TickCount"/>. Commands for ticks at
/// or after the end were scheduled and never ran.
/// </para>
/// <para>
/// These rules bound the ticks a replay asks for by the number of its hash records, so that a
/// damaged file cannot ask for a run without end.
/// </para>
/// </remarks>
public sealed class Replay
{
    /// <summary>The version of the layout above.</summary>
    public const ushort Version = 1;

    internal const byte CommandsRecord = 1;
    internal const byte HashRecord = 2;
    internal const byte EndRecord = 3;

    private const uint Magic = 0x594C5052; // "RPLY" as the bytes 52 50 4C 59

    private readonly List<(int Tick, Command Command)> _commands = [];
    private readonly List<ReplayHash> _hashes = [];

    private Replay(int hashInterval, MatchDescription description)
    {
        HashInterval = hashInterval;
        Description = description;
    }

    public MatchDescription Description { get; }

    /// <summary>A state hash was recorded every so many ticks.</summary>
    public int HashInterval { get; }

    /// <summary>Every command, with the tick it runs in, in the order they run.</summary>
    public IReadOnlyList<(int Tick, Command Command)> Commands => _commands;

    /// <summary>The state hashes recorded while the match ran, in tick order. The end record is not among them.</summary>
    public IReadOnlyList<ReplayHash> Hashes => _hashes;

    /// <summary>How the match ended: the ticks it ran and its final state hash. Null when the recording stopped before the match did.</summary>
    public ReplayHash? End { get; private set; }

    /// <summary>True when the replay has its end record.</summary>
    public bool IsComplete => End is not null;

    /// <summary>
    /// The position in the data where reading stopped because a record was damaged or cut short,
    /// or null when everything was read. Only <see cref="Read"/> with <c>keepWhatIsWhole</c> sets it.
    /// </summary>
    public int? DamagedAt { get; private set; }

    /// <summary>
    /// The number of ticks to run: those the match ran. Without an end record, the ticks up to
    /// the last hash, and past it as far as the replay has commands, which is always less than
    /// one hash interval more.
    /// </summary>
    public int TickCount
    {
        get
        {
            if (End is { } end)
            {
                return end.Tick;
            }

            var lastHash = _hashes.Count > 0 ? _hashes[^1].Tick : 0;
            var afterLastCommand = _commands.Count > 0 ? (long)_commands[^1].Tick + 1 : 0;
            return (int)Math.Clamp(afterLastCommand, lastHash, (long)lastHash + HashInterval - 1);
        }
    }

    /// <summary>Reads a replay, which must be all that is left of the data.</summary>
    /// <param name="reader">The data.</param>
    /// <param name="keepWhatIsWhole">
    /// False rejects a replay with a record that is damaged or cut short. True keeps the records
    /// before it and sets <see cref="DamagedAt"/>, for the file of a game that failed while writing.
    /// The header must be whole either way.
    /// </param>
    /// <exception cref="InvalidDataException">The bytes are not a replay this build reads.</exception>
    public static Replay Read(ByteReader reader, bool keepWhatIsWhole = false)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("The data is not a replay: it does not start with RPLY.");
        }

        var version = reader.ReadUInt16();
        if (version != Version)
        {
            throw new InvalidDataException($"The replay has layout version {version} and this build reads version {Version}.");
        }

        var commandVersion = reader.ReadUInt16();
        if (commandVersion != CommandEncoding.Version)
        {
            throw new InvalidDataException($"The replay's commands have encoding version {commandVersion} and this build reads version {CommandEncoding.Version}.");
        }

        var hashInterval = reader.ReadUInt16();
        if (hashInterval == 0)
        {
            throw new InvalidDataException("The replay has a hash interval of 0 ticks.");
        }

        var replay = new Replay(hashInterval, MatchDescription.Read(reader));
        while (!reader.IsAtEnd)
        {
            var start = reader.Position;
            try
            {
                replay.ReadRecord(reader);
            }
            catch (InvalidDataException) when (keepWhatIsWhole)
            {
                replay.DamagedAt = start;
                break;
            }
        }

        return replay;
    }

    /// <summary>Builds the match from the data with <see cref="MatchDescription.CreateMatch"/> and runs the replay in it.</summary>
    /// <exception cref="InvalidDataException">The match cannot be built from this data.</exception>
    public ReplayResult Run(GameData data) => Run(Description.CreateMatch(data));

    /// <summary>
    /// Runs the replay in a match that was built as the recorded one was and has not run a tick,
    /// and compares the state with every recorded hash. It stops at the first that differs.
    /// </summary>
    public ReplayResult Run(Match match)
    {
        var simulation = Prepare(match);
        var world = simulation.World;
        var ticks = TickCount;
        var nextHash = 0;
        var compared = 0;
        while (true)
        {
            var recorded = nextHash < _hashes.Count && _hashes[nextHash].Tick == world.Tick ? _hashes[nextHash++] : (ReplayHash?)null;
            var atEnd = world.Tick == ticks;
            if (atEnd && End is { } end)
            {
                recorded = end;
            }

            if (recorded is not null || atEnd)
            {
                var computed = world.ComputeHash();
                if (recorded is { } expected)
                {
                    compared++;
                    if (computed != expected.Hash)
                    {
                        return new ReplayResult(world.Tick, computed, compared, new ReplayMismatch(world.Tick, expected.Hash, computed));
                    }
                }

                if (atEnd)
                {
                    return new ReplayResult(ticks, computed, compared, null);
                }
            }

            simulation.Tick();
        }
    }

    /// <summary>
    /// Runs the commands of the replay in a match built from other rules or other data, and writes
    /// the replay of that run: the same commands in the same ticks, with the hashes the match has
    /// now and an end record. This is how a recorded replay is brought up to date after a change
    /// that was meant to change the match.
    /// </summary>
    /// <param name="match">A match that has not run a tick.</param>
    /// <param name="description">The description of that match, for the new replay's header.</param>
    /// <param name="writer">Receives the new replay.</param>
    public void RecordAgain(Match match, MatchDescription description, ByteWriter writer)
    {
        var simulation = Prepare(match);
        var recorder = new ReplayRecorder(writer, description, simulation.World, HashInterval);
        var ticks = TickCount;
        var next = 0;
        var due = new List<Command>();
        for (var tick = 0; tick < ticks; tick++)
        {
            due.Clear();
            for (; next < _commands.Count && _commands[next].Tick == tick; next++)
            {
                due.Add(_commands[next].Command);
            }

            recorder.RecordCommands(tick, CollectionsMarshal.AsSpan(due));
            simulation.Tick();
            recorder.AfterTick();
        }

        recorder.Finish();
    }

    internal static void WriteHeader(ByteWriter writer, int hashInterval, MatchDescription description)
    {
        writer.WriteUInt32(Magic);
        writer.WriteUInt16(Version);
        writer.WriteUInt16(checked((ushort)CommandEncoding.Version));
        writer.WriteUInt16(checked((ushort)hashInterval));
        description.Write(writer);
    }

    /// <summary>Queues every command of the replay. Those for ticks at or after the end wait in the queue and never run.</summary>
    private Simulation Prepare(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var simulation = match.Simulation;
        if (simulation.World.Tick != 0)
        {
            throw new ArgumentException($"A replay runs in a match that has not run a tick, and this one has run {simulation.World.Tick}.", nameof(match));
        }

        foreach (var (tick, command) in _commands)
        {
            simulation.Commands.Enqueue(tick, command);
        }

        return simulation;
    }

    private void ReadRecord(ByteReader reader)
    {
        if (End is not null)
        {
            throw new InvalidDataException("The replay has data after its end record.");
        }

        var kind = reader.ReadByte();
        switch (kind)
        {
            case CommandsRecord:
                var tick = ReadTick(reader);
                if (_commands.Count > 0 && tick <= _commands[^1].Tick)
                {
                    throw new InvalidDataException($"The replay has commands for tick {tick} after those for tick {_commands[^1].Tick}.");
                }

                var count = reader.ReadCount(CommandEncoding.MinimumSize);
                if (count == 0)
                {
                    throw new InvalidDataException($"The replay has a commands record for tick {tick} without a command.");
                }

                // Added only once the whole record has been read, so that a record cut short adds nothing.
                var commands = new (int Tick, Command Command)[count];
                for (var i = 0; i < count; i++)
                {
                    commands[i] = (tick, CommandEncoding.Read(reader));
                }

                _commands.AddRange(commands);
                break;

            case HashRecord:
                var hash = new ReplayHash(ReadTick(reader), reader.ReadUInt64());
                var due = (long)_hashes.Count * HashInterval;
                if (hash.Tick != due)
                {
                    throw new InvalidDataException($"The replay has the hash of tick {hash.Tick} where that of tick {due} is due.");
                }

                _hashes.Add(hash);
                break;

            case EndRecord:
                var end = new ReplayHash(ReadTick(reader), reader.ReadUInt64());
                if (_hashes.Count == 0)
                {
                    throw new InvalidDataException("The replay ends without the hash of tick 0.");
                }

                var last = _hashes[^1];
                if (end.Tick < last.Tick || end.Tick - (long)last.Tick >= HashInterval)
                {
                    throw new InvalidDataException($"The replay ends at tick {end.Tick}, and its last hash is that of tick {last.Tick}.");
                }

                if (end.Tick == last.Tick && end.Hash != last.Hash)
                {
                    throw new InvalidDataException($"The replay ends at tick {end.Tick} with another hash than the one it has for that tick.");
                }

                End = end;
                break;

            default:
                throw new InvalidDataException($"The replay has a record of unknown kind {kind}.");
        }
    }

    private static int ReadTick(ByteReader reader)
    {
        var tick = reader.ReadInt32();
        return tick >= 0 ? tick : throw new InvalidDataException($"The replay names tick {tick}.");
    }
}
