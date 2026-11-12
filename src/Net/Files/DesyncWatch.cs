using System.Collections.Immutable;
using Sim;
using Sim.Core;
using Sim.Serialization;
using Sim.Setup;

namespace Net;

/// <summary>How much a <see cref="DesyncWatch"/> keeps.</summary>
public sealed record DesyncWatchOptions
{
    /// <summary>The whole state is kept every so many ticks.</summary>
    public int SnapshotInterval { get; init; } = SimInfo.TicksPerSecond;

    /// <summary>The number of such states kept. Older ones make room for newer.</summary>
    public int SnapshotsKept { get; init; } = 24;

    /// <summary>The number of ticks whose part hashes are kept.</summary>
    public int HashTicksKept { get; init; } = 512;

    /// <summary>
    /// The whole state is also kept after every tick, for this many of the last ticks, so that
    /// the bundle has the state the match really had in the tick the fault happened in. 0 keeps none.
    /// </summary>
    public int LiveStatesKept { get; init; } = 64;

    /// <summary>The most memory those states may take, in bytes. When they take more, fewer ticks are kept.</summary>
    public long LiveStateBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>The most states the bundle holds for the ticks the fault may have happened in.</summary>
    public int MaxStates { get; init; } = 64;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(SnapshotInterval, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(SnapshotsKept, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(HashTicksKept, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(LiveStatesKept);
        ArgumentOutOfRangeException.ThrowIfNegative(LiveStateBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxStates);
    }
}

/// <summary>
/// Follows a match that a <see cref="ClientSession"/> runs and keeps, for the last stretch of it,
/// what is needed to find the cause if the machines stop agreeing: the whole state once a second,
/// the commands since the oldest of those states, the hash of every part of the state after
/// every tick, and the whole state after each of the last few ticks. When the session stops on a
/// desync, <see cref="CreateBundle"/> makes the <see cref="DesyncBundle"/> from them.
/// </summary>
/// <remarks>
/// <para>
/// Create it when the match is built and before its first tick. It follows the session through
/// its events and needs no other call until the match has stopped. It only reads the match:
/// nothing it does changes a field or the state hash.
/// </para>
/// <para>
/// The session compares hashes once every <see cref="ClientSession.HashInterval"/> ticks, and
/// the host finds a difference only when the slowest machine's report is in, by when the others
/// have run on. So the tick the session names is the comparison that failed, not the tick the
/// fault happened in. The fault is after the comparison before it, which every machine passed,
/// and the state kept from then or earlier is the "agreed" state of the bundle.
/// </para>
/// <para>
/// What is kept is bounded: with the defaults, the state once a second for the last 24 seconds,
/// the hashes of the last 25, and the state after every tick for the last 3 seconds or 32
/// megabytes, whichever is less. A machine that was further behind than that when the host
/// stopped the match has no agreed state in its bundle, and the bundle says so.
/// </para>
/// <para>
/// The states of the ticks the fault may have happened in come from the last of these where it
/// reaches back far enough: they are the states the match had. Where it does not, they come from
/// the machine's second run of those ticks, for as long as that run gave the hashes of the first.
/// </para>
/// <para>
/// The parts of the state are those the match's world has: its own fields and every store
/// registered with it, by name. Nothing here knows what the stores are.
/// </para>
/// </remarks>
public sealed class DesyncWatch
{
    private readonly ClientSession _session;
    private readonly Match _match;
    private readonly World _world;
    private readonly DesyncWatchOptions _options;
    private readonly StoreHasher _hasher = new();
    private readonly ImmutableArray<string> _parts;

    // A ring of the part hashes of the last ticks: slot i holds _parts.Length hashes.
    private readonly ulong[] _hashes;
    private readonly int[] _hashTicks;
    private int _hashNext;
    private int _hashCount;

    // A ring of states. The writers are reused, so that a match in full flow allocates nothing here.
    private readonly ByteWriter[] _snapshots;
    private readonly int[] _snapshotTicks;
    private int _snapshotNext;
    private int _snapshotCount;

    // The state after each of the last ticks, oldest first, and the writers that are free to use again.
    private readonly Queue<(int Tick, ByteWriter State)> _live = new();
    private readonly Stack<ByteWriter> _freeWriters = new();
    private long _liveBytes;

    // The commands of every tick from the oldest state kept on, in the order they were scheduled.
    private readonly Queue<(int Tick, ImmutableArray<Command> Commands)> _commands = new();

    /// <param name="session">The session that runs the match.</param>
    /// <param name="match">The match, before its first tick.</param>
    /// <param name="options">How much to keep, or null for the defaults.</param>
    public DesyncWatch(ClientSession session, Match match, DesyncWatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(match);

        _options = options ?? new DesyncWatchOptions();
        _options.Validate();
        _session = session;
        _match = match;
        _world = match.World;
        _parts = [.. StoreHasher.PartNames(_world)];
        _hashes = new ulong[checked(_options.HashTicksKept * _parts.Length)];
        _hashTicks = new int[_options.HashTicksKept];
        _snapshots = new ByteWriter[_options.SnapshotsKept];
        _snapshotTicks = new int[_options.SnapshotsKept];

        KeepHashes();
        KeepLiveState();
        KeepSnapshot();

        session.CommandsScheduled += (tick, commands) =>
        {
            if (!commands.IsDefaultOrEmpty)
            {
                _commands.Enqueue((tick, commands));
            }
        };
        session.TickCompleted += _ =>
        {
            KeepHashes();
            KeepLiveState();
            if (_world.Tick % _options.SnapshotInterval == 0)
            {
                KeepSnapshot();
            }
        };
    }

    /// <summary>The names of the parts of the state whose hashes are kept.</summary>
    public ImmutableArray<string> Parts => _parts;

    /// <summary>
    /// Makes the bundle of a match that has stopped. It is meant for a match that stopped on a
    /// desync, and makes one for any stop.
    /// </summary>
    /// <param name="end">How the session stopped.</param>
    /// <param name="description">The match, as every peer was given it.</param>
    /// <param name="replay">The replay of the match as this machine recorded it, or empty if it has none.</param>
    /// <param name="machine">The build and the system, as lines for <see cref="DesyncBundle.Machine"/>.</param>
    /// <remarks>
    /// This runs the last stretch of the match a second time, in a match of its own, which takes
    /// about as long as that stretch took to compute the first time. If the second run fails, the
    /// bundle is made without it and says why.
    /// </remarks>
    public DesyncBundle CreateBundle(
        SessionEndInfo end,
        MatchDescription description,
        ReadOnlyMemory<byte> replay,
        IEnumerable<KeyValuePair<string, string>>? machine = null)
    {
        ArgumentNullException.ThrowIfNull(end);
        ArgumentNullException.ThrowIfNull(description);

        var finalTick = _world.Tick;
        var final = new ByteWriter(1024);
        Snapshot.Write(_world, final);

        var interval = Math.Max(1, _session.HashInterval);
        var agreed = AgreedSlot(end.DesyncTick, interval);
        var agreedTick = agreed < 0 ? -1 : _snapshotTicks[agreed];
        var agreedBytes = agreed < 0 ? null : _snapshots[agreed].ToArray();

        // The states the match had in the ticks the fault may have happened in: those after the
        // last comparison every machine passed, up to the one that failed.
        var states = new SortedDictionary<int, byte[]>();
        foreach (var (tick, state) in _live)
        {
            if (InFaultWindow(tick, end.DesyncTick, interval) && states.Count < _options.MaxStates)
            {
                states.Add(tick, state.ToArray());
            }
        }

        SecondRun second;
        if (agreedBytes is null)
        {
            second = new SecondRun(SecondRunOutcome.NotRun, Detail: end.DesyncTick < 0
                ? "the session did not say which comparison failed"
                : end.DesyncTick - interval < 0
                    ? "the games differed at their first comparison, so there is no state from before"
                    : "no state from before the failed comparison was still kept");
        }
        else
        {
            try
            {
                second = RunAgain(description, agreedBytes, agreedTick, finalTick, end.DesyncTick, interval, states);
            }
            catch (Exception failed)
            {
                // The second run is a help in finding the fault. It must not cost the tester the rest.
                second = new SecondRun(SecondRunOutcome.NotRun, Detail: $"it failed: {failed.GetType().Name}: {failed.Message.TrimEnd('.')}");
            }
        }

        var held = states.Select(state => new TickState(state.Key, state.Value)).ToImmutableArray();

        return new DesyncBundle
        {
            Player = _session.Player,
            PlayerCount = description.Players.Count,
            GameVersion = description.GameVersion,
            ContentHash = description.ContentHash,
            MapId = description.MapId,
            Seed = description.Seed,
            HashInterval = interval,
            DesyncTick = end.DesyncTick,
            FinalTick = finalTick,
            Replay = replay.ToArray(),
            FinalSnapshot = final.ToArray(),
            AgreedTick = agreedTick,
            AgreedSnapshot = agreedBytes,
            Parts = _parts,
            Hashes = HashesFrom(agreedTick),
            SecondRun = second,
            States = held,
            Machine = machine is null ? [] : [.. machine],
        };
    }

    private void KeepHashes()
    {
        _hasher.Compute(_world, _hashes.AsSpan(_hashNext * _parts.Length, _parts.Length));
        _hashTicks[_hashNext] = _world.Tick;
        _hashNext = (_hashNext + 1) % _hashTicks.Length;
        _hashCount = Math.Min(_hashCount + 1, _hashTicks.Length);
    }

    private void KeepLiveState()
    {
        if (_options.LiveStatesKept == 0)
        {
            return;
        }

        var writer = _freeWriters.Count > 0 ? _freeWriters.Pop() : new ByteWriter(1024);
        writer.Clear();
        Snapshot.Write(_world, writer);
        _live.Enqueue((_world.Tick, writer));
        _liveBytes += writer.Length;

        // The newest is always kept, however large it is.
        while (_live.Count > _options.LiveStatesKept || (_liveBytes > _options.LiveStateBytes && _live.Count > 1))
        {
            var oldest = _live.Dequeue().State;
            _liveBytes -= oldest.Length;
            _freeWriters.Push(oldest);
        }
    }

    private static bool InFaultWindow(int tick, int desyncTick, int interval) => tick > desyncTick - interval && tick <= desyncTick;

    private void KeepSnapshot()
    {
        var writer = _snapshots[_snapshotNext] ??= new ByteWriter(1024);
        writer.Clear();
        Snapshot.Write(_world, writer);
        _snapshotTicks[_snapshotNext] = _world.Tick;
        _snapshotNext = (_snapshotNext + 1) % _snapshots.Length;
        _snapshotCount = Math.Min(_snapshotCount + 1, _snapshots.Length);

        // Commands from before the oldest state kept can never be run again from here.
        var oldest = _snapshotTicks[OldestSnapshot()];
        while (_commands.Count > 0 && _commands.Peek().Tick < oldest)
        {
            _commands.Dequeue();
        }
    }

    private int OldestSnapshot() => _snapshotCount < _snapshots.Length ? 0 : _snapshotNext;

    /// <summary>The slot of the newest state kept from no later than the last comparison every machine passed, or -1.</summary>
    private int AgreedSlot(int desyncTick, int interval)
    {
        if (desyncTick < 0)
        {
            return -1;
        }

        var limit = desyncTick - interval;
        var best = -1;
        for (var i = 0; i < _snapshotCount; i++)
        {
            if (_snapshotTicks[i] <= limit && (best < 0 || _snapshotTicks[i] > _snapshotTicks[best]))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>The slot of the hashes kept for the state after this many ticks, or -1.</summary>
    private int HashSlot(int tick)
    {
        // Ticks are kept in order, one apart, so the slot follows from the newest.
        if (_hashCount == 0)
        {
            return -1;
        }

        var newest = (_hashNext - 1 + _hashTicks.Length) % _hashTicks.Length;
        var back = _hashTicks[newest] - tick;
        if (back < 0 || back >= _hashCount)
        {
            return -1;
        }

        var slot = (newest - back + _hashTicks.Length) % _hashTicks.Length;
        return _hashTicks[slot] == tick ? slot : -1;
    }

    private ImmutableArray<PartHashes> HashesFrom(int agreedTick)
    {
        var table = ImmutableArray.CreateBuilder<PartHashes>();
        var oldest = (_hashNext - _hashCount + _hashTicks.Length) % _hashTicks.Length;
        for (var i = 0; i < _hashCount; i++)
        {
            var slot = (oldest + i) % _hashTicks.Length;
            if (_hashTicks[slot] >= agreedTick)
            {
                table.Add(new PartHashes(_hashTicks[slot], [.. _hashes.AsSpan(slot * _parts.Length, _parts.Length)]));
            }
        }

        return table.ToImmutable();
    }

    private SecondRun RunAgain(
        MatchDescription description,
        byte[] agreed,
        int agreedTick,
        int finalTick,
        int desyncTick,
        int interval,
        SortedDictionary<int, byte[]> states)
    {
        var again = description.CreateMatch(_match.Data);
        var parts = StoreHasher.PartNames(again.World);
        if (!parts.AsSpan().SequenceEqual(_parts.AsSpan()))
        {
            return new SecondRun(SecondRunOutcome.NotRun, Detail: "a match built again from the description has other stores than the match that was played");
        }

        var reader = new ByteReader(agreed);
        Snapshot.Restore(again, reader);
        reader.ExpectEnd();

        foreach (var (tick, commands) in _commands)
        {
            if (tick >= agreedTick)
            {
                foreach (var command in commands)
                {
                    again.Simulation.Commands.Enqueue(tick, command);
                }
            }
        }

        var hashes = new ulong[_parts.Length];
        var partedAt = -1;
        var detail = "";
        var writer = new ByteWriter(agreed.Length);
        while (true)
        {
            var tick = again.World.Tick;
            _hasher.Compute(again.World, hashes);
            var slot = HashSlot(tick);
            if (partedAt < 0 && slot >= 0 && !hashes.AsSpan().SequenceEqual(_hashes.AsSpan(slot * _parts.Length, _parts.Length)))
            {
                partedAt = tick;
                var kept = _hashes.AsSpan(slot * _parts.Length, _parts.Length);
                var differing = new List<string>();
                for (var part = 0; part < hashes.Length; part++)
                {
                    if (hashes[part] != kept[part])
                    {
                        differing.Add(_parts[part]);
                    }
                }

                detail = string.Join(", ", differing);
            }

            // While the second run gives the hashes of the first, its state is the state the match
            // had, and stands in for a tick whose state is no longer kept.
            if (partedAt < 0 && slot >= 0 && InFaultWindow(tick, desyncTick, interval) && !states.ContainsKey(tick) && states.Count < _options.MaxStates)
            {
                writer.Clear();
                Snapshot.Write(again.World, writer);
                states.Add(tick, writer.ToArray());
            }

            if (tick >= finalTick || partedAt >= 0)
            {
                break;
            }

            again.Simulation.Tick();
        }

        return partedAt < 0
            ? new SecondRun(SecondRunOutcome.Repeated, agreedTick, finalTick)
            : new SecondRun(SecondRunOutcome.Parted, agreedTick, partedAt, partedAt, detail);
    }
}
