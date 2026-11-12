using Sim.Core;

namespace Sim.Economy;

/// <summary>What a work site is. The numbers are in the state hash and in snapshots, and do not change.</summary>
public enum WorkSiteKind
{
    /// <summary>A deposit of the map. Workers there earn Materials for the player whose territory it lies in.</summary>
    Deposit = 0,

    /// <summary>A Generator. Its owner's workers earn Energy there.</summary>
    Generator = 1,
}

/// <summary>
/// A place where workers earn (design 4.1): a deposit or a Generator. It covers a square of tiles,
/// and its places are the tiles that share a side with that square.
/// </summary>
public struct WorkSite : IStateValue
{
    public const string StoreName = "worksites";

    /// <summary>The <see cref="Owner"/> of a deposit: none. Whose it is follows from the territories.</summary>
    public const int NoOwner = -1;

    public WorkSiteKind Kind { get; set; }

    /// <summary>The player a Generator belongs to. <see cref="NoOwner"/> for a deposit.</summary>
    public int Owner { get; set; }

    /// <summary>The x of the lowest tile it covers.</summary>
    public int X { get; set; }

    /// <summary>The y of the lowest tile it covers.</summary>
    public int Y { get; set; }

    /// <summary>The tiles it covers along each side.</summary>
    public int Size { get; set; }

    /// <summary>
    /// The Materials a deposit has delivered to stockpiles, in thousandths, Extraction bonus
    /// included. Zero for a Generator.
    /// </summary>
    public long Delivered { get; set; }

    /// <summary>What workers earn here.</summary>
    public readonly Resource Resource => Kind == WorkSiteKind.Deposit ? Resource.Materials : Resource.Energy;

    /// <summary>The number of tiles that share a side with it: eight for a site of 2 by 2.</summary>
    public readonly int PlaceTileCount => 4 * Size;

    /// <summary>One of the tiles that share a side with it, from 0 to <see cref="PlaceTileCount"/> - 1. The tile may be off the map.</summary>
    public readonly (int X, int Y) PlaceTile(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PlaceTileCount);
        var along = index % Size;
        return (index / Size) switch
        {
            0 => (X + along, Y - 1),
            1 => (X + along, Y + Size),
            2 => (X - 1, Y + along),
            _ => (X + Size, Y + along),
        };
    }

    /// <summary>True when the tile is one it covers.</summary>
    public readonly bool Covers(int x, int y) => x >= X && y >= Y && x < X + Size && y < Y + Size;

    /// <summary>True when the tile shares a side with it.</summary>
    public readonly bool HasPlaceAt(int x, int y) =>
        (x >= X && x < X + Size && (y == Y - 1 || y == Y + Size))
        || (y >= Y && y < Y + Size && (x == X - 1 || x == X + Size));

    public void Walk(IStateWalker walker)
    {
        Kind = (WorkSiteKind)walker.Field((int)Kind);
        Owner = walker.Field(Owner);
        X = walker.Field(X);
        Y = walker.Field(Y);
        Size = walker.Field(Size);
        Delivered = walker.Field(Delivered);
    }
}
