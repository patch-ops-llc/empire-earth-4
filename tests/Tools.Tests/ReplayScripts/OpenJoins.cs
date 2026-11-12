namespace Tools.Tests.ReplayScripts;

/// <summary>
/// Why a test of the replay scripts does not run on a branch that carries open pull requests
/// which have not met each other yet. It asks that a recorded match reaches certain rules. The
/// match was recorded before the pieces met, a golden replay is recorded again only by its
/// owner, and with both pieces in the same commands play out differently. The replay itself
/// still reproduces. The skip goes when the owner of whichever piece lands second has changed
/// the script or recorded again, in their own pull request.
/// </summary>
internal static class OpenJoins
{
    /// <summary>Plain walls (#68) with the fog of war (#50, #63).</summary>
    public const string WallsMeetFog =
        "Until #68 carries the fog of war. ridge-walls.replay was recorded without it. Under the fog an attack order may "
        + "name only what its player sees, the match takes another course, and 15 of the rules the replay is for are "
        + "no longer reached. A match recorded again with the same arguments reaches all but four of them.";
}
