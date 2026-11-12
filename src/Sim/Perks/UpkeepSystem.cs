using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Map;

namespace Sim.Perks;

/// <summary>
/// Regeneration and Reserves (design 2.3), for every unit of a chassis, oldest first. A unit has
/// a perk when its owner's blueprint of its chassis carries it in this tick, so a research that
/// finished gives or takes the perk for the units that already stand.
/// </summary>
/// <remarks>
/// <para>
/// <b>Regeneration.</b> "2% of maximum hit points, added once per second, after 5 full seconds
/// without damage. Each unit counts its own ticks from the last damage taken: the first
/// regeneration comes 5 seconds after it and one more follows each second." The share, rounded
/// half up to the thousandth of a hit point, is added in the tick that comes the delay after the
/// tick of the last damage, and in every tick a whole number of seconds after that one, never
/// above the maximum. A unit that has taken no damage counts from the tick it was made. Damage
/// of this tick is counted before this system runs, so a unit hit in the tick its regeneration
/// was due gets none. Each addition is reported as a <see cref="SimEventKind.Regenerated"/>.
/// </para>
/// <para>
/// <b>Reserves.</b> "The refund is 25% of the Materials actually paid for that unit, rounded half
/// up. The unit must carry Reserves at the moment it dies." A unit that died in this tick, its
/// hit points at zero, gives its owner the share of <see cref="PerkCounts.Paid"/>, rounded half up
/// to whole Materials. A unit nobody paid for gives nothing. Each refund is reported as a
/// <see cref="SimEventKind.ReservesRefunded"/>.
/// </para>
/// <para>
/// It runs after the deaths: a destroyed unit is still in its stores until the tick ends, which
/// is how it is found here, exactly once, and a unit that died in this tick does not regenerate.
/// It runs before income and research, so the stockpile has the refund when the tick ends and a
/// research that finishes in this tick changes the perks from the next.
/// </para>
/// </remarks>
public sealed class UpkeepSystem : ISystem
{
    private readonly IPlayerTechSource _tech;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Health> _health;
    private readonly ComponentStore<PerkCounts> _counts;
    private readonly Stockpiles _stockpiles;
    private readonly SimEventList _events;

    public UpkeepSystem(
        IPlayerTechSource tech,
        ComponentStore<Body> bodies,
        ComponentStore<Combatant> combatants,
        ComponentStore<Health> health,
        ComponentStore<PerkCounts> counts,
        Stockpiles stockpiles,
        SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(tech);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(stockpiles);
        ArgumentNullException.ThrowIfNull(events);
        _tech = tech;
        _bodies = bodies;
        _combatants = combatants;
        _health = health;
        _counts = counts;
        _stockpiles = stockpiles;
        _events = events;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (var i = 0; i < _counts.Count; i++)
        {
            var unit = _counts.EntityAt(i);
            var healthIndex = _health.IndexOf(unit);
            if (healthIndex < 0 || !_combatants.TryGet(unit, out var combatant) || !_bodies.TryGet(unit, out var body))
            {
                continue;
            }

            var perks = _tech.Tech(body.Owner).Blueprints[combatant.Chassis].Perks;
            ref var health = ref _health.ValueAt(healthIndex);
            if (!world.Entities.IsAlive(unit))
            {
                if (perks.DeathRefund is { } refund && health.HitPoints <= 0)
                {
                    Refund(unit, body, refund.Share.ToFraction() * _counts.ValueAt(i).Paid);
                }
            }
            else if (perks.Regeneration is { } regeneration && health.HitPoints < health.Maximum)
            {
                var sinceDelay = _counts.ValueAt(i).TicksSinceDamage(world.Tick) - regeneration.DelayTicks;
                if (sinceDelay >= 0 && sinceDelay % SimInfo.TicksPerSecond == 0)
                {
                    var added = Math.Min(
                        (regeneration.SharePerSecond.ToFraction() * health.Maximum).RoundHalfUp(),
                        health.Maximum - health.HitPoints);
                    health.HitPoints += added;
                    _events.Add(SimEventKind.Regenerated, body.Owner, unit, position: body.Position, amount: added);
                }
            }
        }
    }

    private void Refund(EntityId unit, Body body, Numerics.Fraction share)
    {
        var materials = share.RoundHalfUp();
        if (materials <= 0)
        {
            return;
        }

        _stockpiles.Add(body.Owner, checked((int)materials), 0);
        _events.Add(SimEventKind.ReservesRefunded, body.Owner, unit, position: body.Position, amount: materials);
    }
}
