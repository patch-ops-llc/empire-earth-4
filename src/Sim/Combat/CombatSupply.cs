using Sim.Core;
using Sim.Economy;
using Sim.Map;

namespace Sim.Combat;

/// <summary>
/// The supply a player's fighting units use (design 4.1): for each living unit of a chassis, the
/// supply of its owner's blueprint of that chassis as it is now. Counted from the state each time
/// it is asked, like the rest of <see cref="SupplyAccount"/>, so there is nothing to save or to
/// refresh.
/// </summary>
public sealed class CombatSupply : ISupplyUser
{
    private readonly IPlayerTechSource _tech;
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Combatant> _combatants;

    public CombatSupply(IPlayerTechSource tech, EntityRegistry entities, ComponentStore<Body> bodies, ComponentStore<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(tech);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(combatants);
        _tech = tech;
        _entities = entities;
        _bodies = bodies;
        _combatants = combatants;
    }

    public int SupplyUsedBy(int player)
    {
        var blueprints = _tech.Tech(player).Blueprints;
        var used = 0;
        for (var i = 0; i < _combatants.Count; i++)
        {
            var entity = _combatants.EntityAt(i);
            if (_entities.IsAlive(entity) && _bodies.TryGet(entity, out var body) && body.Owner == player)
            {
                used = checked(used + blueprints[_combatants.ValueAt(i).Chassis].Supply);
            }
        }

        return used;
    }
}
