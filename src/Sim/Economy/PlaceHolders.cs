using Sim.Core;
using Sim.Movement;

namespace Sim.Economy;

/// <summary>
/// The workers that hold their ground among their own side (design 2.4, 4.1): every worker that
/// holds a place. A worker earns only while it stands on the center of its place and loses its
/// assignment when it comes to rest anywhere else, so a unit of its own side that stops too close
/// to it steps away alone, and the worker stays.
/// </summary>
/// <remarks>
/// A worker that waits for a place or has no work holds nothing and steps aside like any unit.
/// Enemy units are another matter: they block a worker like any ground unit, and one that stands
/// on or beside a place keeps the worker from reaching it.
/// </remarks>
public sealed class PlaceHolders : IGroundHolders
{
    private readonly ComponentStore<Worker> _workers;

    public PlaceHolders(ComponentStore<Worker> workers)
    {
        ArgumentNullException.ThrowIfNull(workers);
        _workers = workers;
    }

    public bool HoldsGround(EntityId unit) => _workers.TryGet(unit, out var worker) && worker.HoldsPlace;
}
