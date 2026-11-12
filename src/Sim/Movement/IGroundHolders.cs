using Sim.Core;

namespace Sim.Movement;

/// <summary>
/// Says which standing units hold their ground, such as a worker at its place or a unit that is
/// fighting. <see cref="SeparationSystem"/> never steps such a unit away from its own side: a
/// unit that is too close to it takes the whole step instead of half.
/// </summary>
/// <remarks>
/// The answer must follow from the state alone, and it is asked only about a unit that stands.
/// It should be true only of units that came to stand where they are with their spacing kept,
/// because two units that hold their ground too close to each other stay so. It changes nothing
/// between enemies, who never push each other anyway.
/// </remarks>
public interface IGroundHolders
{
    /// <summary>True when the unit, standing where it does, holds its ground.</summary>
    bool HoldsGround(EntityId unit);
}
