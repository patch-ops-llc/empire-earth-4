using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Perks;
using Sim.Setup;
using Sim.Tests.Construction;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Production;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Perks;

/// <summary>
/// Charge (design 2.3): "the first attack after moving 4 or more tiles deals +50% damage", and
/// "The distance is what the unit has actually moved since its last attack. The bonus applies to
/// all damage of one attack, then the count restarts. A unit that has not attacked yet counts
/// from when it was produced. Only walking counts; being moved off a building site does not."
/// </summary>
/// <remarks>
/// A chassis of epoch 1 is Rapid II, which is Charge, for a player with Mobility at tier 3. A
/// Hoplite's hit is 8, a Hippeus's 15 and a Peltast's 6, and a worker takes them as they are.
/// </remarks>
public class ChargeTests
{
    private const long WorkerHitPoints = 50_000;

    private static readonly int[] Rapid = DesignData.Mix(1, 1, 3, 1, 1, 1);

    private static Match Field()
    {
        var match = Create(TestMaps.Open(30, 12));
        match.SetTiers(0, Rapid);
        return match;
    }

    private static Fixed WalkedBy(Match match, EntityId unit) => match.PerkCounts.Get(unit).Walked;

    private static Fixed Tiles(string tiles) => Fraction.Parse(tiles).ToFixed();

    /// <summary>Runs ticks until an attack of the unit is reported, and returns the events of that tick.</summary>
    private static SimEvent[] RunUntilItAttacks(Match match, EntityId unit, int limit = 400)
    {
        for (var tick = 0; tick < limit; tick++)
        {
            match.Run(1);
            var attacks = match.Simulation.Events.Where(simEvent =>
                (simEvent.Kind is SimEventKind.MeleeHit or SimEventKind.ShotFired) && simEvent.Other == unit);
            if (attacks.Any())
            {
                return [.. match.Simulation.Events];
            }
        }

        throw new InvalidOperationException($"No attack in {limit} ticks.");
    }

    [Fact]
    public void TheFirstAttackAfterWalkingFourTilesOrMoreDealsHalfAgainTheDamage()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "8.5", "5.5");
        match.OrderAttack(0, worker, hoplite);

        var events = RunUntilItAttacks(match, hoplite);

        // It walked straight up to the worker, more than 4 tiles, and the count is what it walked.
        var stood = match.PositionOf(hoplite);
        Assert.True(stood.X - Tiles("2.5") >= Tiles("4"));
        Assert.Equal(WorkerHitPoints - 12_000, match.HitPointsOf(worker));

        // The charged attack is reported just before its hit, with the multiplier in thousandths.
        var charged = events.Single(simEvent => simEvent.Kind == SimEventKind.ChargedAttack);
        Assert.Equal((0, hoplite, worker, stood, 1_500L), (charged.Player, charged.Entity, charged.Other, charged.Position, charged.Amount));
        var kinds = events.Select(simEvent => simEvent.Kind).ToList();
        Assert.Equal(kinds.IndexOf(SimEventKind.ChargedAttack) + 1, kinds.IndexOf(SimEventKind.MeleeHit));
        Assert.Equal(12_000, events.Single(simEvent => simEvent.Kind == SimEventKind.MeleeHit).Amount);

        // "Then the count restarts": the next attacks, from where it stands, are plain.
        Assert.Equal(Fixed.Zero, WalkedBy(match, hoplite));
        for (var hit = 0; hit < 3; hit++)
        {
            events = RunUntilItAttacks(match, hoplite);
            Assert.DoesNotContain(events, simEvent => simEvent.Kind == SimEventKind.ChargedAttack);
            Assert.Equal(8_000, events.Single(simEvent => simEvent.Kind == SimEventKind.MeleeHit).Amount);
        }

        Assert.Equal(WorkerHitPoints - 12_000 - (3 * 8_000), match.HitPointsOf(worker));
    }

    /// <summary>The count is the distance walked, step by step: on a straight walk exactly its length.</summary>
    [Fact]
    public void TheCountIsWhatTheUnitHasWalked()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var hippeus = match.Spawn(0, Hippeus, "2.5", "8.5");
        match.OrderMove(0, TestMatches.Point("9.25", "5.5"), hoplite);
        match.OrderMove(0, TestMatches.Point("14.5", "3.5"), hippeus);

        var before = match.PositionOf(hippeus);
        long steps = 0;
        for (var tick = 0; tick < 200; tick++)
        {
            match.Run(1);
            var now = match.PositionOf(hippeus);
            steps += PerkCounters.StepLength(before, now).Raw;
            before = now;
        }

        Assert.Equal(TestMatches.Point("9.25", "5.5"), match.PositionOf(hoplite));
        Assert.Equal(Tiles("6.75"), WalkedBy(match, hoplite));

        // Slanted, each step is rounded to the nearest unit: 13 tiles to within a thousandth.
        Assert.Equal(TestMatches.Point("14.5", "3.5"), match.PositionOf(hippeus));
        Assert.Equal(steps, WalkedBy(match, hippeus).Raw);
        Assert.InRange(WalkedBy(match, hippeus).Raw, Tiles("12.999").Raw, Tiles("13.001").Raw);
    }

    /// <summary>"4 or more": exactly 4 tiles is enough, and a hair less is not.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData((4 * Fixed.RawOne) - 1, false)]
    [InlineData(4 * Fixed.RawOne, true)]
    [InlineData((4 * Fixed.RawOne) + 1, true)]
    [InlineData(int.MaxValue, true)]
    public void FourTilesIsEnoughAndLessIsNot(int walkedRaw, bool charged)
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.PerkCounts.Get(hoplite).Walked = Fixed.FromRaw(walkedRaw);
        match.OrderAttack(0, worker, hoplite);

        match.Run(1);

        Assert.Equal(WorkerHitPoints - (charged ? 12_000 : 8_000), match.HitPointsOf(worker));
        Assert.Equal(charged ? 1 : 0, match.EventsOf(SimEventKind.ChargedAttack).Length);
        Assert.Equal(Fixed.Zero, WalkedBy(match, hoplite));
    }

    /// <summary>
    /// "What the unit has actually moved since its last attack": a walk of less than 4 tiles
    /// gives a plain attack, and that attack sets the count back, so the next walk counts alone
    /// even where the two together are more than 4 tiles.
    /// </summary>
    [Fact]
    public void TheDistanceIsCountedFromTheLastAttack()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5", "5.5");
        var worker = match.SpawnWorker(1, "8.5", "5.5");
        match.OrderAttack(0, worker, hoplite);
        var start = match.PositionOf(hoplite);

        var events = RunUntilItAttacks(match, hoplite);
        Assert.DoesNotContain(events, simEvent => simEvent.Kind == SimEventKind.ChargedAttack);
        Assert.Equal(WorkerHitPoints - 8_000, match.HitPointsOf(worker));
        var first = match.PositionOf(hoplite);
        Assert.InRange((first.X - start.X).Raw, Tiles("2").Raw, Tiles("3").Raw);

        // The worker walks 3 tiles on and the Hoplite follows.
        match.OrderMove(1, TestMatches.Point("11.5", "5.5"), worker);
        match.Run(60);
        events = RunUntilItAttacks(match, hoplite);
        var second = match.PositionOf(hoplite);
        Assert.InRange((second.X - first.X).Raw, Tiles("2.5").Raw, Tiles("3.5").Raw);
        Assert.True(second.X - start.X >= Tiles("4"));
        Assert.DoesNotContain(events, simEvent => simEvent.Kind == SimEventKind.ChargedAttack);
        Assert.Equal(8_000, events.Single(simEvent => simEvent.Kind == SimEventKind.MeleeHit).Amount);
    }

    /// <summary>
    /// A shot keeps the attacker's side as it was when it was fired (design 2.4): the shot of a
    /// charged attack carries the bonus and lands with it, and the next shot is plain.
    /// </summary>
    [Fact]
    public void AShotCarriesTheBonusUntilItLands()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "13.5", "5.5");
        match.OrderAttack(0, worker, peltast);

        var events = RunUntilItAttacks(match, peltast);

        var fired = events.Single(simEvent => simEvent.Kind == SimEventKind.ShotFired);
        var charged = events.Single(simEvent => simEvent.Kind == SimEventKind.ChargedAttack);
        Assert.Equal((peltast, worker, match.PositionOf(peltast), 1_500L), (charged.Entity, charged.Other, charged.Position, charged.Amount));
        Assert.Equal(new Fraction(9, 1), match.Shots.Get(fired.Entity).Damage);
        Assert.Equal(Fixed.Zero, WalkedBy(match, peltast));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));

        // 6 with half again is 9 when it lands, and the shot after it does 6.
        var hits = new List<long>();
        for (var tick = 0; tick < 30; tick++)
        {
            match.Run(1);
            hits.AddRange(match.EventsOf(SimEventKind.ShotHit).Select(simEvent => simEvent.Amount));
            Assert.Empty(match.EventsOf(SimEventKind.ChargedAttack));
        }

        Assert.Equal([9_000, 6_000], hits);
        Assert.Equal(WorkerHitPoints - 15_000, match.HitPointsOf(worker));
    }

    /// <summary>
    /// The bonus is part of the attacker's side, and all factors are multiplied before the one
    /// rounding (design 2.4): a Hippeus's 15, half again, against the counter of its target.
    /// </summary>
    [Theory]
    [InlineData(Peltast)]
    [InlineData(Hoplite)]
    [InlineData(Ballista)]
    public void TheBonusMultipliesWithTheCounterBeforeTheDamageIsRounded(string target)
    {
        var match = Field();
        var hippeus = match.Spawn(0, Hippeus, "5.5", "5.5");
        var enemy = match.Spawn(1, target, "7", "5.5");
        match.PerkCounts.Get(hippeus).Walked = Fixed.FromInt(4);
        var full = match.HitPointsOf(enemy);
        match.OrderAttack(0, enemy, hippeus);

        match.Run(1);

        var counter = match.Combat.Multiplier(Line("cavalry_armor"), match.Combat.LineOf(Chassis(target)));
        var expected = (new Fraction(15, 1) * new Fraction(3, 2) * counter).RoundHalfUp(1000);
        Assert.Equal(full - expected, match.HitPointsOf(enemy));
        Assert.Equal(expected, Assert.Single(match.EventsOf(SimEventKind.MeleeHit)).Amount);
    }

    /// <summary>
    /// "A unit that has not attacked yet counts from when it was produced" (design 8.4, row 42):
    /// a Hippeus that comes out of the Works and walks to its rally point has walked that far,
    /// and its first attack there is a charge.
    /// </summary>
    [Fact]
    public void AUnitThatHasNotAttackedCountsFromWhenItWasProduced()
    {
        var match = ProductionMatches.Create(TestMaps.Open(30, 24));
        match.Research.SetTiers(0, Rapid);
        var works = match.Place(0, "works", 4, 10);
        match.MakeRich(0);
        var rally = TestMatches.Point("16.5", "11.5");
        match.OrderRally(0, rally, works);
        match.OrderTrain(0, works, Chassis(Hippeus));
        match.RunUntilAppeared();
        var hippeus = Assert.Single(match.Appeared());

        // It takes its first step in the tick it appears in.
        var appeared = Assert.Single(match.EventsOf(SimEventKind.UnitTrained)).Position;
        Assert.Equal(PerkCounters.StepLength(appeared, match.PositionOf(hippeus)), WalkedBy(match, hippeus));
        Assert.Equal(new Fraction(119, 1), match.PerkCounts.Get(hippeus).Paid);

        while (match.PositionOf(hippeus) != rally)
        {
            match.Run(1);
        }

        var walked = WalkedBy(match, hippeus);
        Assert.InRange(walked.Raw, Tiles("4").Raw, int.MaxValue);
        Assert.InRange(walked.Raw - FixedVector2.Distance(appeared, rally).Raw, -70, 70);

        var worker = global::Sim.Economy.EconomySetup.SpawnWorker(match, 1, new FixedVector2(rally.X + Tiles("1.5"), rally.Y));
        match.OrderAttack(0, worker, hippeus);
        match.Run(1);

        Assert.Single(match.EventsOf(SimEventKind.ChargedAttack));
        Assert.Equal(WorkerHitPoints - 22_500, match.HitPointsOf(worker));
        Assert.Equal(Fixed.Zero, WalkedBy(match, hippeus));
    }

    /// <summary>
    /// "Only walking counts": the steps by which units that stand too close move apart are not
    /// a walk, however far they take a unit.
    /// </summary>
    [Fact]
    public void SteppingApartFromItsOwnSideDoesNotCount()
    {
        var match = Field();
        var units = Enumerable.Range(0, 6).Select(_ => match.Spawn(0, Hoplite, "5.5", "5.5")).ToArray();

        match.Run(100);

        Assert.Equal(6, units.Select(unit => match.PositionOf(unit)).Distinct().Count());
        Assert.All(units, unit => Assert.Equal(Fixed.Zero, WalkedBy(match, unit)));
    }

    /// <summary>"Being moved off a building site does not": the unit is put on the nearest tile outside, and has walked nothing.</summary>
    [Fact]
    public void BeingMovedOffABuildingSiteDoesNotCount()
    {
        var match = ConstructionMatches.WithACapitol();
        var worker = match.WorkerAt(0, 10, 9);
        var standing = match.SpawnChassis(0, 0, TestMatches.Point("10.4", "10.6"));

        match.OrderBuild(0, "generator", 10, 10, worker);
        match.Simulation.Tick();

        Assert.NotEqual(EntityId.None, match.StructureAt(10, 10));
        Assert.Equal(MapGrid.TileCenter(9, 10), match.PositionOf(standing));
        Assert.Equal(Fixed.Zero, WalkedBy(match, standing));
    }

    /// <summary>
    /// A unit without Charge attacks plain however far it has walked, and its attacks set its
    /// count back all the same.
    /// </summary>
    [Fact]
    public void AUnitWithoutChargeAttacksPlainAndStillCounts()
    {
        var match = Create(TestMaps.Open(30, 12));
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        match.OrderMove(0, TestMatches.Point("7.25", "5.5"), hoplite);
        match.Run(80);
        Assert.Equal(Tiles("4.75"), WalkedBy(match, hoplite));
        var worker = match.SpawnWorker(1, "8.5", "5.5");

        match.OrderAttack(0, worker, hoplite);
        match.Run(1);

        Assert.Empty(match.EventsOf(SimEventKind.ChargedAttack));
        Assert.Equal(WorkerHitPoints - 8_000, match.HitPointsOf(worker));
        Assert.Equal(Fixed.Zero, WalkedBy(match, hoplite));
    }

    /// <summary>
    /// A unit that gains Charge while it stands on the map charges with what it has walked
    /// since its own last attack; one that loses it charges no more.
    /// </summary>
    [Fact]
    public void AUnitThatGainsChargeUsesWhatItHasWalkedAndOneThatLosesItChargesNoMore()
    {
        var match = Create(TestMaps.Open(80, 12));
        var gains = match.Spawn(0, Hoplite, "2.5", "3.5");
        var loses = match.Spawn(1, Hoplite, "62.5", "8.5");
        match.SetTiers(1, Rapid);
        match.OrderMove(0, TestMatches.Point("7.25", "3.5"), gains);
        match.OrderMove(1, TestMatches.Point("67.25", "8.5"), loses);
        match.Run(80);
        Assert.Equal((Tiles("4.75"), Tiles("4.75")), (WalkedBy(match, gains), WalkedBy(match, loses)));
        var forGains = match.SpawnWorker(1, "8.5", "3.5");
        var forLoses = match.SpawnWorker(0, "68.5", "8.5");

        match.SetTiers(0, Rapid);
        match.SetTiers(1, DesignData.Mix(1, 1, 1, 1, 1, 1));
        Assert.Contains(PerkKind.Charge, match.Blueprint(0, Hoplite).Perks.Kinds);
        Assert.DoesNotContain(PerkKind.Charge, match.Blueprint(1, Hoplite).Perks.Kinds);
        match.OrderAttack(0, forGains, gains);
        match.OrderAttack(1, forLoses, loses);
        match.Run(1);

        Assert.Equal(gains, Assert.Single(match.EventsOf(SimEventKind.ChargedAttack)).Entity);
        Assert.Equal((WorkerHitPoints - 12_000, WorkerHitPoints - 8_000), (match.HitPointsOf(forGains), match.HitPointsOf(forLoses)));
    }
}
