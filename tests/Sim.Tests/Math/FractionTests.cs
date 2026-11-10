using Sim.Numerics;

namespace Sim.Tests.Numerics;

public class FractionTests
{
    private static Fraction F(string text) => Fraction.Parse(text);

    [Fact]
    public void FractionsAreKeptInLowestTermsWithAPositiveDenominator()
    {
        var value = new Fraction(6, -8);

        Assert.Equal(-3, value.Numerator);
        Assert.Equal(4, value.Denominator);
        Assert.Equal(new Fraction(-3, 4), value);
        Assert.Equal(new Fraction(0, 1), new Fraction(0, -17));
        Assert.Equal(1, new Fraction(0, -17).Denominator);
        Assert.Throws<DivideByZeroException>(() => new Fraction(1, 0));
    }

    [Fact]
    public void TheDefaultValueIsZero()
    {
        Assert.Equal(Fraction.Zero, default);
        Assert.Equal(0, default(Fraction).Numerator);
        Assert.Equal(1, default(Fraction).Denominator);
        Assert.Equal(new Fraction(0, 5), default);
        Assert.Equal(F("0.5"), default(Fraction) + F("0.5"));
        Assert.Equal(1, Fraction.One.Numerator);
    }

    [Fact]
    public void ArithmeticIsExact()
    {
        Assert.Equal(new Fraction(5, 6), new Fraction(1, 2) + new Fraction(1, 3));
        Assert.Equal(new Fraction(1, 6), new Fraction(1, 2) - new Fraction(1, 3));
        Assert.Equal(new Fraction(1, 6), new Fraction(1, 2) * new Fraction(1, 3));
        Assert.Equal(new Fraction(3, 2), new Fraction(1, 2) / new Fraction(1, 3));
        Assert.Equal(new Fraction(-1, 2), -new Fraction(1, 2));
        Assert.Equal(new Fraction(-7, 3), new Fraction(3, -7).Reciprocal());
        Assert.Equal(Fraction.Zero, new Fraction(1, 3) * Fraction.Zero);
        Assert.Equal(Fraction.Zero, new Fraction(1, 3) - new Fraction(1, 3));
        Assert.Equal(new Fraction(15, 2), new Fraction(5, 4) * 6);
        Assert.Equal(Fraction.FromInteger(6), 6);

        // One third added three times is exactly one, which no binary or decimal fraction manages.
        Assert.Equal(Fraction.One, new Fraction(1, 3) + new Fraction(1, 3) + new Fraction(1, 3));

        Assert.Throws<DivideByZeroException>(() => Fraction.One / Fraction.Zero);
        Assert.Throws<DivideByZeroException>(() => Fraction.Zero.Reciprocal());
    }

    [Fact]
    public void PowersTakeWholeExponentsOfEitherSign()
    {
        Assert.Equal(Fraction.One, F("1.25").Pow(0));
        Assert.Equal(new Fraction(5, 4), F("1.25").Pow(1));
        Assert.Equal(new Fraction(625, 256), F("1.25").Pow(4));
        Assert.Equal(new Fraction(256, 625), F("1.25").Pow(-4));
        Assert.Equal(new Fraction(25, 16), F("0.8").Pow(-2));
        Assert.Equal(new Fraction(-27, 8), new Fraction(-3, 2).Pow(3));
        Assert.Equal(new Fraction(-8, 27), new Fraction(-3, 2).Pow(-3));
        Assert.Equal(Fraction.One, Fraction.Zero.Pow(0));
        Assert.Equal(Fraction.Zero, Fraction.Zero.Pow(3));
        Assert.Throws<DivideByZeroException>(() => Fraction.Zero.Pow(-1));
    }

    [Fact]
    public void TheLargestPowerInTheRulesNeedsMoreThan64Bits()
    {
        // 1.15 to the 14th power: fourteen steps of quality on the price of a unit.
        var power = F("1.15").Pow(14);

        Assert.Equal(Int128.Parse("11592836324538749809", System.Globalization.CultureInfo.InvariantCulture), power.Numerator);
        Assert.Equal(Int128.Parse("1638400000000000000", System.Globalization.CultureInfo.InvariantCulture), power.Denominator);
        Assert.True(power.Numerator > long.MaxValue);
        Assert.Equal(7076, power.RoundHalfUp(1000));

        // A price of 400 at that power, with Manufacturing two steps behind: 3494.18.
        Assert.Equal(3494, (400 * power * F("0.90").Pow(-2)).RoundHalfUp());
    }

    [Fact]
    public void ComparisonsFollowTheValue()
    {
        Assert.True(new Fraction(1, 3) < new Fraction(1, 2));
        Assert.True(new Fraction(-1, 2) < new Fraction(-1, 3));
        Assert.True(new Fraction(2, 4) <= new Fraction(1, 2));
        Assert.True(new Fraction(2, 4) >= new Fraction(1, 2));
        Assert.True(new Fraction(2, 4) == new Fraction(1, 2));
        Assert.True(new Fraction(2, 5) != new Fraction(1, 2));
        Assert.True(new Fraction(7, 8) > new Fraction(6, 7));
        Assert.Equal(0, new Fraction(3, 9).CompareTo(new Fraction(1, 3)));
        Assert.Equal(new Fraction(2, 4).GetHashCode(), new Fraction(1, 2).GetHashCode());
    }

    [Theory]
    [InlineData("0", 0, 1)]
    [InlineData("40", 40, 1)]
    [InlineData("-0", 0, 1)]
    [InlineData("1.25", 5, 4)]
    [InlineData("1.15", 23, 20)]
    [InlineData("0.90", 9, 10)]
    [InlineData("0.80", 4, 5)]
    [InlineData("1.08", 27, 25)]
    [InlineData("1.12", 28, 25)]
    [InlineData("-0.5", -1, 2)]
    [InlineData("007.50", 15, 2)]
    [InlineData("0.000001", 1, 1_000_000)]
    public void ParseReadsPlainDecimalNumbersExactly(string text, long numerator, long denominator)
    {
        Assert.Equal(new Fraction(numerator, denominator), Fraction.Parse(text));
        Assert.True(Fraction.TryParse(text, out var parsed));
        Assert.Equal(new Fraction(numerator, denominator), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("1.2.3")]
    [InlineData("+1")]
    [InlineData("--1")]
    [InlineData("1-")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,5")]
    [InlineData("1e3")]
    [InlineData("1_000")]
    [InlineData("0x10")]
    [InlineData("NaN")]
    [InlineData("\u0661")] // an Arabic-Indic digit one
    [InlineData("1234567890123456789012345678901234567")] // 37 digits
    public void ParseRejectsEverythingElse(string text)
    {
        Assert.False(Fraction.TryParse(text, out var parsed));
        Assert.Equal(Fraction.Zero, parsed);
        Assert.Throws<FormatException>(() => Fraction.Parse(text));
    }

    [Fact]
    public void ParseAcceptsTheLongestNumberItAllows()
    {
        var parsed = Fraction.Parse("123456789012345678.901234567890123456"); // 36 digits

        Assert.Equal(Fraction.FromDecimalDigits(Int128.Parse("123456789012345678901234567890123456", System.Globalization.CultureInfo.InvariantCulture), 18), parsed);
    }

    [Fact]
    public void FromDecimalDigitsTakesDigitsAndAScale()
    {
        Assert.Equal(new Fraction(5, 4), Fraction.FromDecimalDigits(125, 2));
        Assert.Equal(new Fraction(-13, 5), Fraction.FromDecimalDigits(-26, 1));
        Assert.Equal(Fraction.FromInteger(40), Fraction.FromDecimalDigits(40, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fraction.FromDecimalDigits(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fraction.FromDecimalDigits(1, 37));
    }

    [Theory]
    [InlineData(5, 2, 1, 3)] // 2.5: a tie goes up
    [InlineData(-5, 2, 1, -2)] // -2.5: a tie goes up, toward zero here
    [InlineData(7, 3, 1, 2)]
    [InlineData(8, 3, 1, 3)]
    [InlineData(-7, 3, 1, -2)]
    [InlineData(-8, 3, 1, -3)]
    [InlineData(-1, 2, 1, 0)]
    [InlineData(-3, 2, 1, -1)]
    [InlineData(0, 1, 1000, 0)]
    [InlineData(4, 1, 1, 4)]
    [InlineData(-4, 1, 1, -4)]
    [InlineData(1, 3, 1000, 333)]
    [InlineData(2, 3, 1000, 667)]
    [InlineData(1, 2000, 1000, 1)] // 0.5 thousandths
    [InlineData(-1, 2000, 1000, 0)]
    [InlineData(-3, 2000, 1000, -1)]
    public void RoundHalfUpRoundsToNearestWithTiesGoingUp(long numerator, long denominator, long scale, long expected)
    {
        Assert.Equal(expected, new Fraction(numerator, denominator).RoundHalfUp(scale));
    }

    [Fact]
    public void RoundHalfUpRejectsAScaleThatIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fraction.One.RoundHalfUp(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fraction.One.RoundHalfUp(-1));
    }

    /// <summary>
    /// Values from the worked examples in the game design (sections 1.3 and 2.6), which the
    /// technical plan lists with their exact values before rounding. They exercise the arithmetic
    /// with the factors and roundings the rules use; the rules themselves arrive with the resolver.
    /// </summary>
    [Fact]
    public void TheDesignsWorkedNumbersComeOutAsStated()
    {
        const int TicksPerSecond = SimInfo.TicksPerSecond;

        // Research price of tier 4 (225 Materials): two ahead costs 337.5, as catch-up 112.5.
        Assert.Equal(338, (225 * F("1.5")).RoundHalfUp());
        Assert.Equal(113, (225 * F("0.5")).RoundHalfUp());

        // Guard chassis at five tiers of Firepower and Protection: 130 hit points and 8 damage per second.
        Assert.Equal(317_383, (130 * F("1.25").Pow(4)).RoundHalfUp(1000)); // 317.3828125
        Assert.Equal(19_531, (8 * F("1.25").Pow(4)).RoundHalfUp(1000)); // 19.53125

        // Build times: 12 s at two steps of Manufacturing ahead is 7.68 s, 18 s at two behind is 28.125 s.
        Assert.Equal(154, (12 * F("0.80").Pow(2)).RoundHalfUp(TicksPerSecond)); // 153.6 ticks
        Assert.Equal(563, (18 * F("0.80").Pow(-2)).RoundHalfUp(TicksPerSecond)); // 562.5 ticks, a tie

        // Line infantry at two tiers: 60 hit points and 6 damage per second, times 1.25.
        Assert.Equal(75_000, (60 * F("1.25").Pow(1)).RoundHalfUp(1000));
        Assert.Equal(7_500, (6 * F("1.25").Pow(1)).RoundHalfUp(1000));
    }

    [Fact]
    public void ToFixedRoundsOnceToTheDistanceUnit()
    {
        Assert.Equal(Fixed.FromInt(3), Fraction.FromInteger(3).ToFixed());
        Assert.Equal(170_394, F("2.6").ToFixed().Raw); // 170393.6
        Assert.Equal(98_304, F("1.5").ToFixed().Raw);
        Assert.Equal(1, new Fraction(1, 131_072).ToFixed().Raw); // half a unit goes up
        Assert.Equal(0, new Fraction(-1, 131_072).ToFixed().Raw); // minus half a unit goes up
        Assert.Equal(-98_304, F("-1.5").ToFixed().Raw);

        // A speed of 3 tiles per second at three tiers of Mobility, per tick: 3 * 1.08^2 / 20.
        Assert.Equal(11_466, (3 * F("1.08").Pow(2) / SimInfo.TicksPerSecond).ToFixed().Raw); // 11466.18

        Assert.Throws<OverflowException>(() => Fraction.FromInteger(32_768).ToFixed());
    }

    [Fact]
    public void OverflowThrowsInsteadOfWrapping()
    {
        var huge = Fraction.FromInteger(Int128.MaxValue);

        Assert.Throws<OverflowException>(() => huge + huge);
        Assert.Throws<OverflowException>(() => huge * huge);
        Assert.Throws<OverflowException>(() => F("1.15").Pow(40));
        Assert.Throws<OverflowException>(() => huge.RoundHalfUp());
        Assert.Throws<OverflowException>(() => Fraction.FromInteger(long.MaxValue).RoundHalfUp(2));
        Assert.Throws<OverflowException>(() => new Fraction(Int128.MinValue, 1));
        Assert.Throws<OverflowException>(() => new Fraction(1, Int128.MinValue));
    }

    [Theory]
    [InlineData(5, 4, "5/4")]
    [InlineData(-5, 4, "-5/4")]
    [InlineData(6, 2, "3")]
    [InlineData(0, 9, "0")]
    public void ToStringShowsTheReducedFraction(long numerator, long denominator, string expected)
    {
        Assert.Equal(expected, new Fraction(numerator, denominator).ToString());
    }
}
