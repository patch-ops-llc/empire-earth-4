namespace Tools.Tests.ReplayScripts;

/// <summary>
/// Why four tests of the replay scripts do not run on the test build of the open pull requests.
/// Each asks that a recorded match reaches certain rules. The matches were recorded before the
/// pieces met, a golden replay is never recorded again on this branch, and with all pieces in
/// the same commands play out differently. The replays themselves still reproduce. The owner of
/// whichever piece lands second records again, or changes the script, in their own pull request.
/// </summary>
internal static class TestBuild
{
    /// <summary>Structures in combat (#58) with conquest (#56).</summary>
    public const string SiegeMeetsConquest =
        "Test build only. The siege script destroys a Capitol, and with conquest (#56) its player is out from then on: "
        + "in switchback-siege.replay player 0 is eliminated at tick 853 of 8000 and 193 of its 443 commands are dropped, "
        + "so the Towers' rules are not reached; a match recorded by the script ends its placements the same way.";

    /// <summary>Melee places (#54) with the fog of war (#44).</summary>
    public const string SurroundMeetsFog =
        "Test build only. The surround script sends a whole band at one target by an attack order, and under the fog (#44) "
        + "an attack order may name only what its player sees: 72 of the 75 attack orders of ridge-surround.replay are refused, "
        + "so seven units never stand on their places by one target.";

    /// <summary>Combat perks (#63) with the pieces it has not met.</summary>
    public const string CombatPerksMeetTheRest =
        "Test build only. The test asks that every attack order of switchback-combat-perks.replay names what its player sees "
        + "when it is given. With the pieces #63 has not met (production #46, live refresh #37, melee places #54, conquest #56) "
        + "the match runs differently and 154 of its 156 attack orders are refused. Which piece moves it was not found out.";
}
