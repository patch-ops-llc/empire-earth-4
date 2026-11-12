using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Vision;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Vision;

/// <summary>
/// The fog of war in a match (design 5.3; design 8.4, rows 38 and 39): how far each unit sees,
/// that nothing is in the way, when sight is worked out, what stays explored, and which units of
/// another player are seen.
/// </summary>
public class FogOfWarTests
{
    private const int WorkerSight = 6;

    private static Match Field() => Create(TestMaps.Open(40, 24));

    /// <summary>Design 8.4, row 39, and design 5.3: at the start everything is unexplored except what the starting Capitol and workers see.</summary>
    [Fact]
    public void AtTheStartEverythingIsUnexploredExceptWhatTheStartingCapitolAndWorkersSee()
    {
        var result = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        var data = result.Data;
        var match = MatchSetup.Create(data, data.Maps[0], 1, [new(0), new(1)]);
        var grid = match.Vision.Grid;

        Assert.Equal(WorkerSight, data.Resources.Worker.Sight);
        for (var player = 0; player < 2; player++)
        {
            var eyes = Enumerable.Range(0, match.Workers.Count)
                .Select(i => match.Bodies.Get(match.Workers.EntityAt(i)))
                .Where(body => body.Owner == player)
                .Select(body => (body.Position, WorkerSight))
                .ToList();
            Assert.Equal(data.Rules.Start.Workers, eyes.Count);

            // The Capitol sees from the middle of its footprint, as far as its file says (design 2.5: 10).
            var capitol = Enumerable.Range(0, match.Structures.Count)
                .Select(match.Structures.EntityAt)
                .Single(structure => match.Bodies.Get(structure).Owner == player);
            Assert.Equal(10, data.StartStructure.Sight);
            eyes.Add((match.Bodies.Get(capitol).Position, data.StartStructure.Sight));

            VisionAssert.SeesExactly(grid, player, [.. eyes]);
            Assert.Equal(grid.CountVisible(player), grid.CountExplored(player));
            Assert.InRange(grid.CountVisible(player), 316, (match.Map.Width * match.Map.Height) / 4);

            // Not the other player's start, and none of the other player's workers.
            var other = match.Map.Starts[1 - player];
            Assert.Equal(FogState.Unexplored, grid.StateOf(player, other.X, other.Y));
            for (var i = 0; i < match.Workers.Count; i++)
            {
                var worker = match.Workers.EntityAt(i);
                Assert.Equal(match.Bodies.Get(worker).Owner == player, match.Vision.Sees(player, worker));
            }
        }
    }

    [Fact]
    public void AMatchWithNobodyOnTheMapHasEverythingUnexplored()
    {
        var match = Field();

        for (var player = 0; player < 2; player++)
        {
            Assert.Equal(0, match.Vision.Grid.CountExplored(player));
            Assert.Equal(0, match.Vision.Grid.CountVisible(player));
        }

        // And it stays so while nobody is there to see.
        match.Run(9);
        Assert.Equal(0, match.Vision.Grid.CountExplored(0));
    }

    /// <summary>Design 2.4: the sight of each line. Design 4.1: a worker's.</summary>
    [Theory]
    [InlineData(Hoplite, 8)]
    [InlineData(Peltast, 8)]
    [InlineData(Hippeus, 10)]
    [InlineData(Ballista, 8)]
    [InlineData("air", 12)]
    [InlineData("worker", 6)]
    public void AUnitSeesAsFarAsItsBlueprintSaysAndAWorkerAsFarAsItsProfile(string unit, int sight)
    {
        var match = Field();
        var position = TestMatches.Point("20.25", "12.75");
        EntityId entity;
        if (unit == "worker")
        {
            entity = match.SpawnWorker(0, position);
            Assert.Equal(sight, CombatMatches.Data.Resources.Worker.Sight);
        }
        else
        {
            // The line Air has no chassis the fixture names: its first is taken.
            var chassis = unit == "air" ? CombatMatches.Data.Chassis.First(chassis => chassis.Line == Line("air")).Id : unit;
            entity = match.SpawnChassis(0, Chassis(chassis), position);
            Assert.Equal(sight, match.Blueprint(0, chassis).Sight);
        }

        match.Vision.Refresh();

        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, sight));
        Assert.True(match.Vision.TryGetSight(entity, out var found));
        Assert.Equal(Fixed.FromInt(sight), found);
        Assert.Equal(0, match.Vision.Grid.CountVisible(1));

        // It sees the same once the match runs.
        match.Run(1);
        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, sight));
    }

    /// <summary>Design 2.3: the Rapid accent adds 2 to sight, and the blueprint already holds it.</summary>
    [Fact]
    public void TheSightAccentOfTheOwnersBlueprintReachesFurther()
    {
        var match = Field();
        match.SetTiers(0, 6, 3, 7, 4, 3, 5);
        var position = TestMatches.Point("20.5", "12.5");
        Assert.Equal(12, match.Blueprint(0, "cuirassier").Sight);
        Assert.Equal(10, match.Blueprint(1, "cuirassier").Sight);

        match.Spawn(0, "cuirassier", "20.5", "12.5");
        match.Spawn(1, "cuirassier", "20.5", "12.5");

        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, 12));
        VisionAssert.SeesExactly(match.Vision.Grid, 1, (position, 10));
    }

    /// <summary>Design 8.4, row 38: nothing blocks sight. Forest, cliffs and the ground a unit cannot cross do not.</summary>
    [Fact]
    public void NothingBlocksSight()
    {
        var match = Create(TestMaps.FromRows(
            "....#FFFF#..........",
            "....#FFFF#..........",
            "....#FFFF#..........",
            "....#FFFF#..........",
            "....#FFFF#..........",
            "....######..........",
            "...................."));
        var position = TestMatches.Point("2.5", "2.5");
        match.Spawn(0, Hoplite, "2.5", "2.5");

        // The cliff beside it, the forest behind the cliff, the cliff behind that, and the open
        // ground on the far side that it could never walk to.
        Assert.True(match.Vision.Grid.IsVisible(0, 4, 2));
        Assert.True(match.Vision.Grid.IsVisible(0, 6, 2));
        Assert.True(match.Vision.Grid.IsVisible(0, 9, 2));
        Assert.True(match.Vision.Grid.IsVisible(0, 10, 2));
        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, 8));
    }

    [Fact]
    public void WhatAPlayerSeesIsWhatAllItsUnitsSeeTogether()
    {
        var match = Field();
        match.Spawn(0, Hoplite, "5.5", "5.5");
        match.Spawn(0, Hippeus, "30.5", "18.5");
        match.SpawnWorker(0, "9.5", "9.5");
        match.SpawnWorker(1, "20.5", "12.5");

        VisionAssert.SeesExactly(
            match.Vision.Grid,
            0,
            (TestMatches.Point("5.5", "5.5"), 8),
            (TestMatches.Point("30.5", "18.5"), 10),
            (TestMatches.Point("9.5", "9.5"), 6));
        VisionAssert.SeesExactly(match.Vision.Grid, 1, (TestMatches.Point("20.5", "12.5"), 6));
    }

    /// <summary>
    /// Design 8.4, row 38: worked out every 4 ticks. In the ticks between, the picture is the one
    /// of the last time, wherever the unit has walked since.
    /// </summary>
    [Fact]
    public void SightIsWorkedOutInEveryFourthTickAndHeldInBetween()
    {
        var match = Field();
        Assert.Equal(4, match.Vision.RefreshTicks);
        var rider = match.Spawn(0, Hippeus, "8.5", "12.5");
        match.OrderMove(0, TestMatches.Point("32.5", "12.5"), rider);

        var seenFrom = match.PositionOf(rider);
        var changes = 0;
        var stale = 0;
        for (var tick = 0; tick < 60; tick++)
        {
            var revision = match.Vision.Grid.Revision;
            match.Run(1);
            if (tick % 4 == 0)
            {
                seenFrom = match.PositionOf(rider);
            }
            else
            {
                Assert.Equal(revision, match.Vision.Grid.Revision);
                stale += match.PositionOf(rider) == seenFrom ? 0 : 1;
            }

            changes += match.Vision.Grid.Revision == revision ? 0 : 1;
            VisionAssert.SeesExactly(match.Vision.Grid, 0, (seenFrom, 10));
        }

        // The rider covers nine tenths of a tile between two times, so nearly every time changes the picture.
        Assert.InRange(changes, 10, 15);
        Assert.Equal(45, stale);
    }

    [Fact]
    public void GroundSeenOnceStaysExploredWhenTheUnitHasGone()
    {
        var match = Field();
        var rider = match.Spawn(0, Hippeus, "5.5", "12.5");
        var start = TestMatches.Point("5.5", "12.5");
        var end = TestMatches.Point("34.5", "12.5");
        match.OrderMove(0, end, rider);
        match.Run(200);
        Assert.Equal(end, match.PositionOf(rider));

        var grid = match.Vision.Grid;
        VisionAssert.SeesExactly(grid, 0, (end, 10));

        // Where it started and what it passed is explored and not visible; what it never came near is neither.
        Assert.Equal(FogState.Explored, grid.StateOf(0, 5, 12));
        Assert.Equal(FogState.Explored, grid.StateOf(0, 0, 12));
        Assert.Equal(FogState.Explored, grid.StateOf(0, 5, 3));
        Assert.Equal(FogState.Explored, grid.StateOf(0, 15, 21));
        Assert.Equal(FogState.Visible, grid.StateOf(0, 30, 12));
        Assert.Equal(FogState.Unexplored, grid.StateOf(0, 5, 0));
        Assert.Equal(FogState.Unexplored, grid.StateOf(0, 0, 0));
        Assert.Equal(FogState.Unexplored, grid.StateOf(0, 20, 0));

        // Explored is everything within 10 tiles of some point it stood on when sight was worked out.
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var explored = grid.IsExplored(0, x, y);
                var dy = Fixed.FromInt(y) + Fixed.Half - start.Y;
                Assert.True(!explored || Fixed.Abs(dy) <= Fixed.FromInt(10), $"Tile ({x}, {y}) is explored.");
                Assert.True(explored || !grid.IsVisible(0, x, y));
            }
        }

        Assert.True(grid.CountExplored(0) > 2 * grid.CountVisible(0));
        Assert.Equal(0, grid.CountExplored(1));
    }

    /// <summary>
    /// Design 8.4, row 39: an enemy unit is seen while the tile under its center is visible. It is
    /// the tile that counts, not how far away the unit itself stands.
    /// </summary>
    [Theory]
    [InlineData("11.5", true)]
    [InlineData("11.0", true)]
    [InlineData("11.9375", true)]
    [InlineData("12.0", false)]
    [InlineData("12.5", false)]
    [InlineData("10.75", true)]
    public void AnEnemyUnitIsSeenWhenTheTileUnderItsCenterIsVisible(string x, bool seen)
    {
        var match = Field();

        // A worker sees 6 tiles: from 5.5, tile 11 is the last of the row, its center exactly 6 away.
        match.SpawnWorker(0, "5.5", "5.5");
        var enemy = match.SpawnWorker(1, x, "5.5");

        Assert.Equal(seen, match.Vision.Sees(0, enemy));
        Assert.Equal(seen, match.Vision.Grid.IsVisible(0, match.PositionOf(enemy)));
        match.Run(5);
        Assert.Equal(seen, match.Vision.Sees(0, enemy));
    }

    /// <summary>
    /// A unit that walks out of what its enemy saw last is lost to the enemy in that tick, and one
    /// that walks into it is seen in that tick: who is seen is asked of the grid as it stands.
    /// </summary>
    [Fact]
    public void AnEnemyUnitIsSeenWhileItIsOnVisibleGroundAndNoLonger()
    {
        var match = Field();
        var eye = match.SpawnWorker(0, "5.5", "5.5");
        var walker = match.SpawnWorker(1, "16.5", "5.5");
        match.OrderMove(1, TestMatches.Point("9.5", "5.5"), walker);

        var states = new List<bool>();
        for (var tick = 0; tick < 120; tick++)
        {
            if (tick == 60)
            {
                match.OrderMove(1, TestMatches.Point("16.5", "5.5"), walker);
            }

            match.Run(1);

            // The eye does not move, so the grid is the same all along, and the walker is seen
            // exactly while the center of its tile is within 6 tiles of the eye.
            var position = match.PositionOf(walker);
            var onVisibleGround = position.X.FloorToInt() <= 11;
            Assert.Equal(onVisibleGround, match.Vision.Sees(0, walker));
            Assert.Equal(onVisibleGround, match.Vision.Grid.IsVisible(0, position));
            states.Add(onVisibleGround);

            // Each player sees its own all the while.
            Assert.True(match.Vision.Sees(0, eye));
            Assert.True(match.Vision.Sees(1, walker));
        }

        // Unseen, then seen, then unseen again.
        Assert.False(states[0]);
        Assert.True(states[59]);
        Assert.False(states[^1]);
        Assert.Equal(2, Enumerable.Range(1, states.Count - 1).Count(i => states[i] != states[i - 1]));
    }

    [Fact]
    public void EnemyUnitsOnExploredGroundThatIsNotVisibleAreNotSeen()
    {
        var match = Field();
        var rider = match.Spawn(0, Hippeus, "5.5", "12.5");
        match.OrderMove(0, TestMatches.Point("34.5", "12.5"), rider);
        match.Run(200);

        // A unit of the other player arrives where the rider began.
        match.OrderSpawn(1, Hoplite, "5.5", "12.5");
        match.Run(8);
        var arrived = match.Combatants.EntityAt(match.Combatants.Count - 1);
        Assert.Equal(1, match.Bodies.Get(arrived).Owner);

        Assert.Equal(FogState.Explored, match.Vision.Grid.StateOf(0, 5, 12));
        Assert.False(match.Vision.Sees(0, arrived));
        Assert.True(match.Vision.Sees(1, arrived));
        Assert.False(match.Vision.Sees(1, rider));
    }

    /// <summary>Design 5.3: a player always sees their own units, also one that stands where the player sees nothing yet.</summary>
    [Fact]
    public void APlayerAlwaysSeesItsOwnUnits()
    {
        var match = Field();
        match.Run(1);

        // Made in tick 1. Sight is next worked out in tick 4.
        match.OrderSpawn(1, Hoplite, "20.5", "12.5");
        match.Run(1);
        var unit = match.Combatants.EntityAt(0);

        Assert.False(match.Vision.Grid.IsVisible(1, 20, 12));
        Assert.True(match.Vision.Sees(1, unit));
        Assert.False(match.Vision.Sees(0, unit));

        match.Run(2);
        Assert.False(match.Vision.Grid.IsVisible(1, 20, 12));
        match.Run(1);
        Assert.True(match.Vision.Grid.IsVisible(1, 20, 12));
        VisionAssert.SeesExactly(match.Vision.Grid, 1, (TestMatches.Point("20.5", "12.5"), 8));
    }

    [Fact]
    public void WhatADestroyedUnitSawIsNoLongerVisibleTheNextTimeSightIsWorkedOut()
    {
        var match = Field();
        var worker = match.SpawnWorker(0, "20.5", "12.5");
        match.Run(1);
        Assert.Equal(FogState.Visible, match.Vision.Grid.StateOf(0, 20, 12));

        // It dies in tick 1. Sight is next worked out in tick 4.
        match.SetHitPoints(worker, 0);
        match.Run(1);
        Assert.False(match.IsAlive(worker));
        Assert.Equal(FogState.Visible, match.Vision.Grid.StateOf(0, 20, 12));
        match.Run(2);
        Assert.Equal(FogState.Visible, match.Vision.Grid.StateOf(0, 20, 12));
        match.Run(1);
        Assert.Equal(FogState.Explored, match.Vision.Grid.StateOf(0, 20, 12));
        Assert.Equal(0, match.Vision.Grid.CountVisible(0));
        Assert.Equal(113, match.Vision.Grid.CountExplored(0));
    }

    [Fact]
    public void OnlyWhatStandsOnTheMapAndIsAliveSees()
    {
        var match = Create(TestMaps.Open(40, 24).WithDeposits((10, 10)));
        var worker = match.SpawnWorker(0, "20.5", "12.5");
        var deposit = match.WorkSites.EntityAt(0);

        Assert.True(match.Vision.TryGetSight(worker, out _));
        Assert.False(match.Vision.TryGetSight(deposit, out _));
        Assert.False(match.Vision.TryGetSight(new EntityId(999), out _));
        Assert.False(match.Vision.TryGetSight(EntityId.None, out _));
        Assert.False(match.Vision.Sees(0, deposit));
        Assert.False(match.Vision.Sees(0, new EntityId(999)));

        match.SetHitPoints(worker, 0);
        match.Run(1);
        Assert.False(match.Vision.TryGetSight(worker, out _));
        Assert.False(match.Vision.Sees(0, worker));
    }
}
