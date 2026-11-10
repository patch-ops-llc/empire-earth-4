using System.Numerics;
using Sim.Core;
using Sim.Numerics;

namespace Sim.Tests.Numerics;

public class ArcTangentTests
{
    private const int Bits = 200;

    /// <summary>How far from a halfway point, in hundredths of a unit, the rounding of the sine table may decide.</summary>
    private const int ToleranceInHundredths = 1;

    private static readonly BigInteger Pi = 4 * ((4 * ArcTangentOfReciprocal(5)) - ArcTangentOfReciprocal(239));

    private static Angle Atan2(int rawY, int rawX) => Angle.Atan2(Fixed.FromRaw(rawY), Fixed.FromRaw(rawX));

    [Fact]
    public void TheAxesAndDiagonalsAreExact()
    {
        Assert.Equal(0, Atan2(0, 1).Units);
        Assert.Equal(512, Atan2(1, 1).Units);
        Assert.Equal(1024, Atan2(1, 0).Units);
        Assert.Equal(1536, Atan2(1, -1).Units);
        Assert.Equal(2048, Atan2(0, -1).Units);
        Assert.Equal(2560, Atan2(-1, -1).Units);
        Assert.Equal(3072, Atan2(-1, 0).Units);
        Assert.Equal(3584, Atan2(-1, 1).Units);

        Assert.Equal(Angle.QuarterTurn, Angle.Atan2(Fixed.FromInt(96), Fixed.Zero));
        Assert.Equal(Angle.FromUnits(512), Angle.Atan2(Fixed.FromInt(96), Fixed.FromInt(96)));
        Assert.Equal(Angle.HalfTurn, Angle.Atan2(Fixed.Zero, Fixed.FromInt(-96)));
    }

    [Fact]
    public void NoDirectionGivesZero()
    {
        Assert.Equal(Angle.Zero, Angle.Atan2(Fixed.Zero, Fixed.Zero));
    }

    [Fact]
    public void TheLargestValuesDoNotOverflow()
    {
        Assert.Equal(512, Angle.Atan2(Fixed.MaxValue, Fixed.MaxValue).Units);
        Assert.Equal(2560, Angle.Atan2(Fixed.MinValue, Fixed.MinValue).Units);
        Assert.Equal(1024, Angle.Atan2(Fixed.MaxValue, Fixed.Zero).Units);
        Assert.Equal(3072, Angle.Atan2(Fixed.MinValue, Fixed.Zero).Units);
        Assert.Equal(2048, Angle.Atan2(Fixed.Zero, Fixed.MinValue).Units);
        Assert.Equal(0, Angle.Atan2(Fixed.Epsilon, Fixed.MaxValue).Units);
        Assert.Equal(4095, Angle.Atan2(-Fixed.FromInt(50), Fixed.MaxValue).Units);
    }

    /// <summary>The arctangent undoes the sine and cosine of every angle, at any length.</summary>
    [Fact]
    public void ItGivesBackEveryAngleOfTheTable()
    {
        for (var units = 0; units < Angle.UnitsPerTurn; units++)
        {
            var angle = Angle.FromUnits(units);

            Assert.Equal(angle, Angle.Atan2(angle.Sin(), angle.Cos()));
            Assert.Equal(angle, Angle.Atan2(angle.Sin() * 1000, angle.Cos() * 1000));
            Assert.Equal(angle, Angle.Atan2(angle.Sin() / 16, angle.Cos() / 16));
        }
    }

    [Fact]
    public void MirroredInputGivesAMirroredAngle()
    {
        foreach (var (x, y) in Samples())
        {
            if (x == int.MinValue || y == int.MinValue || (x == 0 && y == 0))
            {
                continue;
            }

            var angle = Atan2(y, x);

            Assert.Equal(-angle, Atan2(-y, x));
            Assert.Equal(Angle.HalfTurn - angle, Atan2(y, -x));
            Assert.Equal(angle + Angle.HalfTurn, Atan2(-y, -x));
        }
    }

    /// <summary>
    /// Recomputes the answer without the committed table, in whole numbers only: pi from Machin's
    /// formula and sine and cosine from their power series, carried to 200 binary places. The
    /// direction must lie between the true directions half a unit either side of the result, give
    /// or take the hundredth of a unit by which the table's rounding can move a halfway point.
    /// </summary>
    [Fact]
    public void TheResultIsTheTrueArcTangentRoundedToTheNearestUnit()
    {
        var directions = new Dictionary<int, (BigInteger X, BigInteger Y)>();
        (BigInteger X, BigInteger Y) Direction(int hundredths)
        {
            if (!directions.TryGetValue(hundredths, out var direction))
            {
                var radians = Pi * 2 * hundredths / (Angle.UnitsPerTurn * 100);
                direction = (Sine(radians + (Pi / 2)), Sine(radians));
                directions[hundredths] = direction;
            }

            return direction;
        }

        var checkedCount = 0;
        foreach (var (x, y) in Samples())
        {
            if (x == 0 && y == 0)
            {
                continue;
            }

            var units = Atan2(y, x).Units;
            var middle = Direction(units * 100);
            var before = Direction((units * 100) - 50 - ToleranceInHundredths);
            var after = Direction((units * 100) + 50 + ToleranceInHundredths);

            Assert.True((middle.X * x) + (middle.Y * y) > 0, $"({x}, {y}) gave {units}, which points the other way.");
            Assert.True((before.X * y) - (before.Y * x) >= 0, $"({x}, {y}) gave {units}, which is too far round.");
            Assert.True((x * after.Y) - (y * after.X) >= 0, $"({x}, {y}) gave {units}, which is not far enough round.");
            checkedCount++;
        }

        Assert.True(checkedCount > 20_000);
    }

    /// <summary>
    /// An independent check against the platform math library, which the simulation never calls but
    /// a test may.
    /// </summary>
    [Fact]
    public void ItAgreesWithThePlatformMathLibrary()
    {
        foreach (var (x, y) in Samples())
        {
            if (x == 0 && y == 0)
            {
                continue;
            }

            var expected = Math.Atan2(y, x) * Angle.UnitsPerTurn / (2 * Math.PI);
            var difference = Math.IEEERemainder(Atan2(y, x).Units - expected, Angle.UnitsPerTurn);

            Assert.InRange(difference, -0.5 - (ToleranceInHundredths / 100.0), 0.5 + (ToleranceInHundredths / 100.0));
        }
    }

    /// <summary>
    /// Directions of every size: all the short ones, where rounding matters most; offsets a unit
    /// could have on a map; and values from the whole range of the type.
    /// </summary>
    private static IEnumerable<(int X, int Y)> Samples()
    {
        for (var y = -40; y <= 40; y++)
        {
            for (var x = -40; x <= 40; x++)
            {
                yield return (x, y);
            }
        }

        var random = new DeterministicRandom(2026);
        for (var i = 0; i < 8000; i++)
        {
            yield return (random.NextInt(-200 * Fixed.RawOne, 200 * Fixed.RawOne), random.NextInt(-200 * Fixed.RawOne, 200 * Fixed.RawOne));
        }

        for (var i = 0; i < 8000; i++)
        {
            yield return (unchecked((int)random.NextUInt64()), unchecked((int)random.NextUInt64()));
        }

        // Close to the axes and the diagonals, where one quarter turn meets the next.
        foreach (var large in (int[])[1, 7, 65_536, 6_291_456, int.MaxValue])
        {
            foreach (var small in (int[])[0, 1, 2, 3])
            {
                foreach (var (x, y) in ((int, int)[])[(large, small), (small, large), (large, large - small)])
                {
                    yield return (x, y);
                    yield return (-x, y);
                    yield return (x, -y);
                    yield return (-x, -y);
                }
            }
        }
    }

    private static BigInteger ArcTangentOfReciprocal(int n)
    {
        var term = (BigInteger.One << Bits) / n;
        var sum = BigInteger.Zero;
        for (var k = 0; !term.IsZero; k++)
        {
            sum += (k % 2 == 0 ? term : -term) / ((2 * k) + 1);
            term /= n * n;
        }

        return sum;
    }

    private static BigInteger Sine(BigInteger radians)
    {
        var term = radians;
        var sum = BigInteger.Zero;
        for (var k = 1; !term.IsZero; k += 2)
        {
            sum += term;
            term = -(((term * radians) >> Bits) * radians >> Bits) / ((k + 1) * (k + 2));
        }

        return sum;
    }
}
