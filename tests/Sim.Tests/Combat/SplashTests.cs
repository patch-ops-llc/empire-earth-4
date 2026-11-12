using Sim.Combat;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Overgunned II, splash (design 2.3 and 2.4; design 8.4, row 34): each hit also deals 30% of its
/// damage, taken before the counter multiplier, to every other enemy within 1 tile of the target.
/// </summary>
/// <remarks>
/// <para>
/// A chassis of epoch 1 has Overgunned II when its owner's Firepower is 3. A Hoplite then hits for
/// 8 x 1.25^2 = 12.5, so its splash is 3.75 before anything a target applies; a Peltast for
/// 6 x 1.25^2 = 9.375, with a splash of 2.8125. A worker has 50 hit points and no line.
/// </para>
/// <para>
/// A melee hit lands in the tick it is made, and the units of the other side have not moved by
/// then: these tests read the first tick. Where a shot is in the air for several ticks, those
/// round its target are workers, which stay where they stand.
/// </para>
/// </remarks>
public class SplashTests
{
    private const long Worker = 50_000;
    private static readonly long OneTile = Fixed.FromInt(1).Raw;

    /// <summary>An open field where player 0 has Firepower 3.</summary>
    private static Match Overgunned(int players = 2)
    {
        var match = Create(TestMaps.Open(32, 24), players: players);
        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        return match;
    }

    private static void Put(Match match, EntityId unit, string x, string y)
    {
        match.Bodies.Get(unit).Position = TestMatches.Point(x, y);
        match.RefreshDerivedState();
        match.Vision.Refresh();
    }

    private static SimEvent[] SplashHits(Match match) => match.EventsOf(SimEventKind.SplashHit);

    [Fact]
    public void TheProfileOfAChassisWithOvergunnedTwoHasTheSplashOfTheAccentsFile()
    {
        var match = Overgunned();

        foreach (var chassis in new[] { Hoplite, Peltast, CombatMatches.Hippeus })
        {
            Assert.Equal(new Fraction(3, 10), match.Combat.ProfileOf(0, CombatMatches.Chassis(chassis)).SplashShare);
            Assert.Equal(Fixed.FromInt(1), match.Combat.ProfileOf(0, CombatMatches.Chassis(chassis)).SplashRadius);
            Assert.Equal(Fraction.Zero, match.Combat.ProfileOf(1, CombatMatches.Chassis(chassis)).SplashShare);
            Assert.Equal(Fixed.Zero, match.Combat.ProfileOf(1, CombatMatches.Chassis(chassis)).SplashRadius);
        }

        // Overgunned I alone, at Firepower 2 for a Hoplite and at Firepower 3 for a Legionary of epoch 2, has none.
        Assert.Equal(Fixed.Zero, match.Combat.ProfileOf(0, CombatMatches.Chassis("legionary")).SplashRadius);
    }

    [Fact]
    public void AMeleeHitAlsoDealsThirtyPercentToEveryOtherEnemyWithinOneTileOfItsTarget()
    {
        var match = Overgunned();
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var atOneTile = match.SpawnWorker(1, "7.5", "5.5");
        var above = match.SpawnWorker(1, "6.5", "4.5");
        var justPast = match.SpawnWorker(1, "6.5", "6.5001");
        var onTheDiagonal = match.SpawnWorker(1, "7.5", "4.5");
        var besideTheAttacker = match.SpawnWorker(1, "4.5", "5.5");
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        // The target takes the hit and none of its own splash.
        Assert.Equal(Worker - 12_500, match.HitPointsOf(target));

        // 12.5 x 0.3 = 3.75, at one tile exactly and nearer.
        Assert.Equal(Worker - 3_750, match.HitPointsOf(atOneTile));
        Assert.Equal(Worker - 3_750, match.HitPointsOf(above));

        // Past one tile from the target's center there is none.
        Assert.Equal(Worker, match.HitPointsOf(justPast));
        Assert.Equal(Worker, match.HitPointsOf(onTheDiagonal));

        // The distance is measured from the target, not from the attacker: an enemy one tile from the
        // attacker and two from its target takes nothing.
        Assert.Equal(Squared("1"), match.DistanceSquared(hoplite, besideTheAttacker));
        Assert.Equal(Worker, match.HitPointsOf(besideTheAttacker));
    }

    /// <remarks>
    /// The units round the target belong to a third player: units of one player that stand closer
    /// than their spacing, 1.5 tiles beside a Hippeus, step apart before anyone attacks, and
    /// enemies do not push each other.
    /// </remarks>
    [Fact]
    public void EachEnemyTheSplashReachesAppliesItsOwnCounterMultiplier()
    {
        var match = Overgunned(players: 3);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.Unit(1, CombatMatches.Hippeus, "7", "5.5");
        var peltast = match.Unit(2, Peltast, "7", "4.5");
        var horse = match.Unit(2, CombatMatches.Hippeus, "7", "6.5");
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        // A Guard hits Cavalry/Armor for 1.5: the target takes 12.5 x 1.5, and the splash is 30% of the
        // 12.5, not of the 18.75. The third player's units fight too, so the events say who did what.
        var hit = Assert.Single(match.EventsOf(SimEventKind.MeleeHit), landed => landed.Other == hoplite);
        Assert.Equal((target, 18_750L), (hit.Entity, hit.Amount));
        var splashed = SplashHits(match);
        Assert.Equal(2, splashed.Length);
        Assert.Equal((peltast, 3_750L), (splashed[0].Entity, splashed[0].Amount));

        // The other horse applies its own 1.5 to the splash: 3.75 x 1.5 = 5.625.
        Assert.Equal((horse, 5_625L), (splashed[1].Entity, splashed[1].Amount));
    }

    [Fact]
    public void TheSplashIsRoundedOnceAfterEveryFactor()
    {
        var match = Create(TestMaps.Open(32, 24), players: 3);
        match.SetTiers(0, 4, 1, 1, 1, 1, 1);
        var legionary = match.Unit(0, "legionary", "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var horse = match.Unit(2, CombatMatches.Hippeus, "7.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "4.5");
        match.SetHitPoints(target, 1_000_000);
        match.OrderAttack(0, target, legionary);

        match.Run(1);

        // 8 x 1.25^3 = 15.625, and 30% of it is 4.6875: 4.688 on a worker, half up.
        Assert.Equal(1_000_000 - 15_625, match.HitPointsOf(target));
        Assert.Equal(Worker - 4_688, match.HitPointsOf(worker));

        // On Cavalry/Armor 4.6875 x 1.5 = 7.03125, so 7.031. Rounding the splash first would give 4.688 x 1.5 = 7.032.
        Assert.Equal(160_000 - 7_031, match.HitPointsOf(horse));
    }

    [Fact]
    public void AStructureIsReachedWhenItsCenterIsWithinTheDistance()
    {
        var match = Overgunned();
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");

        // A Wall on the tile (7, 5) has its center one tile from the target. A Generator on the tiles
        // (6, 6) to (7, 7) touches the target's tile, and its center (7, 7) is 1.58 tiles away.
        var wall = match.PlaceStructure(1, Wall, 7, 5);
        var generator = match.PlaceStructure(1, Generator, 6, 6);
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        Assert.Equal(400_000 - 3_750, match.HitPointsOf(wall));
        Assert.Equal(500_000, match.HitPointsOf(generator));
        var hit = Assert.Single(SplashHits(match));
        Assert.Equal((wall, CenterOf(7, 5, 1)), (hit.Entity, hit.Position));
    }

    /// <summary>A structure applies its own reduction to a splash: the fortification rule of design 2.5.</summary>
    [Fact]
    public void AStructureAppliesTheFortificationRuleToASplash()
    {
        var match = Overgunned();
        match.SetTiers(1, 1, 4, 1, 1, 1, 1);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var wall = match.PlaceStructure(1, Wall, 7, 5);
        var before = match.HitPointsOf(wall);
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        // Firepower 3 against the Protection 4 of the Wall's owner: half of 3.75.
        Assert.Equal(before - 1_875, match.HitPointsOf(wall));
    }

    /// <summary>Design 8.4, row 34: splash reaches only what the attacker's line can hit.</summary>
    [Fact]
    public void SplashReachesOnlyWhatTheAttackersLineCanHit()
    {
        var match = Overgunned(players: 3);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var peltast = match.Unit(0, Peltast, "2.5", "8.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var plane = match.Unit(2, "biplane", "6.5", "6");
        var before = match.HitPointsOf(plane);
        match.SetHitPoints(target, 1_000_000);

        // A Guard cannot hit Air, and neither can its splash.
        match.OrderAttack(0, target, hoplite);
        match.Run(1);
        Assert.Single(match.EventsOf(SimEventKind.Splash));
        Assert.Empty(SplashHits(match));
        Assert.Equal(before, match.HitPointsOf(plane));

        // Line infantry can, and the splash of its shot at the same target does: 9.375 x 0.3 = 2.8125.
        // The plane is put back each tick, wherever it has flown to meanwhile.
        match.OrderStop(0, hoplite);
        match.OrderAttack(0, target, peltast);
        var splashes = Array.Empty<SimEvent>();
        for (var tick = 0; tick < 200 && splashes.Length == 0; tick++)
        {
            Put(match, plane, "6.5", "6");
            match.Run(1);
            splashes = match.EventsOf(SimEventKind.Splash);
        }

        Assert.Equal(target, Assert.Single(splashes).Other);
        var hit = Assert.Single(SplashHits(match));
        Assert.Equal((plane, 2_813L), (hit.Entity, hit.Amount));
    }

    /// <summary>Design 2.4: there is no friendly fire. Every other player is an enemy.</summary>
    [Fact]
    public void ASplashNeverTouchesItsOwnersSideAndReachesEveryOtherPlayer()
    {
        var match = Overgunned(players: 3);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var own = match.SpawnWorker(0, "6.5", "4.5");
        var ownUnit = match.Unit(0, Peltast, "7.5", "5.5");
        var third = match.SpawnWorker(2, "6.5", "6.5");
        var ownBefore = match.HitPointsOf(ownUnit);
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        Assert.Equal(Worker, match.HitPointsOf(own));
        Assert.Equal(ownBefore, match.HitPointsOf(ownUnit));
        Assert.Equal(Worker - 3_750, match.HitPointsOf(third));

        // The attacker stands within a tile of its target, and takes none of its own splash.
        Assert.Equal(match.Health.Get(hoplite).Maximum, match.HitPointsOf(hoplite));
    }

    [Fact]
    public void EveryHitSplashesAlsoTheOneThatBringsItsTargetToZero()
    {
        var match = Overgunned();
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var beside = match.SpawnWorker(1, "6.5", "6.5");
        match.SetHitPoints(target, 30_000);
        match.SetHitPoints(beside, 1_000_000);
        match.OrderAttack(0, target, hoplite);

        // Three hits of 12.5 end the 30 hit points of the target. Each of them splashes.
        var splashes = 0;
        for (var tick = 0; tick < 60 && match.IsAlive(target); tick++)
        {
            match.Run(1);
            splashes += match.EventsOf(SimEventKind.Splash).Length;
            Assert.Equal(match.EventsOf(SimEventKind.MeleeHit).Length, match.EventsOf(SimEventKind.Splash).Length);
        }

        Assert.False(match.IsAlive(target));
        Assert.Equal(3, splashes);
        Assert.Equal(1_000_000 - (3 * 3_750), match.HitPointsOf(beside));
    }

    [Fact]
    public void WhatASplashBringsToZeroDiesInThatTick()
    {
        var match = Overgunned();
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var beside = match.SpawnWorker(1, "6.5", "6.5");
        match.SetHitPoints(beside, 3_000);
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        // The event carries the whole damage, more than the worker had left.
        var hit = Assert.Single(SplashHits(match));
        Assert.Equal((beside, 3_750L), (hit.Entity, hit.Amount));
        var death = Assert.Single(match.EventsOf(SimEventKind.Death));
        Assert.Equal(beside, death.Entity);
        Assert.False(match.IsAlive(beside));
        Assert.True(match.IsAlive(target));
    }

    [Fact]
    public void AShotSplashesWhenItLandsRoundWhereItsTargetIsThen()
    {
        var match = Overgunned();
        var peltast = match.Unit(0, Peltast, "5.5", "5.5");
        var target = match.SpawnWorker(1, "10.5", "5.5");
        var whereItWas = match.SpawnWorker(1, "10.5", "4.5");
        var whereItGoes = match.SpawnWorker(1, "10.5", "8.5");
        match.SetHitPoints(target, 1_000_000);
        match.OrderAttack(0, target, peltast);
        match.Run(1);
        Assert.Equal(1, match.Shots.Count);
        Assert.Equal(Worker, match.HitPointsOf(whereItWas));

        // Nothing is splashed when the shot is fired. The target is somewhere else when the shot arrives.
        Put(match, target, "10.5", "7.5");
        var landed = match.RunTo(SimEventKind.ShotHit);

        Assert.Equal((target, 9_375L), (landed.Entity, landed.Amount));
        Assert.Equal(Worker, match.HitPointsOf(whereItWas));

        // 9.375 x 0.3 = 2.8125, which is 2.813 on a worker.
        Assert.Equal(Worker - 2_813, match.HitPointsOf(whereItGoes));
        var splash = Assert.Single(match.EventsOf(SimEventKind.Splash));
        Assert.Equal(TestMatches.Point("10.5", "7.5"), splash.Position);
    }

    /// <summary>Design 2.4: a single-target shot whose target died is lost, and any splash with it.</summary>
    [Fact]
    public void ASplashIsLostWithItsShot()
    {
        var match = Overgunned();
        var peltast = match.Unit(0, Peltast, "5.5", "5.5");
        var target = match.SpawnWorker(1, "10.5", "5.5");
        var beside = match.SpawnWorker(1, "10.5", "4.5");
        match.OrderAttack(0, target, peltast);
        match.Run(1);
        var shot = match.Shots.EntityAt(0);
        Assert.True(match.Shots.Get(shot).SplashRadius > Fixed.Zero);

        match.World.Entities.Destroy(target);
        var events = match.RunAndCollect(12);

        var lost = Assert.Single(events, reported => reported.Kind == SimEventKind.ShotLost);
        Assert.Equal((shot, target), (lost.Entity, lost.Other));
        Assert.DoesNotContain(events, reported => reported.Kind is SimEventKind.Splash or SimEventKind.SplashHit);
        Assert.Equal(Worker, match.HitPointsOf(beside));
    }

    /// <summary>Design 2.4: the attacker's side is fixed when the shot is fired.</summary>
    [Fact]
    public void AShotHasTheSplashItsAttackerHadWhenItFired()
    {
        var match = Create(TestMaps.Open(32, 24));
        var peltast = match.Unit(0, Peltast, "5.5", "5.5");
        var target = match.SpawnWorker(1, "10.5", "5.5");
        var beside = match.SpawnWorker(1, "10.5", "4.5");
        match.SetHitPoints(target, 1_000_000);
        match.SetHitPoints(beside, 1_000_000);
        match.OrderAttack(0, target, peltast);
        match.Run(1);
        Assert.Equal(Fixed.Zero, match.Shots.ValueAt(0).SplashRadius);

        // Its owner's Firepower reaches 3 while the shot is in the air. That shot has no splash.
        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        var landed = match.RunTo(SimEventKind.ShotHit);
        Assert.Equal(6_000, landed.Amount);
        Assert.Empty(match.EventsOf(SimEventKind.Splash));
        Assert.Equal(1_000_000, match.HitPointsOf(beside));

        // The next one does.
        match.RunTo(SimEventKind.Splash);
        Assert.Equal(1_000_000 - 2_813, match.HitPointsOf(beside));
    }

    /// <summary>Design 2.3: splash applies to attacks without a blast radius, and the blast radius bonus to attacks with one.</summary>
    [Fact]
    public void AnAttackWithABlastRadiusHasNoSplash()
    {
        var match = Overgunned();
        var profile = match.Combat.ProfileOf(0, CombatMatches.Chassis(Ballista));
        Assert.Equal(Fraction.Parse("2.25").ToFixed(), profile.Blast);
        Assert.Equal(Fixed.Zero, profile.SplashRadius);
        Assert.Equal(Fraction.Zero, profile.SplashShare);

        var ballista = match.Unit(0, Ballista, "5.5", "5.5");
        var target = match.SpawnWorker(1, "12.5", "5.5");
        match.SpawnWorker(1, "12.5", "4.5");
        match.SpawnWorker(0, "9.5", "8.5");
        match.SetHitPoints(target, 1_000_000);
        match.OrderAttack(0, target, ballista);

        var events = match.RunAndCollect(100);

        Assert.Contains(events, reported => reported.Kind == SimEventKind.Blast);
        Assert.DoesNotContain(events, reported => reported.Kind is SimEventKind.Splash or SimEventKind.SplashHit);
    }

    /// <summary>A Tower has no accents: its shots never splash, whatever its owner has researched.</summary>
    [Fact]
    public void ATowerNeverSplashes()
    {
        var match = Overgunned();
        match.PlaceStructure(0, Tower, 10, 10);
        var target = match.SpawnWorker(1, "15.5", "11");
        var beside = match.SpawnWorker(1, "15.5", "12");
        match.SetHitPoints(target, 1_000_000);

        var events = match.RunAndCollect(60);

        Assert.Contains(events, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == target);
        Assert.DoesNotContain(events, reported => reported.Kind is SimEventKind.Splash or SimEventKind.SplashHit);
        Assert.Equal(Worker, match.HitPointsOf(beside));
    }

    [Fact]
    public void AHitReportsItsSplashAndThenAHitForEachEnemyReachedInTheOrderOfTheirIds()
    {
        var match = Overgunned();
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var first = match.SpawnWorker(1, "7.5", "5.5");
        var second = match.SpawnWorker(1, "6.5", "4.5");
        match.SpawnWorker(1, "9.5", "5.5");
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        var events = match.Simulation.Events.Where(reported => reported.Kind != SimEventKind.Death).ToArray();
        Assert.Equal(
            [SimEventKind.MeleeHit, SimEventKind.Splash, SimEventKind.SplashHit, SimEventKind.SplashHit],
            events.Select(reported => reported.Kind));

        // The splash: by the attacker, round its target, one tile wide.
        var splash = events[1];
        Assert.Equal((0, hoplite, target), (splash.Player, splash.Entity, splash.Other));
        Assert.Equal((match.PositionOf(target), OneTile), (splash.Position, splash.Amount));

        // Each enemy reached: what was hit, by whom, where it stood, and the damage in thousandths.
        Assert.Equal((0, first, hoplite), (events[2].Player, events[2].Entity, events[2].Other));
        Assert.Equal((match.PositionOf(first), 3_750L), (events[2].Position, events[2].Amount));
        Assert.Equal((0, second, hoplite), (events[3].Player, events[3].Entity, events[3].Other));
        Assert.Equal((match.PositionOf(second), 3_750L), (events[3].Position, events[3].Amount));
        Assert.True(first.Value < second.Value);
    }

    [Fact]
    public void AShotReportsItsSplashAfterItsHitAndNamesTheShot()
    {
        var match = Overgunned();
        var peltast = match.Unit(0, Peltast, "5.5", "5.5");
        var target = match.SpawnWorker(1, "10.5", "5.5");
        var beside = match.SpawnWorker(1, "10.5", "4.5");
        match.OrderAttack(0, target, peltast);
        match.Run(1);
        var shot = match.Shots.EntityAt(0);

        match.RunTo(SimEventKind.ShotHit);

        var events = match.Simulation.Events.Where(reported => reported.Kind is not (SimEventKind.Death or SimEventKind.ShotFired)).ToArray();
        Assert.Equal([SimEventKind.ShotHit, SimEventKind.Splash, SimEventKind.SplashHit], events.Select(reported => reported.Kind));
        Assert.Equal((0, shot, target), (events[1].Player, events[1].Entity, events[1].Other));
        Assert.Equal((match.PositionOf(target), OneTile), (events[1].Position, events[1].Amount));
        Assert.Equal((0, beside, shot), (events[2].Player, events[2].Entity, events[2].Other));
        Assert.Equal((match.PositionOf(beside), 2_813L), (events[2].Position, events[2].Amount));
        Assert.False(match.IsAlive(shot));
    }

    /// <summary>A hit with no other enemy round its target still reports its splash, so that a view can draw it.</summary>
    [Fact]
    public void ASplashThatReachesNobodyIsStillReported()
    {
        var match = Overgunned();
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        Assert.Single(match.EventsOf(SimEventKind.Splash));
        Assert.Empty(SplashHits(match));
    }

    /// <summary>A unit without the accent reports nothing of the kind.</summary>
    [Fact]
    public void AUnitWithoutTheAccentSplashesNobody()
    {
        var match = Create(TestMaps.Open(32, 24));
        match.SetTiers(0, 2, 1, 1, 1, 1, 1);
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var beside = match.SpawnWorker(1, "6.5", "6.5");
        match.OrderAttack(0, target, hoplite);

        match.Run(1);

        Assert.Equal(Worker - 10_000, match.HitPointsOf(target));
        Assert.Equal(Worker, match.HitPointsOf(beside));
        Assert.Empty(match.EventsOf(SimEventKind.Splash));
    }

    /// <summary>The accent is read from the owner's blueprint as it is when the unit hits: research gives it to units already on the field.</summary>
    [Fact]
    public void AUnitSplashesFromTheTickItsOwnerHasTheTier()
    {
        var match = Create(TestMaps.Open(32, 24));
        var hoplite = match.Unit(0, Hoplite, "5.5", "5.5");
        var target = match.SpawnWorker(1, "6.5", "5.5");
        var beside = match.SpawnWorker(1, "6.5", "6.5");
        match.SetHitPoints(target, 1_000_000);
        match.SetHitPoints(beside, 1_000_000);
        match.OrderAttack(0, target, hoplite);
        match.Run(1);
        Assert.Equal(1_000_000, match.HitPointsOf(beside));

        match.SetTiers(0, 3, 1, 1, 1, 1, 1);
        match.RunTo(SimEventKind.MeleeHit);

        Assert.Equal(1_000_000 - 3_750, match.HitPointsOf(beside));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(20)]
    public void AMatchWithSplashingShotsInTheAirRestoresAndContinuesFromASnapshot(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(
            () =>
            {
                var match = Overgunned();
                match.Unit(0, Peltast, "5.5", "5.5");
                match.Unit(1, Hoplite, "10.5", "5.5");
                match.Unit(1, Peltast, "10.5", "4.5");
                match.RefreshDerivedState();
                return match;
            },
            [(0, CombatMatches.AttackCommand(0, new EntityId(2), new EntityId(1)))],
            snapshotTick,
            lastTick: 80);
}
