using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Movement;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Vision;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Vision;

/// <summary>
/// <see cref="PlayerView"/>: what the interface, the minimap and the AI may ask about a match for
/// one player. It answers everything about the player's own things, what is seen of the others,
/// and nothing about what the fog hides.
/// </summary>
public class PlayerViewTests
{
    /// <summary>
    /// Three players. Player 0 has a Guard and a worker in the west. Player 1 has a worker that
    /// player 0's worker sees and that sees it, and a rider and a worker in the east that player 0
    /// does not see. Player 2 has nothing. One deposit lies in the west and one in the east.
    /// </summary>
    /// <remarks>
    /// Nobody fights here until a test makes them: no unit of a chassis has a unit of another
    /// player within its own sight, which is as far as it looks for a target.
    /// </remarks>
    private sealed class Scene
    {
        public Scene()
        {
            Match = FogMatches.Create(
                TestMaps.FromRows([.. Enumerable.Range(0, 24).Select(y => y == 2 ? "..FF" + new string('.', 34) + "#." : new string('.', 40))])
                    .WithDeposits((10, 10), (30, 10)),
                players: 3);
            Match.SetTiers(1, 6, 3, 7, 4, 3, 5);
            Hoplite = Match.Spawn(0, CombatMatches.Hoplite, "5.5", "5.5");
            Worker = Match.SpawnWorker(0, "9.5", "12.5");
            Near = Match.SpawnWorker(1, "14.5", "13.5");
            FarRider = Match.Spawn(1, "cuirassier", "35.5", "20.5");
            FarWorker = Match.SpawnWorker(1, "30.5", "5.5");
            West = Match.SiteAt(10, 10);
            East = Match.SiteAt(30, 10);
            Views = [new PlayerView(Match, 0), new PlayerView(Match, 1), new PlayerView(Match, 2)];
        }

        public Match Match { get; }

        public EntityId Hoplite { get; }

        public EntityId Worker { get; }

        public EntityId Near { get; }

        public EntityId FarRider { get; }

        public EntityId FarWorker { get; }

        public EntityId West { get; }

        public EntityId East { get; }

        public PlayerView[] Views { get; }
    }

    private static EntityId[] Ids(List<SeenUnit> units) => [.. units.Select(unit => unit.Entity)];

    [Fact]
    public void ItKnowsItsPlayerTheTickAndTheSizeOfTheMap()
    {
        var scene = new Scene();
        scene.Match.Run(7);

        for (var player = 0; player < 3; player++)
        {
            Assert.Equal(player, scene.Views[player].Player);
            Assert.Equal(7, scene.Views[player].Tick);
            Assert.Equal(40, scene.Views[player].Width);
            Assert.Equal(24, scene.Views[player].Height);
        }

        Assert.Throws<ArgumentNullException>(() => new PlayerView(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerView(scene.Match, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerView(scene.Match, 3));
    }

    [Fact]
    public void TheFogIsThePlayersOwn()
    {
        var scene = new Scene();
        var grid = scene.Match.Vision.Grid;
        var states = new byte[40 * 24];

        for (var player = 0; player < 3; player++)
        {
            var view = scene.Views[player];
            view.CopyFog(states);
            for (var y = 0; y < 24; y++)
            {
                for (var x = 0; x < 40; x++)
                {
                    Assert.Equal(grid.StateOf(player, x, y), view.FogAt(x, y));
                    Assert.Equal((byte)view.FogAt(x, y), states[(y * 40) + x]);
                }
            }

            Assert.Equal(FogState.Unexplored, view.FogAt(-1, 5));
            Assert.Equal(FogState.Unexplored, view.FogAt(5, 24));
        }

        // The Guard sees 8 tiles and stands on (5, 5); the far east is the other player's to see.
        Assert.Equal(FogState.Visible, scene.Views[0].FogAt(5, 5));
        Assert.Equal(FogState.Visible, scene.Views[0].FogAt(13, 5));
        Assert.Equal(FogState.Unexplored, scene.Views[0].FogAt(14, 5));
        Assert.Equal(FogState.Unexplored, scene.Views[0].FogAt(35, 20));
        Assert.Equal(FogState.Visible, scene.Views[1].FogAt(35, 20));
        Assert.Equal(0, states.Count(state => state != 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.Views[0].CopyFog(new byte[(40 * 24) - 1]));
    }

    [Fact]
    public void TheFogRevisionChangesWhenTheFogDoesAndOnlyThen()
    {
        var scene = new Scene();
        var view = scene.Views[0];
        var states = new byte[40 * 24];
        var before = new byte[40 * 24];

        // Nobody moves: the fog is the same tick after tick, and so is the number.
        var revision = view.FogRevision;
        scene.Match.Run(9);
        Assert.Equal(revision, view.FogRevision);

        // The Guard walks: whenever the picture is another, the number is another, and the other way round.
        scene.Match.OrderMove(0, TestMatches.Point("5.5", "20.5"), scene.Hoplite);
        var changes = 0;
        for (var tick = 0; tick < 40; tick++)
        {
            view.CopyFog(before);
            revision = view.FogRevision;
            scene.Match.Run(1);
            view.CopyFog(states);
            Assert.Equal(before.AsSpan().SequenceEqual(states), revision == view.FogRevision);
            changes += revision == view.FogRevision ? 0 : 1;
        }

        Assert.InRange(changes, 3, 10);
    }

    [Fact]
    public void TheGroundIsKnownWhereThePlayerHasExploredAndStaysKnown()
    {
        var scene = new Scene();
        var view = scene.Views[0];

        Assert.True(view.TryGetTerrain(5, 5, out var open));
        Assert.Equal(TerrainKind.Open, open);
        Assert.True(view.TryGetTerrain(2, 2, out var forest));
        Assert.Equal(TerrainKind.Forest, forest);

        // The cliff in the east is on the map and the player has not been there.
        Assert.Equal(TerrainKind.Cliff, scene.Match.Map.TerrainAt(38, 2));
        Assert.False(view.TryGetTerrain(38, 2, out _));
        Assert.True(scene.Views[1].TryGetTerrain(30, 5, out _));
        Assert.False(scene.Views[2].TryGetTerrain(5, 5, out _));
        Assert.False(view.TryGetTerrain(-1, 0, out _));
        Assert.False(view.TryGetTerrain(0, 24, out _));

        // The Guard leaves: the forest is explored and no longer visible, and still known.
        scene.Match.OrderMove(0, TestMatches.Point("20.5", "20.5"), scene.Hoplite);
        scene.Match.Run(300);
        Assert.Equal(FogState.Explored, view.FogAt(2, 2));
        Assert.True(view.TryGetTerrain(2, 2, out forest));
        Assert.Equal(TerrainKind.Forest, forest);
    }

    [Fact]
    public void ADepositIsKnownOnceATileOfItIsExploredAndAGeneratorToItsOwner()
    {
        var scene = new Scene();
        var generator = scene.Match.Place(0, "generator", 16, 18);
        var sites = new List<SeenWorkSite> { default };

        scene.Views[0].CollectWorkSites(sites);
        Assert.Equal(
            [new SeenWorkSite(scene.West, WorkSiteKind.Deposit, 10, 10, 2), new SeenWorkSite(generator, WorkSiteKind.Generator, 16, 18, 2)],
            sites);

        // Player 1's worker sees a tile of the west deposit, and the tiles of the Generator, which is not a deposit and not its own.
        Assert.Equal(FogState.Visible, scene.Views[1].FogAt(11, 11));
        Assert.Equal(FogState.Visible, scene.Views[1].FogAt(16, 18));
        scene.Views[1].CollectWorkSites(sites);
        Assert.Equal([scene.West, scene.East], sites.Select(site => site.Entity));

        scene.Views[2].CollectWorkSites(sites);
        Assert.Empty(sites);

        // Player 0's worker walks away: the deposit is no longer in sight, and is known for good.
        scene.Match.OrderMove(0, TestMatches.Point("2.5", "21.5"), scene.Worker, scene.Hoplite);
        scene.Match.Run(300);
        Assert.Equal(FogState.Explored, scene.Views[0].FogAt(10, 10));
        Assert.Equal(FogState.Explored, scene.Views[0].FogAt(11, 11));
        scene.Views[0].CollectWorkSites(sites);
        Assert.Equal([scene.West, generator], sites.Select(site => site.Entity));
    }

    [Fact]
    public void ADepositWithOneExploredTileIsKnown()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((10, 10)));
        var view = new PlayerView(match, 0);
        var sites = new List<SeenWorkSite>();

        // A worker sees 6 tiles: from (4.5, 10.5) its sight ends on the tile (10, 10) and it sees no other of the deposit.
        match.SpawnWorker(0, "3.5", "10.5");
        view.CollectWorkSites(sites);
        Assert.Empty(sites);

        match.SpawnWorker(0, "4.5", "10.5");
        Assert.Equal(FogState.Visible, view.FogAt(10, 10));
        Assert.Equal(FogState.Unexplored, view.FogAt(11, 10));
        Assert.Equal(FogState.Unexplored, view.FogAt(10, 11));
        view.CollectWorkSites(sites);
        Assert.Equal(match.SiteAt(10, 10), Assert.Single(sites).Entity);
    }

    [Fact]
    public void ThePlayersOwnUnitsAndTheOthersItSeesAreListedInIdOrder()
    {
        var scene = new Scene();
        var units = new List<SeenUnit> { default };

        scene.Views[0].CollectUnits(units);
        Assert.Equal([scene.Hoplite, scene.Worker, scene.Near], Ids(units));

        // Player 1's worker sees 6 tiles: player 0's worker is 5.1 away, the Guard 12.
        scene.Views[1].CollectUnits(units);
        Assert.Equal([scene.Worker, scene.Near, scene.FarRider, scene.FarWorker], Ids(units));

        scene.Views[2].CollectUnits(units);
        Assert.Empty(units);

        foreach (var entity in new[] { scene.Hoplite, scene.Worker, scene.Near, scene.FarRider, scene.FarWorker })
        {
            Assert.Equal(entity != scene.FarRider && entity != scene.FarWorker, scene.Views[0].Sees(entity));
            Assert.Equal(entity != scene.Hoplite, scene.Views[1].Sees(entity));
            Assert.False(scene.Views[2].Sees(entity));
        }
    }

    [Fact]
    public void ASeenUnitIsDescribedByWhatAnyoneWhoSeesItCanTell()
    {
        var scene = new Scene();
        scene.Match.SetHitPoints(scene.Near, 12_345);
        scene.Match.OrderMove(1, TestMatches.Point("12.5", "15.5"), scene.Near);
        scene.Match.Run(4);
        var body = scene.Match.Bodies.Get(scene.Near);
        var facing = scene.Match.Movers.Get(scene.Near).Facing;
        Assert.NotEqual(TestMatches.Point("14.5", "13.5"), body.Position);
        Assert.NotEqual(default, facing);

        // Player 1's tiers give its workers more hit points than the 50 of player 0's.
        var maximum = scene.Match.Health.Get(scene.Near).Maximum;
        Assert.True(maximum > 50_000);

        // The same to the player who sees it as to the player whose it is.
        foreach (var view in new[] { scene.Views[0], scene.Views[1] })
        {
            Assert.True(view.TryGetUnit(scene.Near, out var unit));
            Assert.Equal(new SeenUnit(scene.Near, 1, body.Position, facing, SeenUnit.NoChassis, true, 12_345, maximum), unit);
        }

        Assert.True(scene.Views[1].TryGetUnit(scene.Worker, out var worker));
        Assert.Equal(new SeenUnit(scene.Worker, 0, TestMatches.Point("9.5", "12.5"), default, SeenUnit.NoChassis, true, 50_000, 50_000), worker);

        // A unit of a chassis.
        var health = scene.Match.Health.Get(scene.Hoplite);
        Assert.Equal(130_000, health.Maximum);
        Assert.True(scene.Views[0].TryGetUnit(scene.Hoplite, out var hoplite));
        Assert.Equal(
            new SeenUnit(scene.Hoplite, 0, TestMatches.Point("5.5", "5.5"), default, Chassis(CombatMatches.Hoplite), false, 130_000, 130_000),
            hoplite);
    }

    [Fact]
    public void NothingIsToldOfAUnitThePlayerDoesNotSee()
    {
        var scene = new Scene();
        scene.Match.Run(1);
        var view = scene.Views[0];
        var units = new List<SeenUnit> { default };

        foreach (var hidden in new[] { scene.FarRider, scene.FarWorker })
        {
            Assert.False(view.Sees(hidden));
            Assert.False(view.TryGetUnit(hidden, out var unit));
            Assert.Equal(default, unit);
            Assert.False(view.Inspect(hidden, out _));
            Assert.False(view.TryGetOwnOrder(hidden, out _));
            Assert.False(view.TryGetOwnWalk(hidden, out _));
            Assert.False(view.TryGetOwnWork(hidden, out _));

            var position = scene.Match.PositionOf(hidden);
            view.CollectUnitsNear(position, Fixed.FromInt(3), units);
            Assert.Empty(units);
            units.Add(default);
            view.CollectUnitsIn(position - new FixedVector2(Fixed.One, Fixed.One), position + new FixedVector2(Fixed.One, Fixed.One), units);
            Assert.Empty(units);
            units.Add(default);
        }

        // Nor of what is not a unit, of what never was, and of what is gone.
        Assert.False(view.TryGetUnit(scene.West, out _));
        Assert.False(view.TryGetUnit(new EntityId(99), out _));
        Assert.False(view.TryGetUnit(EntityId.None, out _));
        Assert.True(view.TryGetUnit(scene.Near, out _));
        scene.Match.SetHitPoints(scene.Near, 0);
        scene.Match.Run(1);
        Assert.False(view.TryGetUnit(scene.Near, out _));
        Assert.False(view.Sees(scene.Near));
        view.CollectUnits(units);
        Assert.Equal([scene.Hoplite, scene.Worker], Ids(units));
    }

    [Fact]
    public void UnitsNearAPointAndInARectangleAreThoseThePlayerMayKnowOf()
    {
        var scene = new Scene();

        // The index of where units are is brought up to date by the tick.
        scene.Match.Run(1);
        var units = new List<SeenUnit> { default };

        // Around (9.5, 9.5), 6 tiles: the Guard is 5.7 away and player 0's worker 3; player 1's worker is 6.4 away.
        var center = TestMatches.Point("9.5", "9.5");
        scene.Views[0].CollectUnitsNear(center, Fixed.FromInt(6), units);
        Assert.Equal([scene.Hoplite, scene.Worker], Ids(units));
        scene.Views[0].CollectUnitsNear(center, Fixed.FromInt(7), units);
        Assert.Equal([scene.Hoplite, scene.Worker, scene.Near], Ids(units));

        // Player 1 sees player 0's worker and not the Guard.
        scene.Views[1].CollectUnitsNear(center, Fixed.FromInt(6), units);
        Assert.Equal([scene.Worker], Ids(units));
        scene.Views[2].CollectUnitsNear(center, Fixed.FromInt(7), units);
        Assert.Empty(units);

        // The distance itself is included.
        scene.Views[0].CollectUnitsNear(center, Fixed.FromInt(3), units);
        Assert.Equal([scene.Worker], Ids(units));
        scene.Views[0].CollectUnitsNear(center, Fixed.FromInt(3) - Fixed.Epsilon, units);
        Assert.Empty(units);

        // The whole map: everything the player may know of, and no more.
        var all = new List<SeenUnit>();
        for (var player = 0; player < 3; player++)
        {
            scene.Views[player].CollectUnits(all);
            scene.Views[player].CollectUnitsIn(default, TestMatches.Point("40", "24"), units);
            Assert.Equal(all, units);
            scene.Views[player].CollectUnitsNear(center, Fixed.FromInt(100), units);
            Assert.Equal(all, units);
        }

        // A rectangle with a unit on its edge.
        scene.Views[0].CollectUnitsIn(TestMatches.Point("5.5", "5.5"), TestMatches.Point("9.5", "12.5"), units);
        Assert.Equal([scene.Hoplite, scene.Worker], Ids(units));
        scene.Views[0].CollectUnitsIn(TestMatches.Point("5.75", "5.5"), TestMatches.Point("14.5", "13.25"), units);
        Assert.Equal([scene.Worker], Ids(units));
        scene.Views[1].CollectUnitsIn(TestMatches.Point("5.5", "5.5"), TestMatches.Point("14.5", "13.5"), units);
        Assert.Equal([scene.Worker, scene.Near], Ids(units));
        scene.Views[1].CollectUnitsIn(TestMatches.Point("30", "0"), TestMatches.Point("40", "24"), units);
        Assert.Equal([scene.FarRider, scene.FarWorker], Ids(units));
    }

    /// <summary>Design 3.2: inspecting an enemy unit shows its effective Firepower, Protection and Mobility tiers and its accents.</summary>
    [Fact]
    public void InspectingAUnitShowsItsOwnersEffectiveTiersAndAccents()
    {
        var scene = new Scene();
        var roles = scene.Match.Data.TrackRoles;

        // Player 1's rider comes into the sight of player 0's Guard.
        Assert.False(scene.Views[0].Inspect(scene.FarRider, out var nothing));
        Assert.Equal(default, nothing);
        var rider = scene.Match.Spawn(1, "cuirassier", "10.5", "5.5");

        var theirs = scene.Match.Blueprint(1, "cuirassier");
        var ours = scene.Match.Blueprint(0, "cuirassier");
        Assert.NotEmpty(theirs.Accents);
        Assert.NotEqual(ours.EffectiveTiers[roles.Firepower], theirs.EffectiveTiers[roles.Firepower]);
        foreach (var view in new[] { scene.Views[0], scene.Views[1] })
        {
            Assert.True(view.Inspect(rider, out var inspection));
            Assert.Equal(theirs.EffectiveTiers[roles.Firepower], inspection.Firepower);
            Assert.Equal(theirs.EffectiveTiers[roles.Protection], inspection.Protection);
            Assert.Equal(theirs.EffectiveTiers[roles.Mobility], inspection.Mobility);
            Assert.Equal(theirs.Accents, inspection.Accents);
        }

        // Player 0's own Guard, by its own tiers.
        Assert.True(scene.Views[0].Inspect(scene.Hoplite, out var own));
        Assert.Equal(new Inspection(1, 1, 1, scene.Match.Blueprint(0, CombatMatches.Hoplite).Accents), own);
        Assert.Empty(own.Accents);

        // A worker is of no chassis and has no tiers to show; a player who sees nothing is shown nothing.
        Assert.False(scene.Views[0].Inspect(scene.Worker, out _));
        Assert.False(scene.Views[2].Inspect(rider, out _));
        Assert.False(scene.Views[0].Inspect(scene.West, out _));
    }

    [Fact]
    public void OrdersWalksAndWorkAreToldOfThePlayersOwnUnitsOnly()
    {
        var scene = new Scene();
        var peltast = scene.Match.Spawn(1, CombatMatches.Peltast, "12.5", "5.5");
        scene.Match.OrderAttack(0, peltast, scene.Hoplite);
        scene.Match.OrderAttack(1, scene.Hoplite, peltast);
        scene.Match.Run(2);

        Assert.True(scene.Views[0].TryGetOwnOrder(scene.Hoplite, out var order));
        Assert.Equal(scene.Match.Combatants.Get(scene.Hoplite), order);
        Assert.Equal(UnitOrder.Attack, order.Order);
        Assert.Equal(peltast, order.Target);
        Assert.True(scene.Views[0].TryGetOwnWalk(scene.Hoplite, out var walk));
        Assert.Equal(scene.Match.Movers.Get(scene.Hoplite), walk);
        Assert.NotEqual(MoveState.Idle, walk.State);
        Assert.True(scene.Views[0].TryGetOwnWork(scene.Worker, out var work));
        Assert.Equal(scene.Match.Workers.Get(scene.Worker), work);
        Assert.True(scene.Views[0].TryGetOwnWalk(scene.Worker, out _));

        // What it is not: a worker has no order of a chassis, a Guard no work.
        Assert.False(scene.Views[0].TryGetOwnOrder(scene.Worker, out _));
        Assert.False(scene.Views[0].TryGetOwnWork(scene.Hoplite, out _));

        // The other player sees the Guard and is not told what it was ordered to do, and the same the other way.
        Assert.True(scene.Views[1].Sees(scene.Hoplite));
        Assert.False(scene.Views[1].TryGetOwnOrder(scene.Hoplite, out var hidden));
        Assert.Equal(default, hidden);
        Assert.False(scene.Views[1].TryGetOwnWalk(scene.Hoplite, out _));
        Assert.False(scene.Views[1].TryGetOwnWork(scene.Worker, out _));
        Assert.True(scene.Views[0].Sees(peltast));
        Assert.False(scene.Views[0].TryGetOwnOrder(peltast, out _));
        Assert.True(scene.Views[1].TryGetOwnOrder(peltast, out var theirs));
        Assert.Equal(UnitOrder.Attack, theirs.Order);
        Assert.True(scene.Views[1].TryGetOwnWork(scene.FarWorker, out _));
    }

    /// <summary>
    /// Player 1's Ballista is ordered to attack a worker of player 0 and fires from ground player
    /// 0 does not see. Player 2 watches all of it through a worker, and player 3 sees nothing.
    /// </summary>
    private static Match Ambush(out EntityId victim, out EntityId ballista)
    {
        var match = Create(TestMaps.Open(40, 12), players: 4);

        // A worker sees 6 tiles, the row it stands in up to x = 7; the Ballista sees 8, to the worker's tile and no further.
        victim = match.SpawnWorker(0, "2.25", "5.5");
        ballista = match.Spawn(1, CombatMatches.Ballista, "10.5", "5.5");

        // 5 tiles from the center of the tile of each.
        match.SpawnWorker(2, "6.5", "8.5");
        match.OrderAttack(1, victim, ballista);
        return match;
    }

    [Fact]
    public void AShotIsToldOfToItsOwnerAndToWhoeverSeesTheTileItIsOver()
    {
        var match = Ambush(out var victim, out var ballista);
        match.SetHitPoints(victim, 1_000_000);
        var views = Enumerable.Range(0, 4).Select(player => new PlayerView(match, player)).ToArray();
        var shots = new List<SeenShot> { default };
        var mine = new List<SeenShot>();

        // Player 0 sees the tiles up to x = 7: the shot leaves the Ballista at 10.5 and is unseen at first.
        int hiddenTicks = 0, seenTicks = 0;
        for (var tick = 0; tick < 200; tick++)
        {
            match.Run(1);
            views[1].CollectShots(mine);
            Assert.Equal(match.Shots.Count, mine.Count);
            for (var i = 0; i < mine.Count; i++)
            {
                var shot = match.Shots.ValueAt(i);
                Assert.Equal(new SeenShot(match.Shots.EntityAt(i), 1, shot.Position, Line("artillery")), mine[i]);
            }

            views[0].CollectShots(shots);
            Assert.Equal(mine.Where(shot => shot.Position.X.FloorToInt() <= 7), shots);
            hiddenTicks += mine.Count > shots.Count ? 1 : 0;
            seenTicks += shots.Count;

            // The worker of player 2 sees 6 tiles from (6.5, 8.5): the row of the shot from x = 2 to x = 11.
            views[2].CollectShots(shots);
            Assert.Equal(mine, shots);
            views[3].CollectShots(shots);
            Assert.Empty(shots);
        }

        Assert.True(hiddenTicks >= 8, $"A shot was hidden from player 0 in {hiddenTicks} ticks.");
        Assert.True(seenTicks >= 20, $"Shots were shown to player 0 {seenTicks} times.");
        Assert.False(views[0].Sees(ballista));
        Assert.True(match.HitPointsOf(victim) < 1_000_000);
    }

    [Fact]
    public void AnEventIsKnownToItsPlayerAndAFightAlsoToWhoeverSeesWhereItHappens()
    {
        var match = Ambush(out var victim, out var ballista);
        match.SetHitPoints(victim, 120_000);
        var views = Enumerable.Range(0, 4).Select(player => new PlayerView(match, player)).ToArray();
        var known = Enumerable.Range(0, 4).Select(_ => new List<SimEvent>()).ToArray();
        var events = new List<SimEvent> { default };

        // Player 0 orders an attack on what it does not see, which is refused, and its worker is shot dead.
        match.OrderAttack(0, ballista, victim);
        for (var tick = 0; tick < 1000 && match.IsAlive(victim); tick++)
        {
            match.Run(1);
            for (var player = 0; player < 4; player++)
            {
                views[player].CollectEvents(events);
                Assert.Equal(match.Simulation.Events.Where(happened => views[player].Knows(in happened)), events);
                known[player].AddRange(events);
            }
        }

        SimEventKind[] KindsOf(int player) => [.. known[player].Select(happened => happened.Kind).Distinct().Order()];
        int Count(int player, SimEventKind kind) => known[player].Count(happened => happened.Kind == kind);

        Assert.False(match.IsAlive(victim));

        // The player whose worker it was: the refusal, the hits and the death, and not that anything was fired.
        Assert.Equal([SimEventKind.Death, SimEventKind.OrderRefused, SimEventKind.ShotHit], KindsOf(0));

        // The Ballista's owner: its shots, its hits, and the death, on a tile the Ballista sees.
        Assert.Equal([SimEventKind.Death, SimEventKind.ShotFired, SimEventKind.ShotHit], KindsOf(1));

        // The onlooker: all of the fight, and not the order that was refused.
        Assert.Equal([SimEventKind.Death, SimEventKind.ShotFired, SimEventKind.ShotHit], KindsOf(2));
        Assert.Empty(known[3]);

        Assert.Equal(1, Count(0, SimEventKind.OrderRefused));
        Assert.Equal(1, Count(0, SimEventKind.Death));
        Assert.InRange(Count(1, SimEventKind.ShotHit), 2, 20);
        Assert.Equal(Count(1, SimEventKind.ShotHit), Count(0, SimEventKind.ShotHit));
        Assert.Equal(known[1], known[2]);

        // An event is handed over whole: the hits name the shot, and say nothing of where it came from.
        Assert.All(known[0].Where(happened => happened.Kind == SimEventKind.ShotHit), hit => Assert.Equal(victim, hit.Entity));
        Assert.False(views[0].Sees(ballista));
    }

    [Fact]
    public void AMeleeHitAndAShotThatIsLostAreSeenWhereTheyHappen()
    {
        var match = Create(TestMaps.Open(40, 12), players: 3);
        var view = new PlayerView(match, 2);

        // On tiles player 2 sees, and on tiles it does not.
        match.SpawnWorker(2, "6.5", "5.5");
        var seen = TestMatches.Point("8.5", "5.5");
        var unseen = TestMatches.Point("30.5", "5.5");
        foreach (var kind in new[] { SimEventKind.Death, SimEventKind.MeleeHit, SimEventKind.ShotFired, SimEventKind.ShotHit, SimEventKind.ShotLost })
        {
            Assert.True(view.Knows(new SimEvent(kind, 5, 0, new EntityId(1), new EntityId(2), seen, 7)));
            Assert.False(view.Knows(new SimEvent(kind, 5, 0, new EntityId(1), new EntityId(2), unseen, 7)));
            Assert.True(view.Knows(new SimEvent(kind, 5, 2, new EntityId(1), new EntityId(2), unseen, 7)));
        }

        Assert.False(view.Knows(new SimEvent(SimEventKind.OrderRefused, 5, 0, new EntityId(1), new EntityId(2), seen, 7)));
        Assert.True(view.Knows(new SimEvent(SimEventKind.OrderRefused, 5, 2, new EntityId(1), new EntityId(2), unseen, 7)));
    }

    /// <summary>
    /// Every kind of event is on one of two lists. A piece that adds a kind adds it to one of them
    /// here, and so decides who may know of it.
    /// </summary>
    [Fact]
    public void EveryKindOfEventIsEitherSeenWhereItHappensOrKnownToItsPlayerAlone()
    {
        SimEventKind[] seenWhereItHappens =
        [
            SimEventKind.Death, SimEventKind.MeleeHit, SimEventKind.ShotFired, SimEventKind.ShotHit, SimEventKind.ShotLost,
            SimEventKind.StructurePlaced, SimEventKind.StructureFinished, SimEventKind.StructureRemoved,
        ];
        SimEventKind[] knownToItsPlayerAlone = [SimEventKind.OrderRefused, SimEventKind.BuildRefused, SimEventKind.BuildOrderEnded];

        foreach (var kind in Enum.GetValues<SimEventKind>())
        {
            if (kind == SimEventKind.None)
            {
                continue;
            }

            Assert.True(
                seenWhereItHappens.Contains(kind) != knownToItsPlayerAlone.Contains(kind),
                $"{kind} must be on exactly one of the two lists of this test.");
            Assert.Equal(seenWhereItHappens.Contains(kind), PlayerView.IsSeenWhereItHappens(kind));
        }
    }

    [Fact]
    public void TheAccountIsThePlayersOwn()
    {
        var scene = new Scene();
        scene.Match.Stockpiles.Add(0, 120, 45);
        scene.Match.Stockpiles.Add(1, 7, 9);

        Assert.Equal(120_000, scene.Views[0].Materials);
        Assert.Equal(45_000, scene.Views[0].Energy);
        Assert.Equal(7_000, scene.Views[1].Materials);
        Assert.Equal(9_000, scene.Views[1].Energy);
        Assert.Equal(0, scene.Views[2].Materials);

        for (var player = 0; player < 3; player++)
        {
            var view = scene.Views[player];
            Assert.Equal(scene.Match.Supply.Used(player), view.SupplyUsed);
            Assert.Equal(scene.Match.Supply.Cap(player), view.SupplyCap);
            Assert.Same(scene.Match.Research.Tech(player), view.Tech);
            for (var slot = 0; slot < Sim.Research.ResearchState.SlotsPerPlayer; slot++)
            {
                var expected = scene.Match.Research.State.Slot(player, slot);
                var found = view.ResearchSlot(slot);
                Assert.Equal((expected.IsOpen, expected.Holder, expected.Track), (found.IsOpen, found.Holder, found.Track));
            }
        }

        // Player 0 uses the supply of a Guard and a worker, player 2 none; player 1 is in another epoch.
        Assert.True(scene.Views[0].SupplyUsed > 0);
        Assert.Equal(0, scene.Views[2].SupplyUsed);
        Assert.NotEqual(scene.Views[0].Tech.Epoch, scene.Views[1].Tech.Epoch);
    }

    [Fact]
    public void AskingChangesNothing()
    {
        var match = Ambush(out var victim, out var ballista);
        match.Run(30);
        var hash = match.World.ComputeHash();
        var revision = match.Vision.Grid.Revision;

        for (var player = 0; player < 4; player++)
        {
            var view = new PlayerView(match, player);
            view.CopyFog(new byte[view.Width * view.Height]);
            view.CollectUnits([]);
            view.CollectUnitsNear(match.PositionOf(victim), Fixed.FromInt(20), []);
            view.CollectUnitsIn(default, TestMatches.Point("40", "12"), []);
            view.CollectShots([]);
            view.CollectEvents([]);
            view.CollectWorkSites([]);
            view.Inspect(ballista, out _);
            view.TryGetUnit(victim, out _);
            view.TryGetOwnOrder(ballista, out _);
            view.TryGetTerrain(3, 3, out _);
            _ = view.Tech;
            _ = view.SupplyUsed;
        }

        Assert.Equal(hash, match.World.ComputeHash());
        Assert.Equal(revision, match.Vision.Grid.Revision);

        // And a match that was asked goes on as one that was not.
        var unasked = Ambush(out _, out _);
        unasked.Run(60);
        match.Run(30);
        Assert.Equal(unasked.World.ComputeHash(), match.World.ComputeHash());
    }

    [Fact]
    public void OneViewLastsAcrossARestoredSnapshot()
    {
        var scene = new Scene();
        var view = scene.Views[0];
        scene.Match.OrderMove(0, TestMatches.Point("5.5", "20.5"), scene.Hoplite);
        scene.Match.Run(10);
        var writer = new ByteWriter();
        Snapshot.Write(scene.Match.World, writer);
        var fog = new byte[40 * 24];
        view.CopyFog(fog);
        var units = new List<SeenUnit>();
        view.CollectUnits(units);

        scene.Match.Run(250);
        var later = new byte[40 * 24];
        view.CopyFog(later);
        var revision = view.FogRevision;
        var laterUnits = new List<SeenUnit>();
        view.CollectUnits(laterUnits);
        Assert.NotEqual(fog, later);
        Assert.NotEqual(units, laterUnits);

        Snapshot.Restore(scene.Match, new ByteReader(writer.ToArray()));

        Assert.NotEqual(revision, view.FogRevision);
        Assert.Equal(10, view.Tick);
        view.CopyFog(later);
        Assert.Equal(fog, later);
        view.CollectUnits(laterUnits);
        Assert.Equal(units, laterUnits);
    }
}
