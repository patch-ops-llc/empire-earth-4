namespace Tools.Tests.ReplayScripts;

/// <summary>
/// Why two tests of the replay scripts do not run on a branch that carries open pull requests
/// which have not met each other yet. Each asks that a recorded match reaches certain rules. The
/// matches were recorded before the pieces met, a golden replay is recorded again only by its
/// owner, and with both pieces in the same commands play out differently. The replays
/// themselves still reproduce. Each skip goes when the owner of whichever piece lands second
/// has changed the script or recorded again, in their own pull request.
/// </summary>
internal static class OpenJoins
{
    /// <summary>Plain walls (#68) with the fog of war (#50, #63).</summary>
    public const string WallsMeetFog =
        "Until #68 carries the fog of war. ridge-walls.replay was recorded without it. Under the fog an attack order may "
        + "name only what its player sees, the match takes another course, and 15 of the rules the replay is for are "
        + "no longer reached. A match recorded again with the same arguments reaches all but four of them.";

    /// <summary>Combat perks (#63) with the places of melee units (#54, #66).</summary>
    public const string CombatPerksMeetPlaces =
        "Until #63 and #66 have met. The test asks that every attack order of switchback-combat-perks.replay names what "
        + "its player sees when it is given. With places round a target the units stand elsewhere, the match takes another "
        + "course, and the recorded attack orders no longer name what is in sight.";
}
