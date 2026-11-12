using System.Collections;
using Sim.Numerics;

namespace Sim.Core;

/// <summary>
/// The events of one tick, in the order they were added (<see cref="Simulation.Events"/>). The
/// tick empties the list before it runs its commands, and command handlers and systems add to it.
/// After a tick it holds what happened in that tick, until the next tick starts.
/// </summary>
/// <remarks>
/// <para>
/// <b>The list is output, not state.</b> It is not walked, so it is in neither the state hash nor
/// a snapshot, and restoring a snapshot empties it. Two machines in step hold the same events in
/// the same order, but nothing checks that, and nothing may depend on it.
/// </para>
/// <para>
/// <b>Nothing in the simulation reads the list.</b> A handler or a system only calls
/// <see cref="Add"/>. What a rule needs to know goes in a store, where the hash and a snapshot
/// have it. Reading is for what stands outside: the view, sound, tools and tests. Adding an event
/// draws nothing from the random generator and changes nothing in the world, so a match runs the
/// same whether or not anything is reported.
/// </para>
/// </remarks>
public sealed class SimEventList : IReadOnlyList<SimEvent>
{
    private readonly World _world;
    private readonly List<SimEvent> _events = [];

    internal SimEventList(World world) => _world = world;

    public int Count => _events.Count;

    public SimEvent this[int index] => _events[index];

    /// <summary>
    /// Adds an event, with the tick the world is at. An event added between ticks is gone when the
    /// next tick starts.
    /// </summary>
    public void Add(
        SimEventKind kind,
        int player = SimEvent.NoPlayer,
        EntityId entity = default,
        EntityId other = default,
        FixedVector2 position = default,
        long amount = 0)
    {
        if (kind == SimEventKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An event needs a kind.");
        }

        _events.Add(new SimEvent(kind, _world.Tick, player, entity, other, position, amount));
    }

    public List<SimEvent>.Enumerator GetEnumerator() => _events.GetEnumerator();

    IEnumerator<SimEvent> IEnumerable<SimEvent>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void Clear() => _events.Clear();
}
