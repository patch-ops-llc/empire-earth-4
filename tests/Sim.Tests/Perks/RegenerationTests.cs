using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Production;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Perks;

/// <summary>
/// Regeneration (design 2.3): "2% of maximum hit points, added once per second, after 5 full
/// seconds without damage. Each unit counts its own ticks from the last damage taken: the first
/// regeneration comes 5 seconds after it and one more follows each second."
/// </summary>
/// <remarks>
/// A chassis of epoch 1 is Supplied I, which is Regeneration, for a player with Logistics at
/// tier 2. A Hoplite has 130 hit points, so one regeneration is 2.6.
/// </remarks>
public class RegenerationTests
{
    private const long Full = 130_000;
    private const long Share = 2_600;
    private const long Hurt = 90_000;
    private const int Delay = 100;
    private const int Second = 20;

    private static readonly int[] Supplied = DesignData.Mix(1, 1, 1, 1, 2, 1);

    private static Match Field()
    {
        var match = Create(TestMaps.Open(24, 12));
        match.SetTiers(0, Supplied);
        return match;
    }

    /// <summary>An enemy Hoplite beside a unit, with the order to attack it: it hits in the next tick that runs.</summary>
    private static EntityId Attacker(Match match, EntityId unit)
    {
        var position = match.PositionOf(unit);
        var enemy = match.SpawnChassis(1, Chassis(Hoplite), new(position.X + Fixed.One, position.Y));
        match.OrderAttack(1, unit, enemy);
        return enemy;
    }

    /// <summary>Takes an attacker away before it can hit again: the deaths of the next tick take it, 19 ticks before its next attack.</summary>
    private static void Remove(Match match, EntityId enemy) => match.SetHitPoints(enemy, 0);

    private static SimEvent[] Regenerated(Match match) => match.EventsOf(SimEventKind.Regenerated);

    [Fact]
    public void TheFirstRegenerationComesFiveSecondsAfterTheLastDamageAndOneMoreFollowsEachSecond()
    {
        var match = Field();
        var unit = match.Spawn(0, Hoplite, "5.5", "5.5");
        match.SetHitPoints(unit, Hurt + 8_000);
        var enemy = Attacker(match, unit);

        // The hit of tick 0: a Hoplite's 8.
        match.Run(1);
        Assert.Equal(Hurt, match.HitPointsOf(unit));
        Remove(match, enemy);

        for (var tick = 1; tick <= Delay + (3 * Second); tick++)
        {
            match.Run(1);

            // Nothing for 5 full seconds, then 2% of the maximum in tick 100, 120, 140 and 160.
            var due = tick >= Delay && (tick - Delay) % Second == 0;
            var times = tick < Delay ? 0 : 1 + ((tick - Delay) / Second);
            Assert.Equal(Hurt + (times * Share), match.HitPointsOf(unit));
            if (due)
            {
                var regenerated = Assert.Single(Regenerated(match));
                Assert.Equal((0, unit, match.PositionOf(unit), Share), (regenerated.Player, regenerated.Entity, regenerated.Position, regenerated.Amount));
                Assert.Equal(tick, regenerated.Tick);
            }
            else
            {
                Assert.Empty(Regenerated(match));
            }
        }

        Assert.Equal(Hurt + (4 * Share), match.HitPointsOf(unit));
    }

    /// <summary>
    /// "After 5 full seconds without damage": damage in the very tick a regeneration was due
    /// counts before it, so that tick gives nothing and the five seconds begin again.
    /// </summary>
    [Fact]
    public void DamageInTheTickARegenerationWasDueStartsTheFiveSecondsAgain()
    {
        var match = Field();
        var unit = match.Spawn(0, Hoplite, "5.5", "5.5");
        var first = Attacker(match, unit);
        match.Run(1);
        Remove(match, first);
        match.Run(Delay - 1);
        Assert.Equal((Delay, Full - 8_000), (match.World.Tick, match.HitPointsOf(unit)));

        // The second hit lands in tick 100, where the first regeneration would have come.
        var second = Attacker(match, unit);
        match.Run(1);
        Assert.Equal(Full - 16_000, match.HitPointsOf(unit));
        Assert.Empty(Regenerated(match));
        Remove(match, second);

        for (var tick = Delay + 1; tick < 2 * Delay; tick++)
        {
            match.Run(1);
            Assert.Empty(Regenerated(match));
        }

        match.Run(1);
        Assert.Equal((2 * Delay) + 1, match.World.Tick);
        Assert.Single(Regenerated(match));
        Assert.Equal(Full - 16_000 + Share, match.HitPointsOf(unit));
    }

    /// <summary>A unit that is hit once a second never regenerates, and one hit a tick later than another regenerates a tick later.</summary>
    [Fact]
    public void EachUnitCountsItsOwnTicksFromItsOwnLastDamage()
    {
        var match = Create(TestMaps.Open(60, 12));
        match.SetTiers(0, Supplied);
        var early = match.Spawn(0, Hoplite, "3.5", "3.5");
        var late = match.Spawn(0, Hoplite, "28.5", "3.5");
        var besieged = match.Spawn(0, Hoplite, "55.5", "8.5");
        var besieger = Attacker(match, besieged);
        var onEarly = Attacker(match, early);
        match.Run(7);
        Remove(match, onEarly);
        var onLate = Attacker(match, late);
        match.Run(1);
        Remove(match, onLate);
        Assert.Equal((0, 7), (match.PerkCounts.Get(early).DamagedAt, match.PerkCounts.Get(late).DamagedAt));

        var regenerated = new List<(int Tick, EntityId Unit)>();
        while (match.World.Tick <= Delay + 7 + Second)
        {
            match.Run(1);
            regenerated.AddRange(Regenerated(match).Select(simEvent => (simEvent.Tick, simEvent.Entity)));
        }

        Assert.Equal([(100, early), (107, late), (120, early), (127, late)], regenerated);

        // The one under attack was hit in tick 0, 20, 40 and so on: it has taken damage in tick 120 and has never had five seconds.
        Assert.True(match.IsAlive(besieger));
        Assert.Equal(120, match.PerkCounts.Get(besieged).DamagedAt);
        Assert.Equal(Full - (7 * 8_000), match.HitPointsOf(besieged));
    }

    /// <summary>A unit that has taken no damage counts from the tick it was made, and nothing is added above the maximum.</summary>
    [Fact]
    public void ItStopsAtTheMaximumAndAFullUnitGetsNothing()
    {
        var match = Field();
        var unit = match.Spawn(0, Hoplite, "5.5", "5.5");
        var full = match.Spawn(0, Hoplite, "9.5", "5.5");
        match.SetHitPoints(unit, Full - Share - 500);

        match.Run(Delay);
        Assert.Empty(Regenerated(match));
        match.Run(1);
        Assert.Equal(Share, Assert.Single(Regenerated(match)).Amount);
        Assert.Equal(Full - 500, match.HitPointsOf(unit));

        // The second is cut to what was missing, and after that there is nothing to add.
        match.Run(Second);
        var last = Assert.Single(Regenerated(match));
        Assert.Equal((unit, 500L), (last.Entity, last.Amount));
        Assert.Equal(Full, match.HitPointsOf(unit));
        for (var tick = 0; tick < 3 * Second; tick++)
        {
            match.Run(1);
            Assert.Empty(Regenerated(match));
        }

        Assert.Equal((Full, Full), (match.HitPointsOf(unit), match.HitPointsOf(full)));
    }

    /// <summary>A unit made in a later tick counts from that tick.</summary>
    [Fact]
    public void AUnitMadeLaterCountsFromTheTickItWasMade()
    {
        var match = Field();
        match.Run(33);
        match.OrderSpawn(0, Hoplite, "5.5", "5.5");
        match.Run(1);
        var unit = match.Combatants.EntityAt(0);
        Assert.Equal(33, match.PerkCounts.Get(unit).DamagedAt);
        match.SetHitPoints(unit, 1_000);

        match.Run(Delay - 1);
        Assert.Equal(1_000, match.HitPointsOf(unit));
        match.Run(1);
        Assert.Equal((134, 1_000 + Share), (match.World.Tick, match.HitPointsOf(unit)));
    }

    /// <summary>
    /// "2% of maximum hit points", of the unit's own maximum and rounded half up to the
    /// thousandth: a Hoplite with Protection at tier 3 has 203.125, of which 2% is 4.0625.
    /// </summary>
    [Fact]
    public void TheShareIsOfTheUnitsOwnMaximumRoundedHalfUp()
    {
        var match = Create(TestMaps.Open(24, 12));
        match.SetTiers(0, DesignData.Mix(1, 3, 1, 1, 2, 1));
        Assert.Contains(PerkKind.Regeneration, match.Blueprint(0, Hoplite).Perks.Kinds);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var hippeus = match.Spawn(0, Hippeus, "9.5", "5.5");
        Assert.Equal((203_125, 250_000), (match.Health.Get(hoplite).Maximum, match.Health.Get(hippeus).Maximum));
        match.SetHitPoints(hoplite, 100_000);
        match.SetHitPoints(hippeus, 100_000);

        match.Run(Delay + 1);

        Assert.Equal((100_000 + 4_063, 100_000 + 5_000), (match.HitPointsOf(hoplite), match.HitPointsOf(hippeus)));
    }

    /// <summary>Only a unit whose chassis carries the perk: not one of a player without it, and never a worker or a structure.</summary>
    [Fact]
    public void AUnitWithoutThePerkGetsNothing()
    {
        var match = Create(TestMaps.Open(60, 12));
        match.SetTiers(0, Supplied);
        var enemy = match.Spawn(1, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(0, "55.5", "5.5");
        match.SetHitPoints(enemy, 50_000);
        match.SetHitPoints(worker, 10_000);

        for (var tick = 0; tick < 3 * Delay; tick++)
        {
            match.Run(1);
            Assert.Empty(Regenerated(match));
        }

        Assert.Equal((50_000, 10_000), (match.HitPointsOf(enemy), match.HitPointsOf(worker)));
        Assert.False(match.PerkCounts.Contains(worker));
    }

    /// <summary>A unit that dies in the tick its regeneration was due gets nothing: the deaths come first.</summary>
    [Fact]
    public void AUnitThatDiesInTheTickGetsNothing()
    {
        var match = Field();
        var unit = match.Spawn(0, Hoplite, "5.5", "5.5");
        match.SetHitPoints(unit, 1_000);
        match.Run(Delay);
        match.SetHitPoints(unit, 0);

        match.Run(1);

        Assert.False(match.IsAlive(unit));
        Assert.Single(match.EventsOf(SimEventKind.Death));
        Assert.Empty(Regenerated(match));
    }

    /// <summary>
    /// A research gives the perk to the units that already stand, from the tick after it
    /// finishes, and each goes on from its own count: the one whose regeneration would have
    /// been due in the tick the research finished waits a second for its next.
    /// </summary>
    [Fact]
    public void AUnitThatGainsThePerkByAResearchGoesOnFromItsOwnCount()
    {
        var match = ProductionMatches.Create(TestMaps.Open(24, 24));
        match.Place(0, "capitol", 2, 2);
        match.MakeRich(0);
        match.OrderResearch(0, "logistics");
        match.Run(1);
        match.RunUntilResearchHas(0, ticksLeft: Delay + 1);
        var a = match.Spawn(0, Hoplite, "10.5", "10.5");
        var b = match.Spawn(0, Hoplite, "10.5", "15.5");
        var finishes = match.World.Tick + Delay;

        // One is hit in the tick 100 before the research finishes, the other a tick later.
        var onA = Attacker(match, a);
        match.Run(1);
        Remove(match, onA);
        var onB = Attacker(match, b);
        match.Run(1);
        Remove(match, onB);
        Assert.Equal((finishes - Delay, finishes - Delay + 1), (match.PerkCounts.Get(a).DamagedAt, match.PerkCounts.Get(b).DamagedAt));

        while (match.World.Tick <= finishes)
        {
            match.Run(1);
            Assert.Empty(Regenerated(match));
        }

        Assert.Equal(2, match.Research.State.Tier(0, DesignData.Track("logistics")));
        Assert.Contains(PerkKind.Regeneration, match.Blueprint(0, Hoplite).Perks.Kinds);

        var regenerated = new List<(int Tick, EntityId Unit)>();
        for (var tick = 0; tick < 2 * Second; tick++)
        {
            match.Run(1);
            regenerated.AddRange(Regenerated(match).Select(simEvent => (simEvent.Tick, simEvent.Entity)));
        }

        Assert.Equal([(finishes + 1, b), (finishes + Second, a), (finishes + Second + 1, b), (finishes + (2 * Second), a)], regenerated);
    }

    /// <summary>
    /// A displaced accent (design 2.3): a Hoplite that is Hardened I and Supplied I loses
    /// Supplied when Firepower reaches tier 2, and regenerates no more from the tick after.
    /// </summary>
    [Fact]
    public void AUnitThatLosesThePerkByAResearchRegeneratesNoMore()
    {
        var match = ProductionMatches.Create(TestMaps.Open(24, 24));
        match.Research.SetTiers(0, DesignData.Mix(1, 2, 1, 1, 2, 1));
        Assert.Contains(PerkKind.Regeneration, match.Blueprint(0, Hoplite).Perks.Kinds);
        match.Place(0, "capitol", 2, 2);
        match.MakeRich(0);
        match.OrderResearch(0, "firepower");
        match.Run(1);
        match.RunUntilResearchHas(0, ticksLeft: 2 * Second);
        var unit = match.Spawn(0, Hoplite, "10.5", "10.5");
        match.SetHitPoints(unit, 1_000);
        match.PerkCounts.Get(unit).DamagedAt = match.World.Tick - Delay;

        // Due in the next tick and each second after: twice before the research finishes.
        var regenerated = new List<int>();
        var made = match.World.Tick;
        for (var tick = 0; tick < 6 * Second; tick++)
        {
            match.Run(1);
            regenerated.AddRange(Regenerated(match).Select(simEvent => simEvent.Tick - made));
        }

        Assert.Equal([0, Second], regenerated);
        Assert.DoesNotContain(PerkKind.Regeneration, match.Blueprint(0, Hoplite).Perks.Kinds);

        // Hardened I: 162.5 hit points, of which 2% is 3.25.
        Assert.Equal(1_000 + (2 * 3_250), match.HitPointsOf(unit));
    }
}
