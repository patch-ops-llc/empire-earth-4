using Sim.Core;
using Sim.Map;

namespace Sim.Structures;

/// <summary>
/// A structure standing on the map (design 2.5): which one of the roster it is and the square of
/// tiles it blocks. Its entity also has a <see cref="Body"/> at the center of that square, which
/// names its owner, and its hit points. What a finished structure gives, such as a territory or a
/// Generator's work site, is on the same entity in the store of that thing.
/// </summary>
public struct PlacedStructure : IStateValue
{
    public const string StoreName = "structures";

    /// <summary>Its place in the roster, as in <see cref="Data.StructureDef.Index"/>.</summary>
    public int Type { get; set; }

    /// <summary>The x of the lowest tile it covers.</summary>
    public int X { get; set; }

    /// <summary>The y of the lowest tile it covers.</summary>
    public int Y { get; set; }

    /// <summary>The tiles it covers along each side.</summary>
    public int Size { get; set; }

    /// <summary>The tiles it covers.</summary>
    public readonly TileSquare Tiles => new(X, Y, Size);

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Type = walker.Field(Type);
        X = walker.Field(X);
        Y = walker.Field(Y);
        Size = walker.Field(Size);
    }
}
