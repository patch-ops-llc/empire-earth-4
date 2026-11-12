using Sim.Core;

namespace Sim.Production;

/// <summary>
/// One order in the queue of a production structure. Everything here was settled when the order
/// was placed and never changes afterwards (design 2.3, "An order is fixed when it is placed"):
/// what it trains, whether it is one unit or a pair, the price that was paid and the supply that
/// is held for it.
/// </summary>
public struct TrainOrder
{
    /// <summary>What <see cref="Unit"/> is for a worker, which is no chassis.</summary>
    public const int Worker = -1;

    /// <summary>The most units one order makes: a batch is always a pair (design 2.3).</summary>
    public const int MaxCount = 2;

    /// <summary>The chassis, as its <see cref="Data.ChassisDef.Index"/>, or <see cref="Worker"/>.</summary>
    public int Unit { get; set; }

    /// <summary>The units the order makes: 1, or 2 for a pair (design 2.3, Batch).</summary>
    public int Count { get; set; }

    /// <summary>The Materials paid for the whole order, which cancelling gives back.</summary>
    public int Materials { get; set; }

    /// <summary>The Energy paid for the whole order.</summary>
    public int Energy { get; set; }

    /// <summary>The supply held for the whole order until its units appear.</summary>
    public int Supply { get; set; }

    public readonly bool IsWorker => Unit == Worker;

    internal void Walk(IStateWalker walker)
    {
        Unit = walker.Field(Unit);
        Count = walker.Field(Count);
        Materials = walker.Field(Materials);
        Energy = walker.Field(Energy);
        Supply = walker.Field(Supply);
        if (Count is < 0 or > MaxCount || Materials < 0 || Energy < 0 || Supply < 0)
        {
            throw new InvalidDataException($"An order for {Count} units at {Materials} Materials, {Energy} Energy and {Supply} supply is no order.");
        }
    }
}
