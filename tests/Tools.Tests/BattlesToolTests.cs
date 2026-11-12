using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Tools.Battles;

namespace Tools.Tests;

/// <summary>
/// The balance harness: the four acceptance battles of design 3.1 are set up as the design
/// fixes them, a fight is measured rightly, and a target is judged as the design words it.
/// </summary>
/// <remarks>
/// Who wins a battle with the game's data is not tested: that is what the harness is there to
/// report, and it changes with every number that is tuned.
/// </remarks>
public class BattlesToolTests
{
    private static readonly Lazy<GameData> Loaded = new(() => DataLoader.Load(new DiskDataFiles(Repository.DataFolder)).Data!);

    private static GameData Data => Loaded.Value;

    private static BattleDef Number(int number) => AcceptanceBattles.All.Single(battle => battle.Number == number);

    /// <summary>
    /// Design 3.1's table: the armies, by price where the design gives it, and the widths. The
    /// prices are those of example B at first-playable tiers (design 2.6) with the game's data.
    /// </summary>
    [Theory]
    [InlineData(1, ArenaKind.OpenField, 10, 80, 2240, 80, 20, 2260, 40)]
    [InlineData(2, ArenaKind.OpenField, 10, 40, 1120, 40, 20, 2260, 40)]
    [InlineData(3, ArenaKind.Chokepoint, 6, 80, 2240, 80, 20, 2260, 40)]
    [InlineData(4, ArenaKind.OpenField, 10, 80, 2240, 80, 16, 2208, 34)]
    public void TheArmiesAreTheDesigns(int number, ArenaKind arena, int width, int massUnits, int massPrice, int massSupply, int eliteUnits, int elitePrice, int eliteSupply)
    {
        var battle = Number(number);
        Assert.Equal((arena, width), (battle.Arena, battle.BlockWidth));
        Assert.Null(Battle.Problem(Data, battle));

        var fight = Battle.Start(Data, battle, massWest: true);

        var mass = fight.Result(Side.Mass);
        var elite = fight.Result(Side.Elite);
        Assert.Equal((massUnits, massPrice, massSupply, 100), (mass.Units, mass.Price, mass.Supply, mass.SupplyCap));
        Assert.Equal((eliteUnits, elitePrice, eliteSupply, 55), (elite.Units, elite.Price, elite.Supply, elite.SupplyCap));
        Assert.Equal((massUnits, massPrice), (mass.UnitsKept, mass.PriceKept));
        Assert.Equal([1, 1, 2, 4, 4, 2], fight.Match.Research.Tech(fight.Player(Side.Mass)).Tiers);
        Assert.Equal([4, 4, 2, 1, 1, 2], fight.Match.Research.Tech(fight.Player(Side.Elite)).Tiers);
    }

    [Fact]
    public void TheArmiesOfBattleFourAreGrenadiersWithTwoOnagersBehindThem()
    {
        var fight = Battle.Start(Data, Number(4), massWest: true);
        var match = fight.Match;

        var chassis = fight.Units(Side.Elite).ToArray().Select(unit => Data.Chassis[match.Combatants.Get(unit).Chassis].Id).ToList();

        Assert.Equal([.. Enumerable.Repeat("grenadier", 14), "onager", "onager"], chassis);
        Assert.All(fight.Units(Side.Mass).ToArray(), unit => Assert.Equal("auxiliary", Data.Chassis[match.Combatants.Get(unit).Chassis].Id));
    }

    /// <summary>
    /// "Each army starts as a block in a square grid at its spacing, 10 units wide, with the
    /// front ranks 15 tiles apart": 80 units are 8 ranks of 10, one tile apart each way.
    /// </summary>
    [Fact]
    public void ABlockIsASquareGridAtItsSpacingWithTheFrontRanks15TilesApart()
    {
        var map = Arena.Create(ArenaKind.OpenField);

        var west = Arena.Positions(map, west: true, 10, (80, Fixed.One), (0, Fixed.One));
        var east = Arena.Positions(map, west: false, 10, (20, Fixed.One), (0, Fixed.One));

        Assert.Equal(80, west.Distinct().Count());
        for (var i = 0; i < west.Length; i++)
        {
            // Rank by rank from the front, which is the easternmost rank of the western army.
            Assert.Equal(Fixed.FromInt(28) + Fixed.Half - Fixed.FromInt(i / 10), west[i].X);
            Assert.Equal(Fixed.FromInt(19 + (i % 10)) + Fixed.Half, west[i].Y);
        }

        Assert.Equal(Fixed.FromInt(15), east[0].X - west[0].X);
        Assert.All(east.Take(10), position => Assert.Equal(east[0].X, position.X));
        Assert.All(east.Skip(10), position => Assert.Equal(east[0].X + Fixed.One, position.X));
        Assert.Equal(west.Take(10).Select(position => position.Y), east.Take(10).Select(position => position.Y));
    }

    /// <summary>A spacing of 1.5 tiles, as Cavalry and Armor have, spreads the grid both ways.</summary>
    [Fact]
    public void ABlockOfAWiderSpacingIsAWiderGrid()
    {
        var spacing = Fixed.One + Fixed.Half;

        var positions = Arena.Positions(Arena.Create(ArenaKind.OpenField), west: true, 4, (8, spacing), (0, Fixed.One));

        Assert.Equal(spacing, positions[1].Y - positions[0].Y);
        Assert.Equal(spacing, positions[0].X - positions[4].X);
        Assert.Equal(positions[0].Y, positions[4].Y);
        // Centered on the axis of the map, which is 24 tiles from its north edge.
        Assert.Equal(Fixed.FromInt(48), positions[0].Y + positions[3].Y);
    }

    /// <summary>
    /// A rank that is not full is centered, and so is the row behind the block: "the Onagers in
    /// one row behind the Grenadiers". 14 Grenadiers are a rank of 10 and a rank of 4.
    /// </summary>
    [Fact]
    public void ARankThatIsNotFullAndTheRowBehindAreCenteredOnTheAxis()
    {
        var map = Arena.Create(ArenaKind.OpenField);

        var positions = Arena.Positions(map, west: false, 10, (14, Fixed.One), (2, Fixed.One));

        var front = positions[0].X;
        var axis = Fixed.FromInt(Arena.OpenHeight / 2);
        Assert.Equal(
            [axis - Fixed.One - Fixed.Half, axis - Fixed.Half, axis + Fixed.Half, axis + Fixed.One + Fixed.Half],
            positions[10..14].Select(position => position.Y));
        Assert.All(positions[10..14], position => Assert.Equal(front + Fixed.One, position.X));
        Assert.Equal([axis - Fixed.Half, axis + Fixed.Half], positions[14..].Select(position => position.Y));
        Assert.All(positions[14..], position => Assert.Equal(front + Fixed.FromInt(2), position.X));
    }

    /// <summary>
    /// Swapping the sides is a mirror image: the same block stands as far east of the middle as
    /// it stood west of it, on the same rows.
    /// </summary>
    [Theory]
    [InlineData(ArenaKind.OpenField, 10)]
    [InlineData(ArenaKind.Chokepoint, 6)]
    public void SwappingTheSidesMirrorsTheArmies(ArenaKind kind, int width)
    {
        var map = Arena.Create(kind);

        var west = Arena.Positions(map, west: true, width, (80, Fixed.One), (2, Fixed.One));
        var east = Arena.Positions(map, west: false, width, (80, Fixed.One), (2, Fixed.One));

        Assert.Equal(west.Select(position => new FixedVector2(Fixed.FromInt(map.Width) - position.X, position.Y)), east);
        Assert.Equal(Fixed.FromInt(map.Width), Arena.BlockCenter(map, true, width, (80, Fixed.One)).X + Arena.BlockCenter(map, false, width, (80, Fixed.One)).X);
    }

    /// <summary>
    /// The chokepoint is a corridor 6 tiles wide between cliffs, marked as a chokepoint, and both
    /// blocks stand in it, 6 wide: every unit is on open ground with cliff or a neighbor beside it.
    /// </summary>
    [Fact]
    public void TheChokepointIsACorridorSixTilesWideThatHoldsBothBlocks()
    {
        var map = Arena.Create(ArenaKind.Chokepoint);

        for (var x = 0; x < map.Width; x++)
        {
            var open = Enumerable.Range(0, map.Height).Where(y => map.TerrainAt(x, y) == TerrainKind.Open).ToList();
            Assert.Equal(Enumerable.Range(Arena.CliffRows, 6), open);
        }

        var marker = Assert.Single(map.Markers);
        Assert.Equal((MarkerKind.Chokepoint, 6), (marker.Kind, marker.Width));

        var fight = Battle.Start(Data, Number(3), massWest: true);
        foreach (var side in new[] { Side.Mass, Side.Elite })
        {
            var rows = fight.Units(side).ToArray().Select(unit => fight.Match.Bodies.Get(unit).Position.Y.FloorToInt()).ToList();
            Assert.All(rows, row => Assert.InRange(row, Arena.CliffRows, Arena.CliffRows + 5));
            Assert.Equal(Enumerable.Range(Arena.CliffRows, 6), rows.Take(6));
        }
    }

    /// <summary>
    /// The width of the test chokepoint can be set, for tuning the test: the corridor and both
    /// blocks in it are then that wide. Nothing else of the four battles changes.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void TheChokepointCanBeAnotherWidth(int width)
    {
        var battles = AcceptanceBattles.WithChokepoint(width);
        var third = battles.Single(battle => battle.Number == 3);

        Assert.Equal([Number(1), Number(2), Number(4)], battles.Where(battle => battle.Number != 3));
        Assert.Equal(Number(3) with { Name = $"Equal resources, chokepoint {width} tiles wide", BlockWidth = width }, third);
        Assert.Equal(AcceptanceBattles.All, AcceptanceBattles.WithChokepoint(AcceptanceBattles.ChokepointWidth));

        var fight = Battle.Start(Data, third, massWest: true);
        var map = fight.Match.Map;
        Assert.Equal(width + (2 * Arena.CliffRows), map.Height);
        Assert.Equal((MarkerKind.Chokepoint, width), (Assert.Single(map.Markers).Kind, Assert.Single(map.Markers).Width));
        foreach (var side in new[] { Side.Mass, Side.Elite })
        {
            var rows = fight.Units(side).ToArray().Select(unit => fight.Match.Bodies.Get(unit).Position.Y.FloorToInt()).ToList();
            Assert.Equal(Enumerable.Range(Arena.CliffRows, width), rows.Take(width));
            Assert.All(rows, row => Assert.InRange(row, Arena.CliffRows, Arena.CliffRows + width - 1));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceBattles.WithChokepoint(AcceptanceBattles.NarrowestChokepoint - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceBattles.WithChokepoint(AcceptanceBattles.WidestChokepoint + 1));
    }

    [Fact]
    public void TheToolFightsTheThirdBattleInAChokepointOfTheWidthAsked()
    {
        var (exitCode, output, errors) = Run("--data", Repository.DataFolder, "--battle", "3", "--chokepoint", "8", "--seconds", "1");

        Assert.Equal(("", ExitCodes.Ok), (errors, exitCode));
        var lines = output.Split(Environment.NewLine);
        Assert.EndsWith("A missed target is reported and fails nothing. The chokepoint of battle 3 is 8 tiles wide, not the design's 6.", lines[0], StringComparison.Ordinal);
        Assert.Contains("3. Equal resources, chokepoint 8 tiles wide", lines);
        Assert.DoesNotContain("not the design's", Run("--data", Repository.DataFolder, "--battle", "3", "--chokepoint", "6", "--seconds", "1").Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOpenFieldIsOpenGroundMarkedAsAnOpenField()
    {
        var map = Arena.Create(ArenaKind.OpenField);

        Assert.All(map.Tiles, tile => Assert.Equal(TerrainKind.Open, tile));
        Assert.Equal(MarkerKind.OpenField, Assert.Single(map.Markers).Kind);
        Assert.Empty(map.Starts);
        Assert.Empty(map.Deposits);
    }

    /// <summary>
    /// "Each side gets one attack-move order onto the other's starting position": after the
    /// first tick every unit is on an attack-move, no order was refused, and nothing else is
    /// ever ordered.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EachSideGetsOneAttackMoveOrderOntoTheOthersStartingPosition(bool massWest)
    {
        var fight = Battle.Start(Data, Number(4), massWest);
        var match = fight.Match;
        Assert.Equal(massWest ? 0 : 1, fight.Player(Side.Mass));
        Assert.Equal(massWest ? 1 : 0, fight.Player(Side.Elite));
        var massStartsWest = match.Bodies.Get(fight.Units(Side.Mass)[0]).Position.X < match.Bodies.Get(fight.Units(Side.Elite)[0]).Position.X;
        Assert.Equal(massWest, massStartsWest);

        match.Simulation.Tick();

        Assert.DoesNotContain(match.Simulation.Events, simEvent => simEvent.Kind == SimEventKind.OrderRefused);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        foreach (var side in new[] { Side.Mass, Side.Elite })
        {
            Assert.All(fight.Units(side).ToArray(), unit => Assert.Equal(UnitOrder.AttackMove, match.Combatants.Get(unit).Order));
        }

        // The mass army walks toward where the elite block stood, and the elite army toward the mass block.
        var massStart = Arena.BlockCenter(match.Map, massWest, 10, (80, Fixed.One));
        var eliteStart = Arena.BlockCenter(match.Map, !massWest, 10, (14, Fixed.One));
        Assert.Equal(eliteStart, Centroid(fight.Units(Side.Mass).ToArray().Select(unit => match.Movers.Get(unit).Destination)));
        Assert.Equal(massStart.Y, Centroid(fight.Units(Side.Elite).ToArray().Select(unit => match.Movers.Get(unit).Destination)).Y);
    }

    /// <summary>
    /// "Army value kept is the price of the surviving units divided by the price of the starting
    /// units; damaged units count in full." Here four Auxiliaries meet twenty Grenadiers.
    /// </summary>
    [Fact]
    public void ArmyValueKeptIsThePriceOfTheSurvivorsOverThePriceAtTheStart()
    {
        var battle = Number(2) with { Mass = new ArmyDef(AcceptanceBattles.MassTiers, new ArmyPart("auxiliary", 4)) };

        var result = Battle.Fight(Data, battle, massWest: true);

        Assert.Equal(Side.Elite, result.Winner);
        Assert.False(result.TimedOut);
        Assert.Equal((4, 0, 112, 0), (result.Mass.Units, result.Mass.UnitsKept, result.Mass.Price, result.Mass.PriceKept));
        Assert.Equal((20, 20, 2260, 2260), (result.Elite.Units, result.Elite.UnitsKept, result.Elite.Price, result.Elite.PriceKept));
        Assert.Equal(100.0, result.Elite.PercentKept);
        Assert.Equal(0.0, result.Mass.PercentKept);
        Assert.True(result.Mass.Attacks > 0, "The Auxiliaries outrange the Grenadiers and never fired.");
    }

    /// <summary>A unit that falls takes its price with it: one Onager is 313 of the 2,208.</summary>
    [Fact]
    public void AUnitThatFallsTakesItsOwnPriceWithIt()
    {
        var fight = Battle.Start(Data, Number(4), massWest: true);
        var match = fight.Match;
        var onager = fight.Units(Side.Elite)[^1];
        match.Health.Get(onager).HitPoints = 0;
        match.Health.Get(fight.Units(Side.Elite)[0]).HitPoints = 1;

        match.Simulation.Tick();

        var elite = fight.Result(Side.Elite);
        Assert.Equal((16, 15, 2208, 2208 - 313), (elite.Units, elite.UnitsKept, elite.Price, elite.PriceKept));
        Assert.Equal(15, fight.Living(Side.Elite));
    }

    [Fact]
    public void AFightThatRunsOutOfTimeHasNoWinner()
    {
        var result = Battle.Fight(Data, Number(1), massWest: true, mostTicks: 20);

        Assert.Null(result.Winner);
        Assert.True(result.TimedOut);
        Assert.Equal(20, result.Ticks);
        Assert.Equal((100.0, 100.0), (result.Mass.PercentKept, result.Elite.PercentKept));
        Assert.False(Number(1).Target.IsMetBy(result));
        Assert.False(Number(4).Target.IsMetBy(result));
    }

    /// <summary>Nothing in a fight is random, so the same fight gives the same result, and a report with no times in it.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheSameFightGivesTheSameResult(int number)
    {
        var first = Battle.Fight(Data, Number(number), massWest: false);
        var second = Battle.Fight(Data, Number(number), massWest: false);

        Assert.Equal(first, second);
        Assert.NotNull(first.Winner);
        Assert.False(first.MassWest);
    }

    /// <summary>
    /// The attacks an army made are never more than its units had time for, and both are counted
    /// from the first attack of the fight: the walk toward the enemy is not in them.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void TheAttacksMadeAreCountedAgainstThoseTheUnitsHadTimeFor(int number)
    {
        var result = Battle.Fight(Data, Number(number), massWest: true);

        foreach (var army in new[] { result.Mass, result.Elite })
        {
            Assert.InRange(army.Attacks, 1, army.AttacksPossible);
            Assert.InRange(army.OpeningAttacks, 1, Math.Min(army.Attacks, army.OpeningAttacksPossible));
            Assert.InRange(army.OpeningAttacksPossible, 1, Math.Min(army.AttacksPossible, army.Units * Battle.OpeningSeconds));
            Assert.InRange(army.PercentOfAttacksMade, 0.0, 100.0);
        }

        // Both lines attack once a second, so no unit had time for more attacks than the fight has seconds.
        Assert.True(result.Mass.AttacksPossible <= result.Mass.Units * Math.Ceiling(result.Seconds));
    }

    /// <summary>
    /// The shells of a fight are the shots of units with a blast radius, here the two Onagers,
    /// and each is counted once with the units it caught.
    /// </summary>
    [Fact]
    public void TheShellsOfAFightAreCountedWithWhatEachCaught()
    {
        var result = Battle.Fight(Data, Number(4), massWest: true);
        var without = Battle.Fight(Data, Number(1), massWest: true);

        Assert.Equal(ShellCount.None, without.Shells);
        Assert.True(result.Shells.Landed > 0);
        Assert.InRange(result.Shells.MostCaught, 1, 80);
        Assert.InRange(result.Shells.Caught, result.Shells.MostCaught, result.Shells.Landed * result.Shells.MostCaught);
        // An Onager fires once in 4 seconds.
        Assert.True(result.Shells.Landed <= 2 * (Math.Ceiling(result.Seconds / 4) + 1));
    }

    /// <summary>
    /// What a shell catches is every hit that names it, counted when it has left the air: three
    /// units for a blast in a crowd, none for a shell that lands on nobody.
    /// </summary>
    [Fact]
    public void AShellHasCaughtEveryUnitThatAHitNamesItFor()
    {
        var counter = new ShellCounter();
        var (crowd, miss, single, late, bullet) = (new EntityId(1), new EntityId(2), new EntityId(3), new EntityId(4), new EntityId(9));
        foreach (var shell in new[] { crowd, miss, single, late })
        {
            counter.Fired(shell);
        }

        counter.Hit(crowd);
        counter.Hit(bullet);
        counter.Hit(crowd);
        counter.Hit(single);
        counter.Hit(crowd);
        counter.EndOfTick(shot => shot == late);

        Assert.Equal(new ShellCount(Landed: 3, Caught: 4, MostCaught: 3), counter.Count);
        Assert.Equal(4.0 / 3, counter.Count.AverageCaught);

        counter.Hit(late);
        counter.Hit(late);
        counter.EndOfTick(_ => false);
        counter.Hit(late);
        counter.EndOfTick(_ => false);

        Assert.Equal(new ShellCount(Landed: 4, Caught: 6, MostCaught: 3), counter.Count);
        Assert.Equal(0.0, ShellCount.None.AverageCaught);
    }

    /// <summary>Design 3.1's targets, judged at the edges of their bands.</summary>
    [Theory]
    [InlineData(1, Side.Mass, 30, true)]
    [InlineData(1, Side.Mass, 60, true)]
    [InlineData(1, Side.Mass, 29, false)]
    [InlineData(1, Side.Mass, 61, false)]
    [InlineData(1, Side.Elite, 45, false)]
    [InlineData(2, Side.Elite, 70, true)]
    [InlineData(2, Side.Elite, 100, true)]
    [InlineData(2, Side.Elite, 69, false)]
    [InlineData(2, Side.Mass, 80, false)]
    [InlineData(3, Side.Elite, 1, true)]
    [InlineData(3, Side.Elite, 100, true)]
    [InlineData(3, Side.Mass, 50, false)]
    [InlineData(4, Side.Elite, 35, true)]
    [InlineData(4, Side.Mass, 35, true)]
    [InlineData(4, Side.Mass, 1, true)]
    [InlineData(4, Side.Elite, 36, false)]
    [InlineData(4, Side.Mass, 36, false)]
    public void ATargetIsJudgedAsTheDesignWordsIt(int number, Side winner, int percentKept, bool met)
    {
        Assert.Equal(met, Number(number).Target.IsMetBy(Result(winner, percentKept)));
    }

    /// <summary>A band's edge is exact: 29.99% is not 30%, and 60.01% is not 60%.</summary>
    [Fact]
    public void TheEdgeOfABandIsExact()
    {
        var target = Number(1).Target;

        Assert.True(target.IsMetBy(Result(Side.Mass, price: 10000, priceKept: 3000)));
        Assert.False(target.IsMetBy(Result(Side.Mass, price: 10000, priceKept: 2999)));
        Assert.True(target.IsMetBy(Result(Side.Mass, price: 10000, priceKept: 6000)));
        Assert.False(target.IsMetBy(Result(Side.Mass, price: 10000, priceKept: 6001)));
    }

    /// <summary>
    /// "A target is met only when it is met both ways, and the harness reports the difference
    /// between the two runs."
    /// </summary>
    [Fact]
    public void ATargetIsMetOnlyWhenItIsMetBothWaysAndTheDifferenceIsReported()
    {
        var bothWays = new BattleReport(Number(1), Result(Side.Mass, 40), Result(Side.Mass, 55, massWest: false));
        var oneWay = new BattleReport(Number(1), Result(Side.Mass, 40), Result(Side.Mass, 25, massWest: false));
        var otherWinner = new BattleReport(Number(1), Result(Side.Mass, 40), Result(Side.Elite, 10, massWest: false));

        Assert.True(bothWays.Met);
        Assert.False(oneWay.Met);
        Assert.False(otherWinner.Met);
        Assert.Equal((true, 15.0), (bothWays.SameWinner, bothWays.DifferenceInPoints));
        // With a different winner the difference is that of the mass army: 40% against none.
        Assert.Equal((false, 40.0), (otherWinner.SameWinner, otherWinner.DifferenceInPoints));

        Assert.Contains("between the two fights: the same winner, 15.0 points of army value kept apart", Text(bothWays), StringComparison.Ordinal);
        Assert.Contains("target MET", Text(bothWays), StringComparison.Ordinal);
        Assert.Contains("target MISSED with mass east", Text(oneWay), StringComparison.Ordinal);
        Assert.Contains("between the two fights: a different winner, 40.0 points of army value kept apart", Text(otherWinner), StringComparison.Ordinal);

        var summary = new StringWriter();
        BattleReport.WriteSummary([bothWays, oneWay with { Battle = Number(3) }], summary);
        Assert.Equal("battles: 1 of 2 targets met (1); missed: 3." + Environment.NewLine, summary.ToString());

        static string Text(BattleReport report)
        {
            var writer = new StringWriter();
            report.Write(writer);
            return writer.ToString();
        }
    }

    /// <summary>The summary says so when no shell of any fight caught more than one unit: then nothing has a blast yet.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void TheSummarySaysWhenNoShellCaughtMoreThanOneUnit(int mostCaught, bool said)
    {
        var report = new BattleReport(
            Number(4),
            Result(Side.Elite, 30) with { Shells = new ShellCount(12, 9, 1) },
            Result(Side.Elite, 30, massWest: false) with { Shells = new ShellCount(12, 14, mostCaught) });
        var summary = new StringWriter();

        BattleReport.WriteSummary([report], summary);

        Assert.Equal(said, summary.ToString().Contains("no shell caught more than one unit", StringComparison.Ordinal));
    }

    /// <summary>"Every army here can be built in a first-playable match": a battle whose army could not be is not fought.</summary>
    [Fact]
    public void ABattleWhoseArmyCouldNotBeBuiltIsNotFought()
    {
        var battle = Number(1);
        var lockedChassis = battle with { Mass = new ArmyDef(AcceptanceBattles.MassTiers, new ArmyPart("grenadier", 10)) };
        var overTheCap = battle with { Elite = new ArmyDef(AcceptanceBattles.EliteTiers, new ArmyPart("grenadier", 28)) };
        var noSuchChassis = battle with { Elite = new ArmyDef(AcceptanceBattles.EliteTiers, new ArmyPart("dragoon", 5)) };
        var tooFewTiers = battle with { Mass = new ArmyDef([1, 1, 2], new ArmyPart("auxiliary", 5)) };

        Assert.Equal("the mass player has not unlocked 'grenadier' with tiers 1, 1, 2, 4, 4, 2", Battle.Problem(Data, lockedChassis));
        Assert.Equal("the elite army needs 56 supply and its player's cap is 55", Battle.Problem(Data, overTheCap));
        Assert.Equal("the data has no chassis 'dragoon'", Battle.Problem(Data, noSuchChassis));
        Assert.Equal("the mass army has 3 tiers and the data has 6 tracks", Battle.Problem(Data, tooFewTiers));
        Assert.Throws<ArgumentException>(() => Battle.Start(Data, overTheCap, massWest: true));
    }

    /// <summary>
    /// The tool the way the command line runs it, with the game's data: every battle is fought
    /// both ways and reported, and a missed target fails nothing.
    /// </summary>
    [Fact]
    public void TheToolFightsEveryBattleBothWaysAndAMissedTargetFailsNothing()
    {
        var (exitCode, output, errors) = Run("--data", Repository.DataFolder);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        var lines = output.Split(Environment.NewLine);
        Assert.Equal(
            [
                "1. Equal resources, open field",
                "2. Equal supply, open field",
                "3. Equal resources, chokepoint 6 tiles wide",
                "4. Equal resources, elite with Artillery, open field",
            ],
            lines.Where(line => line.Length > 1 && char.IsDigit(line[0]) && line[1] == '.'));
        Assert.Equal(4, lines.Count(line => line.StartsWith("   mass west: ", StringComparison.Ordinal)));
        Assert.Equal(4, lines.Count(line => line.StartsWith("   mass east: ", StringComparison.Ordinal)));
        Assert.Equal(4, lines.Count(line => line is "   target MET" || line.StartsWith("   target MISSED", StringComparison.Ordinal)));
        Assert.Contains("   mass:   80 auxiliary, price 2,240, supply 80 of 100", lines);
        Assert.Contains("   elite:  14 grenadier and 2 onager, price 2,208, supply 34 of 55", lines);
        Assert.Contains("   target: mass wins and keeps 30% to 60% of its army value. The design expects, with its starting numbers: met.", lines);
        Assert.Matches(@"battles: \d of 4 targets met", output);
        Assert.Equal(2, lines.Count(line => line.StartsWith("              shells: ", StringComparison.Ordinal)));

        // No time of this machine is in the report, so it is the same in every run.
        Assert.Equal(output, Run("--data", Repository.DataFolder).Output);
    }

    [Fact]
    public void TheToolFightsOnlyTheBattlesNamed()
    {
        var (exitCode, output, _) = Run("--data", Repository.DataFolder, "--battle", "2", "--battle", "3", "--seconds", "5");

        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.DoesNotContain("1. Equal resources, open field", output, StringComparison.Ordinal);
        Assert.Contains("2. Equal supply, open field", output, StringComparison.Ordinal);
        Assert.Contains("3. Equal resources, chokepoint 6 tiles wide", output, StringComparison.Ordinal);
        Assert.Contains("no winner after 5.0 s", output, StringComparison.Ordinal);
        Assert.Contains("battles: 0 of 2 targets met; missed: 2, 3.", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--battle", "5")]
    [InlineData("--battle", "one")]
    [InlineData("--seconds", "0")]
    [InlineData("--chokepoint", "3")]
    [InlineData("--chokepoint", "9")]
    [InlineData("--chokepoint")]
    [InlineData("--data")]
    [InlineData("--fast")]
    public void AnArgumentItDoesNotKnowIsAnErrorOfUsage(params string[] arguments)
    {
        var (exitCode, output, errors) = Run(arguments);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains("Usage:", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatIsNotThereIsAnErrorOfUsage()
    {
        var (exitCode, _, errors) = Run("--data", Path.Combine(Repository.Root, "no-such-folder"));

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("there is no folder", errors, StringComparison.Ordinal);
    }

    /// <summary>Data in which an army's chassis is no longer unlocked at its tiers: the tool says which battle and why, and fails.</summary>
    [Fact]
    public void DataInWhichAnArmyCannotBeBuiltFailsTheTool()
    {
        using var folder = Repository.CopyOfData();
        folder.Edit("lines/guard.jsonc", "\"id\": \"grenadier\"", "\"id\": \"fusilier\"");

        var (exitCode, output, errors) = Run("--data", folder.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal("", output);
        Assert.Contains("battle 1 cannot be fought", errors, StringComparison.Ordinal);
        Assert.Contains("the data has no chassis 'grenadier'", errors, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var exitCode = ToolRunner.Run(["battles", .. arguments], output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }

    private static BattleResult Result(Side winner, int percentKept, bool massWest = true) =>
        Result(winner, price: 100, priceKept: percentKept, massWest);

    private static BattleResult Result(Side winner, long price, long priceKept, bool massWest = true)
    {
        var kept = new ArmyResult(10, 5, price, priceKept, 20, 55, 0, 0, 0, 0);
        var fallen = new ArmyResult(10, 0, price, 0, 20, 55, 0, 0, 0, 0);
        return new BattleResult(massWest, winner, 600, TimedOut: false, winner == Side.Mass ? kept : fallen, winner == Side.Elite ? kept : fallen, ShellCount.None);
    }

    private static FixedVector2 Centroid(IEnumerable<FixedVector2> points)
    {
        var all = points.ToList();
        long x = 0;
        long y = 0;
        foreach (var point in all)
        {
            x += point.X.Raw;
            y += point.Y.Raw;
        }

        return new FixedVector2(Fixed.FromRaw((int)(x / all.Count)), Fixed.FromRaw((int)(y / all.Count)));
    }
}
