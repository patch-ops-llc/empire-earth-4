using Sim.Core;

namespace Sim.Construction;

/// <summary>
/// The orders of another kind that a worker carries out in place of a build order: the wall
/// orders (design 2.5). A worker has one order at most, of either kind, so the handlers of
/// construction ask both.
/// </summary>
public interface IWorkerOrders
{
    /// <summary>True when the worker is carrying out an order of this kind.</summary>
    bool Has(EntityId worker);

    /// <summary>What ending the worker's order now would give back, in whole resources. Nothing when it has none.</summary>
    (int Materials, int Energy) HeldBy(EntityId worker);

    /// <summary>
    /// Ends the worker's order, gives back what it holds and tells the owner why. The worker
    /// itself is left as it is.
    /// </summary>
    void EndOrder(EntityId worker, BuildProblem why);
}
