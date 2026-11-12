using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The development spawn, <see cref="CommandType.DebugSpawn"/>: one unit of a chassis from nothing
/// for the player who sends it, and only while the rules switch it on.
/// </summary>
public class DebugSpawnTests
{
    private static readonly GameData SwitchedOff =
        DataAssert.Valid(DesignData.Files().Edit(DesignData.RulesPath, "\"spawn\": true", "\"spawn\": false"));

    /// <summary>Open ground of 24 by 12 tiles with a wall on the tiles (12, 0) to (12, 5).</summary>
    private static MapDef Walled => TestMaps.FromRows(
        [.. Enumerable.Range(0, 12).Select(row => row < 6 ? "............#..........." : "........................")]);

    private static Match Field(GameData? data = null) =>
        MatchSetup.Create(data ?? DesignData.Data, Walled, 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);

    [Fact]
    public void TheSwitchIsReadFromTheRules()
    {
        Assert.True(DesignData.Data.Rules.Debug.Spawn);
        Assert.False(SwitchedOff.Rules.Debug.Spawn);
        Assert.NotEqual(DesignData.Data.ContentHash, SwitchedOff.ContentHash);
    }

    [Fact]
    public void ItMakesOneUnitOfTheChassisForTheSenderAtThePoint()
    {
        var match = Field();
        var before = match.World.Entities.Count;
        match.OrderSpawn(1, Peltast, "7.25", "8.75");
        match.Run(1);

        Assert.Equal(before + 1, match.World.Entities.Count);
        Assert.Equal(1, match.Combatants.Count);
        var unit = match.Combatants.EntityAt(0);
        var point = TestMatches.Point("7.25", "8.75");
        Assert.Equal(1, match.Bodies.Get(unit).Owner);
        Assert.Equal(point, match.PositionOf(unit));
        Assert.Equal(Chassis(Peltast), match.UnitOf(unit).Chassis);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.Equal(point, match.UnitOf(unit).Anchor);
        Assert.Equal(Health.Full(match.Blueprint(1, Peltast).HitPoints), match.Health.Get(unit));
        Assert.Equal(match.Blueprint(1, Peltast).SpeedPerTick, match.Movers.Get(unit).Speed);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Empty(match.Simulation.Events);
    }

    [Fact]
    public void EachCommandMakesOneUnitInTheOrderTheCommandsRun()
    {
        var match = Field();
        match.OrderSpawn(0, Hoplite, "3.5", "8.5");
        match.OrderSpawn(1, Ballista, "20.5", "8.5");
        match.OrderSpawn(0, Hoplite, "3.5", "8.5");
        match.Run(1);

        Assert.Equal(3, match.Combatants.Count);
        Assert.Equal(
            [(0, Chassis(Hoplite)), (1, Chassis(Ballista)), (0, Chassis(Hoplite))],
            Enumerable.Range(0, 3).Select(i => (match.Bodies.Get(match.Combatants.EntityAt(i)).Owner, match.Combatants.ValueAt(i).Chassis)));
        Assert.True(match.Combatants.EntityAt(0).Value < match.Combatants.EntityAt(1).Value);
        Assert.True(match.Combatants.EntityAt(1).Value < match.Combatants.EntityAt(2).Value);

        // Two units of one player may be made on the same point: it is the post of both. They
        // stand closer than their spacing there, so they step apart in the same tick (design 2.4).
        var point = TestMatches.Point("3.5", "8.5");
        Assert.Equal((point, point), (match.Combatants.ValueAt(0).Anchor, match.Combatants.ValueAt(2).Anchor));
        Assert.NotEqual(match.PositionOf(match.Combatants.EntityAt(0)), match.PositionOf(match.Combatants.EntityAt(2)));
    }

    [Fact]
    public void ASpawnedUnitPicksATargetOnItsFirstTurnToLookAndFights()
    {
        var match = Field();
        var worker = match.SpawnWorker(1, "6.5", "8.5");
        match.RefreshDerivedState();
        match.OrderSpawn(0, Hoplite, "5.5", "8.5");

        // It is made by a command, so it is there when the attacks of the same tick are made, and
        // it looks for a target on its own turn to look, within four ticks.
        match.Run(1 + match.Combat.LookInterval);

        Assert.Equal(50_000 - 8_000, match.HitPointsOf(worker));
    }

    [Fact]
    public void TheUnitHasTheBlueprintOfItsOwnersTiersInTheTickItIsMade()
    {
        var match = Field();
        match.SetTiers(1, 1, 3, 1, 1, 1, 1);
        match.OrderSpawn(0, Hoplite, "3.5", "8.5");
        match.OrderSpawn(1, Hoplite, "20.5", "8.5");
        match.Run(1);

        var plain = match.Health.Get(match.Combatants.EntityAt(0)).Maximum;
        var hardened = match.Health.Get(match.Combatants.EntityAt(1)).Maximum;
        Assert.Equal(130_000, plain);
        Assert.Equal(match.Blueprint(1, Hoplite).HitPoints, hardened);
        Assert.True(hardened > plain);
    }

    [Fact]
    public void AChassisThatIsNotUnlockedCanBeMade()
    {
        var match = Field();
        Assert.False(match.Blueprint(0, "void_marine").Unlocked);
        match.OrderSpawn(0, "void_marine", "3.5", "8.5");
        match.Run(1);

        Assert.Equal(Chassis("void_marine"), match.Combatants.ValueAt(0).Chassis);
    }

    [Fact]
    public void ItCostsNothingAndIsMadeEvenWithNoFreeSupply()
    {
        var match = Field();
        var cap = match.Supply.Cap(0);
        var supply = match.Blueprint(0, Hippeus).Supply;
        var units = (cap / supply) + 2;
        for (var i = 0; i < units; i++)
        {
            match.OrderSpawn(0, Hippeus, "3.5", "8.5");
        }

        match.Run(1);

        Assert.Equal(units, match.Combatants.Count);
        Assert.Equal(units * supply, match.Supply.Used(0));
        Assert.True(match.Supply.Used(0) > cap);
        Assert.False(match.Supply.HasRoomFor(0, 1));
        Assert.Equal((0, 0), (match.Stockpiles.Materials(0), match.Stockpiles.Energy(0)));
    }

    public static TheoryData<int, int, string, string> CommandsThatAreNotValid() => new()
    {
        { -1, 0, "3.5", "8.5" },
        { 2, 0, "3.5", "8.5" },
        { 0, -1, "3.5", "8.5" },
        { 0, DesignData.Data.Chassis.Count, "3.5", "8.5" },
        { 0, 0, "12.5", "2.5" },
        { 0, 0, "-0.5", "8.5" },
        { 0, 0, "3.5", "12" },
        { 0, 0, "24", "8.5" },
    };

    /// <summary>A player not in the match, a chassis the data does not have, a wall, and points off the map.</summary>
    [Theory]
    [MemberData(nameof(CommandsThatAreNotValid))]
    public void ACommandThatIsNotValidChangesNothingAndIsReported(int player, int chassis, string x, string y)
    {
        var match = Field();
        var point = TestMatches.Point(x, y);
        var hash = match.World.ComputeHash();
        match.Simulation.Commands.Enqueue(0, SpawnCommand(player, chassis, point));
        match.Run(1);

        Assert.Equal(0, match.Combatants.Count);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(
            [new SimEvent(SimEventKind.OrderRefused, 0, player, EntityId.None, EntityId.None, point, (long)CommandType.DebugSpawn)],
            match.Simulation.Events);

        // Nothing but the tick has changed.
        var untouched = Field();
        untouched.Run(1);
        Assert.NotEqual(hash, match.World.ComputeHash());
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    [Fact]
    public void WithTheSwitchOffTheCommandIsDroppedLikeAnyCommandWithoutAHandler()
    {
        var match = Field(SwitchedOff);
        match.Simulation.Commands.Enqueue(0, SpawnCommand(0, Chassis(Hoplite), TestMatches.Point("3.5", "8.5")));
        match.Run(1);

        Assert.Equal(0, match.Combatants.Count);
        Assert.Equal(0, match.Bodies.Count);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Empty(match.Simulation.Events);

        var untouched = Field(SwitchedOff);
        untouched.Run(1);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    [Fact]
    public void WithTheSwitchOffEverythingElseOfCombatStillRuns()
    {
        var match = Field(SwitchedOff);
        var hoplite = match.SpawnChassis(0, Chassis(Hoplite), TestMatches.Point("5.5", "8.5"));
        var worker = match.SpawnWorker(1, "6.5", "8.5");
        match.RefreshDerivedState();
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);

        Assert.Equal(50_000 - 8_000, match.HitPointsOf(worker));
    }

    /// <summary>
    /// A match with spawned units is saved and restored like any other: before the spawns, between
    /// them, with a shot of a spawned unit in the air, and after.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(25)]
    [InlineData(41)]
    [InlineData(80)]
    public void AMatchWithSpawnedUnitsRestoresAndContinuesFromASnapshot(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(
            () => Field(),
            [
                (0, SpawnCommand(0, Chassis(Hoplite), TestMatches.Point("8.5", "8.5"))),
                (0, SpawnCommand(0, Chassis(Peltast), TestMatches.Point("6.5", "8.5"))),
                (1, SpawnCommand(1, Chassis(Hippeus), TestMatches.Point("14.5", "8.5"))),
                (40, SpawnCommand(1, Chassis(Ballista), TestMatches.Point("20.5", "8.5"))),
            ],
            snapshotTick,
            lastTick: 120);

    /// <summary>The command survives the bytes a replay and the network give it.</summary>
    [Fact]
    public void TheCommandKeepsItsPlayerItsChassisAndItsPointThroughItsEncoding()
    {
        var command = SpawnCommand(1, Chassis(Ballista), new FixedVector2(Fixed.FromRaw(123_457), Fixed.FromRaw(7)));
        var writer = new Sim.Serialization.ByteWriter();
        Sim.Serialization.CommandEncoding.Write(writer, in command);
        var read = Sim.Serialization.CommandEncoding.Read(new Sim.Serialization.ByteReader(writer.ToArray()));

        Assert.Equal(
            (CommandType.DebugSpawn, 1, Chassis(Ballista), Fixed.FromRaw(123_457), Fixed.FromRaw(7)),
            (read.Type, read.Player, read.ContentId, read.TargetX, read.TargetY));
        Assert.Equal(8, (int)CommandType.DebugSpawn);
    }
}
