using Sim.Core;
using Sim.Outcome;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Outcome.PlayerStanding;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Outcome.ConquestByAttackMatches;

namespace Sim.Tests.Outcome;

/// <summary>
/// Conquest (design 5.1) when structures fall to attacks (design 2.4 and 2.5), as they do in a
/// match: nothing here takes a structure off the map but the units and Towers that attack it.
/// <see cref="ConquestTests"/> has the rule itself, with structures whose hit points are set
/// to zero.
/// </summary>
/// <remarks>
/// A Capitol has 2,000 hit points and an Outpost 1,200. A Hoplite hits for 8 once a second; a
/// Peltast shoots for 6 once a second at range 5; a Ballista fires a shell every 4 seconds at
/// range 9 that does 48, and three times that to a structure. A Tower has 600 hit points and
/// shoots for 10 once a second at range 7.
/// </remarks>
public class ConquestByAttackTests
{
    /// <summary>
    /// The hits are counted by hand from the numbers above: 2,000 hit points are 250 hits of a
    /// Hoplite, 334 shots of a Peltast and 14 shells of a Ballista, and units that hit together
    /// all land their last volley.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, 4, "0.5", 252)]
    [InlineData(Peltast, 6, "4", 336)]
    [InlineData(Ballista, 4, "7", 16)]
    public void APlayerWhoseOnlyCapitolIsDestroyedByAttacksIsOutInTheTickItFalls(string chassis, int count, string distance, int hits)
    {
        var match = WithStarts();
        var capitol = match.CapitolOf(1);
        match.OrderAttack(0, capitol, match.Besiegers(0, chassis, count, capitol, distance));

        var fall = match.RunUntilItFalls(capitol);

        fall.AssertDeathThenRemoved(capitol, owner: 1);
        Assert.Equal(hits, fall.Hits);
        Assert.Equal([Playing, Playing], fall.StandingsBefore);
        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.True(match.Standings.IsDecided);
        Assert.Equal((0, fall.Tick, fall.Tick), (match.Standings.Winner, match.Standings.OutTick(1), match.Standings.DecidedTick));
        Assert.Equal(Standings.None, match.Standings.OutTick(0));
        Assert.False(match.IsAlive(capitol));
    }

    /// <summary>
    /// Hoplites that stand at the Capitol hit in the tick of their order and once a second from
    /// then on, so the 63rd volley of four, which is the first to bring the hits to 250, lands in
    /// tick 1,240.
    /// </summary>
    [Fact]
    public void TheTickOfAConquestIsTheTickOfTheLastHit()
    {
        var match = WithStarts();
        var capitol = match.CapitolOf(1);
        match.OrderAttack(0, capitol, match.Besiegers(0, Hoplite, 4, capitol, "0.5"));

        var fall = match.RunUntilItFalls(capitol);

        Assert.Equal(1240, fall.Tick);
        Assert.Equal((1240, 1240), (match.Standings.OutTick(1), match.Standings.DecidedTick));
        Assert.Equal(1241, match.World.Tick);
    }

    /// <summary>
    /// An Outpost holds its owner in the match, finished or not (decision 3 of match flow), and
    /// it has to be shot down like any structure.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOutpostFinishedOrNotKeepsItsOwnerInTheMatchUntilAttacksDestroyItToo(bool finished)
    {
        var match = WithStarts();
        var capitol = match.CapitolOf(1);
        var outpost = finished ? match.PlaceStructure(1, Outpost, 40, 30) : match.Lay(1, Outpost, 40, 30);
        var atTheCapitol = match.Besiegers(0, Ballista, 4, capitol, "7");
        var atTheOutpost = match.Besiegers(0, Ballista, 3, outpost, "7");
        match.OrderAttack(0, capitol, atTheCapitol);

        var first = match.RunUntilItFalls(capitol);
        match.Run(100);

        first.AssertDeathThenRemoved(capitol, owner: 1);
        Assert.Equal([Playing, Playing], match.Standings.All());
        Assert.False(match.Standings.IsDecided);
        Assert.Equal(1_200_000, match.HitPointsOf(outpost));

        match.OrderAttack(0, outpost, atTheOutpost);
        var second = match.RunUntilItFalls(outpost);

        second.AssertDeathThenRemoved(outpost, owner: 1);
        Assert.Equal([Playing, Playing], second.StandingsBefore);
        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal((0, second.Tick, second.Tick), (match.Standings.Winner, match.Standings.OutTick(1), match.Standings.DecidedTick));
    }

    /// <summary>
    /// Decision 4 of match flow, with real shells: the same guns at the same distance, ordered in
    /// the same tick, bring both Capitols down in one tick, and the deaths of a tick are taken
    /// after every attack of it. Both players are out and nobody has won.
    /// </summary>
    [Fact]
    public void TwoPlayersWhoseLastCapitolsFallToAttacksInOneTickGoOutTogether()
    {
        var match = WithStarts();
        var capitols = new[] { match.CapitolOf(0), match.CapitolOf(1) };
        match.OrderAttack(0, capitols[1], match.Besiegers(0, Ballista, 4, capitols[1], "7"));
        match.OrderAttack(1, capitols[0], match.Besiegers(1, Ballista, 4, capitols[0], "7"));

        var fall = match.RunUntilItFalls(capitols[0]);

        fall.AssertDeathThenRemoved(capitols[0], owner: 0);
        fall.AssertDeathThenRemoved(capitols[1], owner: 1);
        Assert.Equal([Playing, Playing], fall.StandingsBefore);
        Assert.Equal([Eliminated, Eliminated], match.Standings.All());
        Assert.True(match.Standings.IsDecided);
        Assert.Equal((Standings.None, fall.Tick), (match.Standings.Winner, match.Standings.DecidedTick));
        Assert.Equal((fall.Tick, fall.Tick), (match.Standings.OutTick(0), match.Standings.OutTick(1)));
    }

    /// <summary>
    /// A match of three goes on when one player is taken out, with that player's orders dropped
    /// from then on, and ends in the tick the second one's Capitol falls.
    /// </summary>
    [Fact]
    public void AMatchOfThreeGoesOnWhenAttacksTakeOnePlayerOutAndEndsWhenTheyTakeTheSecond()
    {
        var match = WithStarts(players: 3);
        var capitols = new[] { match.CapitolOf(0), match.CapitolOf(1), match.CapitolOf(2) };
        match.OrderAttack(0, capitols[2], match.Besiegers(0, Ballista, 6, capitols[2], "7"));
        match.OrderAttack(0, capitols[1], match.Besiegers(0, Ballista, 4, capitols[1], "7"));

        var first = match.RunUntilItFalls(capitols[2]);

        first.AssertDeathThenRemoved(capitols[2], owner: 2);
        Assert.Equal([Playing, Playing, Eliminated], match.Standings.All());
        Assert.False(match.Standings.IsDecided);
        Assert.Equal((Standings.None, Standings.None, first.Tick), (match.Standings.Winner, match.Standings.DecidedTick, match.Standings.OutTick(2)));

        var worker = Enumerable.Range(0, match.Workers.Count).Select(match.Workers.EntityAt).First(entity => match.Bodies.Get(entity).Owner == 2);
        var stood = match.PositionOf(worker);
        var dropped = match.Simulation.DroppedCommandCount;
        match.OrderMove(2, TestMatches.Point("20.5", "20.5"), worker);
        match.OrderMove(1, TestMatches.Point("20.5", "20.5"), match.Besiegers(1, Hoplite, 1, capitols[1], "12"));
        match.Run(1);
        Assert.Equal(dropped + 1, match.Simulation.DroppedCommandCount);

        var second = match.RunUntilItFalls(capitols[1]);

        Assert.True(second.Tick > first.Tick);
        Assert.Equal(stood, match.PositionOf(worker));
        Assert.Equal([Playing, Eliminated, Eliminated], match.Standings.All());
        Assert.Equal((0, second.Tick), (match.Standings.Winner, match.Standings.DecidedTick));
        Assert.Equal((first.Tick, second.Tick), (match.Standings.OutTick(2), match.Standings.OutTick(1)));
    }

    /// <summary>
    /// Decisions 6 and 7 of match flow, with real shells. What a player who is out had stays on
    /// the map with the orders it had, so its guns fire on; and a match that is decided stays
    /// decided, so the winner is the winner still when its own Capitol falls to them later.
    /// </summary>
    [Fact]
    public void TheGunsOfAPlayerWhoIsOutFireOnAndTheWinnerStaysTheWinner()
    {
        var match = BothBesieged(out var script);
        var capitols = new[] { match.CapitolOf(0), match.CapitolOf(1) };
        foreach (var (tick, command) in script)
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        var first = match.RunUntilItFalls(capitols[1]);
        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.True(match.IsAlive(capitols[0]));

        var second = match.RunUntilItFalls(capitols[0]);

        second.AssertDeathThenRemoved(capitols[0], owner: 0);
        Assert.True(second.Tick > first.Tick);
        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal((0, first.Tick), (match.Standings.Winner, match.Standings.DecidedTick));
        Assert.Equal((Standings.None, first.Tick), (match.Standings.OutTick(0), match.Standings.OutTick(1)));
    }

    /// <summary>
    /// The same match, saved and restored round both falls: before the tick of each, and after
    /// it. Every restored match goes on to the same state as the one that was never interrupted,
    /// shells in the air and standings included.
    /// </summary>
    [Fact]
    public void AMatchRestoresAndContinuesRoundTheTickACapitolFalls()
    {
        var first = BothBesieged(out var script);
        foreach (var (tick, command) in script)
        {
            first.Simulation.Commands.Enqueue(tick, command);
        }

        var fallOfTheLoser = first.RunUntilItFalls(first.CapitolOf(1)).Tick;
        var fallOfTheWinner = first.RunUntilItFalls(first.CapitolOf(0)).Tick;

        foreach (var snapshotTick in new[] { fallOfTheLoser, fallOfTheLoser + 1, fallOfTheWinner, fallOfTheWinner + 1 })
        {
            SnapshotAssert.RestoresAndContinues(() => BothBesieged(out _), script, snapshotTick, lastTick: fallOfTheWinner + 40);
        }
    }

    /// <summary>Six guns of player 0 at the Capitol of player 1 and four of player 1 at the Capitol of player 0, with their orders for the first tick.</summary>
    private static Match BothBesieged(out (int Tick, Command Command)[] script)
    {
        var match = WithStarts();
        var capitols = new[] { match.CapitolOf(0), match.CapitolOf(1) };
        script =
        [
            (0, CombatMatches.AttackCommand(0, capitols[1], match.Besiegers(0, Ballista, 6, capitols[1], "7"))),
            (0, CombatMatches.AttackCommand(1, capitols[0], match.Besiegers(1, Ballista, 4, capitols[0], "7"))),
        ];
        match.RefreshDerivedState();
        return match;
    }

    /// <summary>
    /// A Tower picks units and Towers and never another structure (structures in combat), so a
    /// Tower by itself conquers nobody: it shoots every worker beside the Capitol and then has
    /// nothing to shoot at. Losing every unit puts nobody out.
    /// </summary>
    [Fact]
    public void ATowerKillsTheWorkersAtACapitolAndLeavesTheCapitolAndItsOwnerInTheMatch()
    {
        var match = WithStarts();
        var capitol = match.CapitolOf(1);
        var tiles = match.Structures.Get(capitol).Tiles;
        var tower = match.PlaceStructure(0, Tower, tiles.X + 1, tiles.Y + 7);

        var events = match.RunAndCollect(1500);

        Assert.Equal(6, events.Count(reported => reported.Kind == SimEventKind.Death && reported.Player == 1));
        Assert.DoesNotContain(events, reported => reported.Entity == capitol);
        Assert.Equal(2_000_000, match.HitPointsOf(capitol));
        Assert.Equal(EntityId.None, match.Turrets.Get(tower).Target);
        Assert.Equal([Playing, Playing], match.Standings.All());
    }

    /// <summary>
    /// A Tower that a Tower shoots down is a death and then a structure removed, like any
    /// structure, and it holds nobody in the match and puts nobody out.
    /// </summary>
    [Fact]
    public void ATowerShotDownByTowersIsADeathThenAStructureRemovedAndPutsNobodyOut()
    {
        var match = WithStarts();
        var theirs = match.PlaceStructure(1, Tower, 30, 30);
        match.PlaceStructure(0, Tower, 26, 30);
        match.PlaceStructure(0, Tower, 34, 30);

        var fall = match.RunUntilItFalls(theirs);

        fall.AssertDeathThenRemoved(theirs, owner: 1);
        Assert.Equal(60, fall.Hits);
        Assert.Equal([Playing, Playing], match.Standings.All());
        Assert.False(match.Standings.IsDecided);
    }

    /// <summary>
    /// The Capitol falls to shells from beyond the reach of the Tower beside it. Its owner is out
    /// though the Tower stands; the Tower takes no orders, as nothing of that player does, and it
    /// still shoots at what comes into its range (decision 7 of match flow).
    /// </summary>
    [Fact]
    public void APlayerWhoseCapitolFallsIsOutThoughItsTowerStandsAndTheTowerFiresOn()
    {
        var match = WithStarts();
        var capitol = match.CapitolOf(1);
        var tiles = match.Structures.Get(capitol).Tiles;
        var tower = match.PlaceStructure(1, Tower, tiles.X - 6, tiles.Y + 1);
        var hoplite = match.Unit(0, Hoplite, $"{tiles.X - 18}.5", $"{tiles.Y + 2}");
        match.OrderAttack(0, capitol, match.Besiegers(0, Ballista, 4, capitol, "7"));

        var fall = match.RunUntilItFalls(capitol);

        Assert.DoesNotContain(fall.Events, reported => reported.Kind == SimEventKind.ShotFired && reported.Other == tower);
        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal((0, fall.Tick), (match.Standings.Winner, match.Standings.DecidedTick));
        Assert.True(match.IsAlive(tower));
        Assert.Equal(600_000, match.HitPointsOf(tower));

        match.OrderMove(0, TestMatches.Point($"{tiles.X - 9}.5", $"{tiles.Y + 2}"), hoplite);
        var after = match.RunAndCollect(300);

        Assert.Contains(after, reported => reported.Kind == SimEventKind.ShotFired && reported.Other == tower && reported.Player == 1);
        Assert.Contains(after, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == hoplite && reported.Player == 1);
        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal((0, fall.Tick), (match.Standings.Winner, match.Standings.DecidedTick));
    }
}
