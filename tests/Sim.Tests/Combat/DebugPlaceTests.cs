using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The development placement, <see cref="CommandType.DebugPlace"/>: one finished structure from
/// nothing for the player who sends it, and only while the rules switch the development spawn on.
/// </summary>
public class DebugPlaceTests
{
    private static readonly GameData SwitchedOff =
        DataAssert.Valid(Files().Edit(DesignData.RulesPath, "\"spawn\": true", "\"spawn\": false"));

    /// <summary>Open ground of 24 by 12 tiles with a wall of the map on the tiles (12, 0) to (12, 5).</summary>
    private static Match Walled(GameData? data = null) => Create(
        TestMaps.FromRows([.. Enumerable.Range(0, 12).Select(row => row < 6 ? "............#..........." : "........................")]),
        data);

    [Theory]
    [InlineData(Capitol, 4, 2_000_000)]
    [InlineData(Barracks, 3, 1_000_000)]
    [InlineData(Wall, 1, 400_000)]
    [InlineData(Tower, 2, 600_000)]
    public void ItPlacesOneFinishedStructureForTheSenderWithItsLowestTileOnTheTileOfThePoint(string structure, int size, long hitPoints)
    {
        var match = Walled();
        match.Simulation.Commands.Enqueue(0, PlaceCommand(1, StructureIndex(structure), TestMatches.Point("3.9", "7.1")));

        match.Run(1);

        Assert.Equal(1, match.Structures.Count);
        var placed = match.Structures.EntityAt(0);
        var center = CenterOf(3, 7, size);
        Assert.Equal(new PlacedStructure { Type = StructureIndex(structure), X = 3, Y = 7, Size = size }, match.Structures.Get(placed));
        Assert.Equal((1, center), (match.Bodies.Get(placed).Owner, match.PositionOf(placed)));
        Assert.Equal(hitPoints, match.HitPointsOf(placed));
        Assert.Equal(
            [new SimEvent(SimEventKind.StructurePlaced, 0, 1, placed, EntityId.None, center, StructureIndex(structure))],
            match.Simulation.Events);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.False(match.Grid.IsPassable(3, 7));
        Assert.False(match.Grid.IsPassable(3 + size - 1, 7 + size - 1));
        Assert.True(match.Grid.IsPassable(3 + size, 7));
        Assert.Equal(structure == Tower ? 1 : 0, match.Turrets.Count);
    }

    [Fact]
    public void ATowerPlacedThisWayAttacksAndAStructureIsPlacedThoughItIsNotUnlockedAndCostsNothing()
    {
        var match = Walled();
        var worker = match.SpawnWorker(0, "8.5", "8.5");
        var stock = (match.Stockpiles.Materials(1), match.Stockpiles.Energy(1));
        match.OrderPlace(1, Tower, 3, 7);

        var events = match.RunAndCollect(30);

        Assert.Contains(events, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == worker && reported.Amount == 10_000);
        Assert.Equal(stock, (match.Stockpiles.Materials(1), match.Stockpiles.Energy(1)));
        Assert.Equal(0, match.Territories.Count);
    }

    public static TheoryData<int, int, string, string> CommandsThatAreNotValid => new()
    {
        { -1, StructureIndex(Tower), "3.5", "8.5" },
        { 2, StructureIndex(Tower), "3.5", "8.5" },
        { 0, -1, "3.5", "8.5" },
        { 0, SiegeMatches.Data.Rules.Structures.Count, "3.5", "8.5" },
        { 0, SiegeMatches.Data.Rules.Structures.ToList().IndexOf("airfield"), "3.5", "8.5" },
        { 0, StructureIndex(Tower), "12.5", "2.5" },
        { 0, StructureIndex(Tower), "11.5", "2.5" },
        { 0, StructureIndex(Tower), "-0.5", "8.5" },
        { 0, StructureIndex(Tower), "23.5", "8.5" },
        { 0, StructureIndex(Tower), "3.5", "11.5" },
        { 0, StructureIndex(Tower), "19.5", "8.5" },
        { 0, StructureIndex(Tower), "17.5", "7.5" },
        { 0, StructureIndex(Capitol), "9.5", "3.5" },
        { 0, StructureIndex(Tower), "7.5", "9.5" },
    };

    /// <summary>
    /// A player not in the match, a structure the roster does not have or the data has no file
    /// for, blocked ground, a footprint that reaches it or leaves the map, another structure in
    /// the way, and a unit standing on one of the tiles.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommandsThatAreNotValid))]
    public void ACommandThatIsNotValidChangesNothingAndIsReported(int player, int structure, string x, string y)
    {
        Match Build()
        {
            var match = Walled();
            match.PlaceStructure(1, Barracks, 18, 8);
            match.SpawnWorker(1, "8.5", "10.5");
            return match;
        }

        var match = Build();
        var point = TestMatches.Point(x, y);
        match.Simulation.Commands.Enqueue(0, PlaceCommand(player, structure, point));
        match.Run(1);

        Assert.Equal(1, match.Structures.Count);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(
            [new SimEvent(SimEventKind.OrderRefused, 0, player, EntityId.None, EntityId.None, point, (long)CommandType.DebugPlace)],
            match.Simulation.Events);

        var untouched = Build();
        untouched.Run(1);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    /// <summary>A unit made by a command earlier in the same tick is in the way like any other.</summary>
    [Fact]
    public void AUnitMadeEarlierInTheSameTickIsInTheWay()
    {
        var match = Walled();
        match.Simulation.Commands.Enqueue(0, CombatMatches.SpawnCommand(0, CombatMatches.Chassis(Hoplite), TestMatches.Point("4.5", "8.5")));
        match.OrderPlace(1, Tower, 4, 8);
        match.OrderPlace(1, Tower, 6, 8);

        match.Run(1);

        Assert.Equal(1, match.Structures.Count);
        Assert.Equal(6, match.Structures.ValueAt(0).X);
        Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
    }

    /// <summary>
    /// A player has room for two Academies (design 2.5), and each gives a research slot. A third
    /// would have none to give, so it is refused.
    /// </summary>
    [Fact]
    public void AThirdAcademyIsRefused()
    {
        var match = Walled();
        match.OrderPlace(0, Academy, 2, 7);
        match.OrderPlace(0, Academy, 5, 7);
        match.OrderPlace(0, Academy, 8, 7);
        match.OrderPlace(1, Academy, 15, 7);

        match.Run(1);

        Assert.Equal(3, match.Structures.Count);
        Assert.Equal([0, 0, 1], Enumerable.Range(0, 3).Select(i => match.Bodies.Get(match.Structures.EntityAt(i)).Owner));
        Assert.Equal(0, Assert.Single(match.EventsOf(SimEventKind.OrderRefused)).Player);
    }

    [Fact]
    public void WithTheSwitchOffTheCommandIsDroppedLikeAnyCommandWithoutAHandler()
    {
        var match = Walled(SwitchedOff);
        match.Simulation.Commands.Enqueue(0, PlaceCommand(0, StructureIndex(Tower), TestMatches.Point("3.5", "8.5")));
        match.Run(1);

        Assert.Equal(0, match.Structures.Count);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Empty(match.Simulation.Events);

        var untouched = Walled(SwitchedOff);
        untouched.Run(1);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    /// <summary>With the switch off, structures are still targets and Towers still attack.</summary>
    [Fact]
    public void WithTheSwitchOffStructuresStillFight()
    {
        var match = Walled(SwitchedOff);
        var tower = match.PlaceStructure(1, Tower, 3, 7);
        var hoplite = match.Unit(0, Hoplite, "2.5", "8");
        match.OrderAttack(0, tower, hoplite);

        var events = match.RunAndCollect(30);

        Assert.Contains(events, reported => reported.Kind == SimEventKind.MeleeHit && reported.Entity == tower);
        Assert.Contains(events, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == hoplite);
    }

    /// <summary>The command survives the bytes a replay and the network give it.</summary>
    [Fact]
    public void TheCommandKeepsItsPlayerItsStructureAndItsPointThroughItsEncoding()
    {
        var command = PlaceCommand(1, StructureIndex(Tower), new FixedVector2(Fixed.FromRaw(123_457), Fixed.FromRaw(7)));
        var writer = new ByteWriter();
        CommandEncoding.Write(writer, in command);
        var read = CommandEncoding.Read(new ByteReader(writer.ToArray()));

        Assert.Equal(
            (CommandType.DebugPlace, 1, StructureIndex(Tower), Fixed.FromRaw(123_457), Fixed.FromRaw(7)),
            (read.Type, read.Player, read.ContentId, read.TargetX, read.TargetY));
        Assert.Equal(16, (int)CommandType.DebugPlace);
    }
}
