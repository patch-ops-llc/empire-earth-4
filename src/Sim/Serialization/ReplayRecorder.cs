using Sim.Core;

namespace Sim.Serialization;

/// <summary>
/// Writes the replay of a match while it runs: the description of the match, the commands of
/// every tick, the state hash once a second, and how it ended. <see cref="Replay"/> has the layout
/// and reads it back.
/// </summary>
/// <remarks>
/// <para>
/// Create the recorder when the match has been built and before its first tick. Then, for a match
/// run by a session:
/// </para>
/// <code>
/// var recorder = new ReplayRecorder(writer, description, match.World);
/// client.CommandsScheduled += (tick, commands) => recorder.RecordCommands(tick, commands.AsSpan());
/// client.TickCompleted += _ => recorder.AfterTick();
/// client.Stopped += _ => recorder.Finish();
/// </code>
/// <para>
/// The recorder only ever appends to its writer and never goes back, so the bytes written so far
/// can be moved to a file at any time: write out <see cref="ByteWriter.WrittenSpan"/> and call
/// <see cref="ByteWriter.Clear"/>. A file that ends there, because the game was closed or failed,
/// is a replay of the match up to that point.
/// </para>
/// <para>
/// Recording reads the world and changes nothing in it.
/// </para>
/// </remarks>
public sealed class ReplayRecorder
{
    /// <summary>Unless told otherwise, a state hash is recorded when the number of ticks run is a multiple of this: once a second of game time.</summary>
    public const int DefaultHashInterval = SimInfo.TicksPerSecond;

    /// <summary>The longest hash interval a replay can have.</summary>
    public const int MaxHashInterval = ushort.MaxValue;

    private readonly ByteWriter _writer;
    private readonly World _world;
    private readonly int _hashInterval;
    private int _lastCommandTick = -1;
    private int _lastHashTick = -1;

    /// <param name="writer">Receives the replay. Nothing else may write to it while the recorder is in use.</param>
    /// <param name="description">The match being recorded.</param>
    /// <param name="world">The world of that match, before its first tick.</param>
    /// <param name="hashInterval">
    /// A state hash is recorded every so many ticks, 1 to <see cref="MaxHashInterval"/>. A shorter
    /// interval finds the tick where two runs part more closely and makes the file larger.
    /// </param>
    public ReplayRecorder(ByteWriter writer, MatchDescription description, World world, int hashInterval = DefaultHashInterval)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(world);
        if (world.Tick != 0)
        {
            throw new ArgumentException($"A replay starts before the first tick, and this world has run {world.Tick}.", nameof(world));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(hashInterval, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hashInterval, MaxHashInterval);

        _writer = writer;
        _world = world;
        _hashInterval = hashInterval;
        Replay.WriteHeader(writer, hashInterval, description);
        RecordHash();
    }

    /// <summary>True once <see cref="Finish"/> has been called. Nothing more can be recorded.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// Records the commands scheduled for a tick, in the order they will run. Call it before that
    /// tick runs, at most once for a tick and for later ticks each time. A tick without commands
    /// may be passed or left out.
    /// </summary>
    public void RecordCommands(int tick, ReadOnlySpan<Command> commands)
    {
        ThrowIfFinished();
        ArgumentOutOfRangeException.ThrowIfLessThan(tick, _world.Tick);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tick, _lastCommandTick);

        _lastCommandTick = tick;
        if (commands.IsEmpty)
        {
            return;
        }

        _writer.WriteByte(Replay.CommandsRecord);
        _writer.WriteInt32(tick);
        _writer.WriteCount(commands.Length);
        foreach (ref readonly var command in commands)
        {
            CommandEncoding.Write(_writer, in command);
        }
    }

    /// <summary>Call it after every tick. It records the state hash when one is due.</summary>
    /// <exception cref="InvalidOperationException">A tick whose hash was due went by without this call.</exception>
    public void AfterTick()
    {
        ThrowIfFinished();
        ThrowIfAHashWasMissed();
        if (_world.Tick % _hashInterval == 0 && _world.Tick > _lastHashTick)
        {
            RecordHash();
        }
    }

    /// <summary>
    /// Records that the match ended where the world stands now, with its state hash. Calling it
    /// again does nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">A tick whose hash was due went by without a call to <see cref="AfterTick"/>.</exception>
    public void Finish()
    {
        if (IsFinished)
        {
            return;
        }

        ThrowIfAHashWasMissed();
        if (_world.Tick - _lastHashTick == _hashInterval)
        {
            RecordHash();
        }

        IsFinished = true;
        _writer.WriteByte(Replay.EndRecord);
        _writer.WriteInt32(_world.Tick);
        _writer.WriteUInt64(_world.ComputeHash());
    }

    private void RecordHash()
    {
        _lastHashTick = _world.Tick;
        _writer.WriteByte(Replay.HashRecord);
        _writer.WriteInt32(_world.Tick);
        _writer.WriteUInt64(_world.ComputeHash());
    }

    /// <summary>The state of a tick that has gone by cannot be hashed any more, and a replay may not leave a hash out.</summary>
    private void ThrowIfAHashWasMissed()
    {
        if (_world.Tick - (long)_lastHashTick > _hashInterval)
        {
            throw new InvalidOperationException(
                $"The hash of tick {_lastHashTick + _hashInterval} was not recorded: the world is at tick {_world.Tick}. Call AfterTick after every tick.");
        }
    }

    private void ThrowIfFinished()
    {
        if (IsFinished)
        {
            throw new InvalidOperationException("The replay has been finished.");
        }
    }
}
