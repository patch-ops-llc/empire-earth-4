using Sim.Combat;
using Sim.Core;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Vision;

/// <summary>
/// Where the fog of war meets structures as targets (design 5.3; design 8.4, rows 17, 33, 39 and
/// 40): a unit and a Tower pick only what their owner sees, a structure is seen by any tile of
/// its footprint, and an attack order may name a structure its player sees or remembers.
/// </summary>
/// <remarks>
/// A Generator has 2 by 2 tiles, 500 hit points and sight 6; a Barracks 3 by 3 tiles; a Tower 2 by
/// 2 tiles, sight 10 and range 7. A worker sees 6 tiles, a Hoplite and a Peltast 8.
/// </remarks>
public class StructureTargetsInFogTests
{
    private static Match Field() => FogMatches.Create(TestMaps.Open(60, 24));

    /// <summary>Puts a unit somewhere else at once and works sight out again, as a fixture may before a tick.</summary>
    private static void Put(Match match, EntityId unit, string x, string y)
    {
        match.Bodies.Get(unit).Position = TestMatches.Point(x, y);
        match.RefreshDerivedState();
        match.Vision.Refresh();
    }

    /// <summary>A Generator of player 1 at (40, 10) that player 0 has seen and does not see now.</summary>
    private static Match WithRememberedGenerator(out EntityId generator)
    {
        var match = Field();
        generator = match.Place(1, "generator", 40, 10);
        var scout = match.SpawnWorker(0, "36.5", "11.5");
        Put(match, scout, "2.5", "2.5");
        Assert.False(match.Vision.Sees(0, generator));
        Assert.True(match.Vision.Remembers(0, generator, out _));
        return match;
    }

    private static void RunUntilIdle(Match match, EntityId unit, int limit = 400)
    {
        for (var tick = 0; tick < limit && match.UnitOf(unit).Order != UnitOrder.Idle; tick++)
        {
            match.Run(1);
        }

        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void AStructureIsAnEnemyToPickWhileAnyTileOfItsFootprintIsSeen()
    {
        var match = Field();

        // Tiles 40 to 42 by 10 to 12, with the center on tile (41, 11).
        var barracks = match.Place(1, "barracks", 40, 10);
        var worker = match.SpawnWorker(0, "34.5", "11.5");
        var targeting = match.Targeting();

        // The worker sees the tile (40, 11) and not the one under the center.
        Assert.True(match.Vision.Grid.IsVisible(0, 40, 11));
        Assert.False(match.Vision.Grid.IsVisible(0, 41, 11));
        Assert.True(targeting.IsEnemy(match.World, 0, barracks, out var body));
        Assert.False(match.Vision.Sees(0, in body));
        Assert.True(targeting.Sees(0, barracks, in body));

        Put(match, worker, "33.5", "11.5");
        Assert.False(targeting.IsEnemy(match.World, 0, barracks, out _));
        Assert.True(targeting.IsEnemyThere(match.World, 0, barracks, out _));
        Assert.False(targeting.Sees(0, barracks, in body));

        // Its owner sees it wherever it stands, and it is no enemy of its owner.
        Assert.True(targeting.Sees(1, barracks, in body));
        Assert.False(targeting.IsEnemy(match.World, 1, barracks, out _));
    }

    [Fact]
    public void AUnitPicksATowerItSeesByOneTileOfItsFootprint()
    {
        var match = Field();

        // Tiles 40 and 41 by 10 and 11, with the center on the corner of tile (41, 11).
        var tower = match.Place(1, "tower", 40, 10);
        var peltast = match.Spawn(0, Peltast, "33.5", "10.5");
        Assert.True(match.Vision.Grid.IsVisible(0, 40, 10));
        Assert.False(match.Vision.Grid.IsVisible(0, 41, 11));

        match.Run(4);

        Assert.Equal(tower, match.UnitOf(peltast).Target);
    }

    [Fact]
    public void AUnitPicksNoStructureItsOwnerDoesNotSee()
    {
        var match = Field();
        var tower = match.Place(1, "tower", 40, 10);

        // Nine tiles from the nearest tile of the Tower: out of the Peltast's sight of 8.
        var peltast = match.Spawn(0, Peltast, "31.5", "10.5");
        Assert.False(match.Vision.Sees(0, tower));

        match.Run(8);

        Assert.Equal(EntityId.None, match.UnitOf(peltast).Target);
    }

    /// <summary>Design 8.4, row 33: a Tower picks what its owner sees, and a Tower sees for itself (row 11).</summary>
    [Fact]
    public void ATowerFiresAtAUnitThatOnlyTheTowerSees()
    {
        var match = Field();
        var tower = match.Place(1, "tower", 40, 10);
        var hoplite = match.Spawn(0, Hoplite, "47.5", "11");
        Assert.True(match.Vision.Sees(1, hoplite));

        var fired = match.RunTo(SimEventKind.ShotFired, limit: 8);

        Assert.Equal((1, tower), (fired.Player, fired.Other));
    }

    [Fact]
    public void AnAttackOrderOnAStructureThePlayerHasNeverSeenIsRefused()
    {
        var match = Field();
        var barracks = match.Place(1, "barracks", 40, 10);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "11.5");
        Assert.False(match.Vision.KnowsStructure(0, barracks));

        match.OrderAttack(0, barracks, hoplite);
        match.Run(1);

        var refused = Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal((0, hoplite, barracks), (refused.Player, refused.Entity, refused.Other));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
    }

    /// <summary>Design 8.4, row 40: an attack order may name a structure the player remembers.</summary>
    [Fact]
    public void AnAttackOrderOnARememberedStructureIsTakenAndCarriedOut()
    {
        var match = WithRememberedGenerator(out var generator);
        var hoplite = match.Spawn(0, Hoplite, "20.5", "11.5");
        Assert.False(match.Vision.Sees(0, generator));

        match.OrderAttack(0, generator, hoplite);
        match.Run(1);

        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal((UnitOrder.Attack, generator), (match.UnitOf(hoplite).Order, match.UnitOf(hoplite).Target));

        // On its way it does not see the Generator, and the order holds: a structure stands where it was seen.
        match.Run(40);
        Assert.False(match.Vision.Sees(0, generator));
        Assert.Equal((UnitOrder.Attack, generator), (match.UnitOf(hoplite).Order, match.UnitOf(hoplite).Target));

        var hit = match.RunTo(SimEventKind.MeleeHit);
        Assert.Equal((0, generator, hoplite), (hit.Player, hit.Entity, hit.Other));
    }

    /// <summary>
    /// The rule that an attack order ends when its target is lost from sight is for a unit, which
    /// may have walked on (design 8.4, row 40). A structure cannot.
    /// </summary>
    [Fact]
    public void LosingSightOfAStructureDoesNotEndTheAttackOrderOnIt()
    {
        var match = Field();
        var generator = match.Place(1, "generator", 40, 10);
        var scout = match.SpawnWorker(0, "36.5", "11.5");
        var ballista = match.Spawn(0, Ballista, "20.5", "11.5");
        match.OrderAttack(0, generator, ballista);
        match.Run(1);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(ballista).Order);

        Put(match, scout, "2.5", "2.5");
        match.Run(8);
        Assert.False(match.Vision.Sees(0, generator));
        Assert.Equal((UnitOrder.Attack, generator), (match.UnitOf(ballista).Order, match.UnitOf(ballista).Target));

        // The Ballista reaches 9 tiles and sees 8: it fires at the Generator before it sees it again.
        var fired = match.RunTo(SimEventKind.ShotFired);
        Assert.Equal(ballista, fired.Other);
        Assert.False(match.Vision.Sees(0, generator));
        var hit = match.RunTo(SimEventKind.ShotHit);
        Assert.Equal(generator, hit.Entity);
    }

    /// <summary>
    /// A remembered structure may be gone. The player cannot know, so the order is not refused,
    /// which would tell it: the units go to where the structure stood, see that it is gone, and
    /// are idle there.
    /// </summary>
    [Fact]
    public void AnAttackOrderOnARememberedStructureThatIsGoneSendsTheUnitsToLook()
    {
        var match = WithRememberedGenerator(out var generator);
        var hoplite = match.Spawn(0, Hoplite, "25.5", "11.5");
        var worker = match.SpawnWorker(0, "25.5", "13.5");
        match.Demolish(generator);
        match.Run(4);
        Assert.False(match.IsAlive(generator));
        Assert.True(match.Vision.KnowsStructure(0, generator));

        match.OrderAttack(0, generator, hoplite, worker);
        match.Run(1);

        // The order is taken. The worker, which cannot attack, stays as it would for a standing structure.
        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal((UnitOrder.Move, EntityId.None), (match.UnitOf(hoplite).Order, match.UnitOf(hoplite).Target));
        Assert.Equal(TestMatches.Point("41", "11"), match.Movers.Get(hoplite).Destination);
        Assert.Equal(TestMatches.Point("25.5", "13.5"), match.PositionOf(worker));

        RunUntilIdle(match, hoplite);
        Assert.True(FixedVector2.DistanceSquaredRaw(match.PositionOf(hoplite), TestMatches.Point("41", "11")) <= Squared("1"));

        // It has seen the tiles without the Generator, so the player knows now, and a new order is refused.
        Assert.False(match.Vision.KnowsStructure(0, generator));
        match.OrderAttack(0, generator, hoplite);
        match.Run(1);
        Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
    }

    [Fact]
    public void AnAttackOrderOnARememberedStructureThatIsGoneNeedsAUnitThatCanAttack()
    {
        var match = WithRememberedGenerator(out var generator);
        var worker = match.SpawnWorker(0, "25.5", "13.5");
        match.Demolish(generator);
        match.Run(4);

        match.OrderAttack(0, generator, worker);
        match.Run(1);

        // As for a standing structure: no listed unit can take the order.
        Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(TestMatches.Point("25.5", "13.5"), match.PositionOf(worker));
    }

    /// <summary>
    /// A structure that falls out of its attackers' owner's sight leaves an order that the owner
    /// thinks still stands: the units go on to where it stood. One that falls in sight ends the
    /// order where the units stand (<see cref="StructureTargetTests"/>).
    /// </summary>
    [Fact]
    public void AStructureThatFallsOutOfSightIsWalkedToByTheUnitsSentAtIt()
    {
        var match = WithRememberedGenerator(out var generator);
        var hoplite = match.Spawn(0, Hoplite, "20.5", "11.5");
        match.OrderAttack(0, generator, hoplite);
        match.Run(10);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        var whereItWas = match.PositionOf(hoplite);

        match.Demolish(generator);
        match.Run(2);

        // It did not stop where it stood: it walks on, as on a move order, to where the Generator was. The
        // fixture took the Generator away between two ticks, so its tiles were still blocked when the unit
        // chose where to go, and it goes to the tile beside them.
        Assert.Equal((UnitOrder.Move, EntityId.None), (match.UnitOf(hoplite).Order, match.UnitOf(hoplite).Target));
        Assert.NotEqual(whereItWas, match.PositionOf(hoplite));
        Assert.Equal(TestMatches.Point("39.5", "11.5"), match.Movers.Get(hoplite).Destination);

        RunUntilIdle(match, hoplite);
        Assert.Equal(TestMatches.Point("39.5", "11.5"), match.PositionOf(hoplite));
        Assert.False(match.Vision.KnowsStructure(0, generator));
    }
}
