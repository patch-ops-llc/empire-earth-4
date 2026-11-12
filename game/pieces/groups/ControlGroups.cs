using Sim.Core;

namespace Game;

/// <summary>
/// The player's numbered groups of units. Like the selection they are the player's own business:
/// they are not part of the match, the simulation never sees them, and no other machine learns
/// of them.
/// </summary>
/// <remarks>
/// A unit may stand in several groups at once. A group is counted from 0 here and shown to the
/// player by the digit of its key: the first nine as 1 to 9, the tenth as 0.
/// </remarks>
public sealed class ControlGroups
{
    /// <summary>The number of groups: one for every digit key.</summary>
    public const int Count = 10;

    private readonly SortedSet<EntityId>[] _groups = new SortedSet<EntityId>[Count];

    public ControlGroups()
    {
        for (var group = 0; group < Count; group++)
        {
            _groups[group] = [];
        }
    }

    /// <summary>Raised after a group has changed.</summary>
    public event Action? Changed;

    /// <summary>The digit a group is known by: 1 to 9 for the first nine, 0 for the tenth.</summary>
    public static int DigitOf(int group) => (group + 1) % Count;

    /// <summary>The units of a group in ascending ID order, the order an order lists them in.</summary>
    public IReadOnlyCollection<EntityId> Of(int group) => _groups[group];

    /// <summary>True when a group holds exactly these units, and at least one.</summary>
    public bool Holds(int group, IReadOnlyCollection<EntityId> entities) =>
        _groups[group].Count > 0 && _groups[group].SetEquals(entities);

    /// <summary>Makes a group these units and no others.</summary>
    public void Set(int group, IEnumerable<EntityId> entities)
    {
        var chosen = new SortedSet<EntityId>(entities);
        if (!_groups[group].SetEquals(chosen))
        {
            _groups[group] = chosen;
            Changed?.Invoke();
        }
    }

    /// <summary>Puts these units into a group as well.</summary>
    public void Add(int group, IEnumerable<EntityId> entities)
    {
        var before = _groups[group].Count;
        _groups[group].UnionWith(entities);
        if (_groups[group].Count != before)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Drops from every group each unit the test accepts, such as one that has died.</summary>
    public void RemoveWhere(Predicate<EntityId> gone)
    {
        var removed = 0;
        foreach (var group in _groups)
        {
            removed += group.RemoveWhere(gone);
        }

        if (removed > 0)
        {
            Changed?.Invoke();
        }
    }
}
