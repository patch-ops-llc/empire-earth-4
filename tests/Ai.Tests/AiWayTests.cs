using Sim.Combat;
using Sim.Core;
using Sim.Map;

namespace Ai.Tests;

/// <summary>
/// What it does about a unit that an enemy stands in the way of (design 2.4: a blocked unit
/// keeps its order and waits, and fights what is in its way only on an attack-move). Each test
/// puts a row of the other player's workers across the northern gap in the ridge of Ridge, and a
/// unit on its way through it: workers block like any unit and fight nobody.
/// </summary>
public class AiWayTests
{
    private const int FromX = 42;
    private const int ToX = 54;
    private const int Y = 22;

    /// <summary>The gap is the tiles 20 to 25 of the columns 46 to 49.</summary>
    private const int RowX = 47;
    private const int GapFirstY = 20;
    private const int GapLastY = 25;

    [Fact]
    public void AUnitThatStandsBlockedOnAMoveOrderIsToldToFightWhatIsInItsWay()
    {
        var (fixture, way) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y);
        Row(fixture);
        Order(fixture, CommandType.Move, unit);

        var order = RoundsUntilAnOrder(fixture, way, out var blockedFor);

        Assert.InRange(blockedFor, AiWay.BlockedTicks, AiWay.BlockedTicks + (2 * AiPlayer.ThinkInterval));
        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal([unit], order.Entities.ToArray());
        Assert.Equal((ToX, Y), (order.TargetX.FloorToInt(), order.TargetY.FloorToInt()));
        Assert.True(way.IsClearing(unit));
        Assert.Equal(1, way.Cleared);

        // On the attack-move it fights its way through, and arrives.
        fixture.Carry();
        for (var round = 0; round < 400 && Fighter(fixture, unit).Position.X.FloorToInt() < ToX - 1; round++)
        {
            way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);
            Assert.Empty(fixture.Commands);
            fixture.Run(AiPlayer.ThinkInterval);
        }

        Assert.InRange(Fighter(fixture, unit).Position.X.FloorToInt(), ToX - 1, ToX + 1);
        way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);
        Assert.False(way.IsClearing(unit));
        Assert.Equal(1, way.Cleared);
    }

    [Fact]
    public void SoIsOneOnAnOrderToAttackSomethingBehindWhatBlocksIt()
    {
        var (fixture, way) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y);
        Row(fixture);
        var behind = fixture.SpawnWorkerAt(1, ToX + 8, Y);

        // One of its own beyond the gap, which sees the worker there.
        fixture.SpawnAt(AiFixture.Player, "peltast", ToX + 8, Y + 3);
        fixture.Commands.Add(new Command(CommandType.Attack, AiFixture.Player, [unit], targetEntity: behind));
        fixture.Carry();
        Assert.Equal(UnitOrder.Attack, Fighter(fixture, unit).Order);

        var order = RoundsUntilAnOrder(fixture, way, out _);

        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal([unit], order.Entities.ToArray());
    }

    [Fact]
    public void TheOtherOrdersOfTheRoundForAUnitItHasOnAnAttackMoveAreTakenOut()
    {
        var (fixture, way) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y);
        var other = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y + 12);
        Row(fixture);
        Order(fixture, CommandType.Move, unit);
        RoundsUntilAnOrder(fixture, way, out _);
        fixture.Carry();
        fixture.Run(AiPlayer.ThinkInterval);
        Assert.True(way.IsClearing(unit));

        // What the parts that lead units would give in a round: none of it reaches the unit.
        var home = MapGrid.TileCenter(FromX - 2, Y);
        fixture.Commands.Add(new Command(CommandType.Train, AiFixture.Player, [fixture.Sight.Structures[0].Entity]));
        fixture.Commands.Add(new Command(CommandType.Move, AiFixture.Player, [unit], home.X, home.Y));
        fixture.Commands.Add(new Command(CommandType.Move, AiFixture.Player, [unit, other], home.X, home.Y));
        way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);

        Assert.Equal(2, fixture.Commands.Count);
        Assert.Equal(CommandType.Train, fixture.Commands[0].Type);
        Assert.Equal(CommandType.Move, fixture.Commands[1].Type);
        Assert.Equal([other], fixture.Commands[1].Entities.ToArray());
        Assert.Equal(home.X, fixture.Commands[1].TargetX);
    }

    [Fact]
    public void OrdersGivenBeforeTheRoundAreNotItsToChange()
    {
        var (fixture, way) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y);
        Row(fixture);
        Order(fixture, CommandType.Move, unit);
        RoundsUntilAnOrder(fixture, way, out _);
        fixture.Carry();
        fixture.Run(AiPlayer.ThinkInterval);

        var home = MapGrid.TileCenter(FromX - 2, Y);
        fixture.Commands.Add(new Command(CommandType.Move, AiFixture.Player, [unit], home.X, home.Y));
        way.Think(AiPlayer.ThinkInterval, fixture.Commands, first: 1);

        Assert.Single(fixture.Commands);
    }

    [Fact]
    public void AUnitWithNothingInItsWayIsGivenNothing()
    {
        var (fixture, way) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y);
        Order(fixture, CommandType.Move, unit);

        for (var round = 0; round < 20; round++)
        {
            way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);
            Assert.Empty(fixture.Commands);
            fixture.Run(AiPlayer.ThinkInterval);
        }

        Assert.Equal(0, way.Cleared);
        Assert.False(way.IsClearing(unit));
    }

    [Fact]
    public void TheScoutIsLeftOnItsOrder()
    {
        var (fixture, way) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y);
        fixture.Sight.Scout = unit;
        Row(fixture);
        Order(fixture, CommandType.Move, unit);

        for (var round = 0; round < 20; round++)
        {
            way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);
            Assert.Empty(fixture.Commands);
            fixture.Run(AiPlayer.ThinkInterval);
        }

        Assert.True(Fighter(fixture, unit).IsBlocked);
    }

    /// <summary>
    /// A unit is held for the attack-move only while it is on it: one that is on another order
    /// once the time an order takes has passed is the other parts' to lead again.
    /// </summary>
    [Fact]
    public void AUnitThatIsNotOnTheAttackMoveOnceItsOrderHasHadItsTimeIsGivenBack()
    {
        var (fixture, way) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", FromX, Y);
        Row(fixture);
        Order(fixture, CommandType.Move, unit);
        RoundsUntilAnOrder(fixture, way, out _);

        // The attack-move is not carried out here, so the unit stays on its move order.
        fixture.Commands.Clear();
        fixture.Run(AiPlayer.ThinkInterval);
        way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);
        Assert.True(way.IsClearing(unit));

        fixture.Run(AiPlayer.ThinkInterval);
        way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);
        Assert.False(way.IsClearing(unit));
    }

    private static (AiFixture Fixture, AiWay Way) Make()
    {
        var fixture = new AiFixture(players: 2);
        return (fixture, new AiWay(fixture.Sight));
    }

    /// <summary>A row of the other player's workers across the gap, a tile apart, which leaves no way through.</summary>
    private static void Row(AiFixture fixture)
    {
        for (var y = GapFirstY; y <= GapLastY; y++)
        {
            fixture.SpawnWorkerAt(1, RowX, y);
        }
    }

    private static void Order(AiFixture fixture, CommandType type, EntityId unit)
    {
        var to = MapGrid.TileCenter(ToX, Y);
        fixture.Commands.Add(new Command(type, AiFixture.Player, [unit], to.X, to.Y));
        fixture.Carry();
    }

    /// <summary>Has rounds thought, a round's ticks apart, until one gives an order, and says how long the unit had stood blocked then.</summary>
    private static Command RoundsUntilAnOrder(AiFixture fixture, AiWay way, out int blockedFor)
    {
        var blockedAt = -1;
        for (var round = 0; round < 200; round++)
        {
            if (blockedAt < 0 && fixture.Sight.Fighters.Any(fighter => fighter.IsBlocked))
            {
                blockedAt = fixture.Match.World.Tick;
            }

            way.Think(AiPlayer.ThinkInterval, fixture.Commands, 0);
            if (fixture.Commands.Count > 0)
            {
                blockedFor = fixture.Match.World.Tick - blockedAt;
                return Assert.Single(fixture.Commands);
            }

            fixture.Run(AiPlayer.ThinkInterval);
        }

        Assert.Fail("No order was given.");
        blockedFor = 0;
        return default;
    }

    private static OwnFighter Fighter(AiFixture fixture, EntityId unit) =>
        fixture.Sight.Fighters.Single(fighter => fighter.Entity == unit);
}
