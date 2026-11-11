using Sim.Core;

namespace Sim.Combat;

/// <summary>
/// The hit points of anything that can be damaged, in thousandths. An entity whose hit points are
/// zero or less at the end of a tick's attacks is destroyed (design 2.4).
/// </summary>
public struct Health : IStateValue
{
    /// <summary>The name of the store that holds the hit points of every entity that has them.</summary>
    public const string StoreName = "health";

    /// <summary>
    /// Hit points now, in thousandths. Not held at zero: a unit hit several times in the tick it
    /// dies goes below it, and is removed at the end of that tick.
    /// </summary>
    public long HitPoints { get; set; }

    /// <summary>Hit points when undamaged, in thousandths.</summary>
    public long Maximum { get; set; }

    /// <summary>Full hit points.</summary>
    public static Health Full(long maximum) => new() { HitPoints = maximum, Maximum = maximum };

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        HitPoints = walker.Field(HitPoints);
        Maximum = walker.Field(Maximum);
    }
}
