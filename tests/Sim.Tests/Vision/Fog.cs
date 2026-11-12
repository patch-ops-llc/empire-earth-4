using Sim.Numerics;
using Sim.Setup;

namespace Sim.Tests.Vision;

/// <summary>For the fixtures of tests that are not about the fog of war.</summary>
internal static class Fog
{
    /// <summary>
    /// Lets every player have explored the whole map, and see all of it until sight is next
    /// worked out, which the first tick does. For tests of rules that ask what a player knows of
    /// the ground, such as where a structure may be ordered, and are not about how it came to
    /// know it.
    /// </summary>
    public static Match Explored(this Match match)
    {
        var grid = match.Vision.Grid;
        var all = Fixed.FromInt(grid.Width + grid.Height);
        for (var player = 0; player < grid.PlayerCount; player++)
        {
            grid.Reveal(player, default, all);
        }

        return match;
    }
}
