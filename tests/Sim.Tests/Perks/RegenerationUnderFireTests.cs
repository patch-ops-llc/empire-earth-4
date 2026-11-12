using Sim.Core;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Tech;

namespace Sim.Tests.Perks;

/// <summary>
/// Regeneration counts "from the last damage taken" (design 2.3), whatever dealt it: the splash
/// of a hit on someone else, the blast of a shell and the shot of a Tower start a unit's five
/// seconds again like a hit of its own.
/// </summary>
/// <remarks>
/// A chassis of epoch 1 has Regeneration for a player with Logistics at tier 2, and splash for
/// one with Firepower at tier 3. The Tower is that of <see cref="SiegeMatches"/>: 10 once a
/// second at range 7.
/// </remarks>
public class RegenerationUnderFireTests
{
    private const int Delay = 100;

    private static readonly int[] Supplied = DesignData.Mix(1, 1, 1, 1, 2, 1);

    private static readonly int[] Overgunned = DesignData.Mix(3, 1, 1, 1, 1, 1);

    private static Match Field()
    {
        var match = SiegeMatches.Field();
        match.SetTiers(0, Supplied);
        return match;
    }

    /// <summary>
    /// Runs ticks and returns their events. After each tick, every unit that a hit of that tick
    /// damaged counts from that tick.
    /// </summary>
    private static List<SimEvent> RunAndCheckTheCounts(Match match, int ticks)
    {
        var events = new List<SimEvent>();
        for (var tick = 0; tick < ticks; tick++)
        {
            match.Run(1);
            foreach (var reported in match.Simulation.Events)
            {
                var landed = reported.Kind is SimEventKind.MeleeHit or SimEventKind.ShotHit or SimEventKind.SplashHit;
                if (landed && reported.Amount > 0 && match.IsAlive(reported.Entity) && match.PerkCounts.Contains(reported.Entity))
                {
                    Assert.Equal(reported.Tick, match.PerkCounts.Get(reported.Entity).DamagedAt);
                }
            }

            events.AddRange(match.Simulation.Events);
        }

        return events;
    }

    /// <summary>
    /// Two Hoplites stand a tile apart, and an enemy that splashes is ordered onto the first. The
    /// second is never hit itself: the splash alone keeps it from regenerating, and its first
    /// regeneration comes five seconds after the last splash reached it.
    /// </summary>
    [Fact(Skip = TestBuild.PerksMeetMeleePlaces)]
    public void ASplashStartsTheFiveSecondsAgain()
    {
        var match = Field();
        match.SetTiers(1, Overgunned);
        var first = match.Unit(0, SiegeMatches.Hoplite, "6.5", "5.5");
        var second = match.Unit(0, SiegeMatches.Hoplite, "5.5", "5.5");
        match.SetHitPoints(second, 60_000);
        var enemy = match.Unit(1, SiegeMatches.Hoplite, "7.5", "5.5");

        // Half its hit points: the first Hoplite, which hits back for 8, outlives it.
        match.SetHitPoints(enemy, 64_000);
        match.OrderAttack(1, first, enemy);

        var events = RunAndCheckTheCounts(match, 500);

        Assert.DoesNotContain(events, reported => (reported.Kind is SimEventKind.MeleeHit or SimEventKind.ShotHit) && reported.Entity == second);
        Assert.True(match.IsAlive(first));
        var splashes = events.Where(reported => reported.Kind == SimEventKind.SplashHit && reported.Entity == second).ToArray();

        // The enemy lives long enough to splash past the tick in which the first regeneration would have come.
        Assert.True(splashes.Length > 5);
        var last = splashes[^1].Tick;
        Assert.True(last > Delay);
        Assert.False(match.IsAlive(enemy));
        var regenerated = events.Where(reported => reported.Kind == SimEventKind.Regenerated && reported.Entity == second).ToArray();
        Assert.Equal(last + Delay, regenerated[0].Tick);
    }

    /// <summary>A shell lands among units with Regeneration: each one its blast reaches counts from that tick.</summary>
    [Fact]
    public void TheBlastOfAShellCountsAsDamage()
    {
        var match = Field();
        var near = match.Unit(0, SiegeMatches.Hoplite, "12.5", "5.5");
        var far = match.Unit(0, SiegeMatches.Hoplite, "13.5", "5.5");
        var ballista = match.Unit(1, SiegeMatches.Ballista, "5.5", "5.5");
        match.OrderAttack(1, near, ballista);

        var events = RunAndCheckTheCounts(match, 60);

        var blast = events.First(reported => reported.Kind == SimEventKind.Blast);
        var reached = events.Where(reported => reported.Kind == SimEventKind.ShotHit && reported.Tick == blast.Tick).Select(reported => reported.Entity);
        Assert.Equal([near, far], reached.Order());
        Assert.True(blast.Tick > 0);
    }

    /// <summary>A Tower's shot is damage like any other: the unit it hits counts from the tick it lands.</summary>
    [Fact]
    public void TheShotOfATowerCountsAsDamage()
    {
        var match = Field();
        var tower = match.PlaceStructure(1, SiegeMatches.Tower, 10, 10);
        var unit = match.Unit(0, SiegeMatches.Hoplite, "15.5", "11");

        var events = RunAndCheckTheCounts(match, 60);

        var shots = events.Where(reported => reported.Kind == SimEventKind.ShotFired && reported.Other == tower).Select(reported => reported.Entity).ToHashSet();
        var hits = events.Where(reported => reported.Kind == SimEventKind.ShotHit && shots.Contains(reported.Other)).ToArray();
        Assert.True(hits.Length >= 2);
        Assert.All(hits, hit => Assert.Equal(unit, hit.Entity));
        Assert.DoesNotContain(events, reported => reported.Kind == SimEventKind.Regenerated);
        Assert.Equal(hits[^1].Tick, match.PerkCounts.Get(unit).DamagedAt);
    }
}
