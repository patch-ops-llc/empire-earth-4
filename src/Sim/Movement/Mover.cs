using Sim.Core;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>What a mover is doing. The numbers are in the state hash and in snapshots, and do not change.</summary>
public enum MoveState
{
    /// <summary>Standing still, with no order.</summary>
    Idle = 0,

    /// <summary>Walking straight to <see cref="Mover.Destination"/>, with nothing in the way.</summary>
    Moving = 1,

    /// <summary>Standing still until its route has been found.</summary>
    Planning = 2,

    /// <summary>Walking along its route, toward the tile at <see cref="Mover.RouteNode"/>.</summary>
    Following = 3,
}

/// <summary>
/// What lets an entity move: its speed, the way it faces, the distance it keeps from other units,
/// and the move order it is carrying out. Its position is in its <see cref="Map.Body"/>.
/// </summary>
public struct Mover : IStateValue
{
    /// <summary>The name of the store that holds every mover.</summary>
    public const string StoreName = "movers";

    /// <summary>
    /// How far a walking unit stays from blocked tiles when it leaves its route for a straight
    /// line: three eighths of a tile. Below half a tile, so that a passage one tile wide is
    /// walked down its middle.
    /// </summary>
    public static Fixed Clearance => Fixed.FromRaw(3 * Fixed.RawOne / 8);

    /// <summary>
    /// How far from blocked tiles an order's destination is kept: seven sixteenths of a tile. A
    /// little more than <see cref="Clearance"/>, so that a unit standing at its destination has a
    /// clear line to walk away on.
    /// </summary>
    public static Fixed StandOff => Fixed.FromRaw(7 * Fixed.RawOne / 16);

    /// <summary>
    /// The spacing of a unit whose line gives none: one tile, the spacing of a worker and of every
    /// line but Cavalry/Armor (design 2.4).
    /// </summary>
    public static Fixed DefaultSpacing => Fixed.One;

    /// <summary>
    /// The distance covered in one tick, in tiles. Zero for a mover that cannot move now. Whoever
    /// places the mover sets it, and movement only reads it. For a unit of a chassis it is the
    /// speed of its owner's blueprint, and <see cref="Research.UnitRefresh"/> sets it again when a
    /// research of the owner finishes; an order under way goes on at the new speed.
    /// </summary>
    public Fixed Speed { get; set; }

    /// <summary>
    /// The direction it last walked in, as (<see cref="Angle.Cos"/>, <see cref="Angle.Sin"/>) in map
    /// coordinates. y grows downward, so a quarter turn faces the bottom of the map.
    /// </summary>
    public Angle Facing { get; set; }

    public MoveState State { get; set; }

    /// <summary>
    /// Where the current order ends: the point that was ordered, moved off any blocked tile it
    /// touches by <see cref="StandOff"/>, or the center of the nearest tile the mover can reach when
    /// it cannot reach that point. Meaningful while <see cref="State"/> is not <see cref="MoveState.Idle"/>.
    /// </summary>
    public FixedVector2 Destination { get; set; }

    /// <summary>
    /// The first tile of its route, as <c>y * width + x</c>. The route itself is not state: it is
    /// what <see cref="RouteFinder"/> finds between this tile and <see cref="RouteGoal"/>. Zero
    /// unless <see cref="State"/> is <see cref="MoveState.Planning"/> or <see cref="MoveState.Following"/>.
    /// </summary>
    public int RouteStart { get; set; }

    /// <summary>The last tile of its route. Zero when it has no route.</summary>
    public int RouteGoal { get; set; }

    /// <summary>The position in its route of the tile it is walking to. Zero when it has no route.</summary>
    public int RouteNode { get; set; }

    /// <summary>
    /// The least distance this unit keeps between its center and another unit's, in tiles (design
    /// 2.4). It comes from the unit's line and does not change. Between two units the larger of
    /// their two values applies: ask <see cref="SpacingRule"/>, never this value alone. Zero for a
    /// mover that keeps no distance.
    /// </summary>
    public Fixed Spacing { get; set; }

    /// <summary>
    /// Where this unit walks beside its route: the offset from the center of a route tile to the
    /// point it aims for there. It is the unit's place in the group that got the order, so that the
    /// group walks as it stood. It shrinks where the ground leaves too little room, and is zero for
    /// a unit that walks alone or has no route.
    /// </summary>
    public FixedVector2 Spread { get; set; }

    /// <summary>
    /// True while the unit has an order and stands still because an enemy unit is in its way and
    /// it found no step round it (design 2.4). It keeps its order and tries again every tick.
    /// Movement sets this for every walking unit, every tick.
    /// </summary>
    public bool Waiting { get; set; }

    /// <summary>
    /// A speed in tiles per second as the distance per tick, rounded once, half up, to the distance
    /// unit, as design 2.3 rules.
    /// </summary>
    public static Fixed SpeedPerTick(Fraction tilesPerSecond) => (tilesPerSecond / SimInfo.TicksPerSecond).ToFixed();

    /// <summary>Starts a straight walk to a point, replacing any order.</summary>
    public void WalkStraightTo(FixedVector2 destination)
    {
        State = MoveState.Moving;
        Destination = destination;
        RouteStart = 0;
        RouteGoal = 0;
        RouteNode = 0;
        Spread = FixedVector2.Zero;
        Waiting = false;
    }

    /// <summary>Starts a walk to a point along the route between two tiles, replacing any order. The route is yet to be found.</summary>
    /// <param name="destination">Where the walk ends. The walk from the center of the route's last tile to it must be clear.</param>
    /// <param name="routeStart">The first tile of the route.</param>
    /// <param name="routeGoal">The last tile of the route.</param>
    /// <param name="spread">How far beside the route the unit walks: see <see cref="Spread"/>.</param>
    public void WalkRouteTo(FixedVector2 destination, int routeStart, int routeGoal, FixedVector2 spread = default)
    {
        State = MoveState.Planning;
        Destination = destination;
        RouteStart = routeStart;
        RouteGoal = routeGoal;
        RouteNode = 0;
        Spread = spread;
        Waiting = false;
    }

    /// <summary>Ends the order, if there is one. The mover stands where it is.</summary>
    public void Stop()
    {
        State = MoveState.Idle;
        RouteStart = 0;
        RouteGoal = 0;
        RouteNode = 0;
        Spread = FixedVector2.Zero;
        Waiting = false;
    }

    public void Walk(IStateWalker walker)
    {
        Speed = walker.Field(Speed);
        Facing = walker.Field(Facing);
        State = (MoveState)walker.Field((int)State);
        Destination = new FixedVector2(walker.Field(Destination.X), walker.Field(Destination.Y));
        RouteStart = walker.Field(RouteStart);
        RouteGoal = walker.Field(RouteGoal);
        RouteNode = walker.Field(RouteNode);
        Spacing = walker.Field(Spacing);
        Spread = new FixedVector2(walker.Field(Spread.X), walker.Field(Spread.Y));
        Waiting = walker.Field(Waiting);
    }
}
