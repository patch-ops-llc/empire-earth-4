using Sim.Core;
using Sim.Numerics;

namespace Sim.Perks;

/// <summary>
/// What a unit of a chassis keeps count of for the perks of design 2.3 that need a memory:
/// Regeneration, Reserves and Charge. Every unit of a chassis has one from the moment it is made,
/// whether its chassis carries such a perk or not, so a unit that gains one by a research goes on
/// from its own counts.
/// </summary>
public struct PerkCounts : IStateValue
{
    /// <summary>The name of the store that holds the counts of every unit of a chassis.</summary>
    public const string StoreName = "perkcounts";

    /// <summary>
    /// The tick in which it last took damage; the tick in which it was made while it has taken
    /// none. The ticks since the last damage are the world's tick less this.
    /// </summary>
    public int DamagedAt { get; set; }

    /// <summary>
    /// The Materials paid for it, exactly: the price of its order divided by the units the order
    /// made, so each unit of a pair has half the pair's price. Zero for a unit nobody paid for.
    /// </summary>
    public Fraction Paid { get; set; }

    /// <summary>
    /// The distance it has walked since its last attack, in tiles; since it was made while it has
    /// not attacked. Only the steps of <see cref="Movement.MovementSystem"/> count. It stops at
    /// <see cref="Fixed.MaxValue"/>.
    /// </summary>
    public Fixed Walked { get; set; }

    /// <summary>The counts of a unit made in a tick: no damage taken, nothing paid, nothing walked.</summary>
    public static PerkCounts MadeAt(int tick) => new() { DamagedAt = tick };

    /// <summary>The ticks that have passed since it last took damage, or since it was made.</summary>
    public readonly int TicksSinceDamage(int tick) => tick - DamagedAt;

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        DamagedAt = walker.Field(DamagedAt);
        Paid = walker.Field(Paid);
        Walked = walker.Field(Walked);
        if (DamagedAt < 0 || Paid < Fraction.Zero || Walked.Raw < 0)
        {
            throw new InvalidDataException($"Damage in tick {DamagedAt}, {Paid} Materials paid and {Walked} tiles walked are not the counts of a unit.");
        }
    }
}
