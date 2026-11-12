using Sim.Core;
using Sim.Data;

namespace Ai.Tests;

/// <summary>
/// The scout (design 5.5, item 4): when one is sent, which unit, where to, and when it turns
/// for home. Each test puts units on Ridge and has rounds thought; the tests of the walk run
/// the match between the rounds.
/// </summary>
public class AiScoutTests
{
    private const int Every = 100;

    /// <summary>A tile on its own side of the ridge.</summary>
    private const int HomeX = 24;
    private const int HomeY = 48;

    [Fact]
    public void ItSendsItsFirstScoutWhenTheIntervalHasPassed()
    {
        var (fixture, fight, scout) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);

        fixture.Run(Every - 1);
        Round(fixture, fight, scout);

        Assert.Empty(fixture.Commands);
        Assert.Equal(ScoutPhase.None, scout.Phase);
        Assert.Equal(-1, scout.FirstSentTick);
        Assert.Equal(EntityId.None, fixture.Sight.Scout);

        fixture.Run(1);
        Round(fixture, fight, scout);

        // It walks on a move order, which fights nothing on the way.
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Move, order.Type);
        Assert.Equal([unit], order.Entities.ToArray());
        Assert.Equal(ScoutPhase.Out, scout.Phase);
        Assert.Equal(1, scout.Sent);
        Assert.Equal(Every, scout.FirstSentTick);
        Assert.Equal(unit, fixture.Sight.Scout);
    }

    [Fact]
    public void WithNothingOfTheEnemysKnownItGoesToTheStartOfTheOtherSeat()
    {
        var (fixture, fight, scout) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);
        Assert.Empty(fixture.Sight.EnemyStructures);

        fixture.Run(Every);
        Round(fixture, fight, scout);

        Assert.Equal(Assert.Single(fixture.Sight.OtherStarts), TargetOf(Assert.Single(fixture.Commands)));
    }

    /// <summary>What it has seen of the enemy's is nearer the truth than the start it was given.</summary>
    [Fact]
    public void ItGoesToTheNearestStructureOfTheEnemysThatHoldsTerritory()
    {
        var (fixture, fight, scout) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", 56, 26);

        // A Barracks nearer than both Outposts, and of the Outposts the nearer one.
        fixture.PlaceAt(1, "barracks", 58, 26);
        fixture.PlaceAt(1, "outpost", 56, 30);
        fixture.PlaceAt(1, "outpost", 56, 19);
        Assert.Equal(3, fixture.Sight.EnemyStructures.Count);

        fixture.Run(Every);
        Round(fixture, fight, scout);

        var size = AiFixture.Structure("outpost").Footprint;
        Assert.Equal((56 + (size / 2), 30 + (size / 2)), TargetOf(Assert.Single(fixture.Commands)));
    }

    [Fact]
    public void WithNoStructureThatHoldsTerritoryItGoesToTheNearestOtherStructure()
    {
        var (fixture, fight, scout) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", 56, 28);
        fixture.PlaceAt(1, "barracks", 58, 18);
        fixture.PlaceAt(1, "barracks", 60, 30);

        fixture.Run(Every);
        Round(fixture, fight, scout);

        var size = AiFixture.Structure("barracks").Footprint;
        Assert.Equal((60 + (size / 2), 30 + (size / 2)), TargetOf(Assert.Single(fixture.Commands)));
    }

    /// <summary>The unit that sees furthest, of those the fastest, and of those the oldest.</summary>
    [Theory]
    [InlineData(new[] { "peltast", "hippeus", "hoplite" }, 1)]
    [InlineData(new[] { "ballista", "hoplite", "peltast", "peltast" }, 2)]
    [InlineData(new[] { "hoplite", "hoplite" }, 0)]
    [InlineData(new[] { "hippeus", "peltast", "hippeus" }, 0)]
    public void ItSendsTheUnitThatSeesFurthestThenTheFastestThenTheOldest(string[] chassis, int chosen)
    {
        var (fixture, fight, scout) = Make();
        var units = chassis.Select((id, index) => fixture.SpawnAt(AiFixture.Player, id, HomeX + index, HomeY + 2)).ToList();
        var blueprints = fixture.Sight.Tech.Blueprints;
        Assert.True(blueprints[AiFixture.Chassis("hippeus")].Sight > blueprints[AiFixture.Chassis("peltast")].Sight);
        Assert.Equal(blueprints[AiFixture.Chassis("peltast")].Sight, blueprints[AiFixture.Chassis("hoplite")].Sight);
        Assert.True(blueprints[AiFixture.Chassis("peltast")].SpeedPerTick > blueprints[AiFixture.Chassis("hoplite")].SpeedPerTick);
        Assert.True(blueprints[AiFixture.Chassis("hoplite")].SpeedPerTick > blueprints[AiFixture.Chassis("ballista")].SpeedPerTick);

        fixture.Run(Every);
        Round(fixture, fight, scout);

        Assert.Equal([units[chosen]], Assert.Single(fixture.Commands).Entities.ToArray());
    }

    /// <summary>A worker sees less than any fighting unit (design 4.1), and it has work to do.</summary>
    [Fact]
    public void ItSendsNoWorkerAndWaitsForAUnit()
    {
        var (fixture, fight, scout) = Make();
        fixture.SpawnWorkers(4);

        fixture.Run(Every + 30);
        Round(fixture, fight, scout);

        Assert.Empty(fixture.Commands);
        Assert.Equal(0, scout.Sent);

        // The scout is sent in the round in which it has a unit, and the next one an interval after that.
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);
        Round(fixture, fight, scout);

        Assert.Equal([unit], Assert.Single(fixture.Commands).Entities.ToArray());
        Assert.Equal(Every + 30, scout.FirstSentTick);
    }

    [Fact]
    public void WithoutATileToComeHomeToItSendsNone()
    {
        var (fixture, fight, scout) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);
        fixture.Run(Every);

        fixture.Commands.Clear();
        scout.Think(fight, hasMuster: false, 0, 0, fixture.Commands);

        Assert.Empty(fixture.Commands);
        Assert.Equal(ScoutPhase.None, scout.Phase);
    }

    /// <summary>A personality whose file has no <c>scouting</c> block sends no scout.</summary>
    [Fact]
    public void APersonalityWithoutAScoutingBlockSendsNone()
    {
        var (fixture, fight, scout) = Make(AiScoutingDef.None);
        fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);

        for (var round = 0; round < 30; round++)
        {
            fixture.Run(AiPlayer.ThinkInterval);
            Round(fixture, fight, scout);
            Assert.Empty(fixture.Commands);
        }

        Assert.Equal(0, scout.Sent);
    }

    /// <summary>The army that is out is not robbed of a unit: the scout is one of the units at home.</summary>
    [Fact]
    public void ItSendsNoUnitOfAForceThatIsAway()
    {
        var (fixture, fight, scout) = Make();
        var force = fixture.SpawnRow(AiFixture.Player, "hippeus", 2, HomeX, HomeY + 2);
        fixture.Commands.Clear();
        fight.Watch();
        fight.Think(hasMuster: true, fixture.Muster.X, fixture.Muster.Y, atSupplyCap: true, fixture.Commands);
        Assert.Equal(FightPhase.Advance, fight.Phase);
        Assert.All(force, unit => Assert.True(fight.IsAway(unit)));

        fixture.Run(Every);
        Round(fixture, fight, scout);

        Assert.Empty(fixture.Commands);
        Assert.Equal(0, scout.Sent);

        // A unit trained since is at home, and goes, though it sees less far than those of the force.
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 4);
        Round(fixture, fight, scout);

        Assert.Equal([unit], Assert.Single(fixture.Commands).Entities.ToArray());
    }

    /// <summary>The scout is counted in its army's worth, and is given no order with the army while it is out.</summary>
    [Fact]
    public void TheArmyDoesNotTakeTheScoutAlong()
    {
        var (fixture, fight, scout) = Make();
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 3, HomeX, HomeY + 2);
        var worth = fixture.Sight.ValueOf(AiFixture.Chassis("peltast"));
        fixture.Run(Every);
        Round(fixture, fight, scout);
        Assert.Equal(units[0], fixture.Sight.Scout);

        fixture.Commands.Clear();
        fight.Watch();
        fight.Think(hasMuster: true, fixture.Muster.X, fixture.Muster.Y, atSupplyCap: true, fixture.Commands);

        Assert.Equal(FightPhase.Advance, fight.Phase);
        Assert.Equal(3 * worth, fight.ArmyValue);
        Assert.Equal([units[1], units[2]], Assert.Single(fixture.Commands).Entities.ToArray());
        Assert.False(fight.IsAway(units[0]));
    }

    /// <summary>
    /// The whole trip: out to the Outpost it knows of, round when the Outpost is a tile inside
    /// its sight, home, and at home it is a unit of the army again.
    /// </summary>
    [Fact]
    public void ItTurnsForHomeWhenItSeesThePlaceAndIsReleasedAtHome()
    {
        const int every = 400;
        var (fixture, fight, scout) = Make(new AiScoutingDef(every, []), musterX: 50, musterY: 40);
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", 50, 40);
        var watcher = fixture.SpawnAt(AiFixture.Player, "peltast", 70, 40);
        fixture.PlaceAt(1, "outpost", 74, 38);
        fixture.Kill(watcher);
        fixture.Run(fixture.Match.Vision.RefreshTicks);
        var outpost = Assert.Single(fixture.Sight.EnemyStructures);
        Assert.True(outpost.IsRemembered);
        var sightOfUnit = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")].Sight;

        fixture.Run(every - fixture.Match.World.Tick);
        Round(fixture, fight, scout);
        Assert.Equal(unit, fixture.Sight.Scout);
        fixture.Carry();

        var rounds = 0;
        while (scout.Phase == ScoutPhase.Out)
        {
            Assert.True(++rounds < 100);
            fixture.Run(AiPlayer.ThinkInterval - 1);
            Round(fixture, fight, scout);
            fixture.Carry();
        }

        // It turned round where it sees the Outpost, and not on top of it.
        Assert.Equal(ScoutPhase.Back, scout.Phase);
        Assert.False(Assert.Single(fixture.Sight.EnemyStructures).IsRemembered);
        var turnedAt = Assert.Single(fixture.Sight.Fighters);
        var distance = AiSearch.DistanceToSquare(outpost.X, outpost.Y, outpost.Size, AiOrders.TileX(turnedAt), AiOrders.TileY(turnedAt));
        Assert.InRange(distance, (sightOfUnit - 3) * (sightOfUnit - 3), (sightOfUnit - 1) * (sightOfUnit - 1));
        Assert.Equal(0, scout.Returned);

        while (scout.Phase == ScoutPhase.Back)
        {
            Assert.True(++rounds < 200);
            fixture.Run(AiPlayer.ThinkInterval - 1);
            Round(fixture, fight, scout);
            fixture.Carry();
        }

        Assert.Equal(1, scout.Returned);
        Assert.Equal(EntityId.None, fixture.Sight.Scout);
        var home = Assert.Single(fixture.Sight.Fighters);
        Assert.True(AiSearch.DistanceSquared(AiOrders.TileX(home), AiOrders.TileY(home), 50, 40) <= AiFight.Near * AiFight.Near);

        // The next scout is sent an interval after the first, not after it came home.
        Assert.True(fixture.Match.World.Tick < 2 * every, "The test is of a trip shorter than the interval.");
        fixture.Run((2 * every) - 1 - fixture.Match.World.Tick);
        Round(fixture, fight, scout);
        Assert.Empty(fixture.Commands);

        fixture.Run(1);
        Round(fixture, fight, scout);
        Assert.Equal(CommandType.Move, Assert.Single(fixture.Commands).Type);
        Assert.Equal(2, scout.Sent);
        Assert.Equal(every, scout.FirstSentTick);
    }

    [Fact]
    public void AScoutThatDoesNotComeBackIsGivenUp()
    {
        var (fixture, fight, scout) = Make();
        var unit = fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);
        fixture.Run(Every);
        Round(fixture, fight, scout);
        Assert.Equal(ScoutPhase.Out, scout.Phase);

        fixture.Kill(unit);
        Round(fixture, fight, scout);

        Assert.Empty(fixture.Commands);
        Assert.Equal(ScoutPhase.None, scout.Phase);
        Assert.Equal(EntityId.None, fixture.Sight.Scout);
        Assert.Equal(1, scout.Sent);
        Assert.Equal(0, scout.Returned);
    }

    /// <summary>While a scout is out no second one is sent, however long it takes.</summary>
    [Fact]
    public void ItHasOneScoutOutAtATime()
    {
        var (fixture, fight, scout) = Make();
        fixture.SpawnRow(AiFixture.Player, "peltast", 3, HomeX, HomeY + 2);
        fixture.Run(Every);
        Round(fixture, fight, scout);
        Assert.Single(fixture.Commands);

        // The way to the other seat's start is longer than the interval.
        fixture.Carry();
        fixture.Run(Every);
        Round(fixture, fight, scout);

        Assert.Equal(1, scout.Sent);
        Assert.Equal(ScoutPhase.Out, scout.Phase);
        Assert.Empty(fixture.Commands);
    }

    private static (AiFixture Fixture, AiFight Fight, AiScout Scout) Make(AiScoutingDef? scouting = null, int musterX = HomeX, int musterY = HomeY)
    {
        var fixture = new AiFixture(players: 2) { Muster = (musterX, musterY) };
        var fight = new AiFight(fixture.Sight, new AiSearch(fixture.Sight), AiFixture.Fight());
        return (fixture, fight, new AiScout(fixture.Sight, scouting ?? new AiScoutingDef(Every, [])));
    }

    /// <summary>Has the scout's part of one round thought, with the orders of the round before taken off the list.</summary>
    private static void Round(AiFixture fixture, AiFight fight, AiScout scout)
    {
        fixture.Commands.Clear();
        scout.Think(fight, hasMuster: true, fixture.Muster.X, fixture.Muster.Y, fixture.Commands);
    }

    private static (int X, int Y) TargetOf(Command command) => (command.TargetX.FloorToInt(), command.TargetY.FloorToInt());
}
