using Sim.Core;
using Sim.Map;

namespace Sim.Combat;

/// <summary>
/// EXPERIMENT, variant A, not for merging. Notes, after the units have walked and before their
/// own side steps them apart, which fighting units walked in this tick: those that no longer
/// stand where they stood at their last turn to attack. <see cref="AttackSystem"/> then counts
/// only that walk as moving, and not a step that <see cref="Movement.SeparationSystem"/> gives a
/// unit afterwards.
/// </summary>
/// <remarks>
/// The notes are scratch of one tick, made and read within it, so they are no state: nothing of
/// them is in the hash or in a snapshot.
/// </remarks>
public sealed class WalkMarks(ComponentStore<Body> bodies, ComponentStore<Combatant> combatants) : ISystem
{
    private readonly HashSet<EntityId> _walked = [];

    /// <summary>True when the unit walked in this tick.</summary>
    public bool Walked(EntityId unit) => _walked.Contains(unit);

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _walked.Clear();
        for (var i = 0; i < combatants.Count; i++)
        {
            var entity = combatants.EntityAt(i);
            if (bodies.TryGet(entity, out var body) && body.Position != combatants.ValueAt(i).Stood)
            {
                _walked.Add(entity);
            }
        }
    }
}
