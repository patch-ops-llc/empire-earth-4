using Godot;
using Sim.Construction;
using Sim.Data;
using Sim.Setup;

namespace Game;

/// <summary>
/// A site for a structure as the placement preview judges it: the lowest tile of the footprint,
/// what the rules say of the site now, and whether an enemy's territory would turn the worker
/// away on arrival.
/// </summary>
/// <param name="X">The x of the lowest tile of the footprint.</param>
/// <param name="Y">The y of the lowest tile of the footprint.</param>
/// <param name="Problem">Why an order for the site would be refused, or <see cref="BuildProblem.None"/>.</param>
/// <param name="NearEnemy">
/// True when the order would be accepted and then refused on arrival, as things stand, because
/// the structure's territory would overlap an enemy's (design 5.4). A warning: the click is taken.
/// </param>
public readonly record struct Placement(int X, int Y, BuildProblem Problem, bool NearEnemy)
{
    /// <summary>True when a build order for the site would be accepted.</summary>
    public bool IsAccepted => Problem == BuildProblem.None;

    /// <summary>
    /// The site whose footprint has the point of the ground under the pointer at its middle, as
    /// near as whole tiles allow.
    /// </summary>
    public static Placement At(Match match, int player, StructureDef structure, Vector2 ground)
    {
        var x = Mathf.RoundToInt(ground.X - (structure.Footprint / 2f));
        var y = Mathf.RoundToInt(ground.Y - (structure.Footprint / 2f));
        return Of(match, player, structure, x, y);
    }

    /// <summary>The site with its lowest tile at (x, y).</summary>
    public static Placement Of(Match match, int player, StructureDef structure, int x, int y)
    {
        var rules = match.Construction;
        var problem = rules.CheckSite(player, structure, x, y);
        return new Placement(x, y, problem, problem == BuildProblem.None && rules.IsTooCloseToEnemyTerritory(player, structure, x, y));
    }
}
