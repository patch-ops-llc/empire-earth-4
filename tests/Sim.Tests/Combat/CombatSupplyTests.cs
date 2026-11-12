using Sim.Setup;
using Sim.Tests.Map;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>Design 4.1: fighting units use supply, each the supply of its owner's blueprint of its chassis.</summary>
public class CombatSupplyTests
{
    private static Match Field() => Create(TestMaps.Open(24, 12));

    [Fact]
    public void EachLivingUnitUsesTheSupplyOfItsBlueprintBesideTheWorkers()
    {
        var match = Field();
        Assert.Equal(0, match.Supply.Used(0));

        // The design's table: a Hoplite and a Peltast use 2, a Hippeus and a Ballista 3, a worker 1.
        match.Spawn(0, Hoplite, "2.5", "2.5");
        match.Spawn(0, Hoplite, "3.5", "2.5");
        match.Spawn(0, Peltast, "4.5", "2.5");
        match.Spawn(0, Hippeus, "5.5", "2.5");
        match.Spawn(0, Ballista, "6.5", "2.5");
        match.SpawnWorker(0, "7.5", "2.5");
        match.Spawn(1, Ballista, "20.5", "2.5");

        Assert.Equal(2 + 2 + 2 + 3 + 3 + 1, match.Supply.Used(0));
        Assert.Equal(match.Blueprint(1, Ballista).Supply, match.Supply.Used(1));
        Assert.Equal(match.Supply.Cap(0) - 13, match.Supply.Free(0));
        Assert.True(match.Supply.HasRoomFor(0, match.Supply.Cap(0) - 13));
        Assert.False(match.Supply.HasRoomFor(0, match.Supply.Cap(0) - 12));
    }

    [Fact]
    public void AUnitThatDiesFreesItsSupplyInTheTickItDies()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "2.5");
        match.Spawn(0, Hippeus, "5.5", "2.5");
        Assert.Equal(5, match.Supply.Used(0));

        match.SetHitPoints(hoplite, 0);
        match.Run(1);

        Assert.Equal(3, match.Supply.Used(0));
    }

    [Fact]
    public void AUnitMadeByTheDevelopmentSpawnUsesSupplyFromTheTickItIsMade()
    {
        var match = Field();
        match.OrderSpawn(1, Hippeus, "5.5", "2.5");
        Assert.Equal(0, match.Supply.Used(1));
        match.Run(1);

        Assert.Equal(3, match.Supply.Used(1));
        Assert.Equal(0, match.Supply.Used(0));
    }

    [Fact]
    public void TheSupplyUsedIsTheSameAfterASnapshotIsRestored()
    {
        var match = Field();
        match.Spawn(0, Hoplite, "2.5", "2.5");
        match.Spawn(0, Ballista, "6.5", "2.5");
        var writer = new Sim.Serialization.ByteWriter();
        Sim.Serialization.Snapshot.Write(match.World, writer);

        var restored = Field();
        Sim.Serialization.Snapshot.Restore(restored.Simulation, new Sim.Serialization.ByteReader(writer.ToArray()));
        restored.RefreshDerivedState();

        Assert.Equal(5, restored.Supply.Used(0));
    }
}
