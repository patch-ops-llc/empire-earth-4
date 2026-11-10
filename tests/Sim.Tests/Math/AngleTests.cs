using System.Numerics;
using Sim.Numerics;

namespace Sim.Tests.Numerics;

public class AngleTests
{
    [Fact]
    public void UnitsWrapAroundTheTurn()
    {
        Assert.Equal(0, default(Angle).Units);
        Assert.Equal(Angle.Zero, Angle.FromUnits(4096));
        Assert.Equal(1, Angle.FromUnits(4097).Units);
        Assert.Equal(4095, Angle.FromUnits(-1).Units);
        Assert.Equal(1024, Angle.FromUnits(-3072).Units);
        Assert.Equal(0, Angle.FromUnits(int.MinValue).Units);
        Assert.Equal(4095, Angle.FromUnits(int.MaxValue).Units);

        Assert.Equal(Angle.HalfTurn, Angle.QuarterTurn + Angle.QuarterTurn);
        Assert.Equal(Angle.Zero, Angle.HalfTurn + Angle.HalfTurn);
        Assert.Equal(Angle.FromUnits(3072), Angle.QuarterTurn - Angle.HalfTurn);
        Assert.Equal(Angle.FromUnits(3072), -Angle.QuarterTurn);
        Assert.True(Angle.FromUnits(5) == Angle.FromUnits(4101));
        Assert.True(Angle.FromUnits(5) != Angle.FromUnits(6));
    }

    [Fact]
    public void SineAndCosineAreExactAtQuarterTurns()
    {
        Assert.Equal(Fixed.Zero, Angle.Zero.Sin());
        Assert.Equal(Fixed.One, Angle.Zero.Cos());
        Assert.Equal(Fixed.One, Angle.QuarterTurn.Sin());
        Assert.Equal(Fixed.Zero, Angle.QuarterTurn.Cos());
        Assert.Equal(Fixed.Zero, Angle.HalfTurn.Sin());
        Assert.Equal(-Fixed.One, Angle.HalfTurn.Cos());
        Assert.Equal(-Fixed.One, Angle.FromUnits(3072).Sin());
        Assert.Equal(Fixed.Zero, Angle.FromUnits(3072).Cos());
    }

    [Fact]
    public void SineAndCosineHaveTheSymmetriesOfTheCircle()
    {
        for (var units = 0; units < Angle.UnitsPerTurn; units++)
        {
            var angle = Angle.FromUnits(units);

            Assert.Equal(-angle.Sin(), (-angle).Sin());
            Assert.Equal(angle.Cos(), (-angle).Cos());
            Assert.Equal(-angle.Sin(), (angle + Angle.HalfTurn).Sin());
            Assert.Equal(angle.Sin(), (Angle.HalfTurn - angle).Sin());
            Assert.Equal(angle.Sin(), (angle - Angle.QuarterTurn).Cos());
        }
    }

    /// <summary>
    /// Recomputes the committed table with whole-number arithmetic only: pi from Machin's formula
    /// and the sine from its power series, both carried to 200 binary places.
    /// </summary>
    [Fact]
    public void TheCommittedTableIsTheSineRoundedToTheNearestUnit()
    {
        const int Bits = 200;
        var pi = 4 * ((4 * ArcTangentOfReciprocal(5, Bits)) - ArcTangentOfReciprocal(239, Bits));

        for (var units = 0; units <= Angle.UnitsPerTurn / 4; units++)
        {
            var radians = pi * 2 * units / Angle.UnitsPerTurn;
            var sine = Sine(radians, Bits);
            var expected = (int)((sine + (BigInteger.One << (Bits - 17))) >> (Bits - 16));

            Assert.Equal(expected, Angle.FromUnits(units).Sin().Raw);
        }
    }

    /// <summary>
    /// An independent check against the platform math library. The simulation never calls it, but a
    /// test may. Each value is within half a unit of the true sine, with a margin for the error of
    /// the floating-point sine itself.
    /// </summary>
    [Fact]
    public void SineAndCosineAgreeWithThePlatformMathLibrary()
    {
        for (var units = 0; units < Angle.UnitsPerTurn; units++)
        {
            var radians = 2 * Math.PI * units / Angle.UnitsPerTurn;
            var angle = Angle.FromUnits(units);

            Assert.InRange(angle.Sin().Raw - (Math.Sin(radians) * 65536), -0.5001, 0.5001);
            Assert.InRange(angle.Cos().Raw - (Math.Cos(radians) * 65536), -0.5001, 0.5001);
        }
    }

    [Fact]
    public void DirectionVectorsHaveUnitLengthWithinRounding()
    {
        for (var units = 0; units < Angle.UnitsPerTurn; units++)
        {
            long x = Angle.FromUnits(units).Cos().Raw;
            long y = Angle.FromUnits(units).Sin().Raw;
            long length = IntegerMath.Sqrt((ulong)((x * x) + (y * y)));

            Assert.InRange(length, 65_535, 65_536);
        }
    }

    private static BigInteger ArcTangentOfReciprocal(int n, int bits)
    {
        var term = (BigInteger.One << bits) / n;
        var sum = BigInteger.Zero;
        for (var k = 0; !term.IsZero; k++)
        {
            sum += (k % 2 == 0 ? term : -term) / ((2 * k) + 1);
            term /= n * n;
        }

        return sum;
    }

    private static BigInteger Sine(BigInteger radians, int bits)
    {
        var term = radians;
        var sum = BigInteger.Zero;
        for (var k = 1; !term.IsZero; k += 2)
        {
            sum += term;
            term = -(((term * radians) >> bits) * radians >> bits) / ((k + 1) * (k + 2));
        }

        return sum;
    }
}
