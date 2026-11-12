using Sim.Core;
using Sim.Research;

namespace Sim.Combat;

/// <summary>
/// The hit points of anything that can be damaged, in thousandths. An entity whose hit points are
/// zero at the end of a tick's attacks is destroyed (design 2.4).
/// </summary>
/// <remarks>
/// <see cref="HitPoints"/> is always from 0 to <see cref="Maximum"/>. That is the shape the rule
/// for a maximum that changes needs (design 2.3, "Hit points on update": a unit keeps the same
/// fraction of its maximum). When a research finishes, <see cref="UnitRefresh"/> walks this store,
/// gives each of the player's units and workers its new maximum with <see cref="ChangeMaximum"/>,
/// and so scales its hit points with it. The new maximum of a unit of a chassis is its owner's
/// blueprint's; a worker's follows its owner's Protection tier.
/// </remarks>
public struct Health : IStateValue
{
    /// <summary>The name of the store that holds the hit points of every entity that has them.</summary>
    public const string StoreName = "health";

    /// <summary>
    /// Hit points now, in thousandths, never below zero. An entity at zero is still there until the
    /// tick's attacks have all been made.
    /// </summary>
    public long HitPoints { get; set; }

    /// <summary>Hit points when undamaged, in thousandths.</summary>
    public long Maximum { get; set; }

    /// <summary>Full hit points.</summary>
    public static Health Full(long maximum) => new() { HitPoints = maximum, Maximum = maximum };

    /// <summary>
    /// Gives it another maximum, with the same fraction of it as hit points (design 2.3, "Hit
    /// points on update"), as <see cref="HitPointRules.KeepFraction"/> rounds it.
    /// </summary>
    public void ChangeMaximum(long maximum)
    {
        HitPoints = HitPointRules.KeepFraction(HitPoints, Maximum, maximum);
        Maximum = maximum;
    }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        HitPoints = walker.Field(HitPoints);
        Maximum = walker.Field(Maximum);
    }
}
