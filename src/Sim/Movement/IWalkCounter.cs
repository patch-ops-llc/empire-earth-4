using Sim.Core;
using Sim.Numerics;

namespace Sim.Movement;

/// <summary>
/// Is told of every step a mover walks, for whoever keeps count of the distance a unit has walked
/// (Charge, design 2.3).
/// </summary>
/// <remarks>
/// Only <see cref="MovementSystem"/> tells it, so only walking counts: the step
/// <see cref="SeparationSystem"/> moves a unit away from its neighbors is not told, nor is a unit
/// that is moved off a building site.
/// </remarks>
public interface IWalkCounter
{
    /// <summary>The unit has walked from one point to another in this tick. The two differ.</summary>
    void Walked(EntityId unit, FixedVector2 origin, FixedVector2 destination);
}
