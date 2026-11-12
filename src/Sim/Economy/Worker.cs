using Sim.Core;

namespace Sim.Economy;

/// <summary>What a worker is doing about its work. The numbers are in the state hash and in snapshots, and do not change.</summary>
public enum WorkerState
{
    /// <summary>Assigned to no work site.</summary>
    Idle = 0,

    /// <summary>Holding a place at its site and walking to it.</summary>
    Walking = 1,

    /// <summary>Standing at its place, counting the ticks to its next payment.</summary>
    Working = 2,

    /// <summary>Assigned to a site whose places are all taken, and waiting beside it for one.</summary>
    Waiting = 3,

    /// <summary>
    /// Assigned to a site and away from it on an order that keeps the assignment. It holds no
    /// place and has no count of ticks.
    /// </summary>
    Away = 4,
}

/// <summary>
/// What makes an entity a worker (design 4.1): the work site it is assigned to, the tile it holds
/// there and its count of ticks. Its owner and position are in its <see cref="Map.Body"/>.
/// </summary>
public struct Worker : IStateValue
{
    public const string StoreName = "workers";

    public WorkerState State { get; set; }

    /// <summary>The site it is assigned to. None while <see cref="State"/> is <see cref="WorkerState.Idle"/>.</summary>
    public EntityId Site { get; set; }

    /// <summary>
    /// The tile it stands on or walks to, as <c>y * width + x</c>: its place while walking or
    /// working, and the tile it waits on while waiting. Zero while idle or away.
    /// </summary>
    public int Tile { get; set; }

    /// <summary>
    /// The ticks since it arrived at its place or was last due a payment. Zero unless
    /// <see cref="State"/> is <see cref="WorkerState.Working"/>.
    /// </summary>
    public int Timer { get; set; }

    /// <summary>The tick in which it began to wait. Zero unless <see cref="State"/> is <see cref="WorkerState.Waiting"/>.</summary>
    public int WaitingSince { get; set; }

    /// <summary>True while it holds one of its site's places.</summary>
    public readonly bool HoldsPlace => State is WorkerState.Walking or WorkerState.Working;

    public void Walk(IStateWalker walker)
    {
        State = (WorkerState)walker.Field((int)State);
        Site = walker.Field(Site);
        Tile = walker.Field(Tile);
        Timer = walker.Field(Timer);
        WaitingSince = walker.Field(WaitingSince);
    }
}
