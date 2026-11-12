using Sim.Combat;
using Sim.Core;
using Sim.Numerics;
using Sim.Perks;
using Sim.Tests.Map;
using Sim.Tests.Production;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Perks;

/// <summary>
/// The counts every unit of a chassis keeps for Regeneration, Reserves and Charge
/// (<see cref="PerkCounts"/>): who has them, how they begin, and how a step is measured.
/// </summary>
public class PerkCountsTests
{
    [Fact]
    public void TheStoreIsPartOfTheStateUnderItsName()
    {
        var match = Create(TestMaps.Open(12, 12));

        Assert.Equal("perkcounts", PerkCounts.StoreName);
        Assert.Contains(match.World.Stores, store => ReferenceEquals(store, match.PerkCounts) && store.Name == "perkcounts");
    }

    /// <summary>Every unit of a chassis has counts from the tick it is made, however it is made; nothing else has any.</summary>
    [Fact]
    public void EveryUnitOfAChassisHasCountsFromTheTickItIsMade()
    {
        var match = ProductionMatches.Create(TestMaps.Open(24, 24));
        var placed = match.Spawn(0, Hoplite, "20.5", "20.5");
        var worker = match.SpawnWorker(0, "20.5", "18.5");
        var barracks = match.Place(0, "barracks", 10, 10);
        match.MakeRich(0);
        match.Run(5);
        match.OrderSpawn(1, Peltast, "20.5", "3.5");
        match.Run(1);
        var spawned = match.Combatants.EntityAt(1);
        match.OrderTrain(0, barracks, Chassis(Peltast));
        var ticks = match.RunUntilAppeared();
        var trained = Assert.Single(match.Appeared());

        Assert.Equal(new PerkCounts(), match.PerkCounts.Get(placed));
        Assert.Equal(new PerkCounts { DamagedAt = 5 }, match.PerkCounts.Get(spawned));
        Assert.Equal(new PerkCounts { DamagedAt = 6 + ticks - 1, Paid = new Fraction(40, 1) }, match.PerkCounts.Get(trained));
        Assert.Equal(3, match.PerkCounts.Count);
        Assert.Equal(match.Combatants.Count, match.PerkCounts.Count);
        Assert.False(match.PerkCounts.Contains(worker));
        Assert.False(match.PerkCounts.Contains(barracks));
    }

    /// <summary>The counts go with the unit: at the end of the tick it is destroyed in.</summary>
    [Fact]
    public void TheCountsOfADestroyedUnitAreDropped()
    {
        var match = Create(TestMaps.Open(12, 12));
        var unit = match.Spawn(0, Hoplite, "5.5", "5.5");
        match.SetHitPoints(unit, 0);

        match.Run(1);

        Assert.Equal(0, match.PerkCounts.Count);
    }

    /// <summary>Any hit that does damage is the last damage, from a melee attack or from a shot when it lands.</summary>
    [Fact]
    public void AHitSetsTheTickOfTheLastDamage()
    {
        var match = Create(TestMaps.Open(24, 12));
        var struck = match.Spawn(0, Hoplite, "5.5", "3.5");
        var shotAt = match.Spawn(0, Hoplite, "5.5", "8.5");
        match.Run(3);
        var hoplite = match.Spawn(1, Hoplite, "6.5", "3.5");
        var peltast = match.Spawn(1, Peltast, "10.5", "8.5");
        match.OrderAttack(1, struck, hoplite);
        match.OrderAttack(1, shotAt, peltast);

        // The melee hit lands in the tick it is made, the shot some ticks after it is fired.
        match.Run(1);
        Assert.Equal((3, 0), (match.PerkCounts.Get(struck).DamagedAt, match.PerkCounts.Get(shotAt).DamagedAt));
        while (match.EventsOf(SimEventKind.ShotHit).Length == 0)
        {
            match.Run(1);
        }

        Assert.Equal(match.World.Tick - 1, match.PerkCounts.Get(shotAt).DamagedAt);
        Assert.InRange(match.PerkCounts.Get(shotAt).DamagedAt, 5, 12);
    }

    /// <summary>A step's length is rounded to the nearest unit, so long walks drift to neither side.</summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(9937, 0, 9937)]
    [InlineData(0, -9937, 9937)]
    [InlineData(3, 4, 5)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 2, 3)]
    [InlineData(5, 5, 7)]
    [InlineData(-7000, 7000, 9899)]
    [InlineData(65536, 65536, 92682)]
    public void AStepIsMeasuredToTheNearestUnit(int x, int y, int length)
    {
        var from = TestMatches.Point("7.5", "3.25");
        var to = new FixedVector2(Fixed.FromRaw(from.X.Raw + x), Fixed.FromRaw(from.Y.Raw + y));

        Assert.Equal(length, PerkCounters.StepLength(from, to).Raw);
        Assert.Equal(length, PerkCounters.StepLength(to, from).Raw);
    }

    /// <summary>The distance walked stops at the largest value and does not wrap.</summary>
    [Fact]
    public void TheDistanceWalkedStopsAtTheLargestValue()
    {
        var match = Create(TestMaps.Open(24, 12));
        var unit = match.Spawn(0, Hoplite, "2.5", "5.5");
        match.PerkCounts.Get(unit).Walked = Fixed.FromRaw(int.MaxValue - 100);
        match.OrderMove(0, TestMatches.Point("9.5", "5.5"), unit);

        match.Run(40);

        Assert.NotEqual(TestMatches.Point("2.5", "5.5"), match.PositionOf(unit));
        Assert.Equal(Fixed.MaxValue, match.PerkCounts.Get(unit).Walked);
    }
}
