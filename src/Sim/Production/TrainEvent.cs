using Sim.Core;
using Sim.Numerics;

namespace Sim.Production;

/// <summary>
/// One thing production reports, as whatever shows the match reads it from
/// <see cref="Simulation.Events"/>: an order placed, refused, cancelled or refunded, a unit that
/// appeared, a rally point set. <see cref="TryRead"/> takes it out of a <see cref="SimEvent"/>.
/// </summary>
/// <param name="Kind">One of the kinds production adds: see <see cref="IsProduction"/>.</param>
/// <param name="Tick">The tick it happened in.</param>
/// <param name="Player">The owner of the structure; for an order refused, the player the command names.</param>
/// <param name="Structure">The structure. For an order refused, the first entity the command lists, which may be none or no structure.</param>
/// <param name="Unit">What is trained: a chassis by its index, or <see cref="TrainOrder.Worker"/>. For an order refused, what the command asked for. Nothing, and zero, for a rally point.</param>
/// <param name="Count">The units of the order, 1 or 2. For a unit that appeared, 1. Zero for an order refused and for a rally point.</param>
/// <param name="Reason">Why an order was refused. <see cref="TrainCheck.Allowed"/> for every other kind.</param>
/// <param name="Trained">The unit that appeared, for <see cref="SimEventKind.UnitTrained"/>. None otherwise.</param>
/// <param name="Position">
/// Where the unit appeared, for <see cref="SimEventKind.UnitTrained"/>; the rally point, for
/// <see cref="SimEventKind.RallyPointSet"/>; the center of the structure for the others, and
/// nothing for an order refused.
/// </param>
public readonly record struct TrainEvent(
    SimEventKind Kind,
    int Tick,
    int Player,
    EntityId Structure,
    int Unit,
    int Count,
    TrainCheck Reason,
    EntityId Trained,
    FixedVector2 Position)
{
    private const int CountShift = 32;
    private const int ReasonShift = 40;
    private const long ByteMask = 0xFF;

    /// <summary>True for the kinds of event production adds.</summary>
    public static bool IsProduction(SimEventKind kind) => kind is SimEventKind.RallyPointSet
        or SimEventKind.TrainCancelled
        or SimEventKind.TrainOrdered
        or SimEventKind.TrainRefunded
        or SimEventKind.TrainRefused
        or SimEventKind.UnitTrained;

    /// <summary>Reads one of production's events. False, with nothing read, for an event of another kind.</summary>
    public static bool TryRead(in SimEvent simEvent, out TrainEvent train)
    {
        if (!IsProduction(simEvent.Kind))
        {
            train = default;
            return false;
        }

        var appeared = simEvent.Kind == SimEventKind.UnitTrained;
        train = new TrainEvent(
            simEvent.Kind,
            simEvent.Tick,
            simEvent.Player,
            appeared ? simEvent.Other : simEvent.Entity,
            unchecked((int)simEvent.Amount),
            (int)((simEvent.Amount >> CountShift) & ByteMask),
            (TrainCheck)((simEvent.Amount >> ReasonShift) & ByteMask),
            appeared ? simEvent.Entity : EntityId.None,
            simEvent.Position);
        return true;
    }

    /// <summary>What an event's <see cref="SimEvent.Amount"/> holds: the unit, the count and the reason, each in bits of its own.</summary>
    internal static long Amount(int unit, int count = 0, TrainCheck reason = TrainCheck.Allowed) =>
        unchecked((uint)unit) + ((long)count << CountShift) + ((long)reason << ReasonShift);
}
