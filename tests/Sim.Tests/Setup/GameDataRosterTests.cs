using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Production;
using Sim.Research;
using Sim.Setup;
using Sim.Tests.Construction;
using Sim.Tests.Production;

namespace Sim.Tests.Setup;

/// <summary>
/// A match on the real <c>game/data</c>, from its real start on its real map, in which a player
/// places every structure and trains every chassis that <c>rules.jsonc</c> says a match is played
/// with (design 6), by the commands a player gives: build orders, research and orders to train.
/// </summary>
/// <remarks>
/// The tests pin no number of the data, so they hold whatever it is tuned to. The stockpile alone
/// is topped up by hand, since earning is not what is asked here.
/// </remarks>
public class GameDataRosterTests
{
    private const int Player = 0;

    private static readonly Lazy<GameData> Loaded = new(() =>
    {
        var result = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        return result.Data;
    });

    private static GameData Data => Loaded.Value;

    /// <summary>
    /// Every structure of the match roster but the wall is placed by a build order of a starting
    /// worker inside the starting territory and stands finished after its build time. One that a
    /// track unlocks is placed once the tracks are researched. A wall is laid by the wall order
    /// (design 2.5), which is a piece of its own.
    /// </summary>
    [Fact]
    public void EveryStructureAMatchIsPlayedWithIsBuiltByAWorker()
    {
        var match = Start();
        var built = new List<string>();

        foreach (var index in Data.Rules.Match.Structures!)
        {
            var structure = Data.StructureOf(index)!;
            if (structure.Wall is not null)
            {
                continue;
            }

            if (match.Construction.CheckStructure(Player, structure.Index) == BuildProblem.Locked)
            {
                ResearchEveryTrackToTheCap(match);
            }

            var entity = Build(match, structure);

            Assert.True(match.Construction.IsFinished(entity), $"The {structure.Id} is not finished after its build time.");
            Assert.Equal(structure.Index, match.Structures.Get(entity).Type);
            built.Add(structure.Id);
        }

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(Data.Rules.Match.Structures.Count - 1, built.Count);
    }

    /// <summary>
    /// At the start a player trains the chassis of epoch 1 of every line and no other. With every
    /// track researched to the epoch cap, by research orders, each chassis of the match roster is
    /// ordered at the structure its line is built at and appears.
    /// </summary>
    [Fact]
    public void EveryChassisAMatchIsPlayedWithIsTrainedOnceItsTiersAreResearched()
    {
        var match = Start();
        var cap = Data.Rules.Match.EpochCap;
        var roster = Data.Chassis.Where(chassis => Data.Rules.Match.Lines!.Contains(chassis.Line) && chassis.Epoch <= cap).ToList();
        Assert.Equal(Data.Rules.Match.Lines!.Count * cap, roster.Count);

        var trainedAt = new Dictionary<int, EntityId>();
        foreach (var line in Data.Rules.Match.Lines.Select(index => Data.LineOf(index)!))
        {
            if (!trainedAt.ContainsKey(line.BuiltAt))
            {
                trainedAt[line.BuiltAt] = Build(match, Data.StructureOf(line.BuiltAt)!);
            }
        }

        foreach (var chassis in roster)
        {
            var expected = chassis.Epoch == Data.Rules.Match.StartEpoch ? TrainCheck.Allowed : TrainCheck.Locked;
            Assert.Equal(expected, match.Production.CheckTrain(Player, trainedAt[Data.LineOf(chassis.Line)!.BuiltAt], chassis.Index));
        }

        ResearchEveryTrackToTheCap(match);

        foreach (var chassis in roster)
        {
            var structure = trainedAt[Data.LineOf(chassis.Line)!.BuiltAt];
            Assert.Equal(TrainCheck.Allowed, match.Production.CheckTrain(Player, structure, chassis.Index));

            match.OrderTrain(Player, structure, chassis.Index);
            match.RunUntilAppeared(limit: 20_000);

            var unit = Assert.Single(match.Appeared());
            Assert.Equal(chassis.Index, match.Combatants.Get(unit).Chassis);
            Assert.Equal(Player, match.Bodies.Get(unit).Owner);
        }

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(roster.Count, match.Combatants.Count);
    }

    /// <summary>A match of two players on the first map of the data, as a match starts, with a full stockpile for the first.</summary>
    private static Match Start()
    {
        var match = MatchSetup.Create(Data, Data.Maps[0], 1, [new(0), new(1)]);
        match.MakeRich(Player);
        return match;
    }

    /// <summary>
    /// Has the player's first worker build a structure on a free site of the starting territory
    /// and runs the match until its build time is over.
    /// </summary>
    private static EntityId Build(Match match, StructureDef structure)
    {
        Assert.Equal(BuildProblem.None, match.Construction.CheckStructure(Player, structure.Index));
        var worker = match.Workers.EntityAt(0);
        Assert.Equal(Player, match.Bodies.Get(worker).Owner);
        var (x, y) = FreeSite(match, structure);

        match.OrderBuild(Player, structure.Id, x, y, worker);
        match.RunUntilOrderEnds(worker, limit: 4000);
        var entity = match.StructureAt(x, y);
        Assert.True(entity != EntityId.None, $"No foundation of the {structure.Id} was laid at ({x}, {y}).");

        match.Simulation.Run(structure.BuildTicks);
        return entity;
    }

    /// <summary>
    /// A site near the start with a tile of open ground left on every side of the footprint, so
    /// that what is built there leaves room to walk and for units to appear.
    /// </summary>
    private static (int X, int Y) FreeSite(Match match, StructureDef structure)
    {
        var start = match.Map.Starts[Player];
        var size = structure.Footprint;
        for (var reach = 0; reach <= 12; reach++)
        {
            for (var y = start.Y - reach; y <= start.Y + reach; y++)
            {
                for (var x = start.X - reach; x <= start.X + reach; x++)
                {
                    if (Math.Max(Math.Abs(x - start.X), Math.Abs(y - start.Y)) == reach
                        && match.Construction.CheckSite(Player, structure, x, y) == BuildProblem.None
                        && HasOpenGroundRound(match, x, y, size))
                    {
                        return (x, y);
                    }
                }
            }
        }

        throw new InvalidOperationException($"There is no free site for the {structure.Id} near the start.");
    }

    private static bool HasOpenGroundRound(Match match, int x, int y, int size)
    {
        for (var tileY = y - 1; tileY <= y + size; tileY++)
        {
            for (var tileX = x - 1; tileX <= x + size; tileX++)
            {
                if (!match.Grid.IsPassable(tileX, tileY))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Starts whatever research is allowed, one after another, until none is, and checks that every track is at the cap.</summary>
    private static void ResearchEveryTrackToTheCap(Match match)
    {
        var tracks = Data.Tracks.Count;
        var cap = Data.Rules.Match.EpochCap;
        for (var research = 0; research < tracks * cap; research++)
        {
            var track = Enumerable.Range(0, tracks).FirstOrDefault(track => match.Research.CheckStart(Player, track) == ResearchStartCheck.Allowed, -1);
            if (track < 0)
            {
                break;
            }

            var before = match.Research.State.Tier(Player, track);
            match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.StartResearch, Player, contentId: track));
            for (var ticks = 0; ticks < 40_000 && match.Research.State.Tier(Player, track) == before; ticks++)
            {
                match.Simulation.Tick();
            }

            Assert.Equal(before + 1, match.Research.State.Tier(Player, track));
        }

        Assert.All(match.Research.State.Tiers(Player).ToArray(), tier => Assert.Equal(cap, tier));
    }
}
