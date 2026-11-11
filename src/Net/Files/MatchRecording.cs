using Sim;
using Sim.Core;
using Sim.Serialization;
using Sim.Setup;

namespace Net;

/// <summary>
/// Writes the replay of a match to a stream while a <see cref="ClientSession"/> runs it, in the
/// format of <see cref="Replay"/>: the description, the commands of every tick as the session
/// scheduled them, the state hash once a second and, when the session stops, the end.
/// </summary>
/// <remarks>
/// <para>
/// Create it when the match is built and before its first tick. It follows the session through
/// its events and needs no other call. What it has is handed to the stream once a second of game
/// time, so a file that ends early, because the game was closed or failed, is still a replay of
/// the match up to there.
/// </para>
/// <para>
/// With a hash interval of 1 the replay holds the state hash of every tick. Two peers whose match
/// stopped on a desync then have, in their replays, the exact tick at which they parted, which
/// the session itself only knows to the second.
/// </para>
/// <para>
/// A stream that fails does not stop the match: recording ends, and <see cref="Failure"/> says
/// why.
/// </para>
/// </remarks>
public sealed class MatchRecording
{
    private readonly ByteWriter _writer = new(1024);
    private readonly ReplayRecorder _recorder;
    private readonly Stream _output;
    private readonly World _world;

    /// <param name="session">The session that runs the match.</param>
    /// <param name="description">The match, as every peer was given it.</param>
    /// <param name="match">The match built from it, before its first tick.</param>
    /// <param name="output">Receives the replay. The caller closes it after the session has stopped.</param>
    /// <param name="hashInterval">A state hash is recorded every so many ticks.</param>
    public MatchRecording(
        ClientSession session,
        MatchDescription description,
        Match match,
        Stream output,
        int hashInterval = ReplayRecorder.DefaultHashInterval)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(output);

        _output = output;
        _world = match.World;
        _recorder = new ReplayRecorder(_writer, description, match.World, hashInterval);
        Flush();

        session.CommandsScheduled += (tick, commands) =>
        {
            if (IsRecording)
            {
                _recorder.RecordCommands(tick, commands.AsSpan());
            }
        };
        session.TickCompleted += _ =>
        {
            if (IsRecording)
            {
                _recorder.AfterTick();
                if (_world.Tick % SimInfo.TicksPerSecond == 0)
                {
                    Flush();
                }
            }
        };
        session.Stopped += _ => Finish();
    }

    /// <summary>Why recording ended before the match did, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>True once the replay has its end record and all of it is in the stream.</summary>
    public bool IsComplete { get; private set; }

    private bool IsRecording => Failure is null && !_recorder.IsFinished;

    /// <summary>
    /// Ends the replay where the match stands. The session's end does this; call it yourself only
    /// when the game closes while the session is still running.
    /// </summary>
    public void Finish()
    {
        if (!IsRecording)
        {
            return;
        }

        _recorder.Finish();
        Flush();
        IsComplete = Failure is null;
    }

    private void Flush()
    {
        try
        {
            _output.Write(_writer.WrittenSpan);
            _output.Flush();
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
        {
            Failure = failed.Message;
        }

        _writer.Clear();
    }
}
