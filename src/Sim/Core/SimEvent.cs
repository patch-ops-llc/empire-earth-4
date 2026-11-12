using Sim.Numerics;

namespace Sim.Core;

/// <summary>
/// Something that happened in a tick and that the state alone does not show afterwards: a hit, a
/// shot fired, a death. Plain immutable data, for whatever draws or plays the match. Which fields
/// a kind fills is up to the code that adds it, and is written at its <see cref="SimEventKind"/>.
/// </summary>
/// <remarks>
/// An event says everything a reader needs about it. The entities it names may have been removed
/// by the time it is read, since destroyed entities leave the stores at the end of the tick, so an
/// event that has a place carries the place.
/// </remarks>
/// <param name="Kind">What happened.</param>
/// <param name="Tick">The tick it happened in. <see cref="SimEventList.Add"/> sets it.</param>
/// <param name="Player">The player it concerns, or <see cref="NoPlayer"/>.</param>
/// <param name="Entity">The entity it is about, or <see cref="EntityId.None"/>.</param>
/// <param name="Other">A second entity, such as the target of an attack, or <see cref="EntityId.None"/>.</param>
/// <param name="Position">Where it happened, in tiles.</param>
/// <param name="Amount">A number, such as the damage of a hit.</param>
public readonly record struct SimEvent(
    SimEventKind Kind,
    int Tick,
    int Player,
    EntityId Entity,
    EntityId Other,
    FixedVector2 Position,
    long Amount)
{
    /// <summary>The value of <see cref="Player"/> for an event that concerns no player.</summary>
    public const int NoPlayer = -1;
}
