namespace Sim.Core;

/// <summary>
/// A store holding one value of type <typeparamref name="T"/> for each entity that has one, in two
/// parallel arrays kept in ascending entity ID order. Walking it by index visits entities from
/// oldest to newest, the order every system must use.
/// </summary>
/// <remarks>
/// Adding a value for the newest entity appends. Adding or removing anywhere else shifts the
/// values after it, so do not add to or remove from a store while walking it, and do not keep a
/// reference from <see cref="Get"/> or <see cref="ValueAt"/> across an <see cref="Add"/>.
/// Destroying an entity is safe while walking: its values stay in place until the end of the tick.
/// </remarks>
public sealed class ComponentStore<T> : IStateStore
    where T : struct, IStateHashable
{
    private EntityId[] _entities = new EntityId[16];
    private T[] _values = new T[16];
    private int _count;

    public ComponentStore(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public string Name { get; }

    public int Count => _count;

    /// <summary>The entity at a position. IDs ascend with the position.</summary>
    public EntityId EntityAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        return _entities[index];
    }

    /// <summary>The value at a position, by reference so that it can be changed in place.</summary>
    public ref T ValueAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        return ref _values[index];
    }

    public bool Contains(EntityId entity) => IndexOf(entity) >= 0;

    /// <summary>The position of an entity's value, or -1 if it has none.</summary>
    public int IndexOf(EntityId entity)
    {
        var index = Array.BinarySearch(_entities, 0, _count, entity);
        return index < 0 ? -1 : index;
    }

    /// <summary>The entity's value, by reference. Throws if it has none.</summary>
    public ref T Get(EntityId entity)
    {
        var index = IndexOf(entity);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Entity {entity} has no value in store '{Name}'.");
        }

        return ref _values[index];
    }

    public bool TryGet(EntityId entity, out T value)
    {
        var index = IndexOf(entity);
        value = index < 0 ? default : _values[index];
        return index >= 0;
    }

    /// <summary>Gives an entity its value. Throws if it already has one.</summary>
    public void Add(EntityId entity, in T value)
    {
        if (entity == EntityId.None)
        {
            throw new ArgumentException("EntityId.None cannot hold a value.", nameof(entity));
        }

        var index = Array.BinarySearch(_entities, 0, _count, entity);
        if (index >= 0)
        {
            throw new ArgumentException($"Entity {entity} already has a value in store '{Name}'.", nameof(entity));
        }

        index = ~index;
        if (_count == _entities.Length)
        {
            Array.Resize(ref _entities, _count * 2);
            Array.Resize(ref _values, _count * 2);
        }

        Array.Copy(_entities, index, _entities, index + 1, _count - index);
        Array.Copy(_values, index, _values, index + 1, _count - index);
        _entities[index] = entity;
        _values[index] = value;
        _count++;
    }

    /// <summary>Takes an entity's value away at once. Returns false if it had none.</summary>
    public bool Remove(EntityId entity)
    {
        var index = IndexOf(entity);
        if (index < 0)
        {
            return false;
        }

        _count--;
        Array.Copy(_entities, index + 1, _entities, index, _count - index);
        Array.Copy(_values, index + 1, _values, index, _count - index);
        _values[_count] = default;
        return true;
    }

    public void RemoveEntities(ReadOnlySpan<EntityId> destroyed)
    {
        // Both lists ascend, so one pass over the store is enough.
        var kept = 0;
        var next = 0;
        for (var i = 0; i < _count; i++)
        {
            while (next < destroyed.Length && destroyed[next] < _entities[i])
            {
                next++;
            }

            if (next < destroyed.Length && destroyed[next] == _entities[i])
            {
                continue;
            }

            _entities[kept] = _entities[i];
            _values[kept] = _values[i];
            kept++;
        }

        Array.Clear(_values, kept, _count - kept);
        _count = kept;
    }

    public void AddToHash(ref StateHasher hasher)
    {
        hasher.Add(_count);
        for (var i = 0; i < _count; i++)
        {
            hasher.Add(_entities[i]);
            _values[i].AddToHash(ref hasher);
        }
    }
}
