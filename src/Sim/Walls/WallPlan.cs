using Sim.Core;

namespace Sim.Walls;

/// <summary>
/// One tile of a wall order that is paid for and not yet laid (design 2.5). Each is an entity of
/// its own with nothing but this value: it is on no tile and in nobody's way. The tiles of one
/// order are those with the same worker, and the worker lays them in the order of their IDs.
/// </summary>
/// <remarks>
/// A plan is gone when its tile is laid, when the tile is skipped, and when the order ends. A
/// view draws the wall still to come from this store.
/// </remarks>
public struct WallPlan : IStateValue
{
    public const string StoreName = "wallplans";

    /// <summary>The worker that carries out the order this tile belongs to.</summary>
    public EntityId Worker { get; set; }

    /// <summary>The player who gave the order.</summary>
    public int Owner { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>The Materials paid for this tile, which it gives back in full when it is not laid.</summary>
    public int Materials { get; set; }

    /// <summary>The Energy paid for this tile.</summary>
    public int Energy { get; set; }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Worker = walker.Field(Worker);
        Owner = walker.Field(Owner);
        X = walker.Field(X);
        Y = walker.Field(Y);
        Materials = walker.Field(Materials);
        Energy = walker.Field(Energy);
    }
}
