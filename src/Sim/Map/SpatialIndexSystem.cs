using Sim.Core;

namespace Sim.Map;

/// <summary>
/// Rebuilds the <see cref="SpatialIndex"/> once a tick. It runs right after movement, so that every
/// system after it sees where things stand now.
/// </summary>
public sealed class SpatialIndexSystem : ISystem
{
    private readonly SpatialIndex _index;

    public SpatialIndexSystem(SpatialIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
    }

    public void Run(World world) => _index.Rebuild();
}
