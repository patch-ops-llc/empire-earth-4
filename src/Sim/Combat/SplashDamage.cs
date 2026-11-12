using Sim.Core;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// The splash of a hit (design 2.3, Overgunned II): every hit of a unit that has it also deals a
/// share of its damage to every other enemy within a distance of the target.
/// </summary>
/// <remarks>
/// <para>
/// The share is taken of the attacker's side of the hit, before the counter multiplier, and each
/// enemy the splash reaches applies its own: its counter multiplier, Hardened I, since splash is
/// area damage, plating, and for a structure the fortification rule. The distance is measured from
/// the target's center when the hit lands, to the center of a unit and of a structure alike. It
/// reaches only what the attacker's line can hit, and never the attacker's own side.
/// </para>
/// <para>
/// The attacker's side is fixed when it attacks: a shot carries the share and the distance its
/// attacker had when it fired. A shot that is lost splashes nobody, and a hit splashes when its
/// target is brought to zero by it all the same.
/// </para>
/// </remarks>
internal static class SplashDamage
{
    /// <summary>Lands the splash of a hit that has just landed, and reports it.</summary>
    /// <param name="world">The world.</param>
    /// <param name="targeting">The match's targeting.</param>
    /// <param name="bodies">The store of bodies.</param>
    /// <param name="events">Where the <see cref="SimEventKind.Splash"/> and each <see cref="SimEventKind.SplashHit"/> go.</param>
    /// <param name="reached">Scratch: emptied and filled here.</param>
    /// <param name="hit">The hit that splashes.</param>
    public static void Land(
        World world, Targeting targeting, ComponentStore<Body> bodies, SimEventList events, List<EntityId> reached, in SplashingHit hit)
    {
        events.Add(SimEventKind.Splash, hit.Owner, hit.Source, hit.Target, hit.Center, hit.Radius.Raw);
        targeting.ReachedBySplash(world, hit.Owner, hit.Line, hit.Target, hit.Center, hit.Radius, reached);
        var side = hit.Damage * hit.Share;
        foreach (var entity in reached)
        {
            var damage = targeting.Hit(entity, side, hit.Line, hit.Firepower, area: true);
            events.Add(SimEventKind.SplashHit, hit.Owner, entity, hit.Source, bodies.Get(entity).Position, damage);
        }
    }
}

/// <summary>A hit that splashes: the attacker's side of it, and where it landed.</summary>
/// <param name="Owner">The attacker's owner.</param>
/// <param name="Source">The shot that landed, or the attacker of a melee hit.</param>
/// <param name="Target">What the hit landed on, which the splash leaves out.</param>
/// <param name="Center">The target's center when the hit landed.</param>
/// <param name="Radius">How far the splash reaches from there.</param>
/// <param name="Damage">The attacker's damage of the hit, before anything a target applies.</param>
/// <param name="Share">The share of it that the splash deals.</param>
/// <param name="Line">The attacker's line.</param>
/// <param name="Firepower">The attacker's effective Firepower when it attacked.</param>
internal readonly record struct SplashingHit(
    int Owner, EntityId Source, EntityId Target, FixedVector2 Center, Fixed Radius, Fraction Damage, Fraction Share, int Line, int Firepower);
