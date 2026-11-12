namespace Game;

/// <summary>
/// What a player is told about the file their game wrote when a match stopped because the
/// players' games no longer agreed. The end screen shows it under the sentence that says what
/// happened, and <c>docs/desync.md</c> tells a tester the same at more length.
/// </summary>
public static class DesyncReport
{
    /// <summary>What happened, in words for someone who does not know how the game works inside.</summary>
    public const string WhatHappened =
        "The match was stopped because the players' games no longer agreed on what had happened in it. "
        + "This is a fault in the game, not something you did.";

    /// <summary>All of it: what happened, then the words about the file.</summary>
    public static string Message(MatchFiles? files) => files is null ? WhatHappened : $"{WhatHappened}\n\n{Words(files)}";

    /// <summary>The words about the file to send, or about why there is none.</summary>
    public static string Words(MatchFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.BundlePath is { } bundle)
        {
            return "The game has saved what is needed to find the fault in one file. "
                + "Please send that file to whoever gave you the game, and ask the other player to send theirs: one file alone seldom shows which game went wrong.\n\n"
                + $"The file is:\n{bundle}";
        }

        if (files.SnapshotPath is not null)
        {
            return $"To find the fault, both players' files are needed. Yours are:\n{files.ReplayPath}\n{files.SnapshotPath}";
        }

        return files.ReplayPath is not null
            ? $"Please send this file to whoever gave you the game, and ask the other player to send theirs:\n{files.ReplayPath}"
            : "The game could not save a file for finding the fault. Please tell whoever gave you the game what you were doing when the match stopped.";
    }
}
