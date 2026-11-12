using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Production;

namespace Ai.Tests;

/// <summary>
/// One round of the whole computer player at a time: what it orders in a state of the match, and
/// what the match makes of the orders.
/// </summary>
public class AiPlayerTests
{
    private static readonly int[] FirepowerFirst = [6, 5, 4, 3, 2, 1];

    [Fact]
    public void APersonalityOfOtherDataIsRefused()
    {
        var match = new AiFixture().Match;
        var personality = AiFixture.Personality() with { TrackWeights = [1, 2] };

        Assert.Throws<ArgumentException>(() => new AiPlayer(match, 0, personality));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiPlayer(match, 1, AiFixture.Personality()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AiPlayer(match, -1, AiFixture.Personality()));
    }

    [Fact]
    public void ItsFirstRoundStartsAResearchTrainsAWorkerOrdersAStructureAndSendsItsWorkersToWork()
    {
        var fixture = new AiFixture();
        var personality = AiFixture.Personality(workers: 10, FirepowerFirst, lines: [("line_infantry", 1), ("cavalry_armor", 1)]);
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, personality);
        Assert.Same(personality, ai.Personality);
        Assert.Equal(AiFixture.Player, ai.Player);
        var workers = fixture.Sight.Workers.Count;

        var given = Round(fixture, ai);

        Assert.All(given, command => Assert.Equal(AiFixture.Player, command.Player));
        Assert.Equal(0, Assert.Single(Of(given, CommandType.StartResearch)).ContentId);
        Assert.Equal(TrainOrder.Worker, Assert.Single(Of(given, CommandType.Train)).ContentId);
        Assert.Equal(workers, Of(given, CommandType.Gather).Count);

        // The start's stockpile covers the Barracks after those, and not the Works.
        Assert.Equal(AiFixture.Structure("barracks").Index, Assert.Single(Of(given, CommandType.Build)).ContentId);

        // The match took every order, and none of them undid another.
        Assert.True(fixture.Sight.IsResearching(0));
        Assert.Equal(1, fixture.Sight.WorkersInTraining);
        Assert.Equal(AiFixture.Structure("barracks").Index, Assert.Single(fixture.Sight.BuildOrders).Type);
        Assert.Equal(1, fixture.Sight.Workers.Count(worker => worker.IsBuilding));
        Assert.Equal(workers - 1, fixture.Sight.Workers.Count(worker => !worker.IsBuilding && worker.State == WorkerState.Walking));
    }

    /// <summary>
    /// A worker that takes a build order leaves its work for it, and an order to gather ends a
    /// build order. So the orders to gather of a round go first: the structure is still built
    /// when the round also moves the worker that takes it.
    /// </summary>
    [Fact]
    public void TheOrdersToGatherOfARoundComeBeforeItsOrdersToBuild()
    {
        var fixture = new AiFixture();
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality(lines: [("line_infantry", 1)]));

        for (var round = 0; round < 40; round++)
        {
            var given = Round(fixture, ai);
            var firstBuild = given.FindIndex(command => command.Type == CommandType.Build);
            var lastGather = given.FindLastIndex(command => command.Type == CommandType.Gather);
            Assert.True(firstBuild < 0 || lastGather < firstBuild, $"Round {round} orders to gather after it orders to build.");
        }
    }

    /// <summary>
    /// With no Energy in the stockpile, everything its next research lacks is Energy, so it wants
    /// every worker at a Generator, and builds Generators until they have places for them.
    /// </summary>
    [Fact]
    public void ItBuildsGeneratorsUntilTheyHavePlacesForTheWorkersItWantsThere()
    {
        var fixture = new AiFixture();
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality(weights: FirepowerFirst));
        var generator = AiFixture.Structure("generator");
        var workers = fixture.Sight.Workers.Count;
        var places = fixture.Data.Resources.Energy.WorkersPerGenerator;

        // The start's stockpile covers its first research, so nothing is missing yet.
        Assert.Empty(Builds(Round(fixture, ai), generator));

        var ordered = 0;
        for (var round = 0; round < 10; round++)
        {
            fixture.SetStock(generator.Materials, 0);
            ordered += Builds(Round(fixture, ai), generator).Count;
        }

        Assert.Equal((workers + places - 1) / places, ordered);
        Assert.Equal(ordered, fixture.Sight.BuildOrders.Count + fixture.Sight.Structures.Count(structure => structure.Type == generator.Index));
        Assert.All(fixture.Sight.BuildOrders, order => Assert.True(fixture.Sight.HoldsTiles(order.X, order.Y, order.Size)));
    }

    /// <summary>The design does not say when it builds an Academy: when a slot is all it lacks for more research.</summary>
    [Fact]
    public void ItBuildsAnAcademyWhenEverySlotIsBusyAndItCouldPayForMoreResearch()
    {
        var fixture = new AiFixture();
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality(weights: FirepowerFirst));
        var academy = AiFixture.Structure("academy");

        // Its slot is free, and it uses it.
        var first = Round(fixture, ai);
        Assert.Single(Of(first, CommandType.StartResearch));
        Assert.Empty(Of(first, CommandType.Build));

        // The slot is busy and it cannot pay for another research: an Academy would be of no use.
        var next = fixture.Sight.Tech.Research[1].Price!.Value;
        fixture.SetStock(AiFixture.Plenty, next.Energy - 1);
        Assert.Empty(Builds(Round(fixture, ai), academy));

        fixture.SetStock(AiFixture.Plenty, next.Energy + academy.Energy);
        Assert.Single(Builds(Round(fixture, ai), academy));

        // While that one is under way it orders no other.
        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);
        Assert.Empty(Builds(Round(fixture, ai), academy));
    }

    [Fact]
    public void ItClaimsTheNearestDepositItDoesNotHoldWhenItsStockpileIsAboveItsMark()
    {
        var fixture = new AiFixture();
        var outpost = AiFixture.Structure("outpost");
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality(expandAt: 600));
        var (homeX, homeY, _) = fixture.Home;
        var nearest = fixture.Sight.Sites
            .Where(site => site.Kind == WorkSiteKind.Deposit && !site.IsHeld)
            .MinBy(site => AiSites.DistanceSquared(site.X, site.Y, site.Size, homeX, homeY));

        // At the mark it stays at home. Its workers have not reached a deposit yet, so nothing is added.
        fixture.SetStock(600, AiFixture.Plenty);
        Assert.Empty(Builds(Round(fixture, ai), outpost));

        fixture.SetStock(601, AiFixture.Plenty);
        Assert.Single(Builds(Round(fixture, ai), outpost));

        var order = fixture.Sight.BuildOrders.Single(candidate => candidate.Type == outpost.Index);
        var territory = Territory.Around(AiFixture.Player, order.X, order.Y, order.Size, outpost.TerritoryRadius!.Value);
        Assert.True(territory.Contains(nearest.X, nearest.Y));
        Assert.True(territory.Contains(nearest.X + nearest.Size - 1, nearest.Y + nearest.Size - 1));

        // One Outpost at a time.
        fixture.SetStock(AiFixture.Plenty, AiFixture.Plenty);
        Assert.Empty(Builds(Round(fixture, ai), outpost));
    }

    [Fact]
    public void ItBuildsTheStructureOfEachOfItsLinesOnce()
    {
        var barracks = AiFixture.Structure("barracks").Index;
        var works = AiFixture.Structure("works").Index;
        Assert.Empty(AiFixture.Structure("academy").Trains);

        // Both lines are trained at the Barracks.
        Assert.Equal([barracks], Built(AiFixture.Personality(lines: [("line_infantry", 1), ("guard", 1)])));

        // One at the Barracks and one at the Works, in the order of the file.
        Assert.Equal([works, barracks], Built(AiFixture.Personality(lines: [("artillery", 2), ("guard", 1)])));

        // No lines, no structures.
        Assert.Equal([], Built(AiFixture.Personality()));

        static List<int> Built(AiPersonalityDef personality)
        {
            var fixture = new AiFixture();
            var ai = new AiPlayer(fixture.Match, AiFixture.Player, personality);
            var built = new List<int>();
            for (var round = 0; round < 4; round++)
            {
                fixture.SetStock(1000, 1000);
                built.AddRange(Of(Round(fixture, ai), CommandType.Build).Select(command => command.ContentId));
            }

            // It builds an Academy as well, which is not a line's.
            var producers = fixture.Data.Structures.Where(structure => structure.Trains.Count > 0).Select(structure => structure.Index).ToList();
            return [.. built.Where(producers.Contains)];
        }
    }

    /// <summary>With the queues it has at two orders each, it has resources for more than they can train.</summary>
    [Fact]
    public void ItBuildsAnotherStructureForALineWhenEveryQueueItHasIsFull()
    {
        var fixture = new AiFixture();
        var barracks = AiFixture.Structure("barracks");
        fixture.Place("barracks");
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality(lines: [("line_infantry", 1)]));
        var research = fixture.Sight.Tech.Research[0].Price!.Value;
        var unit = fixture.Sight.Tech.Blueprints[AiFixture.Chassis("peltast")];

        // The first round fills the queue, which was empty when it looked.
        fixture.SetStock(research.Materials + (AiEconomy.QueueLength * unit.Materials), research.Energy + (AiEconomy.QueueLength * unit.Energy));
        var first = Round(fixture, ai);
        Assert.Equal(AiEconomy.QueueLength, Of(first, CommandType.Train).Count);
        Assert.Empty(Builds(first, barracks));

        fixture.SetStock(barracks.Materials, barracks.Energy);
        Assert.Single(Builds(Round(fixture, ai), barracks));

        // And no third while the second is under way.
        fixture.SetStock(barracks.Materials, barracks.Energy);
        Assert.Empty(Builds(Round(fixture, ai), barracks));
    }

    /// <summary>New units leave the door of the structure, so that the next ones have room to appear.</summary>
    [Fact]
    public void AStructureThatTrainsItsArmyGetsARallyPointOnce()
    {
        var fixture = new AiFixture();
        var barracks = fixture.Place("barracks");
        var ai = new AiPlayer(fixture.Match, AiFixture.Player, AiFixture.Personality());
        var (homeX, homeY, _) = fixture.Home;

        var given = Round(fixture, ai);

        var order = Assert.Single(Of(given, CommandType.SetRallyPoint));
        Assert.Equal([barracks], order.Entities.ToArray());
        var tileX = order.TargetX.FloorToInt();
        var tileY = order.TargetY.FloorToInt();
        Assert.True(fixture.Match.Grid.IsPassable(tileX, tileY));
        Assert.True(fixture.Sight.HoldsTiles(tileX, tileY, 1));
        Assert.True(fixture.Sight.HasRallyPoint(barracks));

        // Away from the Capitol, on the side of the middle of the map.
        var centerX = fixture.Match.Grid.Width / 2;
        var centerY = fixture.Match.Grid.Height / 2;
        Assert.True(Square(tileX - centerX) + Square(tileY - centerY) < Square(homeX - centerX) + Square(homeY - centerY));
        Assert.True(Square(tileX - homeX) + Square(tileY - homeY) >= Square(6));

        Assert.Empty(Of(Round(fixture, ai), CommandType.SetRallyPoint));

        static long Square(long value) => value * value;
    }

    [Fact]
    public void TwoComputerPlayersOfOneMatchOrderOnlyTheirOwn()
    {
        var run = new AiMatch(GameFiles.Data, "ridge", playerCount: 2);
        run.Add(0, "swarm");
        run.Add(1, "elite");

        for (var tick = 0; tick < 60 * Sim.SimInfo.TicksPerSecond; tick++)
        {
            run.Tick();
        }

        Assert.Equal(0, run.Match.Simulation.DroppedCommandCount);
        Assert.Contains(run.Given, given => given.Command.Player == 0);
        Assert.Contains(run.Given, given => given.Command.Player == 1);
        Assert.All(run.Given, given =>
        {
            foreach (var entity in given.Command.Entities)
            {
                Assert.Equal(given.Command.Player, run.Match.Bodies.Get(entity).Owner);
            }
        });
    }

    /// <summary>
    /// Runs the match up to the computer player's next round, has the match carry out the orders
    /// of that round, and returns them.
    /// </summary>
    private static List<Command> Round(AiFixture fixture, AiPlayer ai)
    {
        fixture.Run(AiPlayer.ThinkInterval);
        Assert.Empty(fixture.Commands);
        ai.Think(fixture.Commands);
        var given = fixture.Commands.ToList();
        fixture.Carry();
        return given;
    }

    private static List<Command> Of(List<Command> commands, CommandType type) => [.. commands.Where(command => command.Type == type)];

    /// <summary>The orders to build one structure.</summary>
    private static List<Command> Builds(List<Command> commands, StructureDef structure) =>
        [.. commands.Where(command => command.Type == CommandType.Build && command.ContentId == structure.Index)];
}
