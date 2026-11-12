using Sim.Construction;
using Sim.Data;
using Sim.Setup;
using Sim.Vision;

namespace Game;

/// <summary>
/// Every question the build menu and the placement preview ask of the rules of construction,
/// for one player. Nothing else in the piece asks the rules what may be built or where, so that
/// who answers is decided in this one file.
/// </summary>
/// <remarks>
/// <para>
/// The answers come from the player's own picture of the match, the simulation's
/// <see cref="PlayerView"/>, and not from the true state: a site is marked as blocked only by
/// what the player may know (design 5.4). A structure the player has never seen is not in the
/// way in the preview, one the player remembers is, and an enemy territory warns only while the
/// player sees the Capitol or Outpost it comes from. These are the checks an order is judged by
/// when it is given, so the preview and the order agree. What is really there is judged when the
/// worker arrives.
/// </para>
/// <para>
/// The development switch that lifts the fog from the screen changes nothing here: it changes
/// what is drawn, and an order is judged by what the player knows whatever is drawn.
/// </para>
/// </remarks>
public readonly struct BuildChecks
{
    private readonly PlayerView _seen;

    /// <summary>The checks for the player of a view of a match.</summary>
    public BuildChecks(Match match, PlayerView seen)
    {
        Match = match;
        _seen = seen;
    }

    /// <summary>The match.</summary>
    public Match Match { get; }

    /// <summary>The player the answers are for.</summary>
    public int Player => _seen.Player;

    /// <summary>The checks for the local player of a view.</summary>
    public static BuildChecks For(MatchView view) => new(view.Match, view.Sight.Seen);

    /// <summary>True for a structure a worker places, which is what the menu lists. It is the same for every player and all match long.</summary>
    public bool Lists(StructureDef structure) => Match.Construction.Buildable(structure.Index) is not null;

    /// <summary>
    /// Whether the player may order the structure at all, wherever it goes: locked, at its
    /// limit, beyond what the player has, or <see cref="BuildProblem.None"/>.
    /// </summary>
    public BuildProblem Structure(StructureDef structure) => _seen.CheckStructure(structure.Index);

    /// <summary>How many of the structure the player has, counting those under way.</summary>
    public int CountOf(StructureDef structure) => _seen.CountOf(structure.Index);

    /// <summary>
    /// Whether an order for the structure with its lowest tile at (x, y) would be accepted, and
    /// why not, as far as the player may know.
    /// </summary>
    public BuildProblem Site(StructureDef structure, int x, int y) => _seen.CheckSite(structure, x, y);

    /// <summary>
    /// True when, as far as the player may know, the structure may cover this tile. A site is
    /// accepted exactly when every tile of it is.
    /// </summary>
    public bool Tile(StructureDef structure, int x, int y) => _seen.CanBuildOnTile(structure, x, y);

    /// <summary>
    /// True when the structure's territory, with its lowest tile at (x, y), would overlap that
    /// of an enemy Capitol or Outpost the player sees now. The order is accepted all the same
    /// and judged against every enemy territory when the worker arrives, so this is a warning.
    /// </summary>
    public bool NearEnemyTerritory(StructureDef structure, int x, int y) => _seen.IsTooCloseToSeenEnemyTerritory(structure, x, y);
}
