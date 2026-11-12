using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>What the movement tests ask of a group of units: where its shape puts each, and how its units stand.</summary>
internal static class Groups
{
    /// <summary>
    /// The rule of design 5.3, worked out here from where the units stand: the target plus each
    /// unit's offset from the center of the box that holds them all. Call it before the order runs.
    /// </summary>
    public static FixedVector2[] PlacesAround(this Match match, FixedVector2 target, params EntityId[] units)
    {
        var positions = units.Select(match.PositionOf).ToArray();
        var middle = new FixedVector2(
            (positions.Min(position => position.X) + positions.Max(position => position.X)) / 2,
            (positions.Min(position => position.Y) + positions.Max(position => position.Y)) / 2);
        return [.. positions.Select(position => target + (position - middle))];
    }

    /// <summary>Places units in a square grid, row by row, <paramref name="spacing"/> apart.</summary>
    public static EntityId[] SpawnBlock(this Match match, int player, FixedVector2 first, int columns, int count, Fixed speed, Fixed spacing) =>
        [.. Enumerable.Range(0, count).Select(i => match.SpawnUnit(
            player,
            first + new FixedVector2(spacing * (i % columns), spacing * (i / columns)),
            speed,
            spacing))];

    /// <summary>
    /// Runs ticks until every unit listed has no order left and none of them has moved in a tick.
    /// Returns the number of ticks, the still one included.
    /// </summary>
    public static int RunUntilAtRest(this Match match, int mostTicks, params EntityId[] units)
    {
        var before = new FixedVector2[units.Length];
        for (var ticks = 1; ticks <= mostTicks; ticks++)
        {
            for (var i = 0; i < units.Length; i++)
            {
                before[i] = match.PositionOf(units[i]);
            }

            match.Simulation.Tick();
            var still = true;
            for (var i = 0; i < units.Length && still; i++)
            {
                still = match.PositionOf(units[i]) == before[i] && match.Movers.Get(units[i]).State == MoveState.Idle;
            }

            if (still)
            {
                return ticks;
            }
        }

        Assert.Fail($"After {mostTicks} ticks a unit is still moving.");
        return 0;
    }

    /// <summary>
    /// Asserts that no two of the units stand closer than the spacing between them (design 2.4).
    /// </summary>
    public static void AssertKeepTheirSpacing(this Match match, params EntityId[] units)
    {
        for (var i = 0; i < units.Length; i++)
        {
            for (var j = i + 1; j < units.Length; j++)
            {
                var spacing = SpacingRule.Between(match.Movers.Get(units[i]), match.Movers.Get(units[j]));
                var (first, second) = (match.PositionOf(units[i]), match.PositionOf(units[j]));
                Assert.True(
                    FixedVector2.DistanceSquaredRaw(first, second) >= FixedVector2.SquareRaw(spacing),
                    $"Units {units[i]} at {first} and {units[j]} at {second} stand closer than {spacing}.");
            }
        }
    }

    /// <summary>The least distance between a unit of one list and a unit of the other, squared, in raw units.</summary>
    public static long ClosestSquared(this Match match, EntityId[] first, EntityId[] second)
    {
        var least = long.MaxValue;
        foreach (var one in first)
        {
            foreach (var other in second)
            {
                least = Math.Min(least, FixedVector2.DistanceSquaredRaw(match.PositionOf(one), match.PositionOf(other)));
            }
        }

        return least;
    }

    /// <summary>
    /// Asserts that a unit stands on passable ground, no closer to any blocked tile or to the edge
    /// of the map than <see cref="Mover.Clearance"/>, give or take a thousandth of a tile of rounding.
    /// </summary>
    public static void AssertStandsClear(this Match match, EntityId unit)
    {
        var position = match.PositionOf(unit);
        var tileX = position.X.FloorToInt();
        var tileY = position.Y.FloorToInt();
        Assert.True(match.Grid.IsPassable(tileX, tileY), $"Unit {unit} stands on the blocked tile ({tileX}, {tileY}).");

        var least = FixedVector2.SquareRaw(Mover.Clearance - Fixed.FromRaw(64));
        for (var y = tileY - 1; y <= tileY + 1; y++)
        {
            for (var x = tileX - 1; x <= tileX + 1; x++)
            {
                if (match.Grid.IsPassable(x, y))
                {
                    continue;
                }

                // The nearest point of the tile's square, edges included.
                var nearest = new FixedVector2(
                    Fixed.Clamp(position.X, Fixed.FromInt(x), Fixed.FromInt(x + 1)),
                    Fixed.Clamp(position.Y, Fixed.FromInt(y), Fixed.FromInt(y + 1)));
                Assert.True(
                    FixedVector2.DistanceSquaredRaw(position, nearest) >= least,
                    $"Unit {unit} at {position} is too close to the blocked tile ({x}, {y}).");
            }
        }
    }
}
