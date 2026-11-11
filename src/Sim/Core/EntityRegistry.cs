namespace Sim.Core;

/// <summary>
/// The list of entities that exist, in ascending ID order. An entity is only an ID: its data lives
/// in the stores registered with the <see cref="World"/>.
/// </summary>
/// <remarks>
/// Destroying an entity is settled in two steps. <see cref="Destroy"/> takes effect for
/// <see cref="IsAlive"/> at once. The entity keeps its place in this list and its data in every
/// store until the end of the tick, when <see cref="World.FlushDestroyedEntities"/> removes both.
/// A system can therefore destroy entities while it walks a store by index.
/// </remarks>
public sealed class EntityRegistry
{
    private EntityId[] _ids = new EntityId[64];
    private bool[] _destroyed = new bool[64];
    private EntityId[] _destroyedIds = new EntityId[16];
    private int _count;
    private int _destroyedCount;
    private int _nextId = 1;

    /// <summary>The number of entities in the list, including those destroyed during the current tick.</summary>
    public int Count => _count;

    /// <summary>The number of entities that have not been destroyed.</summary>
    public int AliveCount => _count - _destroyedCount;

    /// <summary>The entity at a position in the list. IDs ascend with the position.</summary>
    public EntityId this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            return _ids[index];
        }
    }

    /// <summary>Creates an entity with the next ID. It goes to the end of the list.</summary>
    public EntityId Create()
    {
        if (_count == _ids.Length)
        {
            Array.Resize(ref _ids, _count * 2);
            Array.Resize(ref _destroyed, _count * 2);
        }

        var id = new EntityId(_nextId);
        _nextId = checked(_nextId + 1);
        _ids[_count] = id;
        _destroyed[_count] = false;
        _count++;
        return id;
    }

    /// <summary>True from the entity's creation until it is destroyed. False for <see cref="EntityId.None"/>.</summary>
    public bool IsAlive(EntityId id)
    {
        var index = IndexOf(id);
        return index >= 0 && !_destroyed[index];
    }

    /// <summary>
    /// Marks an entity as destroyed. Returns false, and changes nothing, if it does not exist or
    /// was already destroyed.
    /// </summary>
    public bool Destroy(EntityId id)
    {
        var index = IndexOf(id);
        if (index < 0 || _destroyed[index])
        {
            return false;
        }

        _destroyed[index] = true;
        _destroyedCount++;
        return true;
    }

    /// <summary>The entities destroyed since the last flush, in ascending ID order.</summary>
    internal ReadOnlySpan<EntityId> CollectDestroyed()
    {
        if (_destroyedIds.Length < _destroyedCount)
        {
            Array.Resize(ref _destroyedIds, Math.Max(_destroyedCount, _destroyedIds.Length * 2));
        }

        var found = 0;
        for (var i = 0; found < _destroyedCount; i++)
        {
            if (_destroyed[i])
            {
                _destroyedIds[found++] = _ids[i];
            }
        }

        return _destroyedIds.AsSpan(0, found);
    }

    /// <summary>Takes the destroyed entities out of the list, keeping the order of the rest.</summary>
    internal void RemoveDestroyed()
    {
        if (_destroyedCount == 0)
        {
            return;
        }

        var kept = 0;
        for (var i = 0; i < _count; i++)
        {
            if (!_destroyed[i])
            {
                _ids[kept++] = _ids[i];
            }
        }

        Array.Clear(_destroyed, 0, _count);
        _count = kept;
        _destroyedCount = 0;
    }

    internal void Walk(IStateWalker walker)
    {
        _nextId = walker.Field(_nextId);

        // An entity is at least its ID and its destroyed flag.
        _count = walker.Count(_count, minimumBytesPerItem: sizeof(int) + sizeof(bool));
        if (_count > _ids.Length)
        {
            var capacity = Math.Max(_count, _ids.Length * 2);
            Array.Resize(ref _ids, capacity);
            Array.Resize(ref _destroyed, capacity);
        }

        for (var i = 0; i < _count; i++)
        {
            _ids[i] = walker.Field(_ids[i]);
            _destroyed[i] = walker.Field(_destroyed[i]);
        }

        if (walker.IsRestoring)
        {
            FinishRestoring();
        }
    }

    /// <summary>Checks what a restore read, and recounts the destroyed entities, which the state does not store.</summary>
    private void FinishRestoring()
    {
        var previous = 0;
        _destroyedCount = 0;
        for (var i = 0; i < _count; i++)
        {
            var id = _ids[i].Value;
            if (id <= previous)
            {
                throw new InvalidDataException($"Entity IDs must be positive and ascend: {id} follows {previous}.");
            }

            previous = id;
            if (_destroyed[i])
            {
                _destroyedCount++;
            }
        }

        if (_nextId <= previous)
        {
            throw new InvalidDataException($"The next entity ID is {_nextId}, which is not above the newest entity, {previous}.");
        }
    }

    private int IndexOf(EntityId id) => Array.BinarySearch(_ids, 0, _count, id);
}
