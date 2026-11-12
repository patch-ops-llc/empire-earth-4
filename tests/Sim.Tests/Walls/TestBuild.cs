namespace Sim.Tests.Walls;

/// <summary>
/// Why some tests of the wall rule (#68) do not run on the test build of the open pull requests.
/// #68 has not met the fog. Its description lists what the join takes ("When this meets #63");
/// the test build has the two changes to the rules from that list, and the tests of the join are
/// its owner's.
/// </summary>
internal static class TestBuild
{
    /// <summary>The wall rule (#68) with the fog of war (#44, #50, #63).</summary>
    public const string WallsMeetFog =
        "Test build only. The fixture gives an attack order on a target that no unit of its player is near, and with the fog in "
        + "the unit is left idle with no target (design 5.3: an attack order names only what its player sees). "
        + "The same tests with a move or an attack-move order pass.";
}
