using Sim.Data;
using Sim.Numerics;

namespace Sim.Research;

/// <summary>
/// The rules for hit points that depend on research. Hit points are whole numbers of thousandths.
/// </summary>
public static class HitPointRules
{
    /// <summary>How many of the counted units make one hit point.</summary>
    public const long PerHitPoint = 1000;

    /// <summary>
    /// Design 2.3, "Hit points on update": when its maximum changes, a unit or structure keeps the
    /// same fraction of it. The result is rounded once, half up, to a thousandth.
    /// </summary>
    /// <remarks>
    /// A unit at full health stays at full health, exactly. A unit that is alive stays alive: its
    /// hit points never round down to nothing.
    /// </remarks>
    /// <param name="hitPoints">The hit points now, from 0 to <paramref name="oldMaximum"/>.</param>
    /// <param name="oldMaximum">The maximum until now. Above 0.</param>
    /// <param name="newMaximum">The maximum from now on. Above 0.</param>
    public static long KeepFraction(long hitPoints, long oldMaximum, long newMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(oldMaximum);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newMaximum);
        ArgumentOutOfRangeException.ThrowIfNegative(hitPoints);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hitPoints, oldMaximum);

        if (hitPoints == 0 || oldMaximum == newMaximum)
        {
            return hitPoints;
        }

        var scaled = new Fraction((Int128)hitPoints * newMaximum, oldMaximum).RoundHalfUp();
        return Math.Max(1, scaled);
    }

    /// <summary>
    /// The maximum hit points of something that is not a chassis and follows its owner's actual
    /// Protection tier: a worker (design 4.1) or a structure (design 2.5). The base, at
    /// Protection 1, is multiplied once for each tier above the first and rounded once, half up,
    /// to a thousandth.
    /// </summary>
    /// <param name="data">The game data, for the multiplier of one tier.</param>
    /// <param name="baseHitPoints">The hit points at Protection 1, in whole hit points.</param>
    /// <param name="protectionTier">The owner's Protection tier, from 1.</param>
    public static long AtProtectionTier(GameData data, Fraction baseHitPoints, int protectionTier)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfLessThan(protectionTier, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(protectionTier, data.TierCount);
        if (baseHitPoints <= Fraction.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseHitPoints), "Hit points are above 0.");
        }

        var multiplier = data.Rules.PerTier.HitPoints.ToFraction().Pow(protectionTier - 1);
        return (baseHitPoints * multiplier).RoundHalfUp(PerHitPoint);
    }
}
