using System.Diagnostics.CodeAnalysis;

namespace Sim.Core;

/// <summary>
/// Commands waiting for the tick they were scheduled for. Whoever orders commands (the host in a
/// network match, the local session in single-player, a replay file) puts them here, and the tick
/// loop takes them out.
/// </summary>
/// <remarks>
/// Commands for the same tick run in the order they were added, and that order must be the same on
/// every machine. Commands for different ticks can be added in any order. The queue is input, not
/// state: it is not part of the state hash, because peers may have received different amounts of
/// the future when they compare hashes.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is the simulation's command queue, and the plan and the documents call it that.")]
public sealed class CommandQueue
{
    private readonly List<(int Tick, Command Command)> _pending = [];

    /// <param name="nextTick">The first tick that has not run yet.</param>
    public CommandQueue(int nextTick = 0) => NextTick = nextTick;

    /// <summary>The earliest tick a command can still be scheduled for.</summary>
    public int NextTick { get; private set; }

    /// <summary>The number of commands waiting.</summary>
    public int Count => _pending.Count;

    /// <summary>
    /// Schedules a command for a tick. Throws if that tick has already run: a command that arrives
    /// too late is an error in whoever ordered it, not something to drop quietly.
    /// </summary>
    public void Enqueue(int tick, in Command command)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tick, NextTick);

        // After every command for the same or an earlier tick, so that order of arrival is kept.
        var index = _pending.Count;
        while (index > 0 && _pending[index - 1].Tick > tick)
        {
            index--;
        }

        _pending.Insert(index, (tick, command));
    }

    /// <summary>
    /// Adds every command that is waiting to the end of <paramref name="destination"/>, each with
    /// its tick, in the order they will run. The queue keeps them. This is how the commands that
    /// go beside a snapshot are collected: the queue is not part of the world state.
    /// </summary>
    public void CopyPendingTo(List<(int Tick, Command Command)> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.AddRange(_pending);
    }

    /// <summary>
    /// Empties the queue and makes <paramref name="nextTick"/> the next tick to run. Restoring a
    /// snapshot does this: what was waiting belonged to the state that was replaced.
    /// </summary>
    internal void Restart(int nextTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nextTick);
        _pending.Clear();
        NextTick = nextTick;
    }

    /// <summary>
    /// Moves the commands scheduled for a tick to the end of <paramref name="destination"/>, in
    /// the order they were added, and closes that tick to new commands. Ticks are taken in order.
    /// </summary>
    public void TakeDue(int tick, List<Command> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNotEqual(tick, NextTick);

        var due = 0;
        while (due < _pending.Count && _pending[due].Tick == tick)
        {
            destination.Add(_pending[due].Command);
            due++;
        }

        _pending.RemoveRange(0, due);
        NextTick = checked(tick + 1);
    }
}
