using Sim.Numerics;
using Sim.Vision;

namespace Sim.Tests.Vision;

internal static class VisionAssert
{
    /// <summary>Two grids hold the same for every player and every tile: unexplored, explored or visible.</summary>
    public static void SamePicture(VisionGrid expected, VisionGrid actual)
    {
        Assert.Equal(expected.PlayerCount, actual.PlayerCount);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var expectedStates = new byte[expected.Width * expected.Height];
        var actualStates = new byte[expectedStates.Length];
        for (var player = 0; player < expected.PlayerCount; player++)
        {
            expected.CopyStates(player, expectedStates);
            actual.CopyStates(player, actualStates);
            for (var tile = 0; tile < expectedStates.Length; tile++)
            {
                Assert.True(
                    expectedStates[tile] == actualStates[tile],
                    $"Player {player}, tile ({tile % expected.Width}, {tile / expected.Width}): " +
                    $"expected {(FogState)expectedStates[tile]}, found {(FogState)actualStates[tile]}.");
            }

            Assert.Equal(expected.CountVisible(player), actual.CountVisible(player));
            Assert.Equal(expected.CountExplored(player), actual.CountExplored(player));
        }
    }

    /// <summary>
    /// The tiles a player sees are exactly those whose center is within the sight of one of the
    /// given eyes, the distance itself included.
    /// </summary>
    public static void SeesExactly(VisionGrid grid, int player, params (FixedVector2 Center, int Sight)[] eyes)
    {
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var tile = new FixedVector2(Fixed.FromInt(x) + Fixed.Half, Fixed.FromInt(y) + Fixed.Half);
                var within = eyes.Any(eye =>
                    FixedVector2.DistanceSquaredRaw(eye.Center, tile) <= FixedVector2.SquareRaw(Fixed.FromInt(eye.Sight)));
                Assert.True(within == grid.IsVisible(player, x, y), $"Player {player}, tile ({x}, {y}): expected {(within ? "visible" : "not visible")}.");
            }
        }
    }
}
