using Sim.Core;

namespace Game;

/// <summary>
/// The entities the player has selected. This is the player's own business: it is not part of the
/// match, the simulation never sees it, and no other machine learns of it.
/// </summary>
public sealed class Selection
{
    private readonly SortedSet<EntityId> _entities = [];

    /// <summary>Raised after the selection has changed.</summary>
    public event Action? Changed;

    public int Count => _entities.Count;

    /// <summary>The selected entities in ascending ID order, the order an order lists them in.</summary>
    public IReadOnlyCollection<EntityId> Entities => _entities;

    public bool Contains(EntityId entity) => _entities.Contains(entity);

    /// <summary>Selects these entities and no others.</summary>
    public void Set(IEnumerable<EntityId> entities)
    {
        var chosen = new SortedSet<EntityId>(entities);
        if (!_entities.SetEquals(chosen))
        {
            _entities.Clear();
            _entities.UnionWith(chosen);
            Raise();
        }
    }

    /// <summary>Selects these entities as well.</summary>
    public void Add(IEnumerable<EntityId> entities)
    {
        var before = _entities.Count;
        _entities.UnionWith(entities);
        if (_entities.Count != before)
        {
            Raise();
        }
    }

    /// <summary>Selects an entity that is not selected, and the other way round.</summary>
    public void Toggle(EntityId entity)
    {
        if (!_entities.Remove(entity))
        {
            _entities.Add(entity);
        }

        Raise();
    }

    public void Clear()
    {
        if (_entities.Count > 0)
        {
            _entities.Clear();
            Raise();
        }
    }

    /// <summary>Drops every entity the test accepts, such as one that has died.</summary>
    public void RemoveWhere(Predicate<EntityId> gone)
    {
        if (_entities.RemoveWhere(gone) > 0)
        {
            Raise();
        }
    }

    private void Raise()
    {
        if (Changed is null)
        {
            return;
        }

        if (!FrameClock.On)
        {
            Changed.Invoke();
            return;
        }

        // For the frame test each listener is called and timed by itself, in the same order.
        foreach (var listener in Changed.GetInvocationList())
        {
            var span = FrameClock.Begin();
            ((Action)listener).Invoke();

            // A listener written in place belongs to the class it is written in.
            var owner = listener.Method.DeclaringType;
            while (owner is { IsNested: true, DeclaringType: not null })
            {
                owner = owner.DeclaringType;
            }

            FrameClock.End("selection", owner ?? typeof(Selection), span);
        }
    }
}
