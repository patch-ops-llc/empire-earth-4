namespace Sim.Core;

/// <summary>
/// One system's part of the world state. Everything that can change during a match lives in a store
/// registered with the <see cref="World"/>, so that the state hash covers it.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// A name unique among the world's stores. Stores are hashed in the ordinal order of their
    /// names, so the order in which they were registered does not matter.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Drops everything the store holds for entities that were destroyed during the tick. The IDs
    /// are in ascending order. A store that holds nothing per entity does nothing.
    /// </summary>
    void RemoveEntities(ReadOnlySpan<EntityId> destroyed);

    /// <summary>
    /// Feeds every field of the store to the hasher, in an order that depends only on the state:
    /// by ascending entity ID, never in the order of a hash-based collection.
    /// </summary>
    void AddToHash(ref StateHasher hasher);
}

/// <summary>A value that is part of the world state and can feed its fields to the state hash.</summary>
public interface IStateHashable
{
    /// <summary>Feeds every field to the hasher, always in the same order.</summary>
    void AddToHash(ref StateHasher hasher);
}
