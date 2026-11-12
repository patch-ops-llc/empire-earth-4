using Sim.Core;
using Sim.Economy;
using Sim.Movement;
using Sim.Setup;

namespace Game;

/// <summary>
/// Finds a player's idle workers: those assigned to no work site that stand still with no order.
/// A worker on its way somewhere is not idle, and neither is one that waits for a place at its
/// site or is away from it on an order that keeps the assignment.
/// </summary>
public static class IdleWorkers
{
    public static bool IsIdle(Match match, EntityId entity) =>
        match.Workers.TryGet(entity, out var worker)
        && worker.State == WorkerState.Idle
        && match.Movers.TryGet(entity, out var mover)
        && mover.State == MoveState.Idle;

    /// <summary>A player's idle workers in ascending ID order.</summary>
    public static void Find(Match match, int player, List<EntityId> found)
    {
        found.Clear();
        var workers = match.Workers;
        for (var i = 0; i < workers.Count; i++)
        {
            var entity = workers.EntityAt(i);
            if (match.Bodies.TryGet(entity, out var body) && body.Owner == player && IsIdle(match, entity))
            {
                found.Add(entity);
            }
        }

        found.Sort();
    }

    /// <summary>
    /// The idle worker that follows another in ID order, and the first of them after the last.
    /// <see cref="EntityId.None"/> when the player has no idle worker.
    /// </summary>
    /// <param name="idle">The idle workers in ascending ID order, as <see cref="Find"/> gives them.</param>
    /// <param name="after">The worker shown last, or <see cref="EntityId.None"/> to begin with the first.</param>
    public static EntityId Next(IReadOnlyList<EntityId> idle, EntityId after)
    {
        foreach (var entity in idle)
        {
            if (entity > after)
            {
                return entity;
            }
        }

        return idle.Count > 0 ? idle[0] : EntityId.None;
    }
}
