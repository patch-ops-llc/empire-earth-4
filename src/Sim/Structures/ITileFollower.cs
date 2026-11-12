namespace Sim.Structures;

/// <summary>
/// Something worked out from the structures on the map that must follow them, as the tiles of
/// the grid do. <see cref="StructureTiles"/> tells it each time it has rebuilt the tiles.
/// </summary>
public interface ITileFollower
{
    /// <summary>
    /// The tiles have just been rebuilt from the structures that stand, whether or not a tile
    /// changed. What the follower works out from now is in line with the grid.
    /// </summary>
    void TilesRebuilt();
}
