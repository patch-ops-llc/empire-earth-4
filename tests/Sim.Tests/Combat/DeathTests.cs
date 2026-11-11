using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Data;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>Damage and death in one tick (design 2.4), and the hit points units and workers start with.</summary>
public class DeathTests
{
    private static Match Field() => Create(TestMaps.Open(24, 12));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void TwoUnitsThatWouldKillEachOtherInTheSameTickBothDie(int firstPlayer, int secondPlayer)
    {
        // Whoever holds the lower ID, both strike before either death takes effect.
        var match = Field();
        var first = match.Spawn(firstPlayer, Hoplite, "5.5", "5.5");
        var second = match.Spawn(secondPlayer, Hoplite, "6.5", "5.5");
        match.SetHitPoints(first, 8_000);
        match.SetHitPoints(second, 8_000);
        match.OrderAttack(firstPlayer, second, first);
        match.OrderAttack(secondPlayer, first, second);
        match.Run(1);

        Assert.False(match.IsAlive(first));
        Assert.False(match.IsAlive(second));
    }

    [Fact]
    public void EveryAttackDueInATickIsMadeEvenOnATargetAlreadyBroughtToZero()
    {
        var match = Field();
        var first = match.Spawn(0, Hoplite, "5.5", "5.5");
        var second = match.Spawn(0, Hoplite, "7.5", "5.5");
        var target = match.Spawn(1, Hoplite, "6.5", "5.5");
        match.SetHitPoints(target, 8_000);
        match.SetHitPoints(first, 8_000);
        match.OrderAttack(0, target, first, second);
        match.OrderAttack(1, first, target);
        match.Run(1);

        // Both attackers struck, and the target struck back although the first blow had felled it.
        Assert.False(match.IsAlive(target));
        Assert.False(match.IsAlive(first));
        Assert.Equal(20, match.UnitOf(second).Wait);
    }

    [Fact]
    public void AUnitAtZeroIsDestroyedAndAUnitJustAboveIsNot()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var dead = match.SpawnWorker(1, "6.5", "5.5");
        var alive = match.SpawnWorker(1, "20.5", "5.5");
        match.SetHitPoints(dead, 8_000);
        match.SetHitPoints(alive, 1);
        match.OrderAttack(0, dead, hoplite);
        match.Run(1);

        Assert.False(match.IsAlive(dead));
        Assert.True(match.IsAlive(alive));
    }

    [Fact]
    public void HitPointsStopAtZero()
    {
        var match = Field();
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.Targeting().Hit(worker, 49, Line("guard"));
        Assert.Equal(1_000, match.HitPointsOf(worker));
        match.Targeting().Hit(worker, 49, Line("guard"));
        Assert.Equal(0, match.HitPointsOf(worker));
        Assert.Equal(50_000, match.Health.Get(worker).Maximum);
    }

    [Fact]
    public void ADestroyedUnitIsRemovedAtOnceAndLeavesNothingBehind()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var victim = match.Spawn(1, Peltast, "6.5", "5.5");
        match.SetHitPoints(victim, 8_000);
        match.OrderAttack(0, victim, hoplite);
        match.Run(1);

        Assert.False(match.IsAlive(victim));
        Assert.False(match.Bodies.Contains(victim));
        Assert.False(match.Movers.Contains(victim));
        Assert.False(match.Combatants.Contains(victim));
        Assert.False(match.Health.Contains(victim));
        Assert.Equal(1, match.World.Entities.AliveCount);

        var found = new List<EntityId>();
        match.Run(1);
        match.SpatialIndex.QueryCircle(TestMatches.Point("6.5", "5.5"), Fixed.FromInt(3), found);
        Assert.Equal([hoplite], found);

        // The unit that killed it is idle where it stands.
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
    }

    [Fact]
    public void AUnitStartsWithTheHitPointsOfItsOwnersBlueprint()
    {
        var match = Field();
        match.SetTiers(1, 1, 3, 1, 1, 1, 1);
        var plain = match.Spawn(0, Hoplite, "5.5", "5.5");
        var hardened = match.Spawn(1, Hoplite, "15.5", "5.5");

        Assert.Equal(new Health { HitPoints = 130_000, Maximum = 130_000 }, match.Health.Get(plain));
        Assert.Equal(match.Blueprint(1, Hoplite).HitPoints, match.Health.Get(hardened).Maximum);
        Assert.True(match.Health.Get(hardened).Maximum > 130_000);
        Assert.Equal(match.Blueprint(1, Hoplite).SpeedPerTick, match.Movers.Get(hardened).Speed);
        Assert.Equal(Chassis(Hoplite), match.UnitOf(plain).Chassis);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(plain).Order);
        Assert.Equal(TestMatches.Point("5.5", "5.5"), match.UnitOf(plain).Anchor);
    }

    [Fact]
    public void AWorkerHas50HitPointsTimesTheHitPointFactorForEachProtectionTierOfItsOwner()
    {
        var match = Field();
        Assert.Equal(Health.Full(50_000), match.Combat.WorkerHealth(0));

        // 50 x 1.25 x 1.25 at Protection 3.
        match.SetTiers(1, 1, 3, 1, 1, 1, 1);
        Assert.Equal(Health.Full(78_125), match.Combat.WorkerHealth(1));
        var worker = match.SpawnWorker(1, "5.5", "5.5");
        Assert.Equal(78_125, match.HitPointsOf(worker));
        Assert.False(match.Combatants.Contains(worker));
    }

    [Fact]
    public void TheStartingWorkersOfAMatchHaveHitPointsAndAreNotOfAChassis()
    {
        var result = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        var data = result.Data;
        var match = MatchSetup.Create(data, data.Maps[0], 1, [new(0), new(1)]);

        Assert.Equal(2 * data.Rules.Start.Workers, match.Health.Count);
        Assert.Equal(match.Bodies.Count, match.Health.Count);
        Assert.Equal(0, match.Combatants.Count);
        Assert.Equal(0, match.Shots.Count);
        for (var i = 0; i < match.Health.Count; i++)
        {
            Assert.Equal(Health.Full(50_000), match.Health.ValueAt(i));
        }
    }
}
