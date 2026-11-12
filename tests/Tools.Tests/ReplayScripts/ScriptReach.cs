using Recorded = Sim.Serialization.Replay;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// The rules a replay script is there to reach, and the two questions the tests of a script ask
/// about them.
/// <para>
/// Does the script still reach them? That is asked of a match recorded now, by the build under
/// test, and a script that no longer does fails its test. A recording made now follows a change
/// to the game: the script reads the match as it is and names the units there are.
/// </para>
/// <para>
/// Does the golden replay still reach them? A stored file only has to reproduce, which
/// <c>replay verify</c> and <c>ReplayToolTests</c> ask of every file in the folder. Its commands
/// name units by their numbers, so a change to an earlier fight can leave them naming something
/// else, and the match then goes another way. That is no fault of the change. The test of the
/// stored file is therefore skipped, never failed, with what the file no longer reaches as the
/// reason, so that the report of the tests says so.
/// </para>
/// </summary>
internal sealed class ScriptReach
{
    private readonly string[] _rules;
    private readonly Func<Recorded, IEnumerable<string>> _reached;
    private readonly string[][] _tries;
    private string[] _mayAlsoReach = [];

    /// <param name="rules">Every rule the script must reach, each in a few words.</param>
    /// <param name="reached">
    /// Plays the match of a replay and gives what it reached, in the words of
    /// <paramref name="rules"/>. Anything it gives that is on neither list counts against the
    /// match too, so that the lists stay the whole of what the test knows.
    /// </param>
    /// <param name="tries">
    /// The recordings to make, a small fixed list. Each try is the arguments of one
    /// <c>replay record</c>, after the file and without <c>--data</c>, as on a command line; a
    /// try with several lines is several matches that must reach the rules between them, which
    /// is what to give where a rule depends on how a match happens to go: a rule that most
    /// matches reach is then missed only when every one of them misses it. The first try that
    /// reaches every rule is enough, and the later ones are then not made. The first line is
    /// best the one the golden replay was recorded with. Say beside them why they are enough.
    /// </param>
    public ScriptReach(IEnumerable<string> rules, Func<Recorded, IEnumerable<string>> reached, params string[][] tries)
    {
        _rules = [.. rules];
        _reached = reached;
        _tries = tries;
        Assert.NotEmpty(_rules);
        Assert.NotEmpty(_tries);
        Assert.All(_tries, lines => Assert.NotEmpty(lines));
    }

    /// <summary>
    /// Names what a match of the script may reach and need not: something that happens in some
    /// matches and is not what the script is for. It is named so that anything else a match
    /// reaches still counts against it.
    /// </summary>
    public ScriptReach MayAlsoReach(params string[] rules)
    {
        _mayAlsoReach = rules;
        Assert.Empty(_rules.Intersect(rules, StringComparer.Ordinal));
        return this;
    }

    /// <summary>Fails unless one of the tries, recorded now, reaches every rule and nothing else.</summary>
    public void MustHoldOnARecordingMadeNow()
    {
        var failures = new List<string>();
        foreach (var lines in _tries)
        {
            var problem = WhatIsWrongWith(RecordedNow(lines));
            if (problem.Length == 0)
            {
                return;
            }

            failures.Add($"With {Shown(lines)}: {problem}");
        }

        Assert.Fail("The script does not reach the rules it guards in a match recorded now.\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Skips the test, with the reason, when the stored replays no longer reach every rule
    /// between them. It never fails for that: see the remarks of the class.
    /// </summary>
    /// <remarks>
    /// When the match of a stored file has gone so far from the recorded one that
    /// <c>reached</c> cannot follow it and throws, that is reported in the same way. The same
    /// code runs on the recording made now, where a failure does fail.
    /// </remarks>
    public void SayWhatTheGoldenReplaysNoLongerReach(params string[] names)
    {
        string problem;
        try
        {
            problem = WhatIsWrongWith(names.Select(ReplayRuns.ReadGolden));
        }
        catch (Exception exception) when (exception is not Xunit.Sdk.SkipException)
        {
            problem = $"The test could not follow the match: {exception.Message.ReplaceLineEndings(" ")}";
        }

        if (problem.Length == 0)
        {
            return;
        }

        var files = string.Join(" and ", names);
        var holds = _tries.FirstOrDefault(lines => WhatIsWrongWith(RecordedNow(lines)).Length == 0);
        var advice = holds switch
        {
            null => "No recording the test makes reaches them either, which the test of the script reports.",
            [_] => $"The script reaches them in a match recorded now, with {Shown(holds)}. "
                + "A file that has never been on main may be recorded again with that; a file that is on main stays as it is.",
            _ => $"The script reaches them between the matches recorded now with {Shown(holds)}. "
                + "A file that has never been on main may be recorded again with one of these; a file that is on main stays as it is.",
        };
        Assert.Skip($"{files} no longer {(names.Length == 1 ? "reaches" : "reach")} every rule the script guards. That fails nothing: a stored replay only has to reproduce. {problem} {advice}");
    }

    private static IEnumerable<Recorded> RecordedNow(string[] lines) =>
        lines.Select(line => ReplayRuns.Record(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    private static string Shown(string[] lines) => string.Join(" and ", lines.Select(line => $"\"{line}\""));

    /// <summary>What the matches of these replays, taken together, lack or have too much of. Empty when they reach every rule and nothing that is on neither list.</summary>
    private string WhatIsWrongWith(IEnumerable<Recorded> replays)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var replay in replays)
        {
            seen.UnionWith(_reached(replay));
        }

        var missing = _rules.Except(seen, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var unexpected = seen.Except(_rules, StringComparer.Ordinal).Except(_mayAlsoReach, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var parts = new List<string>();
        if (missing.Count > 0)
        {
            parts.Add($"Not reached: {string.Join("; ", missing)}.");
        }

        if (unexpected.Count > 0)
        {
            parts.Add($"Reached and not on the list: {string.Join("; ", unexpected)}.");
        }

        return string.Join(" ", parts);
    }
}
