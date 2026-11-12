using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Construction;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Vision;

namespace Sim.Tests.Vision;

/// <summary>
/// <see cref="PlayerView"/> and structures: what it lists, what it tells of one, which events
/// of structures a player may know of, and that it tells nothing of what the fog hides.
/// </summary>
public class PlayerViewStructureTests
{
    private const long Thousand = 1000;

    /// <summary>
    /// Three players on open ground of 60 by 24 tiles. Player 0 has a Generator in the west and
    /// a worker further east. Player 1 has a Tower of which the worker sees one tile, a Barracks that it has
    /// seen and sees no longer, and an Outpost in the east that it has never seen. Player 2 has
    /// nothing.
    /// </summary>
    private sealed class Scene
    {
        public Scene()
        {
            Match = FogMatches.Create(TestMaps.Open(60, 24), players: 3);
            Match.SetTiers(1, 3, 2, 1, 4, 1, 1);
            Own = Match.Place(0, "generator", 4, 4);
            Remembered = Match.Place(1, "barracks", 30, 4);
            Tower = Match.Place(1, "tower", 30, 16);
            Unseen = Match.Place(1, "outpost", 50, 10);
            Worker = Match.SpawnWorker(0, "26.5", "5.5");
            Match.Bodies.Get(Worker).Position = TestMatches.Point("24.5", "16.5");
            Match.RefreshDerivedState();
            Match.Vision.Refresh();
            Views = [new PlayerView(Match, 0), new PlayerView(Match, 1), new PlayerView(Match, 2)];
        }

        public Match Match { get; }

        public EntityId Own { get; }

        public EntityId Remembered { get; }

        public EntityId Tower { get; }

        public EntityId Unseen { get; }

        public EntityId Worker { get; }

        public PlayerView[] Views { get; }
    }

    [Fact]
    public void ThePlayersOwnStructuresAndTheOthersItSeesOrRemembersAreListedInIdOrder()
    {
        var scene = new Scene();
        var structures = new List<SeenStructure>();

        scene.Views[0].CollectStructures(structures);
        Assert.Equal([scene.Own, scene.Remembered, scene.Tower], structures.Select(structure => structure.Entity));
        Assert.Equal([false, true, false], structures.Select(structure => structure.IsRemembered));

        // Its owner knows all of its own, none of them from memory, and of the others what it sees: nothing.
        scene.Views[1].CollectStructures(structures);
        Assert.Equal([scene.Remembered, scene.Tower, scene.Unseen], structures.Select(structure => structure.Entity));
        Assert.All(structures, structure => Assert.False(structure.IsRemembered));

        scene.Views[2].CollectStructures(structures);
        Assert.Empty(structures);

        // The list is emptied first, and a structure that is gone is in nobody's list but of those who remember it.
        scene.Match.Demolish(scene.Remembered);
        scene.Match.Demolish(scene.Own);
        scene.Match.Run(1);
        scene.Views[0].CollectStructures(structures);
        Assert.Equal([scene.Remembered, scene.Tower], structures.Select(structure => structure.Entity));
        scene.Views[1].CollectStructures(structures);
        Assert.Equal([scene.Tower, scene.Unseen], structures.Select(structure => structure.Entity));
        Assert.Throws<ArgumentNullException>(() => scene.Views[0].CollectStructures(null!));
    }

    [Fact]
    public void AStructureIsDescribedAsItIsOrAsItWasLastSeen()
    {
        var scene = new Scene();
        var view = scene.Views[0];
        scene.Match.Run(6);
        scene.Match.SetHitPoints(scene.Tower, 450 * Thousand);
        scene.Match.SetHitPoints(scene.Remembered, 10 * Thousand);

        Assert.True(view.TryGetStructure(scene.Tower, out var tower));
        Assert.Equal(
            new SeenStructure(scene.Tower, 1, FogMatches.Structure("tower").Index, 30, 16, 2, 450 * Thousand, tower.MaxHitPoints, true, FogMatches.Structure("tower").BuildTicks, 2, SeenStructure.NoTrack, false, 6),
            tower);
        Assert.Equal(new TileSquare(30, 16, 2), tower.Tiles);
        Assert.Equal(TestMatches.Point("31", "17"), tower.Center);
        Assert.Equal(scene.Match.Health.Get(scene.Tower).Maximum, tower.MaxHitPoints);

        // The Barracks as it was when the worker stood beside it, before the match began to run.
        Assert.True(view.TryGetStructure(scene.Remembered, out var barracks));
        Assert.True(barracks.IsRemembered);
        Assert.Equal(0, barracks.SeenTick);
        Assert.Equal(barracks.MaxHitPoints, barracks.HitPoints);
        Assert.Equal(4, barracks.LookTier);
        Assert.Equal(TestMatches.Point("31.5", "5.5"), barracks.Center);

        // The player's own as it is, wherever it stands.
        Assert.True(view.TryGetStructure(scene.Own, out var own));
        Assert.Equal((0, false, 6), (own.Owner, own.IsRemembered, own.SeenTick));

        // Nothing of one it has never seen, of a unit, or of nothing.
        Assert.False(view.TryGetStructure(scene.Unseen, out var nothing));
        Assert.Equal(default, nothing);
        Assert.False(view.TryGetStructure(scene.Worker, out _));
        Assert.False(view.TryGetStructure(EntityId.None, out _));
        Assert.False(view.TryGetStructure(new EntityId(99), out _));
    }

    [Fact]
    public void TheStructureOnATileIsToldWhenThePlayerKnowsOfIt()
    {
        var scene = new Scene();
        var view = scene.Views[0];

        Assert.True(view.TryGetStructureAt(31, 17, out var tower));
        Assert.Equal(scene.Tower, tower.Entity);
        Assert.True(view.TryGetStructureAt(32, 6, out var barracks));
        Assert.Equal((scene.Remembered, true), (barracks.Entity, barracks.IsRemembered));
        Assert.True(view.TryGetStructureAt(5, 5, out var own));
        Assert.Equal(scene.Own, own.Entity);

        Assert.False(view.TryGetStructureAt(32, 17, out _));
        Assert.False(view.TryGetStructureAt(51, 11, out _));
        Assert.False(view.TryGetStructureAt(-1, 4, out _));
        Assert.False(view.TryGetStructureAt(60, 4, out _));
        Assert.True(scene.Views[1].TryGetStructureAt(51, 11, out _));
        Assert.False(scene.Views[2].TryGetStructureAt(31, 17, out _));
    }

    /// <summary>A structure has a body, and is no unit: the calls for units never give one.</summary>
    [Fact]
    public void TheCallsForUnitsNeverGiveAStructure()
    {
        var scene = new Scene();
        var units = new List<SeenUnit>();

        foreach (var view in scene.Views)
        {
            view.CollectUnits(units);
            Assert.DoesNotContain(units, unit => scene.Match.Structures.Contains(unit.Entity));
            view.CollectUnitsNear(TestMatches.Point("31", "17"), Fixed.FromInt(40), units);
            Assert.DoesNotContain(units, unit => scene.Match.Structures.Contains(unit.Entity));
            view.CollectUnitsIn(TestMatches.Point("0", "0"), TestMatches.Point("60", "24"), units);
            Assert.DoesNotContain(units, unit => scene.Match.Structures.Contains(unit.Entity));
            Assert.False(view.TryGetUnit(scene.Tower, out _));
            Assert.False(view.TryGetUnit(scene.Own, out _));
        }

        scene.Views[0].CollectUnits(units);
        Assert.Equal([scene.Worker], units.Select(unit => unit.Entity));

        // Seeing one is by its footprint: the worker is 6 tiles from the Tower's corner tile and further from its center.
        Assert.True(scene.Views[0].Sees(scene.Tower));
        Assert.False(scene.Match.Vision.Grid.IsVisible(0, scene.Match.Bodies.Get(scene.Tower).Position));
        Assert.False(scene.Views[0].Sees(scene.Remembered));
        Assert.True(scene.Views[1].Sees(scene.Remembered));
    }

    /// <summary>Design 3.2: inspecting a building shows the effective tiers it has: its owner's Protection, and its Firepower if it attacks.</summary>
    [Fact]
    public void InspectingAStructureShowsItsOwnersTiers()
    {
        var scene = new Scene();
        var view = scene.Views[0];

        // Player 1 has Firepower 3 and Protection 2.
        Assert.True(view.Inspect(scene.Tower, out var tower));
        Assert.Equal((3, 2, 0), (tower.Firepower, tower.Protection, tower.Mobility));
        Assert.Empty(tower.Accents);

        // The player's own Generator, which does not attack.
        Assert.True(view.Inspect(scene.Own, out var own));
        Assert.Equal((0, 1, 0), (own.Firepower, own.Protection, own.Mobility));

        // Not one it only remembers, and not one it has never seen.
        Assert.False(view.Inspect(scene.Remembered, out _));
        Assert.False(view.Inspect(scene.Unseen, out _));
        Assert.True(scene.Views[1].Inspect(scene.Remembered, out var barracks));
        Assert.Equal((0, 2, 0), (barracks.Firepower, barracks.Protection, barracks.Mobility));
        Assert.False(scene.Views[2].Inspect(scene.Tower, out _));
    }

    [Fact]
    public void TerritoriesAreThePlayersOwnAndThoseOfTheStructuresItKnows()
    {
        var match = FogMatches.Create(TestMaps.Open(60, 24));
        match.Place(0, "outpost", 4, 4);
        var seen = match.Place(1, "outpost", 30, 10);
        match.Place(1, "outpost", 52, 4);
        match.Place(1, "generator", 34, 10);
        var worker = match.SpawnWorker(0, "26.5", "11.5");
        var territories = new List<Territory>();
        var view = new PlayerView(match, 0);

        view.CollectTerritories(territories);
        Assert.Equal([Territory.Around(0, 4, 4, 3, 12), Territory.Around(1, 30, 10, 3, 12)], territories);

        // Remembered, it is still known. Its owner knows its own two and nothing of player 0's.
        match.Bodies.Get(worker).Position = TestMatches.Point("5.5", "9.5");
        match.Vision.Refresh();
        Assert.False(view.Sees(seen));
        view.CollectTerritories(territories);
        Assert.Equal([Territory.Around(0, 4, 4, 3, 12), Territory.Around(1, 30, 10, 3, 12)], territories);
        new PlayerView(match, 1).CollectTerritories(territories);
        Assert.Equal([Territory.Around(1, 30, 10, 3, 12), Territory.Around(1, 52, 4, 3, 12)], territories);
    }

    /// <summary>
    /// A structure that is laid, finished or removed is known to a player who sees any tile of
    /// its footprint, as the structure itself is (design 8.4, row 39). A refusal and the end of
    /// a build order are for the player who gave the order alone.
    /// </summary>
    [Fact]
    public void WhatHappensToAStructureIsKnownToWhoeverSeesATileOfIt()
    {
        var match = FogMatches.Create(TestMaps.Open(60, 24), players: 3);
        match.Place(1, "outpost", 40, 10);
        match.Stockpiles.Add(1, 1000, 1000);
        var builder = match.WorkerAt(1, 46, 12);
        var cancelled = match.Lay(1, "generator", 46, 18);

        // Each sees one tile of a site and not its middle: (46, 14) of the first, (46, 18) of the second.
        match.SpawnWorker(0, "40.5", "14.5");
        match.SpawnWorker(0, "40.5", "18.5");
        Assert.True(match.Vision.Grid.IsVisible(0, 46, 14));
        Assert.False(match.Vision.Grid.IsVisible(0, 47, 15));
        Assert.False(match.Vision.Grid.IsVisible(0, 47, 14) || match.Vision.Grid.IsVisible(0, 46, 15));
        var views = new[] { new PlayerView(match, 0), new PlayerView(match, 1), new PlayerView(match, 2) };
        var events = new List<SimEvent>();

        SimEvent RunUntil(SimEventKind kind)
        {
            for (var ticks = 0; ticks < 1200; ticks++)
            {
                match.Simulation.Tick();
                if (match.EventsOf(kind) is [var found, ..])
                {
                    return found;
                }
            }

            throw new InvalidOperationException($"No {kind} in 1,200 ticks.");
        }

        void AssertKnownToTheOwnerAndWhoeverSeesIt(SimEvent happened)
        {
            Assert.True(views[0].Knows(happened));
            Assert.True(views[1].Knows(happened));
            Assert.False(views[2].Knows(happened));
            views[0].CollectEvents(events);
            Assert.Contains(happened, events);
            views[2].CollectEvents(events);
            Assert.Empty(events);
        }

        // An order that is refused first: only its player is told.
        match.OrderBuild(1, "generator", 20, 2, builder);
        var refused = RunUntil(SimEventKind.BuildRefused);
        Assert.Equal(BuildProblem.OutsideTerritory, (BuildProblem)refused.Amount);
        Assert.False(views[0].Knows(refused));
        Assert.True(views[1].Knows(refused));

        match.OrderBuild(1, "generator", 46, 14, builder);
        var placed = RunUntil(SimEventKind.StructurePlaced);
        Assert.Equal(TestMatches.Point("47", "15"), placed.Position);
        AssertKnownToTheOwnerAndWhoeverSeesIt(placed);

        match.OrderCancelBuild(1, cancelled);
        var removed = RunUntil(SimEventKind.StructureRemoved);
        Assert.Equal(cancelled, removed.Entity);
        AssertKnownToTheOwnerAndWhoeverSeesIt(removed);

        var finished = RunUntil(SimEventKind.StructureFinished);
        Assert.Equal(placed.Entity, finished.Entity);
        AssertKnownToTheOwnerAndWhoeverSeesIt(finished);

        // A site one tile further east is out of sight, and what happens there is its owner's alone.
        match.OrderBuild(1, "generator", 47, 8, builder);
        var unseen = RunUntil(SimEventKind.StructurePlaced);
        Assert.False(views[0].Knows(unseen));
        Assert.True(views[1].Knows(unseen));
    }

    [Fact]
    public void ABuildOrderIsToldOfThePlayersOwnWorkersOnly()
    {
        var match = FogMatches.Create(TestMaps.Open(60, 24));
        match.Place(0, "outpost", 10, 10);
        match.Stockpiles.Add(0, 1000, 1000);
        var worker = match.WorkerAt(0, 6, 6);
        var idle = match.WorkerAt(0, 7, 6);
        match.Vision.Refresh();
        match.OrderBuild(0, "generator", 16, 14, worker);
        match.Run(1);
        var view = new PlayerView(match, 0);

        Assert.True(view.TryGetOwnBuildOrder(worker, out var order));
        Assert.Equal((match.Def("generator").Index, 16, 14, 2), (order.Type, order.X, order.Y, order.Size));
        Assert.False(view.TryGetOwnBuildOrder(idle, out _));
        Assert.False(new PlayerView(match, 1).TryGetOwnBuildOrder(worker, out _));

        // What it may build, and how many it has.
        Assert.Equal(BuildProblem.None, view.CheckStructure(match.Def("generator").Index));
        Assert.Equal(BuildProblem.NoStructure, view.CheckStructure(match.Def("wall").Index));
        Assert.Equal(1, view.CountOf(match.Def("outpost").Index));
        Assert.Equal(0, new PlayerView(match, 1).CountOf(match.Def("outpost").Index));
    }
}
