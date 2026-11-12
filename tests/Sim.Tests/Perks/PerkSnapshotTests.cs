using Sim.Combat;
using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Perks;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Perks;

/// <summary>
/// The counts of the accent perks are state: a match in which units walk toward a charge, are
/// hit, die with Reserves and regenerate is saved and restored in each of those states.
/// </summary>
public class PerkSnapshotTests
{
    private const int LastTick = 700;

    private const int Raiders = 4;

    /// <summary>Rapid II, which is Charge, and Resupplied I, which is Regeneration.</summary>
    private static readonly int[] Raider = DesignData.Mix(1, 1, 3, 1, 2, 1);

    /// <summary>Standardized II, which is pairs with Reserves, and Regeneration.</summary>
    private static readonly int[] Defender = DesignData.Mix(1, 1, 1, 3, 2, 1);

    private static readonly FixedVector2 Front = MapGrid.TileCenter(24, 11);

    private static int Hoplite => Chassis("hoplite");

    private static int Peltast => Chassis("peltast");

    /// <summary>Four Hoplites of player 0 at the left, and a Barracks of player 1 at the right.</summary>
    private static Match Build()
    {
        var match = Create(TestMaps.Open(44, 24));
        match.Research.SetTiers(0, Raider);
        match.Research.SetTiers(1, Defender);
        for (var unit = 0; unit < Raiders; unit++)
        {
            match.SpawnChassis(0, Hoplite, MapGrid.TileCenter(4, 8 + (2 * unit)));
        }

        match.Place(1, "barracks", 30, 10);
        match.MakeRich(1);
        match.RefreshDerivedState();
        return match;
    }

    private static (int Tick, Command Command)[] Script()
    {
        var match = Build();
        var barracks = match.Structures.EntityAt(0);
        var raiders = Enumerable.Range(0, match.Combatants.Count).Select(match.Combatants.EntityAt).ToArray();
        return
        [
            (0, RallyCommand(1, Front, barracks)),
            (0, TrainCommand(1, barracks, Peltast)),
            (1, TrainCommand(1, barracks, Hoplite)),
            (200, Sim.Tests.Combat.CombatMatches.AttackMoveCommand(0, Front, raiders)),
        ];
    }

    private static Match Run(int ticks, List<SimEvent>? events = null)
    {
        var match = Build();
        foreach (var (tick, command) in Script())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        for (var tick = 0; tick < ticks; tick++)
        {
            match.Simulation.Tick();
            events?.AddRange(match.Simulation.Events);
        }

        return match;
    }

    private static PerkCounts[] CountsOf(Match match, int player) =>
        [.. Enumerable.Range(0, match.PerkCounts.Count)
            .Where(i => match.Bodies.Get(match.PerkCounts.EntityAt(i)).Owner == player)
            .Select(i => match.PerkCounts.ValueAt(i))];

    [Fact(Skip = TestBuild.PerksMeetMeleePlaces)]
    public void TheMatchHasEveryStateTheSnapshotsAreTakenIn()
    {
        // The pair has appeared, each unit with its half of the price, and nobody has walked far or been hit.
        var trained = Run(231);
        Assert.Equal([new Fraction(32, 1), new Fraction(32, 1)], CountsOf(trained, 1).Select(counts => counts.Paid));
        Assert.All(CountsOf(trained, 0), counts => Assert.Equal((0, Fraction.Zero), (counts.DamagedAt, counts.Paid)));

        // The raiders are on their way, with a part of the distance of Charge behind them.
        var walking = Run(260);
        Assert.All(CountsOf(walking, 0), counts => Assert.True(counts.Walked >= Fixed.One && counts.Walked < Fixed.FromInt(40)));

        // Between the charges: one raider has attacked and counts again, the others have not.
        var charging = Run(320);
        Assert.Contains(CountsOf(charging, 0), counts => counts.Walked == Fixed.Zero);
        Assert.Contains(CountsOf(charging, 0), counts => counts.Walked >= Fixed.FromInt(4));

        // In the fight: units of both players have been hit, in different ticks.
        var fighting = Run(350);
        Assert.Contains(CountsOf(fighting, 0), counts => counts.DamagedAt > 300);
        Assert.Contains(CountsOf(fighting, 0), counts => counts.DamagedAt == 0);
        Assert.All(CountsOf(fighting, 1), counts => Assert.InRange(counts.DamagedAt, 300, 349));

        // The first of the pair has died and given its refund; its partner fights on for a second.
        var events = new List<SimEvent>();
        var halfGone = Run(377, events);
        Assert.Equal([376], events.Where(simEvent => simEvent.Kind == SimEventKind.ReservesRefunded).Select(simEvent => simEvent.Tick));
        Assert.Single(CountsOf(halfGone, 1));

        // The pair is gone, and both refunds are in the stockpile.
        events.Clear();
        var refunded = Run(400, events);
        Assert.Equal([376, 399], events.Where(simEvent => simEvent.Kind == SimEventKind.ReservesRefunded).Select(simEvent => simEvent.Tick));
        Assert.Empty(CountsOf(refunded, 1));

        // Between the regenerations of two raiders, which count from different ticks.
        events.Clear();
        var regenerating = Run(480, events);
        Assert.Single(events, simEvent => simEvent.Kind == SimEventKind.Regenerated);
        Assert.Equal(2, CountsOf(regenerating, 0).Select(counts => counts.DamagedAt).Where(tick => tick > 0).Distinct().Count());

        // And to the end: the raiders go on to the Barracks and charge it, and a second pair comes
        // out beside them and is hit, while the first fight's wounds still close.
        events.Clear();
        Run(LastTick, events);
        Assert.Equal(8, events.Count(simEvent => simEvent.Kind == SimEventKind.ChargedAttack));
        Assert.Equal(2, events.Count(simEvent => simEvent.Kind == SimEventKind.ReservesRefunded));
        Assert.Contains(events, simEvent => simEvent.Kind == SimEventKind.Regenerated && simEvent.Tick > 600);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(231)]
    [InlineData(260)]
    [InlineData(320)]
    [InlineData(350)]
    [InlineData(376)]
    [InlineData(377)]
    [InlineData(399)]
    [InlineData(400)]
    [InlineData(480)]
    [InlineData(592)]
    [InlineData(650)]
    public void AMatchWithThePerksAtWorkRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(Build, Script(), snapshotTick, LastTick);
}
