using Sim.Core;
using Sim.Map;

namespace Sim.Combat;

/// <summary>
/// Notes which fighting units have walked in this tick, for the rule that a unit attacks only in
/// a tick in which it does not walk (design 2.4). It runs after <see cref="Movement.MovementSystem"/>
/// and before <see cref="Movement.SeparationSystem"/>, and a unit has walked when it no longer
/// stands where it stood at its last turn to attack (<see cref="Combatant.Stood"/>).
/// </summary>
/// <remarks>
/// <para>
/// Where it runs is what makes the rule: a walk is a step a unit takes under an order or toward
/// a target, and those are the steps taken before this system. The step that its own side gives
/// a unit afterwards, to keep their spacing, is no walk, and <see cref="AttackSystem"/> does not
/// see it as one. Anything that moves a unit after the attacks of a tick is seen as a walk in
/// the next.
/// </para>
/// <para>
/// The notes are scratch data of one tick: made from the state, read by the attack system of
/// the same tick, and thrown away when this system runs again. They are in neither the state
/// hash nor a snapshot, and a match that is restored between two ticks needs none of them.
/// </para>
/// </remarks>
public sealed class WalkMarks : ISystem
{
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly HashSet<EntityId> _walked = [];

    public WalkMarks(ComponentStore<Body> bodies, ComponentStore<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(combatants);
        _bodies = bodies;
        _combatants = combatants;
    }

    /// <summary>True when the unit has walked in this tick.</summary>
    public bool Walked(EntityId unit) => _walked.Contains(unit);

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _walked.Clear();
        for (var i = 0; i < _combatants.Count; i++)
        {
            var entity = _combatants.EntityAt(i);
            if (_bodies.TryGet(entity, out var body) && body.Position != _combatants.ValueAt(i).Stood)
            {
                _walked.Add(entity);
            }
        }
    }
}
