using Sim.Numerics;

namespace Sim.Tests.Numerics;

public class FixedVector2Tests
{
    private static FixedVector2 Raw(int x, int y) => new(Fixed.FromRaw(x), Fixed.FromRaw(y));

    [Fact]
    public void ArithmeticWorksOnEachComponent()
    {
        var a = FixedVector2.FromInts(3, -4);
        var b = new FixedVector2(Fixed.Half, Fixed.One);

        Assert.Equal(new FixedVector2(Fixed.FromInt(3) + Fixed.Half, Fixed.FromInt(-3)), a + b);
        Assert.Equal(new FixedVector2(Fixed.FromInt(3) - Fixed.Half, Fixed.FromInt(-5)), a - b);
        Assert.Equal(FixedVector2.FromInts(-3, 4), -a);
        Assert.Equal(FixedVector2.FromInts(6, -8), a * 2);
        Assert.Equal(FixedVector2.FromInts(6, -8), 2 * a);
        Assert.Equal(new FixedVector2(Fixed.FromInt(3) / 2, Fixed.FromInt(-2)), a * Fixed.Half);
        Assert.Equal(new FixedVector2(Fixed.FromInt(3) / 2, Fixed.FromInt(-2)), Fixed.Half * a);
        Assert.Equal(new FixedVector2(Fixed.FromInt(3) / 2, Fixed.FromInt(-2)), a / 2);
        Assert.Equal(default, FixedVector2.Zero);
        Assert.Equal(Fixed.Zero, FixedVector2.Zero.X);
    }

    [Fact]
    public void EqualityComparesBothComponents()
    {
        Assert.True(Raw(1, 2) == Raw(1, 2));
        Assert.True(Raw(1, 2) != Raw(2, 1));
        Assert.True(Raw(1, 2) != Raw(1, 3));
        Assert.True(Raw(1, 2).Equals((object)Raw(1, 2)));
        Assert.False(Raw(1, 2).Equals((object)"(1, 2)"));
        Assert.Equal(Raw(7, -9).GetHashCode(), Raw(7, -9).GetHashCode());
        Assert.Equal("(1.5, -2)", new FixedVector2(Fixed.FromInt(3) / 2, Fixed.FromInt(-2)).ToString());
    }

    [Fact]
    public void LengthsAndDistancesAreExactWhenSquaredAndRoundDownOtherwise()
    {
        var a = FixedVector2.FromInts(1, 1);
        var b = FixedVector2.FromInts(4, 5);

        Assert.Equal(25L << 32, FixedVector2.DistanceSquaredRaw(a, b));
        Assert.Equal(25L << 32, (b - a).LengthSquaredRaw());
        Assert.Equal(FixedVector2.SquareRaw(Fixed.FromInt(5)), FixedVector2.DistanceSquaredRaw(a, b));
        Assert.Equal(Fixed.FromInt(5), FixedVector2.Distance(a, b));
        Assert.Equal(Fixed.FromInt(5), (a - b).Length());

        // The square root of 2 is 92,681.9 units.
        Assert.Equal(92_681, FixedVector2.FromInts(1, 1).Length().Raw);
        Assert.Equal(0, FixedVector2.Zero.Length().Raw);
        Assert.Equal(1, Raw(1, 0).Length().Raw);
        Assert.Equal(1, Raw(1, 1).Length().Raw);
    }

    [Fact]
    public void DistancesAcrossTheLargestMapFitIn64Bits()
    {
        var corner = FixedVector2.FromInts(4096, 4096);

        Assert.Equal(2L * 4096 * 4096 << 32, FixedVector2.DistanceSquaredRaw(FixedVector2.Zero, corner));
        Assert.Equal(379_625_062, FixedVector2.Distance(corner, FixedVector2.Zero).Raw);
    }

    [Fact]
    public void ADistanceTooLargeForTheTypeThrows()
    {
        var far = new FixedVector2(Fixed.MaxValue, Fixed.MaxValue);
        var opposite = new FixedVector2(Fixed.MinValue, Fixed.MinValue);

        Assert.Throws<OverflowException>(() => FixedVector2.DistanceSquaredRaw(far, opposite));
        Assert.Throws<OverflowException>(() => far - opposite);
    }

    [Fact]
    public void DotAndCrossGiveTheSideAndTheSense()
    {
        var right = FixedVector2.FromInts(2, 0);
        var down = FixedVector2.FromInts(0, 3);

        Assert.Equal(0, FixedVector2.DotRaw(right, down));
        Assert.Equal(4L << 32, FixedVector2.DotRaw(right, right));
        Assert.Equal(-(4L << 32), FixedVector2.DotRaw(right, -right));

        // A positive angle turns from +x toward +y.
        Assert.Equal(6L << 32, FixedVector2.CrossRaw(right, down));
        Assert.Equal(-(6L << 32), FixedVector2.CrossRaw(down, right));
        Assert.Equal(0, FixedVector2.CrossRaw(right, right * 5));
    }

    [Fact]
    public void AnglesAndVectorsConvertBothWays()
    {
        Assert.Equal(FixedVector2.FromInts(1, 0), FixedVector2.FromAngle(Angle.Zero));
        Assert.Equal(FixedVector2.FromInts(0, 1), FixedVector2.FromAngle(Angle.QuarterTurn));
        Assert.Equal(FixedVector2.FromInts(-1, 0), FixedVector2.FromAngle(Angle.HalfTurn));

        Assert.Equal(Angle.QuarterTurn, FixedVector2.FromInts(0, 7).ToAngle());
        Assert.Equal(Angle.FromUnits(512), FixedVector2.FromInts(3, 3).ToAngle());
        for (var units = 0; units < Angle.UnitsPerTurn; units++)
        {
            Assert.Equal(Angle.FromUnits(units), FixedVector2.FromAngle(Angle.FromUnits(units)).ToAngle());
        }
    }

    [Fact]
    public void MoveTowardsArrivesExactlyWhenTheTargetIsWithinReach()
    {
        var from = FixedVector2.FromInts(1, 1);
        var to = FixedVector2.FromInts(4, 5);

        Assert.Equal(to, FixedVector2.MoveTowards(from, to, Fixed.FromInt(5)));
        Assert.Equal(to, FixedVector2.MoveTowards(from, to, Fixed.FromInt(6)));
        Assert.Equal(from, FixedVector2.MoveTowards(from, from, Fixed.One));
        Assert.Equal(from, FixedVector2.MoveTowards(from, from, Fixed.Zero));
        Assert.NotEqual(to, FixedVector2.MoveTowards(from, to, Fixed.FromInt(5) - Fixed.Epsilon));
    }

    [Fact]
    public void MoveTowardsTakesAStepOfTheGivenLengthAlongTheLine()
    {
        var from = FixedVector2.FromInts(1, 1);
        var to = FixedVector2.FromInts(4, 5);

        // A 3-4-5 triangle: one tile along it is 0.6 across and 0.8 down.
        var step = FixedVector2.MoveTowards(from, to, Fixed.One) - from;
        Assert.Equal(new Fraction(3, 5).ToFixed(), step.X);
        Assert.Equal(new Fraction(4, 5).ToFixed(), step.Y);

        Assert.Equal(from, FixedVector2.MoveTowards(from, to, Fixed.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => FixedVector2.MoveTowards(from, to, -Fixed.Epsilon));
    }

    /// <summary>
    /// What movement relies on: walking by repeated steps never passes the target, gets closer with
    /// every step, keeps to the straight line, and ends exactly on the target.
    /// </summary>
    [Theory]
    [InlineData(98_304, 98_304, 1_327_104, 638_976, 9_830)]
    [InlineData(1_327_104, 638_976, 98_304, 98_304, 9_830)]
    [InlineData(65_536, 3_000_000, 5_000_000, 65_537, 26_214)]
    [InlineData(100, 100, 6_291_456, 6_291_456, 5_898)]
    [InlineData(0, 0, 1_000_003, 1, 8_520)]
    [InlineData(500_000, 500_000, 500_001, 400_000, 1)]
    public void RepeatedStepsWalkTheStraightLineAndEndOnTheTarget(int fromX, int fromY, int toX, int toY, int speed)
    {
        var start = Raw(fromX, fromY);
        var target = Raw(toX, toY);
        var step = Fixed.FromRaw(speed);
        var whole = target - start;
        var length = whole.Length().Raw;

        var position = start;
        var steps = 0;
        long furthestOffLine = 0;
        while (position != target)
        {
            var next = FixedVector2.MoveTowards(position, target, step);
            var left = FixedVector2.DistanceSquaredRaw(next, target);
            Assert.True(left < FixedVector2.DistanceSquaredRaw(position, target), "A step did not get closer.");

            // The distance from the line through start and target, in units, is this over the length.
            furthestOffLine = Math.Max(furthestOffLine, Math.Abs(FixedVector2.CrossRaw(whole, next - start)));

            if (next != target)
            {
                Assert.InRange((next - position).Length().Raw, speed - 2, speed + 1);
            }

            position = next;
            steps++;
        }

        // Each step is rounded to the unit and the next one aims at the target again, so the walk
        // drifts from the straight line by a few units and no more: under 1/1,024 of a tile here.
        Assert.True(furthestOffLine < 64L * length, $"The walk strayed {furthestOffLine / length} units from the line.");
        Assert.InRange(steps, length / (speed + 1), (length / Math.Max(speed - 2, 1)) + 2);
    }

    [Fact]
    public void MirroredInputGivesAMirroredStep()
    {
        var random = new Sim.Core.DeterministicRandom(5);
        for (var i = 0; i < 2000; i++)
        {
            var from = Raw(random.NextInt(-3_000_000, 3_000_000), random.NextInt(-3_000_000, 3_000_000));
            var to = Raw(random.NextInt(-3_000_000, 3_000_000), random.NextInt(-3_000_000, 3_000_000));
            var reach = Fixed.FromRaw(random.NextInt(1, 40_000));

            var moved = FixedVector2.MoveTowards(from, to, reach);
            var mirroredX = FixedVector2.MoveTowards(Raw(-from.X.Raw, from.Y.Raw), Raw(-to.X.Raw, to.Y.Raw), reach);
            var mirroredY = FixedVector2.MoveTowards(Raw(from.X.Raw, -from.Y.Raw), Raw(to.X.Raw, -to.Y.Raw), reach);

            Assert.Equal(Raw(-moved.X.Raw, moved.Y.Raw), mirroredX);
            Assert.Equal(Raw(moved.X.Raw, -moved.Y.Raw), mirroredY);
        }
    }
}
