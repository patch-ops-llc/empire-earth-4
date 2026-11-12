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
    /// <summary>Combat perks (#63) with the pieces it has not met.</summary>
    public const string CombatPerksMeetTheRest =
        "Test build only. switchback-combat-perks.replay as #63 recorded it again at 5881e13, on top of melee places (#54, #66), "
        + "reaches all but two rules of its list with every piece in: a Tower's shot on a plated unit at equal levels, and a "
        + "splash on a Hardened unit. Its attack orders are no longer refused. Which of the pieces #63 has not met moves it "
        + "(live refresh #37, production #46, conquest #56, the perks of #69, walls #68) was not found out.";

    /// <summary>The accent perks (#61, joined in #69) with the pieces #69 has not met.</summary>
    public const string PerksMeetTheRest =
        "Test build only. With the pieces #69 has not met the two recorded matches of the perks play out differently. "
        + "ridge-perks.replay reaches all fourteen rules here, the two included that the test says it no longer reaches "
        + "(a regeneration of a unit that carries Charge, a regeneration that stops at the maximum). "
        + "ridge-perks-in-fog.replay reaches thirteen: no charged melee attack. "
        + "It is not melee places (#54): with them switched off the result is the same. Which piece it is was not found out.";

    /// <summary>Plain walls (#68) with the pieces it has not met, the fog first among them.</summary>
    public const string WallsMeetTheRest =
        "Test build only. With every piece in, ridge-walls.replay plays out differently: 7 of its 11 development placements "
        + "and all 5 of its attack orders are refused, 17 wall orders are refused as outside the player's territory, and no "
        + "unit comes to a wall in its way. Seven rules of the list are not reached. #68 has not met the fog (#63); its "
        + "description says the tests of that join are its owner's.";

    /// <summary>The headless matches of the balance harness (#62) with the computer's scout (#77).</summary>
    public const string HeadlessMatchMeetsTheScout =
        "Test build only. These tests play swarm against elite for four minutes and ask that nothing is hit and nobody has "
        + "lost a unit by then, which held while the computer did not leave its base before its first attack. With #77 it "
        + "sends a scout at about 3:20, and in some of the matches a scout is hit or dead before 4:00. With #72 alone the "
        + "two tests of ai-match pass.";
}
