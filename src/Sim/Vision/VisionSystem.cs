using Sim.Core;

namespace Sim.Vision;

/// <summary>
/// Works out what every player sees, in every tick whose number is a multiple of
/// <see cref="MatchVision.RefreshTicks"/> (design 5.3: every 4 ticks, so what a player sees may
/// be up to a fifth of a second old).
/// </summary>
/// <remarks>
/// It runs after movement and the spatial index, so that it sees where everything stands in this
/// tick, and before the shots and the attacks, so that a unit picks its target from what its
/// owner sees in this tick.
/// </remarks>
public sealed class VisionSystem : ISystem
{
    private readonly MatchVision _vision;

    public VisionSystem(MatchVision vision)
    {
        ArgumentNullException.ThrowIfNull(vision);
        _vision = vision;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Tick % _vision.RefreshTicks == 0)
        {
            _vision.Refresh();
        }
    }
}
