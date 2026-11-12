using System.Globalization;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Setup;

namespace Game;

/// <summary>
/// Construction in the words the interface shows: what a structure costs and takes, why the
/// build menu does not offer it, why a site is no good, and how a build order ended.
/// </summary>
public static class BuildWords
{
    /// <summary>A price as short as a menu entry has room for: "150 M", "200 M 50 E".</summary>
    public static string ShortPrice(StructureDef structure) => (structure.Materials, structure.Energy) switch
    {
        (0, 0) => "Free",
        (_, 0) => string.Create(CultureInfo.InvariantCulture, $"{structure.Materials} M"),
        (0, _) => string.Create(CultureInfo.InvariantCulture, $"{structure.Energy} E"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{structure.Materials} M  {structure.Energy} E"),
    };

    /// <summary>What a structure is to place, in a line: its price, the tiles it takes and how long it builds itself.</summary>
    public static string Facts(StructureDef structure)
    {
        var price = structure.Materials == 0 && structure.Energy == 0 ? "Free" : ResearchWords.Price(structure.Materials, structure.Energy);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{price}, {structure.Footprint} by {structure.Footprint} tiles, builds itself in {ResearchWords.Duration(structure.BuildTicks)}");
    }

    /// <summary>
    /// What the second line of a menu entry says: the price while the structure can be ordered or
    /// only lacks resources, and otherwise what is in the way.
    /// </summary>
    public static string EntryLine(BuildChecks checks, StructureDef structure, BuildProblem problem) => problem switch
    {
        BuildProblem.Locked => Needs(checks.Match.Data, structure),
        BuildProblem.LimitReached => string.Create(
            CultureInfo.InvariantCulture,
            $"Limit {checks.CountOf(structure)} of {structure.MaxPerPlayer ?? 0}"),
        _ => ShortPrice(structure),
    };

    /// <summary>Why the menu does not offer a structure now, as a sentence, or null when it does.</summary>
    public static string? WhyNot(BuildChecks checks, StructureDef structure, BuildProblem problem) => problem switch
    {
        BuildProblem.None => null,
        BuildProblem.Locked => $"Locked: {Needs(checks.Match.Data, structure).ToLowerInvariant()}.",
        BuildProblem.LimitReached => string.Create(
            CultureInfo.InvariantCulture,
            $"You have {checks.CountOf(structure)} of the {structure.MaxPerPlayer ?? 0} you may have, counting those under way."),
        BuildProblem.CannotAfford => $"You lack {Lacking(checks.Match, checks.Player, structure)}.",
        _ => "It cannot be built.",
    };

    /// <summary>The research a locked structure waits for: "Needs Manufacturing 2".</summary>
    public static string Needs(GameData data, StructureDef structure)
    {
        var track = structure.Track is { } index && index >= 0 && index < data.Tracks.Count ? data.Tracks[index].Name : "research";
        return string.Create(CultureInfo.InvariantCulture, $"Needs {track} {structure.UnlockTier}");
    }

    /// <summary>What a player lacks of a structure's price: "50 Materials", "20 Materials and 10 Energy".</summary>
    public static string Lacking(Match match, int player, StructureDef structure)
    {
        var materials = Math.Max(0L, structure.Materials - Have(match.Stockpiles.Materials(player)));
        var energy = Math.Max(0L, structure.Energy - Have(match.Stockpiles.Energy(player)));
        return ResearchWords.Price((int)materials, (int)energy);
    }

    /// <summary>Why a site is no good for a structure, as the rest of a sentence, or null when it is good.</summary>
    public static string? SiteProblem(BuildProblem problem) => problem switch
    {
        BuildProblem.None => null,
        BuildProblem.SiteNotFree => "the ground there is not free",
        BuildProblem.OutsideTerritory => "it is outside your territory",
        BuildProblem.NotExplored => "you have not seen all of the ground there",
        BuildProblem.TooCloseToEnemyTerritory => "its territory would overlap an enemy's",
        BuildProblem.EnemyOnSite => "an enemy stands on the site",
        BuildProblem.CannotReach => "the worker cannot reach the site",
        BuildProblem.Locked => "it is locked",
        BuildProblem.LimitReached => "you have as many as you may have",
        BuildProblem.CannotAfford => "you cannot pay for it",
        BuildProblem.NoWorker => "no worker of yours was ordered",
        BuildProblem.NoStructure => "it is not a structure a worker places",
        _ => "it cannot go there",
    };

    /// <summary>The message for a build order the simulation refused when it was given.</summary>
    public static string Refused(BuildProblem problem) =>
        $"The build order was not carried out: {SiteProblem(problem) ?? "it could not be"}.";

    /// <summary>
    /// The message for a build order that ended before its foundation was laid, or null for an
    /// ending the player caused and needs no word about.
    /// </summary>
    public static string? Ended(string structure, BuildProblem reason) => reason switch
    {
        BuildProblem.Cancelled or BuildProblem.OtherOrder or BuildProblem.None => null,
        BuildProblem.WorkerDied => $"The worker sent to build the {structure} died. Its price is back.",
        _ => $"The {structure} was not built: {SiteProblem(reason)}. Its price is back.",
    };

    /// <summary>How far a foundation is, in a line: "Building itself: 40%, 21 s left".</summary>
    public static string Progress(StructureDef structure, int built)
    {
        var total = Math.Max(structure.BuildTicks, 1);
        var done = Math.Clamp(built, 0, total);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Building itself: {done * 100 / total}%, {ResearchWords.Duration(total - done)} left");
    }

    // Whole resources a player can pay with, from the stockpile's thousandths.
    private static long Have(long thousandths) => thousandths / Sim.Economy.Stockpiles.PerResource;
}
