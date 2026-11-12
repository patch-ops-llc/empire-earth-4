using Xunit.Sdk;
using Recorded = Sim.Serialization.Replay;

namespace Tools.Tests.ReplayScripts;

/// <summary>
/// <see cref="ScriptReach"/> itself: a script that no longer reaches a rule fails, and a stored
/// replay that no longer does is reported and fails nothing.
/// </summary>
public class ScriptReachTests
{
    private const string Short = "--map ridge --ticks 10 --script movement";
    private const string Long = "--map ridge --ticks 20 --script movement";
    private const string Stored = "ridge-skirmish.replay";

    private static readonly string[] Rules = ["a short match or a stored one", "a long match"];

    [Fact]
    public void ItHoldsWhenOneTryReachesEveryRule()
    {
        var made = new List<int>();
        var reach = new ScriptReach(Rules, replay => Told(replay, made), [Short], [Short, Long], [Long]);

        reach.MustHoldOnARecordingMadeNow();

        // The first try lacks a rule, the two matches of the second reach them between them, and the third is not made.
        Assert.Equal([10, 10, 20], made);
    }

    [Fact]
    public void ItFailsWhenNoTryReachesEveryRuleAndSaysWhatEachLacks()
    {
        var reach = new ScriptReach(Rules, replay => Told(replay, []), [Short], [Long]);

        var failure = Assert.Throws<FailException>(reach.MustHoldOnARecordingMadeNow);

        Assert.Contains($"With \"{Short}\": Not reached: a long match.", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"With \"{Long}\": Not reached: a short match or a stored one.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItFailsWhenAMatchReachesSomethingThatIsNotOnTheList()
    {
        var reach = new ScriptReach(Rules[..1], replay => Told(replay, []), [Short, Long]);

        var failure = Assert.Throws<FailException>(reach.MustHoldOnARecordingMadeNow);

        Assert.Contains("Reached and not on the list: a long match.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatAMatchMayAlsoReachIsNeitherAskedNorCountedAgainstIt()
    {
        var reach = new ScriptReach(Rules[..1], replay => Told(replay, []), [Short], [Short, Long]).MayAlsoReach("a long match");

        reach.MustHoldOnARecordingMadeNow();
        new ScriptReach(Rules[..1], replay => Told(replay, []), [Short, Long]).MayAlsoReach("a long match").MustHoldOnARecordingMadeNow();
        Assert.Throws<EmptyException>(() => new ScriptReach(Rules, replay => Told(replay, []), [Short]).MayAlsoReach("a long match"));
    }

    [Fact]
    public void AStoredReplayThatReachesEveryRuleIsNotReported()
    {
        var reach = new ScriptReach(Rules[..1], replay => Told(replay, []), [Short]);

        reach.SayWhatTheGoldenReplaysNoLongerReach(Stored);
    }

    [Fact]
    public void AStoredReplayThatLacksARuleSkipsItsTestWithWhatItLacksAndARecordingThatHasIt()
    {
        var reach = new ScriptReach(Rules, replay => Told(replay, []), [Short], [Short, Long]);

        var skip = ReasonOfTheSkip(() => reach.SayWhatTheGoldenReplaysNoLongerReach(Stored));

        Assert.Contains(Stored, skip, StringComparison.Ordinal);
        Assert.Contains("Not reached: a long match.", skip, StringComparison.Ordinal);
        Assert.Contains($"between the matches recorded now with \"{Short}\" and \"{Long}\"", skip, StringComparison.Ordinal);
        Assert.Contains("may be recorded again with one of these", skip, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredReplayThatLacksARuleIsToldTheOneRecordingThatHasThemAll()
    {
        var reach = new ScriptReach(Rules[1..], replay => Told(replay, []), [Long]);

        var skip = ReasonOfTheSkip(() => reach.SayWhatTheGoldenReplaysNoLongerReach(Stored));

        Assert.Contains($"in a match recorded now, with \"{Long}\". A file that has never been on main may be recorded again with that", skip, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredReplayThatLacksARuleNoRecordingReachesSkipsItsTestToo()
    {
        var reach = new ScriptReach(Rules, replay => Told(replay, []), [Short]);

        var skip = ReasonOfTheSkip(() => reach.SayWhatTheGoldenReplaysNoLongerReach(Stored));

        Assert.Contains("No recording the test makes reaches them either", skip, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredReplayWhoseMatchTheTestCannotFollowSkipsItsTest()
    {
        var reach = new ScriptReach(
            Rules[..1],
            replay => replay.TickCount > 20 ? throw new InvalidOperationException("No such unit.") : Told(replay, []),
            [Short]);

        var skip = ReasonOfTheSkip(() => reach.SayWhatTheGoldenReplaysNoLongerReach(Stored));

        Assert.Contains("The test could not follow the match: No such unit.", skip, StringComparison.Ordinal);
    }

    /// <summary>The reason a call skips its test with. A skip is an exception that <c>Assert.Throws</c> passes on, so it is caught here.</summary>
    private static string ReasonOfTheSkip(Action call)
    {
        try
        {
            call();
        }
        catch (SkipException skip)
        {
            return skip.Message;
        }

        Assert.Fail("The call did not skip the test.");
        return "";
    }

    /// <summary>A stand-in for the code that reads a match: it tells a replay by its length, and notes the lengths it was asked about.</summary>
    private static string[] Told(Recorded replay, List<int> made)
    {
        made.Add(replay.TickCount);
        return replay.TickCount == 20 ? ["a long match"] : ["a short match or a stored one"];
    }
}
