namespace Sim.Construction;

/// <summary>
/// Why a build order was refused when it was given, or why it ended before its foundation was
/// laid: the <see cref="Core.SimEvent.Amount"/> of a <see cref="Core.SimEventKind.BuildRefused"/>
/// and of a <see cref="Core.SimEventKind.BuildOrderEnded"/>, and what the checks of
/// <see cref="ConstructionRules"/> answer.
/// </summary>
/// <remarks>
/// The numbers are in no state, snapshot or replay, so they may change from one build to the
/// next and nothing may store one.
/// </remarks>
public enum BuildProblem
{
    /// <summary>Nothing is wrong.</summary>
    None = 0,

    /// <summary>The order names nothing a build order can place: no structure with a file, or the wall, which has an order of its own.</summary>
    NoStructure,

    /// <summary>None of the entities the order lists is a living worker of the player.</summary>
    NoWorker,

    /// <summary>The player has not reached the tier the structure needs.</summary>
    Locked,

    /// <summary>
    /// A tile of the footprint is off the map, not open ground, under a deposit or under a
    /// structure. When the order is given, this counts only what the player knows of.
    /// </summary>
    SiteNotFree,

    /// <summary>A tile of the footprint is outside the player's territory, and the structure is neither a Capitol nor an Outpost.</summary>
    OutsideTerritory,

    /// <summary>The structure is a Capitol or an Outpost, and the player has not explored every tile of the footprint.</summary>
    NotExplored,

    /// <summary>The player has as many of the structure as a player may have, counting those under way.</summary>
    LimitReached,

    /// <summary>The stockpile does not cover the price.</summary>
    CannotAfford,

    /// <summary>The territory of the Capitol or Outpost would overlap an enemy's. Found only when the worker arrives.</summary>
    TooCloseToEnemyTerritory,

    /// <summary>An enemy ground unit stood on the site when the worker arrived.</summary>
    EnemyOnSite,

    /// <summary>The worker came to rest without reaching the site: the site lies in ground it cannot reach.</summary>
    CannotReach,

    /// <summary>The player cancelled the order.</summary>
    Cancelled,

    /// <summary>The worker was given another order.</summary>
    OtherOrder,

    /// <summary>The worker died on the way.</summary>
    WorkerDied,
}
