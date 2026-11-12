using Sim.Combat;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// The design's defence example in its early form (design 6): "bastion walls at Protection 4
/// halve the damage of Trebuchets at Firepower 3", played at a wall under the wall rule of design
/// 2.5. <c>FortificationTests</c> has the same numbers for a Capitol named in an attack order;
/// here nobody names the wall: the Trebuchets are ordered to a place behind it.
/// </summary>
/// <remarks>
/// <para>
/// The camp is the one of <see cref="WallRuleTests"/>: a ring of player 1's wall on the edge of
/// the box from tile (14, 8) to tile (22, 16). Trebuchets of player 0 come from the west along
/// y = 12.5, so the tile in their way is (14, 12).
/// </para>
/// <para>
/// The numbers. A wall tile has 400 hit points, and 1.25 times that for each tier of its owner's
/// Protection above the first: 625 at Protection 3 and 781.25 at Protection 4. A Trebuchet's
/// shell is 48 times 1.25 for each tier of Firepower above the first, three times that against a
/// structure, and half of that where the fortification rule holds: 225 at Firepower 3, 112.5
/// against Protection 4, and 281.25 at Firepower 4. So the tile falls to the third shell where
/// the rule does not hold and to the seventh where it does. A Trebuchet fires every 4 seconds.
/// </para>
/// <para>
/// A shell has a blast radius of 1.5 tiles, and a structure is inside a blast when its center is
/// (design 2.5), so the shell that lands on the tile in the way also takes the wall tile on
/// either side of it, each halved alike: Trebuchets open a wall three tiles wide.
/// </para>
/// </remarks>
public class DefenceExampleTests
{
    private const string Trebuchet = "trebuchet";
    private const int FirepowerTrack = 0;

    /// <summary>A Trebuchet fires every 4 seconds (design 2.4).</summary>
    private const int TicksBetweenShells = 80;

    private static readonly (int X, int Y) Low = (14, 8);
    private static readonly (int X, int Y) High = (22, 16);
    private static readonly Fixed MinimumRange = Fixed.FromInt(3);

    /// <summary>The camp of player 1, with the attacker's Firepower and the defender's Protection at the given tiers.</summary>
    private static Match WithCamp(int firepower, int protection)
    {
        var match = Field();
        match.SetTiers(0, DesignData.Mix(firepower, 1, 1, 1, 1, 1));
        match.SetTiers(1, DesignData.Mix(1, protection, 1, 1, 1, 1));
        match.Ring(1, Low, High);
        return match;
    }

    /// <summary>The distance from a unit west of the camp to the nearest edge of the wall tile in its way.</summary>
    private static Fixed DistanceToTheWall(Match match, EntityId unit) => Fixed.FromInt(Low.X) - match.PositionOf(unit).X;

    private static void Order(Match match, bool attackMove, params EntityId[] units)
    {
        if (attackMove)
        {
            match.OrderAttackMove(0, Center(18, 12), units);
        }
        else
        {
            match.OrderMove(0, Center(18, 12), units);
        }
    }

    /// <summary>
    /// Runs until the wall tile in the way is gone. Returns every hit on a wall, how many shells
    /// were fired, and the ticks of the first hit and of the fall, counted from the order.
    /// </summary>
    private static (List<SimEvent> Hits, int Fired, int FirstHit, int Fell) RunUntilTheWallFalls(Match match, EntityId inWay, UnitOrder order, params EntityId[] units)
    {
        var hits = new List<SimEvent>();
        var fired = 0;
        var firstHit = -1;
        var ticks = 0;
        while (match.IsAlive(inWay))
        {
            Assert.True(ticks < 1200, "The wall tile still stands a minute after the order.");
            match.Run(1);
            ticks++;
            foreach (var shot in match.EventsOf(SimEventKind.ShotFired))
            {
                // It fires standing, from no nearer than its minimum range.
                Assert.Contains(shot.Other, units);
                Assert.True(match.Stands(shot.Other));
                Assert.True(DistanceToTheWall(match, shot.Other) >= MinimumRange);
                fired++;
            }

            var landed = match.EventsOf(SimEventKind.ShotHit);
            if (landed.Length > 0 && firstHit < 0)
            {
                firstHit = ticks;
            }

            hits.AddRange(landed);
            Assert.Empty(match.EventsOf(SimEventKind.MeleeHit));
            foreach (var unit in units)
            {
                Assert.Equal(order, match.UnitOf(unit).Order);
                Assert.False(match.IsInside(unit, Low, High));
            }
        }

        return (hits, fired, firstHit, ticks);
    }

    [Theory]
    [InlineData(3, 4, 781_250, 112_500, 7, false)]
    [InlineData(3, 4, 781_250, 112_500, 7, true)]
    [InlineData(3, 3, 625_000, 225_000, 3, false)]
    [InlineData(4, 4, 781_250, 281_250, 3, false)]
    public void ATrebuchetOrderedBehindAWallBreaksInWithShellsHalvedWhereTheWallsAreAhead(
        int firepower, int protection, long hitPoints, long damage, int shells, bool attackMove)
    {
        var match = WithCamp(firepower, protection);
        var trebuchet = match.Spawn(0, Trebuchet, "3.5", "12.5");
        var inWay = match.WallOn(14, 12);
        var north = match.WallOn(14, 11);
        var south = match.WallOn(14, 13);
        var walls = match.WallHitPoints();
        var order = attackMove ? UnitOrder.AttackMove : UnitOrder.Move;
        Assert.Equal(firepower, match.Blueprint(0, Trebuchet).EffectiveTiers[FirepowerTrack]);
        Assert.All(walls.Values, tile => Assert.Equal(hitPoints, tile));

        // Nobody names the wall: the order is to the middle of the camp.
        Order(match, attackMove, trebuchet);
        match.Run(1);
        Assert.Equal((order, inWay), (match.UnitOf(trebuchet).Order, match.UnitOf(trebuchet).WallInWay));
        Assert.Equal(Center(18, 12), match.UnitOf(trebuchet).Goal);

        var (hits, fired, firstHit, fell) = RunUntilTheWallFalls(match, inWay, order, trebuchet);

        // Every shell takes the same off the tile in the way, and the tile falls to the last.
        var onTheTile = hits.Where(hit => hit.Entity == inWay).ToArray();
        Assert.Equal(shells, fired);
        Assert.Equal(shells, onTheTile.Length);
        Assert.All(onTheTile, hit => Assert.Equal(damage, hit.Amount));
        Assert.Equal(firstHit + ((shells - 1) * TicksBetweenShells), fell);

        // The blast of each shell takes the tile on either side too, by the same rule, and no other.
        Assert.All(hits, hit => Assert.Equal(damage, hit.Amount));
        Assert.Equal(3 * shells, hits.Count);
        Assert.Equal([north, inWay, south], match.Damaged(walls));
        Assert.False(match.IsAlive(north) || match.IsAlive(south));

        // Then it goes on to the place it was ordered to.
        match.Run(400);
        Assert.Equal(Center(18, 12), match.PositionOf(trebuchet));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(trebuchet).Order, match.UnitOf(trebuchet).WallInWay));
        Assert.Equal([north, inWay, south], match.Damaged(walls));
    }

    /// <summary>
    /// The example as a player meets it: the same three Trebuchets, the same order, and a wall
    /// that falls to their first volley at Protection 3 stands until their third at Protection 4.
    /// </summary>
    [Theory]
    [InlineData(3, 3, 1)]
    [InlineData(4, 7, 3)]
    public void ThreeTrebuchetsBreakInWithOneVolleyAtProtectionThreeAndNeedThreeAtProtectionFour(int protection, int shells, int volleys)
    {
        var match = WithCamp(3, protection);
        EntityId[] battery = [match.Spawn(0, Trebuchet, "3.5", "11.5"), match.Spawn(0, Trebuchet, "3.5", "12.5"), match.Spawn(0, Trebuchet, "3.5", "13.5")];
        match.OrderMove(0, Center(18, 12), battery);
        match.Run(1);

        // Ordered together, they have one tile of the west side in their way.
        var inWay = match.UnitOf(battery[0]).WallInWay;
        Assert.Equal(Low.X, match.TileOf(inWay).X);
        Assert.All(battery, unit => Assert.Equal(inWay, match.UnitOf(unit).WallInWay));

        var (hits, fired, _, _) = RunUntilTheWallFalls(match, inWay, UnitOrder.Move, battery);

        // The tile falls to the third shell, or to the seventh: the first of their third volley.
        Assert.Equal(shells, hits.Count(hit => hit.Entity == inWay));
        Assert.Equal(volleys * battery.Length, fired);

        // All three go on through the gap, into the camp.
        match.Run(500);
        Assert.All(battery, unit => Assert.True(match.IsInside(unit, Low, High)));
        Assert.All(battery, unit => Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order));
    }

    /// <summary>The same order, the same Trebuchet: the wall that is ahead holds more than twice as long.</summary>
    [Fact]
    public void AWallAtProtectionFourHoldsMoreThanTwiceAsLongAsOneAtProtectionThree()
    {
        var held = new Dictionary<int, int>();
        foreach (var protection in (int[])[3, 4])
        {
            var match = WithCamp(3, protection);
            var trebuchet = match.Spawn(0, Trebuchet, "3.5", "12.5");
            match.OrderMove(0, Center(18, 12), trebuchet);
            held[protection] = RunUntilTheWallFalls(match, match.WallOn(14, 12), UnitOrder.Move, trebuchet).Fell;
        }

        Assert.True(held[4] > 2 * held[3], $"The wall held {held[3]} ticks at Protection 3 and {held[4]} at Protection 4.");
    }

    /// <summary>
    /// A known limit, stated here so that a change to it is seen. The design has no rule for a
    /// unit that stands inside its minimum range of the wall tile in its way. The nearest it has
    /// is for targets a unit picks (design 2.4, "Minimum range"): "A target closer than the unit's
    /// minimum range may not be picked, and the unit does not step back to make room." The code
    /// does the same at a wall: the Trebuchet keeps its order and its wall, and stands.
    /// </summary>
    [Theory]
    [InlineData("12.5", false)]
    [InlineData("12.5", true)]
    [InlineData("11.5", false)]
    public void ATrebuchetInsideItsMinimumRangeOfTheWallInItsWayStandsAndDoesNotStepBack(string x, bool attackMove)
    {
        var match = WithCamp(3, 4);
        var trebuchet = match.Spawn(0, Trebuchet, x, "12.5");
        var inWay = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        var order = attackMove ? UnitOrder.AttackMove : UnitOrder.Move;
        Order(match, attackMove, trebuchet);

        for (var tick = 0; tick < 600; tick++)
        {
            match.Run(1);
            Assert.Empty(match.EventsOf(SimEventKind.ShotFired));
            Assert.Equal((order, inWay), (match.UnitOf(trebuchet).Order, match.UnitOf(trebuchet).WallInWay));
        }

        Assert.True(match.Stands(trebuchet));
        Assert.True(DistanceToTheWall(match, trebuchet) < MinimumRange);
        Assert.True(match.PositionOf(trebuchet).X >= TestMatches.Point(x, "12.5").X, "It has stepped back.");
        Assert.Empty(match.Damaged(walls));
    }

    /// <summary>At exactly its minimum range it fires (design 2.4: "a target at exactly the minimum can be hit").</summary>
    [Fact]
    public void ATrebuchetAtItsMinimumRangeOfTheWallInItsWayFiresFromWhereItStands()
    {
        var match = WithCamp(3, 4);
        var trebuchet = match.Spawn(0, Trebuchet, "11", "12.5");
        var inWay = match.WallOn(14, 12);
        match.OrderMove(0, Center(18, 12), trebuchet);

        match.Run(2);

        Assert.Equal(inWay, match.UnitOf(trebuchet).WallInWay);
        Assert.Equal(TestMatches.Point("11", "12.5"), match.PositionOf(trebuchet));
        match.Run(40);
        Assert.Equal(781_250 - 112_500, match.HitPointsOf(inWay));
        Assert.Equal(TestMatches.Point("11", "12.5"), match.PositionOf(trebuchet));
    }

    /// <summary>The Trebuchet that stands too close is not held for good: it goes on when the tile falls to another.</summary>
    [Fact]
    public void ATrebuchetTooCloseGoesOnWhenTheWallFallsToAnother()
    {
        var match = WithCamp(3, 3);
        var close = match.Spawn(0, Trebuchet, "12.5", "12.5");
        var far = match.Spawn(0, Trebuchet, "6.5", "12.5");
        var inWay = match.WallOn(14, 12);
        match.OrderMove(0, Center(18, 12), close, far);

        while (match.IsAlive(inWay))
        {
            Assert.True(match.World.Tick < 1200, "The wall tile still stands.");
            match.Run(1);
            Assert.All(match.EventsOf(SimEventKind.ShotFired), fired => Assert.Equal(far, fired.Other));
        }

        match.Run(400);
        Assert.All((EntityId[])[close, far], unit => Assert.True(match.IsInside(unit, Low, High)));
        Assert.All((EntityId[])[close, far], unit => Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order));
    }

    /// <summary>What its player can do: move it back, and order it again.</summary>
    [Fact]
    public void ATrebuchetTooCloseBreaksInOnceItsPlayerHasMovedItBack()
    {
        var match = WithCamp(3, 3);
        var trebuchet = match.Spawn(0, Trebuchet, "12.5", "12.5");
        var inWay = match.WallOn(14, 12);
        match.OrderMove(0, Center(18, 12), trebuchet);
        match.Run(100);
        Assert.Equal(inWay, match.UnitOf(trebuchet).WallInWay);
        Assert.Empty(match.Damaged(match.WallHitPoints()));

        match.OrderMove(0, Center(8, 12), trebuchet);
        match.Run(100);
        Assert.Equal(Center(8, 12), match.PositionOf(trebuchet));
        Assert.Equal(EntityId.None, match.UnitOf(trebuchet).WallInWay);

        match.OrderMove(0, Center(18, 12), trebuchet);
        RunUntilTheWallFalls(match, inWay, UnitOrder.Move, trebuchet);
        match.Run(400);
        Assert.Equal(Center(18, 12), match.PositionOf(trebuchet));
    }
}
