using Sim.Core;
using Sim.Data;
using Sim.Map;

namespace Ai.Tests;

/// <summary>
/// How it fights (design 5.5, item 5): when its army sets out, when it comes back, what it goes
/// for and what its units at home do. Each test puts units on Ridge, has one round thought and
/// reads the orders.
/// </summary>
public class AiFightTests
{
    /// <summary>A tile on its own side of the ridge, from which the way to the other seat's start goes through either passage.</summary>
    private const int HomeX = 24;
    private const int HomeY = 48;

    /// <summary>A tile across the ridge, so near the other seat's start that no passage lies on the way.</summary>
    private const int FrontX = 68;
    private const int FrontY = 48;

    private static int Peltast => new AiFixture().Sight.ValueOf(AiFixture.Chassis("peltast"));

    [Fact]
    public void ItSetsOutWhenItsArmyIsWorthTheRatioTimesWhatItTakesTheEnemysToBeWorth()
    {
        // 1.3 times the 400 it assumes is 520, which is thirteen Peltasts.
        Assert.Equal(40, Peltast);
        var (fixture, fight) = Make();
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 12, HomeX - 6, HomeY + 2);

        Round(fixture, fight);

        Assert.Equal(FightPhase.Gather, fight.Phase);
        Assert.Equal(480, fight.ArmyValue);
        Assert.Equal(400, fight.EnemyEstimate);
        Assert.Empty(fixture.Commands);
        Assert.Equal(-1, fight.FirstAttackTick);

        units.Add(fixture.SpawnAt(AiFixture.Player, "peltast", HomeX - 6, HomeY + 3));
        Round(fixture, fight);

        Assert.Equal(FightPhase.Advance, fight.Phase);
        Assert.Equal(fixture.Match.World.Tick, fight.FirstAttackTick);
        Assert.Equal(1, fight.Attacks);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal(units, order.Entities.ToArray());
    }

    [Fact]
    public void AtItsSupplyCapItSetsOutWhateverItsArmyIsWorth()
    {
        var (fixture, fight) = Make();
        fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);

        Round(fixture, fight);
        Assert.Equal(FightPhase.Gather, fight.Phase);

        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(FightPhase.Advance, fight.Phase);
    }

    [Fact]
    public void WithoutAUnitItDoesNotSetOut()
    {
        var (fixture, fight) = Make();

        Round(fixture, fight, atSupplyCap: true);

        Assert.Equal(FightPhase.Gather, fight.Phase);
        Assert.Empty(fixture.Commands);
    }

    /// <summary>It plays under the fog (design 5.5): an army it has not seen is not in its estimate.</summary>
    [Fact]
    public void ItsEstimateIsOfTheEnemyUnitsItHasSeen()
    {
        var (fixture, fight) = Make(AiFixture.Fight(assumedEnemyValue: 100));
        fixture.SpawnRow(AiFixture.Player, "peltast", 4, HomeX - 2, HomeY + 2);
        var hoplite = fixture.Sight.ValueOf(AiFixture.Chassis("hoplite"));

        // Twenty Hoplites across the ridge, where nothing of its own sees.
        fixture.SpawnRow(1, "hoplite", 20, 60, 30);
        Assert.Empty(fixture.Sight.Enemies);
        Round(fixture, fight);
        Assert.Equal(100, fight.EnemyEstimate);
        Assert.Equal(FightPhase.Advance, fight.Phase);

        // Three where its units see them, and a worker, which is no part of an army.
        var (other, otherFight) = Make(AiFixture.Fight(assumedEnemyValue: 100));
        other.SpawnRow(AiFixture.Player, "peltast", 4, HomeX - 2, HomeY + 2);
        other.SpawnRow(1, "hoplite", 3, HomeX, HomeY + 6);
        other.SpawnWorkerAt(1, HomeX, HomeY + 7);
        Assert.Equal(4, other.Sight.Enemies.Count);
        Round(other, otherFight);
        Assert.Equal(3 * hoplite, otherFight.EnemyEstimate);
        Assert.Equal(FightPhase.Gather, otherFight.Phase);
    }

    [Fact]
    public void AnEnemyUnitStaysInItsEstimateUntilItSeesItDie()
    {
        var (fixture, fight) = Make(AiFixture.Fight(assumedEnemyValue: 0, attackRatio: "100"));
        var hoplite = fixture.Sight.ValueOf(AiFixture.Chassis("hoplite"));
        var scout = fixture.SpawnAt(AiFixture.Player, "peltast", 60, 30);
        var enemies = fixture.SpawnRow(1, "hoplite", 3, 60, 33);

        Round(fixture, fight);
        Assert.Equal(3 * hoplite, fight.EnemyEstimate);

        // Seen again, it is counted once.
        Round(fixture, fight);
        Assert.Equal(3 * hoplite, fight.EnemyEstimate);

        // One dies where it sees it.
        fixture.Kill(enemies[0]);
        Round(fixture, fight);
        Assert.Equal(2 * hoplite, fight.EnemyEstimate);

        // With its scout gone it sees none of them, and still takes them to be there.
        fixture.Kill(scout);
        Round(fixture, fight);
        fixture.Run(Sim.SimInfo.TicksPerSecond);
        Assert.Empty(fixture.Sight.Enemies);
        Round(fixture, fight);
        Assert.Equal(2 * hoplite, fight.EnemyEstimate);

        // One that dies out of its sight is not known to be dead.
        fixture.Kill(enemies[1]);
        Round(fixture, fight);
        Assert.Equal(2 * hoplite, fight.EnemyEstimate);
    }

    [Fact]
    public void AReasonMustHoldForTheReactionDelayBeforeItActs()
    {
        var (fixture, fight) = Make(AiFixture.Fight(reactionTicks: 2 * AiPlayer.ThinkInterval));
        fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 2);

        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(FightPhase.Gather, fight.Phase);

        fixture.Run(AiPlayer.ThinkInterval);
        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(FightPhase.Gather, fight.Phase);

        // A reason that is gone for a round counts from the start again.
        fixture.Run(AiPlayer.ThinkInterval);
        Round(fixture, fight, atSupplyCap: false);
        Assert.Equal(FightPhase.Gather, fight.Phase);

        for (var round = 0; round < 2; round++)
        {
            fixture.Run(AiPlayer.ThinkInterval);
            Round(fixture, fight, atSupplyCap: true);
            Assert.Equal(FightPhase.Gather, fight.Phase);
        }

        fixture.Run(AiPlayer.ThinkInterval);
        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(FightPhase.Advance, fight.Phase);
    }

    /// <summary>It finds the passages from the markers of the map file (design 5.5), and its personality says which its army takes.</summary>
    [Theory]
    [InlineData(MarkerKind.OpenField)]
    [InlineData(MarkerKind.Chokepoint)]
    public void ItsArmyGoesThroughThePassageOfItsPersonalityAndItsRaidersThroughTheOther(MarkerKind passage)
    {
        var (fixture, fight) = Make(AiFixture.Fight(passage: passage));
        var army = fixture.SpawnRow(AiFixture.Player, "peltast", 6, HomeX - 3, HomeY + 2);
        var raiders = fixture.SpawnRow(AiFixture.Player, "hippeus", 2, HomeX - 3, HomeY + 3);

        Round(fixture, fight, atSupplyCap: true);

        Assert.Equal(FightPhase.Advance, fight.Phase);
        Assert.Equal(RaidPhase.Advance, fight.Raid.Phase);
        Assert.Equal(2, fixture.Commands.Count);

        var raid = Assert.Single(fixture.Orders(CommandType.Move));
        Assert.Equal(raiders, raid.Entities.ToArray());
        Assert.Equal(MiddleOf(fixture, passage == MarkerKind.OpenField ? MarkerKind.Chokepoint : MarkerKind.OpenField), TargetOf(raid));

        var attack = Assert.Single(fixture.Orders(CommandType.AttackMove));
        Assert.Equal(army, attack.Entities.ToArray());
        Assert.Equal(MiddleOf(fixture, passage), TargetOf(attack));
    }

    /// <summary>"Fights at chokepoints" (design 5.5, item 4): a row of the response table that names a passage overrides the personality's.</summary>
    [Fact]
    public void WhileARowNamesAPassageItsArmyGoesThroughThatOne()
    {
        var fixture = new AiFixture(players: 2) { Muster = (HomeX, HomeY) };
        var personality = AiFixture.Personality(lines: [("line_infantry", 1)]);
        Assert.Equal(MarkerKind.OpenField, personality.Fight.Passage);
        var table = fixture.Responding(personality, new AiDoesDef([], Research: null, MarkerKind.Chokepoint, CatchUpFirst: false));
        var fight = new AiFight(fixture.Sight, new AiSearch(fixture.Sight), personality.Fight, table.Intel, table.Responses);
        Assert.Equal(MarkerKind.Chokepoint, fight.Passage);
        var army = fixture.SpawnRow(AiFixture.Player, "peltast", 6, HomeX - 3, HomeY + 2);

        Round(fixture, fight, atSupplyCap: true);

        var attack = Assert.Single(fixture.Orders(CommandType.AttackMove));
        Assert.Equal(army, attack.Entities.ToArray());
        Assert.Equal(MiddleOf(fixture, MarkerKind.Chokepoint), TargetOf(attack));
    }

    [Fact]
    public void AtThePassageTheForceGoesOnToTheStartOfTheOtherSeatWhileItKnowsNoStructureOfIts()
    {
        var (fixture, fight) = Make();
        var (passageX, passageY) = MiddleOf(fixture, MarkerKind.OpenField);
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 6, passageX - 3, passageY);
        Assert.Empty(fixture.Sight.EnemyStructures);

        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(FightPhase.Advance, fight.Phase);

        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(FightPhase.Attack, fight.Phase);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal(units, order.Entities.ToArray());
        Assert.Equal(fixture.Sight.OtherStarts.Single(), TargetOf(order));

        // The orders stand: nothing is said twice.
        fixture.Carry();
        Round(fixture, fight, atSupplyCap: true);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void WithNothingKnownAndNoOtherStartItLooksWhereItHasNotSeen()
    {
        var fixture = new AiFixture(players: 1) { Muster = (HomeX, HomeY) };
        var search = new AiSearch(fixture.Sight);
        var fight = new AiFight(fixture.Sight, search, AiFixture.Fight());
        fixture.SpawnRow(AiFixture.Player, "peltast", 3, HomeX, HomeY + 2);

        search.Update();
        Round(fixture, fight, atSupplyCap: true);

        Assert.Equal(FightPhase.Attack, fight.Phase);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.AttackMove, order.Type);
        var (x, y) = TargetOf(order);
        Assert.False(fixture.Sight.IsExplored(x, y));
    }

    [Fact]
    public void ItRetreatsWhenItHasLostTheShareOfTheForceItSetOutWith()
    {
        var (fixture, fight) = Make();
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 10, HomeX - 5, HomeY + 2);
        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(10, fight.ForceCount);

        // Three of ten are 30%.
        fixture.Kill(units[0], units[1], units[2]);
        Round(fixture, fight);
        Assert.Equal(FightPhase.Advance, fight.Phase);
        Assert.Equal(0, fight.Retreats);

        // Four of ten are 40%.
        fixture.Kill(units[3]);
        Round(fixture, fight);
        Assert.Equal(FightPhase.Retreat, fight.Phase);
        Assert.Equal(1, fight.Retreats);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Move, order.Type);
        Assert.Equal(units.Skip(4), order.Entities.ToArray());
        Assert.Equal((HomeX, HomeY), TargetOf(order));

        // At home the force is no more, and what is left of it does not set out again by itself.
        Round(fixture, fight);
        Assert.Equal(FightPhase.Gather, fight.Phase);
        Round(fixture, fight);
        Assert.Equal(FightPhase.Gather, fight.Phase);
        Assert.Equal(1, fight.Attacks);
    }

    [Fact]
    public void UnitsThatJoinAForceCountInWhatItMayLose()
    {
        var (fixture, fight) = Make(AiFixture.Fight(reinforceUnits: 2), FrontX, FrontY);
        var first = fixture.SpawnRow(AiFixture.Player, "peltast", 2, FrontX - 8, FrontY + 2);
        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(FightPhase.Attack, fight.Phase);

        // One alone waits at home; with a second, both follow the force.
        fixture.SpawnAt(AiFixture.Player, "peltast", FrontX, FrontY + 3);
        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(2, fight.ForceCount);

        fixture.SpawnAt(AiFixture.Player, "peltast", FrontX + 1, FrontY + 3);
        Round(fixture, fight, atSupplyCap: true);
        Assert.Equal(4, fight.ForceCount);

        // One of four is 25%, two are 50%.
        fixture.Kill(first[0]);
        Round(fixture, fight);
        Assert.Equal(FightPhase.Attack, fight.Phase);

        fixture.Kill(first[1]);
        Round(fixture, fight);
        Assert.Equal(FightPhase.Retreat, fight.Phase);
    }

    [Fact]
    public void ItGoesForAStructureThatHoldsTerritoryBeforeANearerOne()
    {
        var (fixture, fight) = Make(musterX: FrontX, musterY: FrontY);
        fixture.ExploreAll();
        var capitol = fixture.Sight.EnemyStructures.Single(structure => structure.Type == fixture.Data.StartStructure.Index);
        Assert.True(capitol.IsRemembered);
        fixture.PlaceAt(1, "barracks", FrontX - 2, FrontY + 8);

        // From far off the force walks at it on an attack-move, and fights what it meets.
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 4, FrontX - 16, FrontY);
        Round(fixture, fight, atSupplyCap: true);

        Assert.Equal(FightPhase.Attack, fight.Phase);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal(units, order.Entities.ToArray());
        Assert.Equal((capitol.X + (capitol.Size / 2), capitol.Y + (capitol.Size / 2)), TargetOf(order));
    }

    [Fact]
    public void WithNoStructureThatHoldsTerritoryKnownItGoesForTheNearestItKnows()
    {
        var (fixture, fight) = Make(musterX: FrontX, musterY: FrontY);
        var near = fixture.PlaceAt(1, "barracks", FrontX - 4, FrontY + 6);
        fixture.PlaceAt(1, "works", FrontX - 10, FrontY + 24);
        fixture.SpawnAt(AiFixture.Player, "peltast", FrontX - 9, FrontY + 20);
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 4, FrontX - 6, FrontY);
        Assert.Equal(2, fixture.Sight.EnemyStructures.Count);

        Round(fixture, fight, atSupplyCap: true);

        // No enemy unit is near, and the structure is: an attack-move would end beside it, so they are told to attack it.
        // The one that stands far from it, beside the other structure, walks to it on an attack-move.
        Assert.Equal(2, fixture.Commands.Count);
        var order = Assert.Single(fixture.Orders(CommandType.Attack));
        Assert.Equal(near, order.TargetEntity);
        Assert.Equal(units, order.Entities.ToArray());
        Assert.Single(Assert.Single(fixture.Orders(CommandType.AttackMove)).Entities.ToArray());

        fixture.Carry();
        Round(fixture, fight, atSupplyCap: true);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void AnEnemyUnitNearAUnitOfTheForceIsFoughtBeforeTheStructure()
    {
        var (fixture, fight) = Make(musterX: FrontX, musterY: FrontY);
        var barracks = fixture.PlaceAt(1, "barracks", FrontX - 4, FrontY + 6);
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 4, FrontX - 6, FrontY);
        Round(fixture, fight, atSupplyCap: true);
        fixture.Carry();

        // A Hoplite comes within reach of the two on the left, and stays far from the two on the right.
        fixture.SpawnAt(1, "hoplite", FrontX - 12, FrontY - 5);
        Assert.Single(fixture.Sight.Enemies);
        Round(fixture, fight, atSupplyCap: true);

        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal(units.Take(2), order.Entities.ToArray());
        Assert.Equal((FrontX - 12, FrontY - 5), TargetOf(order));

        // With the Hoplite gone they turn on the structure: by an order to attack it, or because
        // their attack-move picked it, in which case they are left to it.
        fixture.Carry();
        fixture.Kill(fixture.Sight.Enemies.Single().Entity);
        for (var round = 0; round < 10; round++)
        {
            fixture.Run(AiPlayer.ThinkInterval);
            Round(fixture, fight, atSupplyCap: true);
            Assert.All(fixture.Commands, command =>
            {
                Assert.Equal(CommandType.Attack, command.Type);
                Assert.Equal(barracks, command.TargetEntity);
            });
            fixture.Carry();
        }

        Assert.All(fixture.Sight.Fighters, unit => Assert.Equal(barracks, unit.Target));
    }

    [Fact]
    public void ATowerIsFoughtLikeAUnit()
    {
        var (fixture, fight) = Make(musterX: FrontX, musterY: FrontY);
        fixture.PlaceAt(1, "barracks", FrontX - 4, FrontY + 6);
        fixture.PlaceAt(1, "tower", FrontX - 6, FrontY - 4);
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 4, FrontX - 6, FrontY);

        Round(fixture, fight, atSupplyCap: true);

        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal(units, order.Entities.ToArray());
        var tower = AiFixture.Structure("tower");
        Assert.Equal((FrontX - 6 + (tower.Footprint / 2), FrontY - 4 + (tower.Footprint / 2)), TargetOf(order));
    }

    [Fact]
    public void ItsUnitsAtHomeGoToAStructureOfItsOwnThatIsDamaged()
    {
        var (fixture, fight) = Make();
        var barracks = fixture.Place("barracks");
        var units = fixture.SpawnRow(AiFixture.Player, "peltast", 3, HomeX - 1, HomeY + 2);
        var structure = fixture.Sight.Structures.Single(candidate => candidate.Entity == barracks);

        Round(fixture, fight);
        Assert.False(fight.IsDefending);
        Assert.Empty(fixture.Commands);

        fixture.Damage(barracks, 1000);
        Round(fixture, fight, atSupplyCap: true);

        Assert.True(fight.IsDefending);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.AttackMove, order.Type);
        Assert.Equal(units, order.Entities.ToArray());
        Assert.Equal((structure.X + (structure.Size / 2), structure.Y + (structure.Size / 2)), TargetOf(order));

        // While it defends, its army does not set out, though it is at its supply cap.
        Assert.Equal(FightPhase.Gather, fight.Phase);

        // When nothing has been damaged for a while it sets out again.
        fixture.Run(AiFight.DefendTicks);
        Round(fixture, fight, atSupplyCap: true);
        Assert.False(fight.IsDefending);
        Assert.Equal(FightPhase.Advance, fight.Phase);
    }

    [Fact]
    public void AnIdleUnitAwayFromWhereItsArmyGathersIsSentThere()
    {
        var (fixture, fight) = Make();
        var near = fixture.SpawnAt(AiFixture.Player, "peltast", HomeX + AiFight.Near, HomeY);
        var far = fixture.SpawnAt(AiFixture.Player, "peltast", HomeX + AiFight.Near + 1, HomeY);

        Round(fixture, fight);

        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Move, order.Type);
        Assert.Equal([far], order.Entities.ToArray());
        Assert.NotEqual(near, far);
        Assert.Equal((HomeX, HomeY), TargetOf(order));

        // On its way it is not told again.
        fixture.Carry();
        Round(fixture, fight);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void APartyOfRaidersSetsOutByItselfWhenItIsWhole()
    {
        var (fixture, fight) = Make(AiFixture.Fight(raidParty: 3));
        fixture.SpawnRow(AiFixture.Player, "hippeus", 2, HomeX, HomeY + 2);
        fixture.SpawnAt(AiFixture.Player, "peltast", HomeX, HomeY + 3);

        Round(fixture, fight);
        Assert.Equal(RaidPhase.None, fight.Raid.Phase);
        Assert.Empty(fixture.Commands);

        fixture.SpawnAt(AiFixture.Player, "hippeus", HomeX, HomeY + 4);
        Round(fixture, fight);

        // The three go, and the army stays: it is not worth enough to set out.
        Assert.Equal(RaidPhase.Advance, fight.Raid.Phase);
        Assert.Equal(FightPhase.Gather, fight.Phase);
        Assert.Equal(1, fight.Raid.Raids);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Move, order.Type);
        Assert.Equal(3, order.Entities.Length);
        Assert.Equal(MiddleOf(fixture, MarkerKind.Chokepoint), TargetOf(order));
    }

    [Fact]
    public void AnArmyOfRaidersAloneSetsOutAsAnArmy()
    {
        var (fixture, fight) = Make();
        fixture.SpawnRow(AiFixture.Player, "hippeus", 2, HomeX, HomeY + 2);

        Round(fixture, fight, atSupplyCap: true);

        Assert.Equal(RaidPhase.None, fight.Raid.Phase);
        Assert.Equal(FightPhase.Advance, fight.Phase);
        Assert.Equal(2, fight.ForceCount);
    }

    [Fact]
    public void ThePartyAttacksTheWorkersItSeesAndThenAGenerator()
    {
        var (fixture, fight) = Make(AiFixture.Fight(raidParty: 2), FrontX, FrontY);
        var raiders = fixture.SpawnRow(AiFixture.Player, "hippeus", 2, FrontX - 4, FrontY);
        var generator = fixture.PlaceAt(1, "generator", FrontX - 4, FrontY + 6);
        var left = fixture.SpawnWorkerAt(1, FrontX - 8, FrontY + 2);
        var right = fixture.SpawnWorkerAt(1, FrontX + 1, FrontY + 2);
        fixture.SpawnAt(1, "hoplite", FrontX - 4, FrontY - 3);

        // No passage is on the way, so it is at its work at once.
        Round(fixture, fight);
        Assert.Equal(RaidPhase.Raid, fight.Raid.Phase);
        Round(fixture, fight);

        // Each goes for the worker nearest to it, and neither for the Hoplite.
        var orders = fixture.Orders(CommandType.Attack);
        Assert.Equal(2, orders.Count);
        Assert.Equal(fixture.Commands.Count, orders.Count);
        Assert.Equal((left, raiders[0]), (orders[0].TargetEntity, orders[0].Entities.ToArray().Single()));
        Assert.Equal((right, raiders[1]), (orders[1].TargetEntity, orders[1].Entities.ToArray().Single()));

        fixture.Carry();
        Round(fixture, fight);
        Assert.Empty(fixture.Commands);

        // With the workers dead, the Generator.
        fixture.Kill(left, right);
        Round(fixture, fight);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Attack, order.Type);
        Assert.Equal(generator, order.TargetEntity);
        Assert.Equal(raiders, order.Entities.ToArray());
    }

    [Fact]
    public void ThePartyGoesHomeWhenItHasLostItsShare()
    {
        var (fixture, fight) = Make(AiFixture.Fight(raidParty: 5), FrontX, FrontY);
        var raiders = fixture.SpawnRow(AiFixture.Player, "hippeus", 5, FrontX - 4, FrontY);
        fixture.PlaceAt(1, "generator", FrontX - 4, FrontY + 6);
        Round(fixture, fight);
        Round(fixture, fight);
        fixture.Carry();

        fixture.Kill(raiders[0]);
        Round(fixture, fight);
        Assert.Equal(RaidPhase.Raid, fight.Raid.Phase);

        fixture.Kill(raiders[1]);
        Round(fixture, fight);
        Assert.Equal(RaidPhase.Return, fight.Raid.Phase);
        var order = Assert.Single(fixture.Commands);
        Assert.Equal(CommandType.Move, order.Type);
        Assert.Equal(raiders.Skip(2), order.Entities.ToArray());
        Assert.Equal((FrontX, FrontY), TargetOf(order));

        // At home they are units of the army again.
        Round(fixture, fight);
        Assert.Equal(RaidPhase.None, fight.Raid.Phase);
    }

    [Fact]
    public void WithNothingToRaidThePartyGoesHome()
    {
        var fixture = new AiFixture(players: 1) { Muster = (HomeX, HomeY) };
        var fight = new AiFight(fixture.Sight, new AiSearch(fixture.Sight), AiFixture.Fight(raidParty: 2));
        fixture.SpawnRow(AiFixture.Player, "hippeus", 2, HomeX, HomeY + 2);

        Round(fixture, fight);
        Assert.Equal(RaidPhase.Raid, fight.Raid.Phase);

        Round(fixture, fight);
        Assert.Equal(RaidPhase.Return, fight.Raid.Phase);
    }

    [Theory]
    [InlineData("1.3", 400, 519, false)]
    [InlineData("1.3", 400, 520, true)]
    [InlineData("1", 400, 400, true)]
    [InlineData("1", 400, 399, false)]
    [InlineData("2.25", 1000, 2250, true)]
    [InlineData("2.25", 1000, 2249, false)]
    [InlineData("1.3", 0, 0, true)]
    public void TheRatioIsWorkedOutInWholeNumbers(string ratio, long enemy, long own, bool enough)
    {
        Assert.Equal(enough, AiFixture.Fight(attackRatio: ratio).IsEnoughToAttack(own, enemy));
    }

    [Theory]
    [InlineData("0.4", 1000, 399, false)]
    [InlineData("0.4", 1000, 400, true)]
    [InlineData("1", 1000, 999, false)]
    [InlineData("1", 1000, 1000, true)]
    [InlineData("0.4", 0, 0, false)]
    public void TheShareLostIsWorkedOutInWholeNumbers(string share, long whole, long lost, bool tooMuch)
    {
        Assert.Equal(tooMuch, AiFixture.Fight(retreatLosses: share).IsTooMuchLost(lost, whole));
    }

    private static (AiFixture Fixture, AiFight Fight) Make(AiFightDef? fight = null, int musterX = HomeX, int musterY = HomeY)
    {
        var fixture = new AiFixture(players: 2) { Muster = (musterX, musterY) };
        return (fixture, new AiFight(fixture.Sight, new AiSearch(fixture.Sight), fight ?? AiFixture.Fight()));
    }

    /// <summary>Has one round thought, with the orders of the round before taken off the list. The match does not run.</summary>
    private static void Round(AiFixture fixture, AiFight fight, bool atSupplyCap = false)
    {
        fixture.Commands.Clear();
        fight.Watch();
        fight.Think(hasMuster: true, fixture.Muster.X, fixture.Muster.Y, atSupplyCap, fixture.Commands);
    }

    private static (int X, int Y) MiddleOf(AiFixture fixture, MarkerKind kind)
    {
        var marker = fixture.Match.Map.Markers.Single(candidate => candidate.Kind == kind);
        return (marker.Area.X + (marker.Area.SizeX / 2), marker.Area.Y + (marker.Area.SizeY / 2));
    }

    private static (int X, int Y) TargetOf(Command command) => (command.TargetX.FloorToInt(), command.TargetY.FloorToInt());
}
