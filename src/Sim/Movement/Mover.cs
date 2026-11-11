using Sim.Core;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>What a mover is doing. The numbers are in the state hash and in snapshots, and do not change.</summary>
public enum MoveState
{
    /// <summary>Standing still, with no order.</summary>
    Idle = 0,

    /// <summary>On its way to <see cref="Mover.Destination"/>.</summary>
    Moving = 1,
}

/// <summary>
/// What lets an entity move: its speed, the way it faces, and the move order it is carrying out.
/// Its position is in its <see cref="Map.Body"/>.
/// </summary>
public struct Mover : IStateValue
{
    /// <summary>The name of the store that holds every mover.</summary>
    public const string StoreName = "movers";

    /// <summary>The distance covered in one tick, in tiles. Zero for a mover that cannot move now.</summary>
    public Fixed Speed { get; set; }

    /// <summary>
    /// The direction it last walked in, as (<see cref="Angle.Cos"/>, <see cref="Angle.Sin"/>) in map
    /// coordinates. y grows downward, so a quarter turn faces the bottom of the map.
    /// </summary>
    public Angle Facing { get; set; }

    public MoveState State { get; set; }

    /// <summary>Where the current order ends. Meaningful while <see cref="State"/> is not <see cref="MoveState.Idle"/>.</summary>
    public FixedVector2 Destination { get; set; }

    /// <summary>
    /// A speed in tiles per second as the distance per tick, rounded once, half up, to the distance
    /// unit, as design 2.3 rules.
    /// </summary>
    public static Fixed SpeedPerTick(Fraction tilesPerSecond) => (tilesPerSecond / SimInfo.TicksPerSecond).ToFixed();

    public void Walk(IStateWalker walker)
    {
        Speed = walker.Field(Speed);
        Facing = walker.Field(Facing);
        State = (MoveState)walker.Field((int)State);
        Destination = new FixedVector2(walker.Field(Destination.X), walker.Field(Destination.Y));
    }
}
