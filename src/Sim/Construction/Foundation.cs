using Sim.Core;

namespace Sim.Construction;

/// <summary>
/// What a structure has while it builds itself (design 2.5): the ticks it has built for and the
/// price paid for it. A structure is unfinished exactly while it has this value. It blocks its
/// tiles and has its full hit points from the moment it is laid; everything else it gives comes
/// when it is finished, and this value is gone.
/// </summary>
public struct Foundation : IStateValue
{
    public const string StoreName = "foundations";

    /// <summary>
    /// The ticks it has built for. It is finished in the tick in which this reaches the
    /// <see cref="Data.StructureDef.BuildTicks"/> of its structure.
    /// </summary>
    public int Built { get; set; }

    /// <summary>The Materials paid for it, of which cancelling gives a part back.</summary>
    public int Materials { get; set; }

    /// <summary>The Energy paid for it.</summary>
    public int Energy { get; set; }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Built = walker.Field(Built);
        Materials = walker.Field(Materials);
        Energy = walker.Field(Energy);
    }
}
