using Sim.Numerics;

namespace Sim.Tests.Numerics;

public class FixedTests
{
    private static Fixed Raw(int raw) => Fixed.FromRaw(raw);

    [Fact]
    public void OneIs65536RawUnitsAndDefaultIsZero()
    {
        Assert.Equal(65536, Fixed.One.Raw);
        Assert.Equal(32768, Fixed.Half.Raw);
        Assert.Equal(1, Fixed.Epsilon.Raw);
        Assert.Equal(0, default(Fixed).Raw);
        Assert.Equal(Fixed.Zero, default);
        Assert.Equal(7 * 65536, Fixed.FromInt(7).Raw);
        Assert.Equal(-7 * 65536, Fixed.FromInt(-7).Raw);
    }

    [Fact]
    public void AdditionSubtractionAndWholeMultiplesAreExact()
    {
        var a = Raw(123_457);
        var b = Raw(-98_765);

        Assert.Equal(24_692, (a + b).Raw);
        Assert.Equal(222_222, (a - b).Raw);
        Assert.Equal(-123_457, (-a).Raw);
        Assert.Equal(370_371, (a * 3).Raw);
        Assert.Equal(370_371, (3 * a).Raw);
    }

    [Theory]
    [InlineData(98_304, 131_072, 196_608)] // 1.5 * 2 = 3
    [InlineData(3, 32_768, 2)] // 1.5 units rounds away from zero
    [InlineData(1, 32_768, 1)] // 0.5 units rounds away from zero
    [InlineData(1, 32_767, 0)] // just under 0.5 units rounds to zero
    [InlineData(5, 32_768, 3)] // 2.5 units rounds away from zero
    [InlineData(46_341, 46_341, 32_768)] // sqrt(0.5) squared, 0.50001 rounds to 0.5
    public void MultiplicationRoundsToNearestWithTiesAwayFromZero(int a, int b, int expected)
    {
        Assert.Equal(expected, (Raw(a) * Raw(b)).Raw);
        Assert.Equal(expected, (Raw(b) * Raw(a)).Raw);
        Assert.Equal(-expected, (Raw(-a) * Raw(b)).Raw);
        Assert.Equal(-expected, (Raw(a) * Raw(-b)).Raw);
        Assert.Equal(expected, (Raw(-a) * Raw(-b)).Raw);
    }

    [Theory]
    [InlineData(65_536, 196_608, 21_845)] // 1 / 3 = 21845.33 units
    [InlineData(131_072, 196_608, 43_691)] // 2 / 3 = 43690.67 units
    [InlineData(1, 131_072, 1)] // half a unit rounds away from zero
    [InlineData(3, 131_072, 2)] // one and a half units rounds away from zero
    [InlineData(1, 131_073, 0)] // just under half a unit
    [InlineData(458_752, 131_072, 229_376)] // 7 / 2 = 3.5
    public void DivisionRoundsToNearestWithTiesAwayFromZero(int a, int b, int expected)
    {
        Assert.Equal(expected, (Raw(a) / Raw(b)).Raw);
        Assert.Equal(-expected, (Raw(-a) / Raw(b)).Raw);
        Assert.Equal(-expected, (Raw(a) / Raw(-b)).Raw);
        Assert.Equal(expected, (Raw(-a) / Raw(-b)).Raw);
    }

    [Theory]
    [InlineData(7, 2, 4)] // 3.5 units
    [InlineData(-7, 2, -4)]
    [InlineData(7, -2, -4)]
    [InlineData(6, 4, 2)] // 1.5 units
    [InlineData(5, 4, 1)] // 1.25 units
    [InlineData(196_608, 3, 65_536)]
    public void DivisionByAWholeNumberRoundsTheSameWay(int raw, int divisor, int expected)
    {
        Assert.Equal(expected, (Raw(raw) / divisor).Raw);
    }

    [Fact]
    public void MultiplicationAndDivisionAgreeWithExactFractions()
    {
        // The exact product or quotient, rounded half away from zero, computed with wide integers.
        var random = new Random(20261110);
        for (var i = 0; i < 20_000; i++)
        {
            var a = random.Next(-40_000_000, 40_000_000);
            var b = random.Next(-200_000, 200_000);
            if (b == 0)
            {
                continue;
            }

            Assert.Equal(RoundHalfAwayFromZero((Int128)a * b, 65536), (Raw(a) * Raw(b)).Raw);

            // A smaller numerator, so that dividing by the smallest divisor still fits.
            var c = a / 2048;
            Assert.Equal(RoundHalfAwayFromZero((Int128)c * 65536, b), (Raw(c) / Raw(b)).Raw);
        }
    }

    [Fact]
    public void OverflowThrowsInsteadOfWrapping()
    {
        Assert.Throws<OverflowException>(() => Fixed.MaxValue + Fixed.Epsilon);
        Assert.Throws<OverflowException>(() => Fixed.MinValue - Fixed.Epsilon);
        Assert.Throws<OverflowException>(() => -Fixed.MinValue);
        Assert.Throws<OverflowException>(() => Fixed.Abs(Fixed.MinValue));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(32_768));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(-32_769));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(200) * Fixed.FromInt(200));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(200) * 200);
        Assert.Throws<OverflowException>(() => Fixed.FromInt(200) / Raw(1));
        Assert.Throws<OverflowException>(() => Fixed.MinValue / -1);

        Assert.Equal(-32_768, Fixed.FromInt(-32_768).FloorToInt());
        Assert.Equal(32_767, Fixed.FromInt(32_767).FloorToInt());
    }

    [Fact]
    public void DivisionByZeroThrows()
    {
        Assert.Throws<DivideByZeroException>(() => Fixed.One / Fixed.Zero);
        Assert.Throws<DivideByZeroException>(() => Fixed.One / 0);
    }

    [Fact]
    public void ComparisonsFollowTheValue()
    {
        var small = Raw(-5);
        var large = Raw(3);

        Assert.True(small < large);
        Assert.True(small <= large);
        Assert.True(large > small);
        Assert.True(large >= small);
        Assert.True(small != large);
        Assert.True(small == Raw(-5));
        Assert.True(small.CompareTo(large) < 0);
        Assert.Equal(0, large.CompareTo(Raw(3)));
        Assert.Equal(small, Fixed.Min(small, large));
        Assert.Equal(large, Fixed.Max(small, large));
        Assert.Equal(Raw(5), Fixed.Abs(small));
        Assert.Equal(large, Fixed.Clamp(Raw(10), small, large));
        Assert.Equal(small, Fixed.Clamp(Raw(-10), small, large));
        Assert.Equal(Raw(1), Fixed.Clamp(Raw(1), small, large));
        Assert.Throws<ArgumentException>(() => Fixed.Clamp(Raw(1), large, small));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(65_536, 1, 1, 1)]
    [InlineData(98_304, 1, 2, 2)] // 1.5: a tie rounds up
    [InlineData(98_303, 1, 2, 1)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(-1, -1, 0, 0)]
    [InlineData(-98_304, -2, -1, -1)] // -1.5: a tie rounds up
    [InlineData(-98_305, -2, -1, -2)]
    [InlineData(-65_536, -1, -1, -1)]
    [InlineData(int.MaxValue, 32_767, 32_768, 32_768)]
    [InlineData(int.MinValue, -32_768, -32_768, -32_768)]
    public void RoundingToWholeNumbers(int raw, int floor, int ceiling, int rounded)
    {
        Assert.Equal(floor, Raw(raw).FloorToInt());
        Assert.Equal(ceiling, Raw(raw).CeilingToInt());
        Assert.Equal(rounded, Raw(raw).RoundToInt());
    }

    [Fact]
    public void SqrtRoundsDown()
    {
        Assert.Equal(Fixed.Zero, Fixed.Sqrt(Fixed.Zero));
        Assert.Equal(Fixed.One, Fixed.Sqrt(Fixed.One));
        Assert.Equal(Fixed.FromInt(12), Fixed.Sqrt(Fixed.FromInt(144)));
        Assert.Equal(Fixed.Half, Fixed.Sqrt(Raw(16_384)));
        Assert.Equal(92_681, Fixed.Sqrt(Fixed.FromInt(2)).Raw); // 1.41421356 * 65536 = 92681.9
        Assert.Equal(256, Fixed.Sqrt(Fixed.Epsilon).Raw); // sqrt(1/65536) = 1/256
        Assert.Equal(11_863_283, Fixed.Sqrt(Fixed.MaxValue).Raw); // sqrt(32768) * 65536 = 11863283.2
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixed.Sqrt(Raw(-1)));

        for (long raw = 0; raw <= int.MaxValue; raw += 104_729)
        {
            long root = Fixed.Sqrt(Raw((int)raw)).Raw;
            Assert.True(root * root <= raw << 16);
            Assert.True((root + 1) * (root + 1) > raw << 16);
        }
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(65_536, "1")]
    [InlineData(98_304, "1.5")]
    [InlineData(-98_304, "-1.5")]
    [InlineData(1, "0.0000152587890625")]
    [InlineData(-1, "-0.0000152587890625")]
    [InlineData(170_394, "2.600006103515625")] // the nearest value to 2.6
    [InlineData(int.MaxValue, "32767.9999847412109375")]
    [InlineData(int.MinValue, "-32768")]
    public void ToStringIsTheExactDecimalValue(int raw, string expected)
    {
        Assert.Equal(expected, Raw(raw).ToString());
    }

    [Fact]
    public void ToStringIgnoresTheCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("1234.5", (Fixed.FromInt(1234) + Fixed.Half).ToString());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ToFractionIsExact()
    {
        Assert.Equal(new Fraction(3, 2), Raw(98_304).ToFraction());
        Assert.Equal(new Fraction(-1, 65_536), Raw(-1).ToFraction());
        Assert.Equal(Raw(170_394), Raw(170_394).ToFraction().ToFixed());
    }

    private static int RoundHalfAwayFromZero(Int128 numerator, Int128 denominator)
    {
        var negative = (numerator < 0) != (denominator < 0);
        var n = Int128.Abs(numerator);
        var d = Int128.Abs(denominator);
        var quotient = ((2 * n) + d) / (2 * d);
        return (int)(negative ? -quotient : quotient);
    }
}
