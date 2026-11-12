using System.Globalization;
using Sim.Construction;
using Sim.Data;
using Sim.Setup;

namespace Game;

/// <summary>
/// Walls in the words the interface shows: what a run costs, why it cannot be laid, what became
/// of a wall order, and what a wall tile is.
/// </summary>
public static class WallWords
{
    /// <summary>A number of tiles: "1 tile", "12 tiles".</summary>
    public static string Tiles(int count) => string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? "tile" : "tiles")}");

    /// <summary>What a run lays and costs, as the tag beside the pointer says it: "12 tiles · 240 Materials".</summary>
    public static string Price(WallRunPlan plan) =>
        plan.AidFor is not null ? $"{Tiles(plan.Laid)} · free"
        : plan.Laid == 0 ? "No tile to lay"
        : $"{Tiles(plan.Laid)} · {ResearchWords.Price(plan.Materials, plan.Energy)}";

    /// <summary>
    /// What else the tag says about a run, or null when the price says it all: why it cannot be
    /// laid, or which of its tiles are left out.
    /// </summary>
    public static string? Note(Match match, WallRunPlan plan)
    {
        if (plan.Problem != BuildProblem.None)
        {
            return Sentence(WhyNot(match, plan));
        }

        if (plan.LeftOut > 0 && plan.Standing > 0)
        {
            return $"{Tiles(plan.LeftOut)} left out: no wall can stand there. {Tiles(plan.Standing)} of wall already.";
        }

        if (plan.LeftOut > 0)
        {
            return $"{Tiles(plan.LeftOut)} left out: no wall can stand there.";
        }

        return plan.Standing > 0 ? $"{Tiles(plan.Standing)} of wall already: nothing to pay there." : null;
    }

    /// <summary>Why the order for a run would be refused, as the rest of a sentence.</summary>
    public static string WhyNot(Match match, WallRunPlan plan) => plan.Problem switch
    {
        BuildProblem.NoStructure => "this match has no walls",
        BuildProblem.Locked => Locked(match),
        BuildProblem.SiteNotFree when plan.Standing == plan.Tiles.Count => "your wall stands on every tile of it already",
        BuildProblem.SiteNotFree => "no wall can stand on any tile of it",
        BuildProblem.OutsideTerritory => "it is outside your territory",
        BuildProblem.NoWorker => "no worker of yours is selected",
        BuildProblem.CannotAfford => $"you lack {ResearchWords.Price(plan.LackingMaterials, plan.LackingEnergy)}",
        _ => "it cannot be laid",
    };

    /// <summary>The message for a wall order the simulation refused when it was given.</summary>
    public static string Refused(Match match, BuildProblem problem) => problem switch
    {
        BuildProblem.NoStructure => "The wall was not laid: this match has no walls.",
        BuildProblem.Locked => $"The wall was not laid: {Locked(match)}.",
        BuildProblem.SiteNotFree => "The wall was not laid: no wall can stand on any of its tiles any more.",
        BuildProblem.OutsideTerritory => "The wall was not laid: none of its tiles can take a wall, and the first is outside your territory.",
        BuildProblem.NoWorker => "The wall was not laid: no worker of yours was there to take the order.",
        BuildProblem.CannotAfford => "The wall was not laid: you could no longer pay for it. Nothing was taken.",
        _ => "The wall was not laid.",
    };

    /// <summary>
    /// The message for tiles of wall that their worker could not lay when it came to them, all
    /// for one reason.
    /// </summary>
    public static string Skipped(int tiles, BuildProblem why)
    {
        var reason = why switch
        {
            BuildProblem.SiteNotFree => "the ground there is not free any more",
            BuildProblem.OutsideTerritory => tiles == 1 ? "it is outside your territory now" : "they are outside your territory now",
            BuildProblem.EnemyOnSite => "an enemy stands there",
            BuildProblem.CannotReach => tiles == 1 ? "the worker cannot get to it" : "the worker cannot get to them",
            BuildProblem.Locked => "walls are locked now",
            _ => tiles == 1 ? "it cannot be laid" : "they cannot be laid",
        };
        return tiles == 1
            ? $"One tile of wall was left out: {reason}. Its price is back."
            : $"{Tiles(tiles)} of wall were left out: {reason}. Their price is back.";
    }

    /// <summary>
    /// The message for a wall order that ended before its last tile, or null for an ending the
    /// player caused and needs no word about.
    /// </summary>
    public static string? Ended(BuildProblem reason) => reason switch
    {
        BuildProblem.Cancelled or BuildProblem.OtherOrder or BuildProblem.None => null,
        BuildProblem.WorkerDied => "The worker laying your wall died. The tiles it had not laid are paid back.",
        _ => "The wall order ended early. The tiles not laid are paid back.",
    };

    /// <summary>The research walls wait for, as the rest of a sentence: "walls need Protection 1".</summary>
    public static string Locked(Match match) =>
        match.Walls.Wall is { } wall ? $"walls need {BuildWords.Needs(match.Data, wall)["Needs ".Length..]}" : "walls are locked";

    private static string Sentence(string words) =>
        words.Length == 0 ? words : string.Concat(char.ToUpperInvariant(words[0]).ToString(), words.AsSpan(1), ".");
}
