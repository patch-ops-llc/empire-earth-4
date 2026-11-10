using Sim.Numerics;

namespace Sim.Tests.Numerics;

public class IntegerMathTests
{
    [Theory]
    [InlineData(0UL, 0u)]
    [InlineData(1UL, 1u)]
    [InlineData(2UL, 1u)]
    [InlineData(3UL, 1u)]
    [InlineData(4UL, 2u)]
    [InlineData(15UL, 3u)]
    [InlineData(16UL, 4u)]
    [InlineData(17UL, 4u)]
    [InlineData(999_999UL, 999u)]
    [InlineData(1_000_000UL, 1_000u)]
    [InlineData(4_294_967_295UL, 65_535u)]
    [InlineData(4_294_967_296UL, 65_536u)]
    [InlineData(18_446_744_065_119_617_024UL, 4_294_967_294u)] // (2^32 - 2)^2 + (2^32 - 2) * 2, one below the next square
    [InlineData(18_446_744_065_119_617_025UL, 4_294_967_295u)] // (2^32 - 1)^2
    [InlineData(ulong.MaxValue, 4_294_967_295u)]
    public void SqrtRoundsDown(ulong value, uint expected)
    {
        Assert.Equal(expected, IntegerMath.Sqrt(value));
    }

    [Fact]
    public void SqrtIsExactAroundEverySquareAndAcrossTheRange()
    {
        // Every root up to 2^16, at the square, just below it and just above it.
        for (ulong root = 1; root <= 65_536; root++)
        {
            Assert.Equal(root, IntegerMath.Sqrt(root * root));
            Assert.Equal(root - 1, IntegerMath.Sqrt((root * root) - 1));
            Assert.Equal(root, IntegerMath.Sqrt((root * root) + 1));
        }

        // Roots spread over the whole 32-bit range.
        for (ulong root = 65_537; root <= uint.MaxValue; root += 1_000_003)
        {
            Assert.Equal(root, IntegerMath.Sqrt(root * root));
            Assert.Equal(root - 1, IntegerMath.Sqrt((root * root) - 1));
            Assert.Equal(root, IntegerMath.Sqrt((root * root) + (2 * root)));
        }
    }

    [Fact]
    public void SqrtOfASquaredDistanceIsTheDistanceInRawUnits()
    {
        // A 3-4-5 triangle measured in tiles.
        long dx = Fixed.FromInt(3).Raw;
        long dy = Fixed.FromInt(4).Raw;

        Assert.Equal((uint)Fixed.FromInt(5).Raw, IntegerMath.Sqrt((ulong)((dx * dx) + (dy * dy))));
    }
}
