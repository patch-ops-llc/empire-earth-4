namespace Sim.Vision;

/// <summary>
/// What one player knows of one tile (design 5.3). The numbers are what
/// <see cref="VisionGrid.CopyStates"/> writes, so that a view can use them as they are.
/// </summary>
public enum FogState
{
    /// <summary>Never seen. The player knows nothing of the tile.</summary>
    Unexplored = 0,

    /// <summary>Seen before and not seen now. The player knows its terrain and deposits, and no unit on it.</summary>
    Explored = 1,

    /// <summary>Seen now, as of the last time sight was worked out.</summary>
    Visible = 2,
}
