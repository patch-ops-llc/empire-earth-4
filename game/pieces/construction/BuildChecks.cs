using Sim.Construction;
using Sim.Data;
using Sim.Setup;

namespace Game;

/// <summary>
/// Every question the build menu and the placement preview ask of the rules of construction,
/// for one player. Nothing else in the piece asks the rules what may be built or where, so that
/// who answers is decided in this one file.
/// </summary>
/// <remarks>
/// Today the answers come from <see cref="ConstructionRules"/>, which reads the true state of
/// the match. That is right while the view shows the whole match. Once the view is fogged, the
/// true state would let the preview show what is in the fog: a site that is not free because of
/// a structure the player has never seen, or an enemy territory the player does not know of
/// (design 5.4: a site is marked as blocked only by what the player can see). Fog for
/// structures (17b) gives the same questions on the player's own picture of the match, with the
/// same names, and the reason <see cref="BuildProblem"/> then has for unexplored ground. Each
/// member below becomes a call on that picture, and nothing else in the piece changes: the
/// words for a reason this piece does not name fall back to a sentence that fits any.
/// </remarks>
public readonly struct BuildChecks
{
    private readonly ConstructionRules _rules;

    /// <summary>The checks for a player of a match.</summary>
    public BuildChecks(Match match, int player)
    {
        Match = match;
        Player = player;
        _rules = match.Construction;
    }

    /// <summary>The match.</summary>
    public Match Match { get; }

    /// <summary>The player the answers are for.</summary>
    public int Player { get; }

    /// <summary>The checks for the local player of a view.</summary>
    public static BuildChecks For(MatchView view) => new(view.Match, view.Player);

    /// <summary>True for a structure a worker places, which is what the menu lists.</summary>
    public bool Lists(StructureDef structure) => _rules.Buildable(structure.Index) is not null;

    /// <summary>
    /// Whether the player may order the structure at all, wherever it goes: locked, at its
    /// limit, beyond what the player has, or <see cref="BuildProblem.None"/>.
    /// </summary>
    public BuildProblem Structure(StructureDef structure) => _rules.CheckStructure(Player, structure.Index);

    /// <summary>How many of the structure the player has, counting those under way.</summary>
    public int CountOf(StructureDef structure) => _rules.CountOf(Player, structure.Index);

    /// <summary>Whether an order for the structure with its lowest tile at (x, y) would be accepted, and why not.</summary>
    public BuildProblem Site(StructureDef structure, int x, int y) => _rules.CheckSite(Player, structure, x, y);

    /// <summary>True when the structure may cover this tile. A site is accepted exactly when every tile of it is.</summary>
    public bool Tile(StructureDef structure, int x, int y) => _rules.CanBuildOnTile(Player, structure, x, y);

    /// <summary>
    /// True when the structure's territory, with its lowest tile at (x, y), would overlap an
    /// enemy's the player knows of. The order is accepted all the same and judged when the
    /// worker arrives, so this is a warning.
    /// </summary>
    public bool NearEnemyTerritory(StructureDef structure, int x, int y) => _rules.IsTooCloseToEnemyTerritory(Player, structure, x, y);
}
