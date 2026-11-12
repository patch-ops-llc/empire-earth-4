namespace Sim.Combat;

/// <summary>
/// The order a unit is carrying out (design 2.4, "Orders"). A new order replaces the old one at
/// once; there is no queue. The numbers are in the state hash and in snapshots, and do not change.
/// </summary>
public enum UnitOrder
{
    /// <summary>No order. The unit guards its post, the point where it became idle, and picks its own targets.</summary>
    Idle = 0,

    /// <summary>Walking to a point. It picks no target and does not attack, even when attacked.</summary>
    Move = 1,

    /// <summary>Walking to a point and fighting what it picks on the way.</summary>
    AttackMove = 2,

    /// <summary>Going after one named enemy until it is destroyed. The target is never swapped.</summary>
    Attack = 3,
}
