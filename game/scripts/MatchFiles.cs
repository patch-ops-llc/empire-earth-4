using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using Net;
using Sim.Serialization;
using Sim.Setup;
using FileAccess = System.IO.FileAccess;

namespace Game;

/// <summary>
/// The files this machine writes for one match: its replay, from the first tick on, and, when the
/// match stops because the players' games no longer agree, one desync bundle with everything
/// this machine has for finding the cause.
/// </summary>
/// <remarks>
/// They go to the folder <c>matches</c> in the game's user data folder, or to the folder named
/// after <see cref="FolderArgument"/> on the command line. A file that cannot be written never
/// stops the match: <see cref="Problem"/> says what went wrong.
/// <para>
/// The replay holds the state hash of every tick. The bundle (<see cref="DesyncBundle"/>) holds
/// the replay, the last state the games agreed on, the state the match stopped in, the hash of
/// every part of the state for each tick in between, and the build and the system it ran on. It
/// is the one file a tester sends, and the <c>desync-diff</c> tool reads it.
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
    private readonly MatchDescription _description;
    private readonly string? _stem;
    private readonly FileStream? _replay;
    private readonly MatchRecording? _recording;
    private readonly DesyncWatch? _watch;

    /// <summary>Starts the replay. Create it when the match is built and before its first tick.</summary>
    /// <param name="session">The session that runs the match.</param>
    /// <param name="description">The match, as every peer was given it.</param>
    /// <param name="match">The match built from it.</param>
    /// <param name="player">The seat of the player at this machine.</param>
    /// <param name="watchForDesync">
    /// False for a match played alone, where there is no other machine to disagree with, so
    /// nothing is kept for a bundle.
    /// </param>
    public MatchFiles(ClientSession session, MatchDescription description, Match match, int player, bool watchForDesync = true)
    {
        _match = match;
        _description = description;

        try
        {
            var folder = Folder;
            Directory.CreateDirectory(folder);
            var name = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{description.MapId}_p{player}");
            (_stem, _replay) = CreateNew(folder, name);
            ReplayPath = _stem + ReplayEnding;
            _recording = new MatchRecording(session, description, match, _replay, HashInterval);
            _watch = watchForDesync ? new DesyncWatch(session, match) : null;

            // Every peer stops a match that came apart at the same tick, so what they write can be
            // compared. The recording heard of the stop first, so the replay is whole by now.
            session.Stopped += end =>
            {
                if (end.Reason == SessionEndReason.Desync)
                {
                    WriteBundle(end);
                }
            };
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Problem = $"The replay could not be started. {failed.Message}";
        }
    }

    /// <summary>
    /// Added to the name of the folder, for the matches that start from now on. The network test
    /// sets it for the match it makes fail: the replay of a game that was made to go wrong does
    /// not give the same match again, and must not lie among the replays that are checked for that.
    /// </summary>
    public static string FolderEnding { get; set; } = string.Empty;

    /// <summary>Where match files go.</summary>
    public static string Folder
    {
        get
        {
            var arguments = OS.GetCmdlineUserArgs();
            var index = Array.IndexOf(arguments, FolderArgument);
            var folder = index >= 0 && index + 1 < arguments.Length
                ? Path.GetFullPath(arguments[index + 1])
                : Path.Combine(OS.GetUserDataDir(), "matches");
            return Path.TrimEndingDirectorySeparator(folder) + FolderEnding;
        }
    }

    /// <summary>The replay file, or null if it could not be created.</summary>
    public string? ReplayPath { get; }

    /// <summary>The desync bundle, once a match that stopped on a desync has written it. It is the one file to send.</summary>
    public string? BundlePath { get; private set; }

    /// <summary>
    /// The file with only the state the match stopped in, which <see cref="WriteSnapshot"/> writes.
    /// A match that stops on a desync writes it when it could not write the bundle.
    /// </summary>
    public string? SnapshotPath { get; private set; }

    /// <summary>What went wrong with a file, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// What the bundle says about this machine: the build, the system and when the file was
    /// written. Nothing in it names the player, the machine or a folder.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> MachineFacts()
    {
        var godot = Engine.GetVersionInfo();
        return
        [
            new("game_build", GameInfo.Version),
            new("build_kind", OS.IsDebugBuild() ? "debug" : "release"),
            new("operating_system", RuntimeInformation.OSDescription),
            new("system_name", $"{OS.GetName()} {OS.GetVersion()}".Trim()),
            new("os_architecture", RuntimeInformation.OSArchitecture.ToString()),
            new("process_architecture", RuntimeInformation.ProcessArchitecture.ToString()),
            new("runtime_identifier", RuntimeInformation.RuntimeIdentifier),
            new("dotnet", RuntimeInformation.FrameworkDescription),
            new("godot", godot.TryGetValue("string", out var version) ? version.AsString() : "unknown"),
            new("processor", OS.GetProcessorName()),
            new("processor_count", OS.GetProcessorCount().ToString(CultureInfo.InvariantCulture)),
            new("culture", CultureInfo.CurrentCulture.Name),
            new(DesyncBundle.WrittenAtKey, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>
    /// Writes the state the match stands in, alone in a file. It is what a build before the
    /// bundle wrote on a desync, and what this one still writes when the bundle fails.
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

    // Makes the bundle of a match that stopped on a desync and writes it beside the replay.
    private void WriteBundle(SessionEndInfo end)
    {
        if (_stem is null || _watch is null || BundlePath is not null)
        {
            return;
        }

        try
        {
            var bundle = _watch.CreateBundle(end, _description, ReadReplay(), MachineFacts());
            var path = _stem + DesyncBundle.FileEnding;
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                bundle.Write(file);
            }

            BundlePath = path;
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidDataException)
        {
            Problem = $"The file for finding the fault could not be written. {failed.Message}";

            // Less than the bundle, and still what the tool can read beside the replay.
            WriteSnapshot();
        }
    }

    // The replay as it is on disk. Its file is still open for writing here, and stays so.
    private byte[] ReadReplay()
    {
        if (ReplayPath is null)
        {
            return [];
        }

        using var file = new FileStream(ReplayPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        return bytes;
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
