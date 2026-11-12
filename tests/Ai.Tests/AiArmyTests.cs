using Sim.Core;
using Sim.Data;

namespace Ai.Tests;

/// <summary>
/// The army (design 5.5, item 3): the newest chassis it has unlocked of each line of its
/// personality, in the ratio of the file.
/// </summary>
public class AiArmyTests
{
    private static readonly AiLineShare[] ThreeToOne = [new(Line: 0, Share: 3), new(Line: 2, Share: 1)];

    /// <summary>Units of line 0 and of line 2, and the position in the file of the line to order next.</summary>
    [Theory]
    [InlineData(0, 0, 0)] // Level: the first of the file.
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 1, 0)] // Level again.
    [InlineData(4, 1, 1)]
    [InlineData(0, 5, 0)]
    [InlineData(30, 9, 1)]
    [InlineData(30, 10, 0)]
    public void TheLineFurthestBehindItsShareIsOrderedNext(int first, int second, int expected)
    {
        int[] units = [first, 99, second, 99];

        Assert.Equal(expected, AiArmy.FurthestBehind(ThreeToOne, units, [false, false]));
    }

    [Fact]
    public void ALineThatIsSkippedIsPassedOverForTheNext()
    {
        int[] units = [0, 0, 5, 0];

        Assert.Equal(0, AiArmy.FurthestBehind(ThreeToOne, units, [false, false]));
        Assert.Equal(1, AiArmy.FurthestBehind(ThreeToOne, units, [true, false]));
        Assert.Equal(0, AiArmy.FurthestBehind(ThreeToOne, units, [false, true]));
        Assert.Equal(-1, AiArmy.FurthestBehind(ThreeToOne, units, [true, true]));
    }

    [Fact]
    public void OrderingTheLineFurthestBehindKeepsTheRatioOfTheFile()
    {
        AiLineShare[] lines = [new(Line: 1, Share: 2), new(Line: 0, Share: 5), new(Line: 2, Share: 3)];
        var units = new int[3];

        for (var order = 1; order <= 200; order++)
        {
            units[lines[AiArmy.FurthestBehind(lines, units, [false, false, false])].Line]++;

            // No line is ever more than one unit away from its share of what was ordered.
            foreach (var line in lines)
            {
                Assert.InRange((units[line.Line] * 10) - (order * line.Share), -10, 10);
            }
        }

        Assert.Equal([100, 40, 60], units);
    }

    [Fact]
    public void TheNewestChassisItHasUnlockedIsTheOneOfTheHighestEpoch()
    {
        var fixture = new AiFixture();
        var line = fixture.Data.LineOf(AiFixture.Line("line_infantry"))!;

        var first = AiArmy.NewestUnlocked(fixture.Sight.Tech, line.Index)!;
        Assert.Equal(1, first.Chassis.Epoch);
        Assert.Equal(line.Index, first.Line.Index);

        // Its primary track at tier 3 and its secondary at tier 2 unlock the chassis of epoch 3.
        var tiers = Enumerable.Repeat(1, fixture.Data.Tracks.Count).ToArray();
        tiers[line.PrimaryTrack] = 3;
        tiers[line.SecondaryTrack] = 2;
        fixture.Match.Research.SetTiers(AiFixture.Player, tiers);

        var newest = AiArmy.NewestUnlocked(fixture.Sight.Tech, line.Index)!;
        Assert.True(newest.Unlocked);
        Assert.Equal(3, newest.Chassis.Epoch);
        Assert.Equal(line.Index, newest.Line.Index);

        // A line that goes by other tracks is where it was.
        var other = fixture.Data.Lines.First(candidate => candidate.PrimaryTrack != line.PrimaryTrack && candidate.PrimaryTrack != line.SecondaryTrack);
        Assert.Equal(1, AiArmy.NewestUnlocked(fixture.Sight.Tech, other.Index)!.Chassis.Epoch);
    }

    [Fact]
    public void ALineWithoutAChassisGivesNone()
    {
        var fixture = new AiFixture();

        // A line of the roster that has no file yet.
        var missing = Enumerable.Range(0, fixture.Data.Rules.Lines.Count).First(line => fixture.Data.LineOf(line) is null);

        Assert.Null(AiArmy.NewestUnlocked(fixture.Sight.Tech, missing));
    }

    [Fact]
    public void ItTrainsEachLineAtItsStructureInTheRatioOfTheFile()
    {
        var fixture = new AiFixture();
        var barracks = fixture.Place("barracks");
        var works = fixture.Place("works");
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("line_infantry", 3), ("cavalry_armor", 1)]));

        army.Think(AiFixture.Plenty, fixture.Rich(), fixture.Commands);

        // Infantry first, then the Cavalry that is behind, and so on until both queues hold two.
        var peltast = AiFixture.Chassis("peltast");
        var hippeus = AiFixture.Chassis("hippeus");
        Assert.All(fixture.Commands, command => Assert.Equal(CommandType.Train, command.Type));
        Assert.Equal(
            [(barracks, peltast), (works, hippeus), (barracks, peltast), (works, hippeus)],
            fixture.Commands.Select(command => (Assert.Single(command.Entities.ToArray()), command.ContentId)).ToList());
        Assert.Equal(AiArmy.OrdersPerRound, fixture.Commands.Count);

        // The match takes every one of them, and with its queues at two it orders no more.
        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);
        fixture.Carry();
        Assert.Equal(AiEconomy.QueueLength, fixture.Sight.OrdersAt(barracks));
        Assert.Equal(AiEconomy.QueueLength, fixture.Sight.OrdersAt(works));
        army.Think(AiFixture.Plenty, fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);
    }

    /// <summary>
    /// Protection makes a unit cost more. An order priced before a research ends and carried out
    /// after it would be refused, so it gives none in the round before one ends.
    /// </summary>
    [Fact]
    public void ItOrdersNoUnitWhileAResearchIsAboutToEnd()
    {
        var fixture = new AiFixture();
        fixture.Place("barracks");
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("line_infantry", 1)]));
        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);
        fixture.Commands.Add(new Command(CommandType.StartResearch, AiFixture.Player, contentId: AiFixture.Track("protection")));
        fixture.Carry();
        Assert.True(fixture.Sight.IsResearching(AiFixture.Track("protection")));

        while (!fixture.Sight.ResearchEndsWithin(AiPlayer.ThinkInterval))
        {
            army.Think(AiFixture.Plenty, fixture.Rich(), fixture.Commands);
            Assert.NotEmpty(fixture.Commands);
            fixture.Commands.Clear();
            fixture.Run(1);
        }

        var before = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")].Materials;
        for (var tick = 0; tick < AiPlayer.ThinkInterval; tick++)
        {
            army.Think(AiFixture.Plenty, fixture.Rich(), fixture.Commands);
            Assert.Empty(fixture.Commands);
            fixture.Run(1);
        }

        Assert.False(fixture.Sight.IsResearching(AiFixture.Track("protection")));
        Assert.True(fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")].Materials > before);
        army.Think(AiFixture.Plenty, fixture.Rich(), fixture.Commands);
        Assert.NotEmpty(fixture.Commands);
    }

    [Fact]
    public void UnitsOnTheMapAndInTheQueuesCountForTheRatio()
    {
        var fixture = new AiFixture();
        var barracks = fixture.Place("barracks");
        var works = fixture.Place("works");
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("cavalry_armor", 1), ("line_infantry", 1)]));
        fixture.Spawn("hippeus", 2);

        // Two Cavalry stand on the map, so two Infantry come first.
        army.Think(AiFixture.Plenty, fixture.Rich(), fixture.Commands);
        Assert.Equal([barracks, barracks, works, works], fixture.Commands.Select(command => command.Entities[0]).ToList());

        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);
        fixture.Carry();
        var units = new int[fixture.Data.Rules.Lines.Count];
        fixture.Sight.CountUnitsByLine(units);
        Assert.Equal(4, units[AiFixture.Line("cavalry_armor")]);
        Assert.Equal(2, units[AiFixture.Line("line_infantry")]);
    }

    [Fact]
    public void ALineWhoseStructureItDoesNotHaveIsPassedOver()
    {
        var fixture = new AiFixture();
        var barracks = fixture.Place("barracks");
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("cavalry_armor", 1), ("line_infantry", 1)]));

        army.Think(AiFixture.Plenty, fixture.Rich(), fixture.Commands);

        Assert.Equal(AiEconomy.QueueLength, fixture.Commands.Count);
        Assert.All(fixture.Commands, command =>
        {
            Assert.Equal(barracks, command.Entities[0]);
            Assert.Equal(AiFixture.Chassis("peltast"), command.ContentId);
        });
    }

    /// <summary>Buying another line in its place would leave the line that is behind there for good.</summary>
    [Fact]
    public void ALineThatIsBehindAndCannotBePaidForIsWaitedFor()
    {
        var fixture = new AiFixture();
        fixture.Place("barracks");
        fixture.Place("works");
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("line_infantry", 1), ("cavalry_armor", 1)]));
        var infantry = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")];
        var cavalry = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("hippeus")];

        // Enough for one of each, less one Materials: Infantry is bought, Cavalry is next and is not.
        army.Think(
            AiFixture.Plenty,
            fixture.With(infantry.Materials + cavalry.Materials - 1, infantry.Energy + cavalry.Energy),
            fixture.Commands);

        Assert.Equal(AiFixture.Chassis("peltast"), Assert.Single(fixture.Commands).ContentId);
    }

    [Fact]
    public void ALineWithNoRoomInTheSupplyIsPassedOverForOneThatFits()
    {
        var fixture = new AiFixture();
        fixture.Place("barracks");
        fixture.Place("works");
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("cavalry_armor", 1), ("line_infantry", 1)]));
        var infantry = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")];
        var cavalry = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("hippeus")];
        Assert.True(cavalry.Supply > infantry.Supply);

        army.Think(supplyRoom: cavalry.Supply - 1, fixture.Rich(), fixture.Commands);

        Assert.Equal(AiFixture.Chassis("peltast"), Assert.Single(fixture.Commands).ContentId);
        Assert.Equal(infantry.Supply, army.SmallestUnitSupply);
    }

    [Fact]
    public void WithoutRoomInTheSupplyItOrdersNothing()
    {
        var fixture = new AiFixture();
        fixture.Place("barracks");
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("line_infantry", 1)]));

        army.Think(supplyRoom: 0, fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);

        army.Think(supplyRoom: 1, fixture.Rich(), fixture.Commands);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void TheWorkersEarnForOneRoundOfTheRatio()
    {
        var fixture = new AiFixture();
        var army = new AiArmy(fixture.Sight, AiFixture.Personality(lines: [("line_infantry", 3), ("cavalry_armor", 1)]));
        var infantry = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")];
        var cavalry = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("hippeus")];

        var budget = fixture.With(0, 0);
        army.Think(AiFixture.Plenty, budget, fixture.Commands);

        Assert.Empty(fixture.Commands);
        Assert.Equal((3 * infantry.Materials) + cavalry.Materials, budget.MissingMaterials);
        Assert.Equal((3 * infantry.Energy) + cavalry.Energy, budget.MissingEnergy);
    }
}
