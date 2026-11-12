namespace Sim.Tests.Perks;

/// <summary>
/// Why two tests of the accent perks do not run on the test build of the open pull requests.
/// Both were written where a melee unit that finds its own side in the way stays behind it. With
/// melee places (#54) it walks round to a free place by the target and fights from there, so the
/// fights of these fixtures end sooner. The rules under test are not at fault. The owner of
/// whichever piece lands second changes the fixtures in their own pull request.
/// </summary>
internal static class TestBuild
{
    /// <summary>The accent perks (#61, joined in #69) with melee places (#54).</summary>
    public const string PerksMeetMeleePlaces =
        "Test build only. With melee places (#54) a melee unit behind its own side walks round to a free place by the target. "
        + "The second death of PerkSnapshotTests comes at tick 381 and not 399, and the enemy of "
        + "RegenerationUnderFireTests.ASplashStartsTheFiveSecondsAgain dies before it has splashed six times. "
        + "With the places switched off both tests pass.";
}
