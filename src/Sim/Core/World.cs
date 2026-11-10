namespace Sim.Core;

/// <summary>
/// The whole state of a match: the tick number, the random generator, the entities and the state
/// stores. Nothing that changes during a match lives anywhere else, and there are no statics.
/// </summary>
public sealed class World
{
    private readonly List<IStateStore> _stores = [];

    public World(ulong seed) => Random = new DeterministicRandom(seed);

    /// <summary>The number of ticks that have been run. The tick being run has this number.</summary>
    public int Tick { get; private set; }

    public DeterministicRandom Random { get; }

    public EntityRegistry Entities { get; } = new();

    /// <summary>The registered stores, in the ordinal order of their names.</summary>
    public IReadOnlyList<IStateStore> Stores => _stores;

    /// <summary>
    /// Registers a store and returns it. Register every store before the first tick, and the same
    /// set on every machine. The order of registration does not matter.
    /// </summary>
    public TStore AddStore<TStore>(TStore store)
        where TStore : IStateStore
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(store.Name, nameof(store));

        var index = 0;
        while (index < _stores.Count)
        {
            var order = string.CompareOrdinal(_stores[index].Name, store.Name);
            if (order == 0)
            {
                throw new ArgumentException($"A store named '{store.Name}' is already registered.", nameof(store));
            }

            if (order > 0)
            {
                break;
            }

            index++;
        }

        _stores.Insert(index, store);
        return store;
    }

    /// <summary>
    /// Removes the entities destroyed since the last flush from the entity list and from every
    /// store. The tick loop calls this at the end of each tick.
    /// </summary>
    public void FlushDestroyedEntities()
    {
        var destroyed = Entities.CollectDestroyed();
        if (destroyed.IsEmpty)
        {
            return;
        }

        foreach (var store in _stores)
        {
            store.RemoveEntities(destroyed);
        }

        Entities.RemoveDestroyed();
    }

    /// <summary>
    /// The 64-bit hash of the whole state: tick, random generator, entities, then each store in name
    /// order. Two worlds with the same hash are, for practical purposes, in the same state. Peers
    /// compare this value to detect a desync.
    /// </summary>
    public ulong ComputeHash()
    {
        var hasher = default(StateHasher);
        hasher.Add(Tick);
        hasher.Add(Random.State);
        Entities.AddToHash(ref hasher);
        hasher.Add(_stores.Count);
        foreach (var store in _stores)
        {
            store.AddToHash(ref hasher);
        }

        return hasher.Finish();
    }

    internal void AdvanceTick() => Tick = checked(Tick + 1);
}
