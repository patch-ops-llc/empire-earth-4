namespace Tools.Tests.ReplayScripts;

/// <summary>
/// Why some tests of the replay scripts do not run on the test build of the open pull requests.
/// Each asks that a recorded match reaches certain rules. The matches were recorded before the
/// pieces met, a golden replay is never recorded again on this branch, and with all pieces in
/// the same commands play out differently. The replays themselves still reproduce. The owner of
/// whichever piece lands second records again, or changes the script, in their own pull request.
/// </summary>
internal static class TestBuild
{
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

    /// <summary>The accent perks (#61, joined in #69) with the pieces #69 has not met.</summary>
    public const string PerksMeetTheRest =
        "Test build only. With the pieces #69 has not met the two recorded matches of the perks play out differently. "
        + "ridge-perks.replay reaches all fourteen rules here, the two included that the test says it no longer reaches "
        + "(a regeneration of a unit that carries Charge, a regeneration that stops at the maximum). "
        + "ridge-perks-in-fog.replay reaches thirteen: no charged melee attack. "
        + "It is not melee places (#54): with them switched off the result is the same. Which piece it is was not found out.";
}
