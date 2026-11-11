namespace Sim.Core;

/// <summary>
/// One system's part of the world state. Everything that can change during a match lives in a store
/// registered with the <see cref="World"/>, so that the state hash and a snapshot cover it.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// A name unique among the world's stores. Stores are walked in the ordinal order of their
    /// names, so the order in which they were registered does not matter. A snapshot records the
    /// names and can only be restored into a world that has the same ones.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Drops everything the store holds for entities that were destroyed during the tick. The IDs
    /// are in ascending order. A store that holds nothing per entity does nothing.
    /// </summary>
    void RemoveEntities(ReadOnlySpan<EntityId> destroyed);

    /// <summary>
    /// Hands every field of the store to the walker and takes back the value it returns, in an
    /// order that depends only on the state: by ascending entity ID, never in the order of a
    /// hash-based collection. This one method is the store's part of the state hash, of a snapshot
    /// and of a restore; see <see cref="IStateWalker"/>.
    /// </summary>
    void Walk(IStateWalker walker);
}

/// <summary>
/// A value that is part of the world state: the per-entity data a <see cref="ComponentStore{T}"/>
/// holds. It hands its fields to the walker as a store does.
/// </summary>
public interface IStateValue
{
    /// <summary>
    /// Hands every field to the walker, always in the same order, and takes back the value it
    /// returns: <c>Speed = walker.Field(Speed);</c>.
    /// </summary>
    void Walk(IStateWalker walker);
}
