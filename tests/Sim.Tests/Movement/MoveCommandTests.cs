using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;

namespace Sim.Tests.Movement;

/// <summary>Which move orders are carried out and which are dropped.</summary>
public class MoveCommandTests
{
    private static readonly Fixed Speed = Fixed.One / 4;

    private static readonly FixedVector2 Start = TestMatches.Point("2.5", "2.5");
    private static readonly FixedVector2 Target = TestMatches.Point("8.5", "5.5");

    private static Match NewMatch() => TestMatches.Create(TestMaps.Open(12, 8));

    /// <summary>Replays and the network protocol store this number, so it can never change.</summary>
    [Fact]
    public void MoveIsCommandNumberOne()
    {
        Assert.Equal(1, (int)CommandType.Move);
    }

    [Fact]
    public void AnOrderSendsTheUnitToTheTarget()
    {
        var match = NewMatch();
        var unit = match.SpawnUnit(0, Start, Speed);

        match.OrderMove(0, Target, unit);
        match.Simulation.Tick();

        var mover = match.Movers.Get(unit);
        Assert.Equal(MoveState.Moving, mover.State);
        Assert.Equal(Target, mover.Destination);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void EveryListedUnitGoesToTheSamePoint()
    {
        var match = NewMatch();
        EntityId[] units =
        [
            match.SpawnUnit(0, TestMatches.Point("1.5", "1.5"), Speed),
            match.SpawnUnit(0, TestMatches.Point("2.5", "6.5"), Speed),
            match.SpawnUnit(0, TestMatches.Point("10.5", "3.5"), Speed),
        ];

        match.OrderMove(0, Target, units);
        match.Simulation.Run(60);

        Assert.All(units, unit => Assert.Equal(Target, match.PositionOf(unit)));
        Assert.All(units, unit => Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State));
    }

    [Fact]
    public void AUnitListedTwiceIsOrderedOnce()
    {
        var match = NewMatch();
        var unit = match.SpawnUnit(0, Start, Speed);

        match.OrderMove(0, Target, unit, unit);
        match.Simulation.Run(60);

        Assert.Equal(Target, match.PositionOf(unit));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void ATargetOnTheLastPointOfTheMapIsAccepted()
    {
        var match = NewMatch();
        var unit = match.SpawnUnit(0, Start, Speed);
        var corner = new FixedVector2(Fixed.FromInt(12) - Fixed.Epsilon, Fixed.FromInt(8) - Fixed.Epsilon);

        match.OrderMove(0, corner, unit);
        match.OrderMove(0, FixedVector2.Zero, unit);
        match.Simulation.Tick();

        // The edge of the map is blocked ground, which a destination keeps off.
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(new FixedVector2(Mover.StandOff, Mover.StandOff), match.Movers.Get(unit).Destination);
    }

    /// <summary>
    /// A unit can die between the moment an order is given and the tick it runs. The rest of the
    /// group still goes.
    /// </summary>
    [Fact]
    public void UnitsThatCannotTakeTheOrderAreSkippedAndTheRestGo()
    {
        var match = NewMatch();
        var own = match.SpawnUnit(0, Start, Speed);
        var dead = match.SpawnUnit(0, Start, Speed);
        var enemy = match.SpawnUnit(1, Start, Speed);
        var structure = match.World.Entities.Create();
        match.Bodies.Add(structure, new Body { Owner = 0, Position = Start });
        match.World.Entities.Destroy(dead);

        match.OrderMove(0, Target, dead, enemy, new EntityId(999), EntityId.None, structure, own);
        match.Simulation.Tick();

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(MoveState.Moving, match.Movers.Get(own).State);
        Assert.Equal(MoveState.Idle, match.Movers.Get(enemy).State);
        Assert.Equal(Start, match.PositionOf(enemy));
        Assert.Equal(Start, match.PositionOf(structure));
        Assert.False(match.Movers.Contains(dead));
    }

    public static TheoryData<string> DroppedOrders() =>
    [
        "no units",
        "a unit that does not exist",
        "no entity",
        "a dead unit",
        "an enemy unit",
        "something that cannot move",
        "a player below zero",
        "a player who is not in the match",
        "a target left of the map",
        "a target above the map",
        "a target on the right edge",
        "a target on the bottom edge",
    ];

    /// <summary>A dropped order changes nothing: the world is the one that never got the order.</summary>
    [Theory]
    [MemberData(nameof(DroppedOrders))]
    public void AnOrderThatMovesNobodyIsDropped(string order)
    {
        var match = Scene(out var own, out var enemy, out var dead, out var structure);
        var untouched = Scene(out _, out _, out _, out _);

        (int Player, FixedVector2 Target, EntityId[] Entities) command = order switch
        {
            "no units" => (0, Target, []),
            "a unit that does not exist" => (0, Target, [new EntityId(999)]),
            "no entity" => (0, Target, [EntityId.None]),
            "a dead unit" => (0, Target, [dead]),
            "an enemy unit" => (0, Target, [enemy]),
            "something that cannot move" => (0, Target, [structure]),
            "a player below zero" => (-1, Target, [own]),
            "a player who is not in the match" => (2, Target, [own]),
            "a target left of the map" => (0, new FixedVector2(-Fixed.Epsilon, Fixed.One), [own]),
            "a target above the map" => (0, new FixedVector2(Fixed.One, -Fixed.Epsilon), [own]),
            "a target on the right edge" => (0, new FixedVector2(Fixed.FromInt(12), Fixed.One), [own]),
            "a target on the bottom edge" => (0, new FixedVector2(Fixed.One, Fixed.FromInt(8)), [own]),
            _ => throw new ArgumentException(order, nameof(order)),
        };

        match.OrderMove(command.Player, command.Target, command.Entities);
        match.Simulation.Tick();
        untouched.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    /// <summary>One unit each for two players, a unit destroyed this tick, and a body that is not a mover.</summary>
    private static Match Scene(out EntityId own, out EntityId enemy, out EntityId dead, out EntityId structure)
    {
        var match = NewMatch();
        own = match.SpawnUnit(0, Start, Speed);
        enemy = match.SpawnUnit(1, Start, Speed);
        dead = match.SpawnUnit(0, Start, Speed);
        structure = match.World.Entities.Create();
        match.Bodies.Add(structure, new Body { Owner = 0, Position = Start });
        match.World.Entities.Destroy(dead);
        return match;
    }
}
