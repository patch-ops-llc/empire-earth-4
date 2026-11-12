using Sim.Core;
using Sim.Map;

namespace Sim.Construction;

/// <summary>
/// A build order that a worker is carrying out (design 5.4): the structure it is on its way to
/// lay the foundation of, where, and the price its owner paid when the order was given. It is a
/// value of the worker, and is gone when the foundation is laid or the order ends.
/// </summary>
public struct BuildOrder : IStateValue
{
    public const string StoreName = "buildorders";

    /// <summary>The structure, as its <see cref="Data.StructureDef.Index"/>.</summary>
    public int Type { get; set; }

    /// <summary>The x of the lowest tile of the footprint.</summary>
    public int X { get; set; }

    /// <summary>The y of the lowest tile of the footprint.</summary>
    public int Y { get; set; }

    /// <summary>The side of the footprint in tiles.</summary>
    public int Size { get; set; }

    /// <summary>The Materials paid for the order, which it gives back in full when it ends without a foundation.</summary>
    public int Materials { get; set; }

    /// <summary>The Energy paid for the order.</summary>
    public int Energy { get; set; }

    /// <summary>The tiles the structure is to stand on.</summary>
    public readonly TileSquare Tiles => new(X, Y, Size);

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Type = walker.Field(Type);
        X = walker.Field(X);
        Y = walker.Field(Y);
        Size = walker.Field(Size);
        Materials = walker.Field(Materials);
        Energy = walker.Field(Energy);
    }
}
