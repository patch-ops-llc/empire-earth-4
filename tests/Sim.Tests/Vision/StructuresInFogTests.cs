using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Vision;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Vision;

/// <summary>
/// The fog of war and structures (design 5.3 and 2.5; design 8.4, rows 11, 24, 39 and 40): how
/// far a structure sees, when a structure of another player is seen, and what a player
/// remembers of one it no longer sees.
/// </summary>
public class StructuresInFogTests
{
    private const long Thousand = 1000;

    private static Match Field() => FogMatches.Create(TestMaps.Open(60, 24));

    private static FixedVector2 CenterOf(Match match, EntityId structure) => match.Bodies.Get(structure).Position;

    /// <summary>Puts a unit somewhere else at once and works sight out again, as a fixture may before a tick.</summary>
    private static void Put(Match match, EntityId unit, string x, string y)
    {
        match.Bodies.Get(unit).Position = TestMatches.Point(x, y);
        match.RefreshDerivedState();
        match.Vision.Refresh();
    }

    /// <summary>Design 8.4, row 11, and the table of design 2.5: the sight of each structure, from the center of its footprint.</summary>
    [Theory]
    [InlineData("capitol", 10)]
    [InlineData("outpost", 8)]
    [InlineData("generator", 6)]
    [InlineData("barracks", 6)]
    [InlineData("academy", 6)]
    [InlineData("tower", 10)]
    [InlineData("wall", 2)]
    public void AFinishedStructureSeesAsFarAsItsFileSaysFromItsCenter(string id, int sight)
    {
        var match = Field();
        Assert.Equal(sight, FogMatches.Structure(id).Sight);

        var structure = match.Place(0, id, 24, 10);

        var size = FogMatches.Structure(id).Footprint;
        var half = Fixed.FromInt(size) / Fixed.FromInt(2);
        var center = new FixedVector2(Fixed.FromInt(24) + half, Fixed.FromInt(10) + half);
        Assert.Equal(center, CenterOf(match, structure));
        VisionAssert.SeesExactly(match.Vision.Grid, 0, (center, sight));
        Assert.Equal(0, match.Vision.Grid.CountVisible(1));
        Assert.True(match.Vision.TryGetSight(structure, out var found));
        Assert.Equal(Fixed.FromInt(sight), found);

        // It sees the same once the match runs, and whenever sight is worked out.
        match.Run(9);
        VisionAssert.SeesExactly(match.Vision.Grid, 0, (center, sight));
    }

    /// <summary>Design 2.5: the sight of a structure is fixed. No track, tier or accent changes it.</summary>
    [Fact]
    public void NoTierChangesTheSightOfAStructure()
    {
        var match = Field();
        match.SetTiers(0, 6, 7, 7, 6, 6, 6);

        var tower = match.Place(0, "tower", 24, 10);

        VisionAssert.SeesExactly(match.Vision.Grid, 0, (CenterOf(match, tower), 10));
    }

    /// <summary>Design 8.4, rows 11 and 24: a structure that is not finished sees its own footprint only, and all its sight comes with completion.</summary>
    [Fact]
    public void AStructureThatIsBuildingItselfSeesOnlyItsOwnFootprint()
    {
        var match = Field();
        var buildTicks = FogMatches.Structure("outpost").BuildTicks;
        Assert.Equal(900, buildTicks);

        var outpost = match.Lay(0, "outpost", 24, 10);

        var grid = match.Vision.Grid;
        void SeesItsFootprint()
        {
            Assert.Equal(9, grid.CountVisible(0));
            Assert.Equal(9, grid.CountExplored(0));
            for (var y = 10; y <= 12; y++)
            {
                for (var x = 24; x <= 26; x++)
                {
                    Assert.True(grid.IsVisible(0, x, y));
                }
            }
        }

        SeesItsFootprint();
        Assert.False(match.Vision.TryGetSight(outpost, out _));

        // Until the tick in which it has built itself.
        match.Run(buildTicks - 1);
        Assert.True(match.Foundations.Contains(outpost));
        SeesItsFootprint();

        // It is finished in the next tick, and sees as a finished Outpost from the next time sight is worked out.
        match.Run(1);
        Assert.False(match.Foundations.Contains(outpost));
        SeesItsFootprint();
        match.Run(4);
        VisionAssert.SeesExactly(grid, 0, (CenterOf(match, outpost), 8));
        Assert.True(match.Vision.TryGetSight(outpost, out var sight));
        Assert.Equal(Fixed.FromInt(8), sight);
    }

    [Fact]
    public void AFootprintOverTheEdgeOfTheMapIsSeenAsFarAsTheMapGoes()
    {
        var grid = new VisionGrid(1, 10, 10);

        grid.RevealTiles(0, new TileSquare(8, -1, 3));
        Assert.Equal(4, grid.CountVisible(0));
        Assert.True(grid.IsVisible(0, 8, 0) && grid.IsVisible(0, 9, 0) && grid.IsVisible(0, 8, 1) && grid.IsVisible(0, 9, 1));

        grid.RevealTiles(0, new TileSquare(12, 12, 3));
        grid.RevealTiles(0, new TileSquare(-5, 3, 3));
        Assert.Equal(4, grid.CountVisible(0));

        Assert.True(grid.IsAnyVisible(0, new TileSquare(9, 1, 4)));
        Assert.False(grid.IsAnyVisible(0, new TileSquare(7, 2, 3)));
        Assert.True(grid.AreAllExplored(0, new TileSquare(8, 0, 2)));
        Assert.False(grid.AreAllExplored(0, new TileSquare(8, 0, 3)));
        Assert.False(grid.AreAllExplored(0, new TileSquare(8, -1, 2)));
    }

    /// <summary>Design 8.4, row 39: a structure is seen when any tile of its footprint is visible.</summary>
    [Fact]
    public void AStructureIsSeenWhenAnyTileOfItsFootprintIsVisible()
    {
        var match = Field();
        var barracks = match.Place(1, "barracks", 30, 10);
        var view = new PlayerView(match, 0);

        // Nobody of player 0 is there yet.
        Assert.False(match.Vision.Sees(0, barracks));
        Assert.False(view.TryGetStructure(barracks, out _));

        // A worker, which sees 6 tiles: the center of the corner tile (30, 10) is exactly 6 away, and no other tile is in reach.
        var worker = match.SpawnWorker(0, "24.5", "10.5");
        var grid = match.Vision.Grid;
        Assert.True(grid.IsVisible(0, 30, 10));
        Assert.False(grid.IsVisible(0, 30, 11));
        Assert.False(grid.IsVisible(0, 31, 10));
        Assert.False(grid.IsVisible(0, CenterOf(match, barracks)));

        Assert.True(match.Vision.Sees(0, barracks));
        Assert.True(view.Sees(barracks));
        Assert.True(view.TryGetStructure(barracks, out var seen));
        Assert.False(seen.IsRemembered);
        Assert.Equal(new TileSquare(30, 10, 3), seen.Tiles);

        // One tile further up, none of its tiles is in reach.
        Put(match, worker, "24.5", "9.5");
        Assert.False(grid.IsAnyVisible(0, new TileSquare(30, 10, 3)));
        Assert.False(match.Vision.Sees(0, barracks));

        // Its owner sees it always, and the player who is not there never did.
        Assert.True(match.Vision.Sees(1, barracks));
        Assert.False(new PlayerView(Field(), 0).TryGetStructure(barracks, out _));
    }

    /// <summary>
    /// Design 5.3 and design 8.4, row 39: on explored tiles that are not visible now, a player
    /// sees each enemy structure as it was when last seen, and the memory is cleared when its
    /// tiles are seen again without it.
    /// </summary>
    [Fact]
    public void AStructureIsRememberedAsItWasWhenLastSeenUntilItsTilesAreSeenWithoutIt()
    {
        var match = Field();
        var barracks = match.Place(1, "barracks", 40, 10);
        var tiles = new TileSquare(40, 10, 3);
        var rider = match.Spawn(0, Hippeus, "31.5", "11.5");
        var view = new PlayerView(match, 0);
        var structures = new List<SeenStructure>();

        Assert.True(view.TryGetStructure(barracks, out var seen));
        Assert.False(seen.IsRemembered);
        Assert.Equal(1000 * Thousand, seen.HitPoints);

        // The rider leaves. What happens to the Barracks afterwards, the player does not see.
        match.OrderMove(0, TestMatches.Point("3.5", "11.5"), rider);
        match.Run(200);
        match.SetHitPoints(barracks, 400 * Thousand);
        match.Run(8);

        Assert.Equal(FogState.Explored, view.FogAt(40, 11));
        Assert.False(view.Sees(barracks));
        Assert.True(view.KnowsStructure(barracks));
        Assert.True(view.TryGetStructure(barracks, out seen));
        Assert.True(seen.IsRemembered);
        Assert.Equal(1000 * Thousand, seen.HitPoints);
        Assert.Equal(1000 * Thousand, seen.MaxHitPoints);
        Assert.Equal(1, seen.Owner);
        Assert.Equal(FogMatches.Structure("barracks").Index, seen.Type);
        Assert.Equal(tiles, seen.Tiles);
        Assert.True(seen.IsFinished);
        Assert.InRange(seen.SeenTick, 1, 200);
        Assert.Equal(0, seen.SeenTick % match.Vision.RefreshTicks);
        view.CollectStructures(structures);
        Assert.Equal([seen], structures);

        // The Barracks is taken off the map. The player has not looked, and still remembers it.
        match.Demolish(barracks);
        match.Run(8);
        Assert.False(match.IsAlive(barracks));
        Assert.True(match.Grid.IsPassable(41, 11));
        Assert.True(view.KnowsStructure(barracks));
        Assert.True(view.TryGetStructure(barracks, out var gone));
        Assert.Equal(seen, gone);
        Assert.True(view.TryGetStructureAt(42, 12, out gone));
        Assert.Equal(barracks, gone.Entity);
        Assert.True(match.Vision.KnowsBlocked(0, tiles));

        // The rider comes back and sees the tiles without it.
        match.OrderMove(0, TestMatches.Point("31.5", "11.5"), rider);
        match.Run(200);
        Assert.Equal(FogState.Visible, view.FogAt(40, 11));
        Assert.False(view.KnowsStructure(barracks));
        Assert.False(view.TryGetStructure(barracks, out _));
        Assert.False(view.TryGetStructureAt(42, 12, out _));
        Assert.Equal(0, match.Vision.Memory.Count(0));
        Assert.False(match.Vision.KnowsBlocked(0, tiles));
        view.CollectStructures(structures);
        Assert.Empty(structures);
    }

    /// <summary>
    /// "As it was when last seen": when sight is worked out and a structure goes out of it, the
    /// structure is remembered as it is in that tick, not as it was when sight was worked out
    /// the time before.
    /// </summary>
    [Fact]
    public void WhatIsRememberedIsTheLastLookBeforeSightIsLost()
    {
        var match = Field();
        var tower = match.Place(1, "tower", 40, 10);
        var worker = match.SpawnWorker(0, "36.5", "11.5");
        var view = new PlayerView(match, 0);
        Assert.True(view.TryGetStructure(tower, out var seen));
        Assert.Equal(600 * Thousand, seen.HitPoints);

        // While the player sees it, the view shows it as it is, whenever sight was last worked out.
        match.SetHitPoints(tower, 500 * Thousand);
        Assert.True(view.TryGetStructure(tower, out seen));
        Assert.False(seen.IsRemembered);
        Assert.Equal(500 * Thousand, seen.HitPoints);

        // It loses 200 more, and then the worker is gone when sight is worked out again.
        match.SetHitPoints(tower, 300 * Thousand);
        Put(match, worker, "5.5", "11.5");
        Assert.True(view.TryGetStructure(tower, out seen));
        Assert.True(seen.IsRemembered);
        Assert.Equal(300 * Thousand, seen.HitPoints);

        // What it loses from here on, the player does not know.
        match.SetHitPoints(tower, 100 * Thousand);
        match.Vision.Refresh();
        match.Run(8);
        Assert.True(view.TryGetStructure(tower, out seen));
        Assert.Equal(300 * Thousand, seen.HitPoints);

        // Until it looks again.
        Put(match, worker, "36.5", "11.5");
        Assert.True(view.TryGetStructure(tower, out seen));
        Assert.False(seen.IsRemembered);
        Assert.Equal(100 * Thousand, seen.HitPoints);
        Assert.True(match.Vision.Memory.TryGet(0, tower, out var remembered));
        Assert.Equal(100 * Thousand, remembered.HitPoints);
    }

    /// <summary>Design 5.3: a player sees structures as last seen, so one it has never seen is not known, also on ground it has explored.</summary>
    [Fact]
    public void AStructureThePlayerHasNeverSeenIsNotKnownAlsoOnExploredGround()
    {
        var match = Field();
        var worker = match.SpawnWorker(0, "36.5", "11.5");
        Put(match, worker, "5.5", "11.5");
        var view = new PlayerView(match, 0);
        Assert.Equal(FogState.Explored, view.FogAt(40, 11));

        var generator = match.Place(1, "generator", 40, 10);

        Assert.False(view.Sees(generator));
        Assert.False(view.KnowsStructure(generator));
        Assert.False(view.TryGetStructure(generator, out _));
        Assert.False(view.TryGetStructureAt(40, 10, out _));
        Assert.False(match.Vision.KnowsBlocked(0, new TileSquare(40, 10, 2)));
        Assert.Equal(0, match.Vision.Memory.Count(0));

        // Its owner knows it, and what it blocks.
        Assert.True(new PlayerView(match, 1).KnowsStructure(generator));
        Assert.True(match.Vision.KnowsBlocked(1, new TileSquare(41, 11, 2)));
    }

    /// <summary>A foundation is remembered as a foundation, with what it had built, also once it is finished out of sight.</summary>
    [Fact]
    public void AFoundationIsRememberedAsItWasAndNotAsWhatItHasBecome()
    {
        var match = Field();
        var outpost = match.Lay(1, "outpost", 40, 10);
        var worker = match.SpawnWorker(0, "36.5", "11.5");
        var view = new PlayerView(match, 0);
        var territories = new List<Sim.Economy.Territory>();
        match.Run(100);

        Assert.True(view.TryGetStructure(outpost, out var seen));
        Assert.False(seen.IsRemembered);
        Assert.False(seen.IsFinished);
        Assert.Equal(100, seen.Built);
        view.CollectTerritories(territories);
        Assert.Empty(territories);

        match.OrderMove(0, TestMatches.Point("5.5", "11.5"), worker);
        match.Run(900);
        Assert.False(match.Foundations.Contains(outpost));

        Assert.True(view.TryGetStructure(outpost, out seen));
        Assert.True(seen.IsRemembered);
        Assert.False(seen.IsFinished);
        Assert.InRange(seen.Built, 101, 200);
        Assert.Equal(seen.Built, seen.SeenTick);

        // A territory comes with completion, so the player knows of none here.
        view.CollectTerritories(territories);
        Assert.Empty(territories);

        // When the player has seen it finished, it knows the territory, and keeps it in memory.
        Put(match, worker, "36.5", "11.5");
        Assert.True(view.TryGetStructure(outpost, out seen));
        Assert.True(seen.IsFinished);
        Assert.Equal(900, seen.Built);
        Put(match, worker, "5.5", "11.5");
        Assert.True(view.TryGetStructure(outpost, out seen));
        Assert.True(seen.IsRemembered && seen.IsFinished);
        view.CollectTerritories(territories);
        Assert.Equal([Sim.Economy.Territory.Around(1, 40, 10, 3, 12)], territories);
    }

    /// <summary>
    /// Design 3.2: a structure's look shows its owner's tier in one track. A remembered
    /// structure keeps the look it had.
    /// </summary>
    [Fact]
    public void ARememberedStructureKeepsTheLookItHad()
    {
        var match = Field();
        match.SetTiers(1, 1, 2, 1, 4, 1, 1);
        var barracks = match.Place(1, "barracks", 40, 10);
        var outpost = match.Place(1, "outpost", 40, 16);
        var worker = match.SpawnWorker(0, "36.5", "14.5");
        var view = new PlayerView(match, 0);

        // The Barracks shows Manufacturing. An Outpost looks the same at every tier.
        Assert.True(view.TryGetStructure(barracks, out var seen));
        Assert.Equal(4, seen.LookTier);
        Assert.Equal(SeenStructure.NoTrack, seen.Banner);
        Assert.True(view.TryGetStructure(outpost, out var plain));
        Assert.Equal(0, plain.LookTier);

        Put(match, worker, "5.5", "11.5");
        match.SetTiers(1, 1, 3, 1, 5, 1, 1);
        Assert.True(view.TryGetStructure(barracks, out seen));
        Assert.True(seen.IsRemembered);
        Assert.Equal(4, seen.LookTier);

        // Protection 2 gave it 1.25 times its hit points, and the player remembers those.
        Assert.Equal(1250 * Thousand, seen.MaxHitPoints);

        Put(match, worker, "36.5", "14.5");
        Assert.True(view.TryGetStructure(barracks, out seen));
        Assert.Equal(5, seen.LookTier);
    }

    /// <summary>Design 8.4, row 40: an attack order may name a structure the player sees or remembers, and no other.</summary>
    [Fact]
    public void AStructureMayBeNamedByAnAttackOrderWhenItIsSeenOrRemembered()
    {
        var match = Field();
        var seenNow = match.Place(1, "tower", 40, 10);
        var remembered = match.Place(1, "generator", 40, 2);
        var neverSeen = match.Place(1, "barracks", 54, 18);
        var own = match.Place(0, "generator", 4, 4);
        var worker = match.SpawnWorker(0, "36.5", "3.5");
        Put(match, worker, "36.5", "11.5");
        var vision = match.Vision;

        Assert.True(vision.Sees(0, seenNow));
        Assert.True(vision.KnowsStructure(0, seenNow));

        Assert.False(vision.Sees(0, remembered));
        Assert.True(vision.Remembers(0, remembered, out _));
        Assert.True(vision.KnowsStructure(0, remembered));

        Assert.False(vision.KnowsStructure(0, neverSeen));
        Assert.True(vision.KnowsStructure(0, own));
        Assert.False(vision.Remembers(0, own, out _));

        // A unit is not a structure, and something that is gone and was never seen is nothing.
        Assert.False(vision.KnowsStructure(0, worker));
        Assert.False(vision.KnowsStructure(0, new EntityId(99)));
        Assert.False(vision.KnowsStructure(5, seenNow));

        // A remembered structure that is gone may still be named, until the player sees that it is gone.
        match.Demolish(remembered);
        match.Run(4);
        Assert.True(vision.KnowsStructure(0, remembered));
        Put(match, worker, "36.5", "3.5");
        Assert.False(vision.KnowsStructure(0, remembered));
    }
}
