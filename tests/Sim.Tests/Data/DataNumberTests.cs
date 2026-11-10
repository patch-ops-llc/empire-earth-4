using Sim.Data;

namespace Sim.Tests.Data;

public class DataNumberTests
{
    [Theory]
    [InlineData("0", 0, 0)]
    [InlineData("130", 130, 0)]
    [InlineData("2.6", 26, 1)]
    [InlineData("1.08", 108, 2)]
    [InlineData("0.90", 9, 1)]
    [InlineData("1.0", 1, 0)]
    [InlineData("-0.5", -5, 1)]
    [InlineData("-0", 0, 0)]
    [InlineData("123456789.123456789", 123456789123456789, 9)]
    public void ParsesPlainDecimalNotationExactly(string text, long unscaled, int scale)
    {
        Assert.True(DataNumber.TryParse(text, out var number));
        Assert.Equal((unscaled, scale), (number.Unscaled, number.Scale));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("01")]
    [InlineData("1e3")]
    [InlineData("1,5")]
    [InlineData("1.2.3")]
    [InlineData("--1")]
    [InlineData("NaN")]
    [InlineData("0.1234567890")]
    [InlineData("1234567890123456789")]
    public void RejectsEverythingElse(string text)
    {
        Assert.False(DataNumber.TryParse(text, out _));
    }

    [Fact]
    public void NumbersWithTheSameValueAreEqual()
    {
        Assert.True(DataNumber.TryParse("0.90", out var padded));
        Assert.True(DataNumber.TryParse("0.9", out var plain));
        Assert.Equal(plain, padded);
        Assert.Equal(DataNumber.FromInteger(3), Parse("3.000"));
    }

    [Fact]
    public void ComparesAcrossScales()
    {
        Assert.True(Parse("1.08") < Parse("1.1"));
        Assert.True(Parse("2") > Parse("1.999999999"));
        Assert.True(Parse("-0.5") < Parse("0"));
        Assert.True(Parse("1.5") <= Parse("1.50"));
        Assert.True(Parse("1.5") >= Parse("1.50"));
    }

    [Theory]
    [InlineData("1", 20)]
    [InlineData("1.5", 30)]
    [InlineData("0.05", 1)]
    [InlineData("7.65", 153)]
    public void ConvertsWholeTickTimes(string seconds, long ticks)
    {
        var number = Parse(seconds);
        Assert.True(number.IsWholeWhenMultipliedBy(20));
        Assert.Equal(ticks, number.MultipliedBy(20));
    }

    [Theory]
    [InlineData("0.33")]
    [InlineData("7.68")]
    [InlineData("0.049")]
    public void DetectsTimesThatAreNotWholeTicks(string seconds)
    {
        var number = Parse(seconds);
        Assert.False(number.IsWholeWhenMultipliedBy(20));
        Assert.Throws<InvalidOperationException>(() => number.MultipliedBy(20));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("130")]
    [InlineData("2.6")]
    [InlineData("0.05")]
    [InlineData("-0.5")]
    [InlineData("-12.345")]
    public void WritesItselfBackAsText(string text)
    {
        Assert.Equal(text, Parse(text).ToString());
    }

    private static DataNumber Parse(string text)
    {
        Assert.True(DataNumber.TryParse(text, out var number), text);
        return number;
    }
}
