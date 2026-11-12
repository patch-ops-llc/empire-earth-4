using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Tests.Map;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>The spacing rule of design 2.4 on its own: the numbers, and the test for one step past one enemy.</summary>
public class SpacingRuleTests
{
    private static readonly Fixed OneAndAHalf = Fixed.One * 3 / 2;

    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    /// <summary>Design 2.4: between units of two lines the larger value applies.</summary>
    [Fact]
    public void BetweenTwoUnitsTheLargerSpacingApplies()
    {
        Assert.Equal(Fixed.One, SpacingRule.Between(Fixed.One, Fixed.One));
        Assert.Equal(OneAndAHalf, SpacingRule.Between(Fixed.One, OneAndAHalf));
        Assert.Equal(OneAndAHalf, SpacingRule.Between(OneAndAHalf, Fixed.One));
        Assert.Equal(OneAndAHalf, SpacingRule.Between(new Mover { Spacing = OneAndAHalf }, new Mover { Spacing = Fixed.One }));
    }

    /// <summary>
    /// What combat asks: the spacing between two entities, to which melee reach adds a quarter of
    /// a tile (design 2.4). A unit spawned without a spacing has that of infantry and workers.
    /// </summary>
    [Fact]
    public void TheSpacingBetweenTwoUnitsOfAMatchCanBeAsked()
    {
        var match = TestMatches.Create(TestMaps.Open(8, 8));
        var infantry = match.SpawnUnit(0, Point("2.5", "2.5"), Fixed.One / 4);
        var cavalry = match.SpawnUnit(1, Point("5.5", "2.5"), Fixed.One / 4, OneAndAHalf);
        var structure = match.World.Entities.Create();
        match.Bodies.Add(structure, new Body { Owner = 0, Position = Point("2.5", "5.5") });

        Assert.Equal(Fixed.One, Mover.DefaultSpacing);
        Assert.Equal(Fixed.One, match.Movers.Get(infantry).Spacing);
        Assert.True(SpacingRule.TryBetween(match.Movers, infantry, infantry, out var same));
        Assert.Equal(Fixed.One, same);
        Assert.True(SpacingRule.TryBetween(match.Movers, infantry, cavalry, out var mixed));
        Assert.Equal(OneAndAHalf, mixed);
        Assert.True(SpacingRule.TryBetween(match.Movers, cavalry, infantry, out mixed));
        Assert.Equal(OneAndAHalf, mixed);

        Assert.False(SpacingRule.TryBetween(match.Movers, infantry, structure, out var none));
        Assert.Equal(Fixed.Zero, none);
        Assert.False(SpacingRule.TryBetween(match.Movers, structure, cavalry, out _));
        Assert.False(SpacingRule.TryBetween(match.Movers, EntityId.None, cavalry, out _));
    }

    [Fact]
    public void UnitsOfDifferentPlayersAreEnemies()
    {
        Assert.True(SpacingRule.AreEnemyGroundUnits(0, 1));
        Assert.True(SpacingRule.AreEnemyGroundUnits(3, 0));
        Assert.False(SpacingRule.AreEnemyGroundUnits(0, 0));
        Assert.False(SpacingRule.AreEnemyGroundUnits(2, 2));
    }

    /// <summary>
    /// Design 2.4: among a player's own units spacing is enforced for units that are not moving.
    /// Walking units keep half of it from each other, and a walking unit keeps none from a
    /// standing one. A unit waiting for a route or for enemies to clear its way keeps none.
    /// </summary>
    [Theory]
    [InlineData(MoveState.Idle, false, MoveState.Idle, false, "1.5")]
    [InlineData(MoveState.Moving, false, MoveState.Moving, false, "0.75")]
    [InlineData(MoveState.Following, false, MoveState.Moving, false, "0.75")]
    [InlineData(MoveState.Following, false, MoveState.Following, false, "0.75")]
    [InlineData(MoveState.Idle, false, MoveState.Moving, false, "0")]
    [InlineData(MoveState.Following, false, MoveState.Idle, false, "0")]
    [InlineData(MoveState.Planning, false, MoveState.Idle, false, "0")]
    [InlineData(MoveState.Planning, false, MoveState.Moving, false, "0")]
    [InlineData(MoveState.Planning, false, MoveState.Planning, false, "0")]
    [InlineData(MoveState.Moving, true, MoveState.Moving, false, "0")]
    [InlineData(MoveState.Following, false, MoveState.Following, true, "0")]
    [InlineData(MoveState.Moving, true, MoveState.Following, true, "0")]
    [InlineData(MoveState.Moving, true, MoveState.Idle, false, "0")]
    public void WhatTwoUnitsOfOneSideKeepDependsOnWhetherTheyWalk(MoveState first, bool firstWaits, MoveState second, bool secondWaits, string expected)
    {
        var one = new Mover { Spacing = Fixed.One, State = first, Waiting = firstWaits };
        var other = new Mover { Spacing = OneAndAHalf, State = second, Waiting = secondWaits };

        Assert.Equal(Fraction.Parse(expected).ToFixed(), SpacingRule.DistanceOwnUnitsKeep(one, other));
        Assert.Equal(Fraction.Parse(expected).ToFixed(), SpacingRule.DistanceOwnUnitsKeep(other, one));
    }

    /// <summary>
    /// A step may end at the spacing from an enemy and no nearer, and it may not cut through the
    /// circle of that radius on the way, even with both its ends outside.
    /// </summary>
    [Theory]
    [InlineData("0", "0", "3", "0", "4", "0", true)] // Ends exactly at the spacing.
    [InlineData("0", "0", "3.01", "0", "4", "0", false)] // Ends a hundredth inside it.
    [InlineData("0", "0", "8", "0", "4", "0", false)] // Through the enemy and out the far side.
    [InlineData("0", "1", "8", "1", "4", "0", true)] // Past it, touching the circle.
    [InlineData("0", "0.99", "8", "0.99", "4", "0", false)] // Past it, a hundredth too close.
    [InlineData("0", "1", "8", "0.99", "4", "0", false)]
    [InlineData("0", "3", "8", "3", "4", "0", true)] // Well clear.
    [InlineData("5", "0", "9", "0", "4", "0", true)] // Straight away from it.
    [InlineData("5", "0", "5", "4", "4", "0", true)] // Along its side, leaving.
    [InlineData("0", "0", "1", "0", "4", "0", true)] // Toward it, but still far.
    [InlineData("4", "3", "4", "-3", "4", "0", false)] // Across it from above.
    public void AStepMustStayTheSpacingFromAnEnemy(string fromX, string fromY, string toX, string toY, string enemyX, string enemyY, bool allowed)
    {
        var from = Point(fromX, fromY);
        var to = Point(toX, toY);
        var enemy = Point(enemyX, enemyY);

        Assert.Equal(allowed, SpacingRule.AllowsStep(from, to, enemy, Fixed.One));

        // The step back is the same line, unless it starts inside the spacing: then it may leave.
        var endsInside = FixedVector2.DistanceSquaredRaw(to, enemy) < FixedVector2.SquareRaw(Fixed.One);
        Assert.Equal(allowed || endsInside, SpacingRule.AllowsStep(to, from, enemy, Fixed.One));
    }

    /// <summary>
    /// Design 2.4: a unit that is already closer than the spacing may move only if that brings it
    /// no nearer. It can always walk away.
    /// </summary>
    [Theory]
    [InlineData("3.5", "0", "3", "0", true)] // Straight away.
    [InlineData("3.5", "0", "0", "0", true)]
    [InlineData("3.5", "0", "3.5", "0.25", true)] // Sideways, which is further.
    [InlineData("3.5", "0", "4", "0.5", true)] // Round it, to the same distance.
    [InlineData("3.5", "0", "3.75", "0", false)] // Nearer.
    [InlineData("3.5", "0", "4.25", "0", false)] // Through it to a nearer point.
    [InlineData("3.5", "0", "5", "0", true)] // Through it and out to the spacing: the ends decide.
    [InlineData("4", "0", "4", "0.001", true)] // From the very same point, any way is away.
    public void AUnitInsideTheSpacingMayMoveOnlyIfThatBringsItNoNearer(string fromX, string fromY, string toX, string toY, bool allowed)
    {
        Assert.Equal(allowed, SpacingRule.AllowsStep(Point(fromX, fromY), Point(toX, toY), Point("4", "0"), Fixed.One));
    }

    /// <summary>
    /// Design 2.4: two enemy units leave a way between them only when they stand at least twice
    /// the spacing apart: 2 tiles for infantry, 3 when either unit is Cavalry/Armor.
    /// </summary>
    [Theory]
    [InlineData("1", "2", true)]
    [InlineData("1", "1.999", false)]
    [InlineData("1", "1", false)]
    [InlineData("1.5", "3", true)]
    [InlineData("1.5", "2.999", false)]
    [InlineData("1.5", "2", false)]
    public void TwoEnemiesLeaveAWayOnlyWhenTwiceTheSpacingApart(string spacingText, string apartText, bool way)
    {
        var spacing = Fraction.Parse(spacingText).ToFixed();
        var apart = Fraction.Parse(apartText).ToFixed();
        var upper = new FixedVector2(Fixed.FromInt(10), Fixed.FromInt(10) - (apart / 2));
        var lower = new FixedVector2(Fixed.FromInt(10), upper.Y + apart);

        // The best line there is: straight through the middle between the two.
        var from = Point("5", "10");
        var to = Point("15", "10");
        Assert.Equal(way, SpacingRule.AllowsStep(from, to, upper, spacing) && SpacingRule.AllowsStep(from, to, lower, spacing));

        // No other line does better: none through the gap is allowed when the middle one is not.
        var random = new DeterministicRandom(4);
        for (var i = 0; i < 2000 && !way; i++)
        {
            var start = new FixedVector2(Fixed.FromInt(5), Fixed.FromRaw(random.NextInt(5 * Fixed.RawOne, 15 * Fixed.RawOne)));
            var end = new FixedVector2(Fixed.FromInt(15), Fixed.FromRaw(random.NextInt(5 * Fixed.RawOne, 15 * Fixed.RawOne)));
            var crossing = start.Y + ((end.Y - start.Y) / 2);
            if (crossing > upper.Y && crossing < lower.Y)
            {
                Assert.False(SpacingRule.AllowsStep(start, end, upper, spacing) && SpacingRule.AllowsStep(start, end, lower, spacing));
            }
        }
    }

    /// <summary>
    /// The test against the rule it stands for, worked out another way: for random steps past a
    /// random enemy, a step is allowed exactly when no point of it is nearer the enemy than the
    /// spacing, or, from inside, when it ends no nearer than it began.
    /// </summary>
    [Fact]
    public void TheTestAgreesWithMeasuringEveryPointOfTheStep()
    {
        var random = new DeterministicRandom(11);
        var decided = 0;
        for (var i = 0; i < 20_000; i++)
        {
            var spacing = Fixed.FromRaw(random.NextInt(Fixed.RawOne / 2, 2 * Fixed.RawOne));
            var enemy = new FixedVector2(Fixed.FromInt(8), Fixed.FromInt(8));
            var from = RandomPoint(random);

            // Short steps like a unit's, and now and then a long one.
            var reach = i % 5 == 0 ? 6 * Fixed.RawOne : Fixed.RawOne / 2;
            var to = from + new FixedVector2(Fixed.FromRaw(random.NextInt(-reach, reach + 1)), Fixed.FromRaw(random.NextInt(-reach, reach + 1)));

            var before = Distance(from, enemy);
            var least = double.MaxValue;
            for (var part = 0; part <= 400; part++)
            {
                var along = part / 400.0;
                least = Math.Min(least, Math.Sqrt(
                    Square(from.X.Raw + ((to.X.Raw - from.X.Raw) * along) - enemy.X.Raw)
                    + Square(from.Y.Raw + ((to.Y.Raw - from.Y.Raw) * along) - enemy.Y.Raw)));
            }

            // Leave out the steps too close to call with 400 measurements and rounding.
            var margin = (before < spacing.Raw ? Distance(to, enemy) - before : least - spacing.Raw) / Fixed.RawOne;
            if (Math.Abs(margin) < 0.001 || Math.Abs(before - spacing.Raw) < 2)
            {
                continue;
            }

            decided++;
            Assert.Equal(margin > 0, SpacingRule.AllowsStep(from, to, enemy, spacing));
        }

        Assert.InRange(decided, 19_000, 20_000);

        static FixedVector2 RandomPoint(DeterministicRandom random) => new(
            Fixed.FromRaw(random.NextInt(5 * Fixed.RawOne, 11 * Fixed.RawOne)),
            Fixed.FromRaw(random.NextInt(5 * Fixed.RawOne, 11 * Fixed.RawOne)));

        static double Square(double value) => value * value;

        static double Distance(FixedVector2 a, FixedVector2 b) => Math.Sqrt(Square((double)a.X.Raw - b.X.Raw) + Square((double)a.Y.Raw - b.Y.Raw));
    }

    /// <summary>The far corners of the largest map do not overflow the test.</summary>
    [Fact]
    public void TheTestHoldsAcrossTheLargestMap()
    {
        var far = Fixed.FromInt(MapGrid.MaxSize) - Fixed.Epsilon;
        var corner = new FixedVector2(far, far);

        Assert.True(SpacingRule.AllowsStep(FixedVector2.Zero, corner, new FixedVector2(far, Fixed.Zero), Fixed.One));
        Assert.False(SpacingRule.AllowsStep(FixedVector2.Zero, corner, new FixedVector2(far / 2, far / 2), Fixed.One));
        Assert.False(SpacingRule.AllowsStep(corner, FixedVector2.Zero, new FixedVector2(Fixed.One, Fixed.One * 2), OneAndAHalf));
    }

    /// <summary>
    /// The numbers of group movement decide where units stand, so changing any of them changes
    /// every recorded match.
    /// </summary>
    [Fact]
    public void TheNumbersOfGroupMovementAreRulesOfTheGame()
    {
        Assert.Equal(16, MoveCommandHandler.LongestGroup);
        Assert.Equal(Fixed.One / 256, SeparationSystem.Slack);
        Assert.Equal(Fixed.One / 8, MovementSystem.LeastSpread);
        Assert.Equal(Fixed.One / 32, MovementSystem.SteerMargin);
        Assert.Equal(Fixed.One / 4, MovementSystem.LongestSteerStep);
    }
}
