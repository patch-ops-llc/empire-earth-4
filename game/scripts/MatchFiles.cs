using System.Globalization;
using Godot;
using Net;
using Sim.Serialization;
using Sim.Setup;
using FileAccess = System.IO.FileAccess;

namespace Game;

/// <summary>
/// The files this machine writes for one match: its replay, from the first tick on, and, when the
/// match stops because the peers no longer agree, a snapshot of the state it stopped in.
/// </summary>
/// <remarks>
/// They go to the folder <c>matches</c> in the game's user data folder, or to the folder named
/// after <see cref="FolderArgument"/> on the command line. A file that cannot be written never
/// stops the match: <see cref="Problem"/> says what went wrong.
/// <para>
/// The replay holds the state hash of every tick, so the replays of two peers show the tick at
/// which they parted, and the <c>desync-diff</c> tool reads the two replays and the two snapshots.
/// </para>
/// </remarks>
public sealed class MatchFiles : IDisposable
{
    /// <summary>Passed after <c>--</c> on the command line and followed by a folder, this is where match files go.</summary>
    public const string FolderArgument = "--match-files";

    public const string ReplayEnding = ".replay";

    public const string SnapshotEnding = ".snapshot";

    // A hash for every tick: 12 bytes a tick, under a megabyte for an hour of play.
    private const int HashInterval = 1;

    private readonly Match _match;
    private readonly string? _stem;
    private readonly FileStream? _replay;
    private readonly MatchRecording? _recording;

    /// <summary>Starts the replay. Create it when the match is built and before its first tick.</summary>
    public MatchFiles(ClientSession session, MatchDescription description, Match match, int player)
    {
        _match = match;

        try
        {
            var folder = Folder;
            Directory.CreateDirectory(folder);
            var name = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{description.MapId}_p{player}");
            (_stem, _replay) = CreateNew(folder, name);
            ReplayPath = _stem + ReplayEnding;
            _recording = new MatchRecording(session, description, match, _replay, HashInterval);

            // Every peer stops a match that came apart at the same tick, so their snapshots can be compared.
            session.Stopped += end =>
            {
                if (end.Reason == SessionEndReason.Desync)
                {
                    WriteSnapshot();
                }
            };
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Problem = $"The replay could not be started. {failed.Message}";
        }
    }

    /// <summary>Where match files go.</summary>
    public static string Folder
    {
        get
        {
            var arguments = OS.GetCmdlineUserArgs();
            var index = Array.IndexOf(arguments, FolderArgument);
            return index >= 0 && index + 1 < arguments.Length
                ? Path.GetFullPath(arguments[index + 1])
                : Path.Combine(OS.GetUserDataDir(), "matches");
        }
    }

    /// <summary>The replay file, or null if it could not be created.</summary>
    public string? ReplayPath { get; }

    /// <summary>The file with the state the match stopped in, once <see cref="WriteSnapshot"/> has written it.</summary>
    public string? SnapshotPath { get; private set; }

    /// <summary>What went wrong with a file, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// Writes the state the match stands in. A match that stops on a desync does this by itself.
    /// </summary>
    public void WriteSnapshot()
    {
        if (_stem is null || SnapshotPath is not null)
        {
            return;
        }

        try
        {
            var writer = new ByteWriter(64 * 1024);
            Snapshot.Write(_match.World, writer);
            var path = _stem + SnapshotEnding;
            File.WriteAllBytes(path, writer.WrittenSpan);
            SnapshotPath = path;
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            Problem = $"The state of the match could not be written. {failed.Message}";
        }
    }

    /// <summary>Ends the replay where the match stands and closes its file.</summary>
    public void Dispose()
    {
        if (_replay is null)
        {
            return;
        }

        _recording?.Finish();
        Problem ??= _recording?.Failure is { } failure ? $"The replay is not complete. {failure}" : null;
        _replay.Dispose();
    }

    /// <summary>Closes the replay and removes it, for a match that was never started.</summary>
    public void Discard()
    {
        Dispose();
        if (ReplayPath is not null && _match.World.Tick == 0)
        {
            try
            {
                File.Delete(ReplayPath);
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
            {
                // An empty replay left behind does no harm.
            }
        }
    }

    // Two games on one machine can start a match for the same seat in the same second, so the name
    // is only taken if no file has it yet.
    private static (string Stem, FileStream File) CreateNew(string folder, string name)
    {
        for (var attempt = 1; ; attempt++)
        {
            var stem = Path.Combine(folder, attempt == 1 ? name : string.Create(CultureInfo.InvariantCulture, $"{name}-{attempt}"));
            try
            {
                return (stem, new FileStream(stem + ReplayEnding, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            }
            catch (IOException) when (attempt < 100 && File.Exists(stem + ReplayEnding))
            {
                // Taken. Try the next name.
            }
        }
    }
}
