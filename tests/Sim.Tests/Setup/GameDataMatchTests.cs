using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Tests.Setup;

/// <summary>A match built from the real <c>game/data</c>, read from disk through the loader.</summary>
public class GameDataMatchTests
{
    private static readonly Lazy<GameData> Loaded = new(() =>
    {
        var result = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        return result.Data;
    });

    [Fact]
    public void EveryMapOfTheGameStartsAMatch()
    {
        var data = Loaded.Value;
        Assert.NotEmpty(data.Maps);

        foreach (var map in data.Maps)
        {
            var match = MatchSetup.Create(data, map, 1, [new(0), new(1)]);
            var workers = data.Rules.Start.Workers;
            var footprint = data.Rules.Map.StartFootprint;

            Assert.Equal(map.Width, match.Grid.Width);
            Assert.Equal(map.Height, match.Grid.Height);
            Assert.Equal(2 * workers, match.Bodies.Count);
            Assert.Equal(2 * workers, match.Movers.Count);

            for (var i = 0; i < match.Bodies.Count; i++)
            {
                var body = match.Bodies.ValueAt(i);
                var start = map.Starts[body.Owner];
                var tileX = body.Position.X.FloorToInt();
                var tileY = body.Position.Y.FloorToInt();

                Assert.Equal(i / workers, body.Owner);
                Assert.Equal(TerrainKind.Open, map.TerrainAt(tileX, tileY));
                Assert.False(
                    new TileRect(start.X, start.Y, footprint, footprint).Overlaps(new TileRect(tileX, tileY, 1, 1)),
                    $"A worker of player {body.Owner} stands on the Capitol's tiles in map '{map.Id}'.");

                // Beside the Capitol, and so inside its territory.
                Assert.InRange(tileX, start.X - 2, start.X + footprint + 1);
                Assert.InRange(tileY, start.Y - 2, start.Y + footprint + 1);

                // Design 5.2: both players start alike, as mirror images.
                var mirror = match.Bodies.ValueAt((i + workers) % (2 * workers)).Position;
                var expected = map.Symmetry == MapSymmetry.MirrorX
                    ? new FixedVector2(Fixed.FromInt(map.Width) - body.Position.X, body.Position.Y)
                    : new FixedVector2(body.Position.X, Fixed.FromInt(map.Height) - body.Position.Y);
                Assert.Equal(expected, mirror);
            }
        }
    }

    /// <summary>On the map <c>ridge</c> the six workers stand in the row below the 4 by 4 Capitol.</summary>
    [Fact]
    public void OnRidgeTheWorkersStandInARowBelowTheCapitol()
    {
        var data = Loaded.Value;
        var ridge = data.Maps.Single(map => map.Id == "ridge");

        var match = MatchSetup.Create(data, ridge, 1, [new(0), new(1)]);

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(MapGrid.TileCenter(9 + i, 50), match.Bodies.ValueAt(i).Position);
            Assert.Equal(MapGrid.TileCenter(86 - i, 50), match.Bodies.ValueAt(6 + i).Position);
        }
    }

    /// <summary>
    /// Design 4.1 and 5.3: on every map of the game each player holds the main deposits from the
    /// start, and the starting workers can walk to one and earn the design's 45 a minute each.
    /// </summary>
    [Fact]
    public void TheStartingWorkersOfEveryMapEarnAtAMainDeposit()
    {
        var data = Loaded.Value;
        var workers = data.Rules.Start.Workers;
        Assert.True(workers <= data.Resources.Materials.WorkersPerDeposit);

        foreach (var map in data.Maps)
        {
            var match = MatchSetup.Create(data, map, 1, [new(0), new(1)]);
            Assert.Equal(map.Deposits.Count, match.WorkSites.Count);

            for (var player = 0; player < 2; player++)
            {
                var held = new List<EntityId>();
                for (var index = 0; index < match.WorkSites.Count; index++)
                {
                    var site = match.WorkSites.ValueAt(index);
                    if (Territory.Holds(match.Territories, match.World.Entities, player, site.X, site.Y, site.Size))
                    {
                        held.Add(match.WorkSites.EntityAt(index));
                    }
                }

                Assert.Equal(data.Rules.Map.MainDepositsPerPlayer, held.Count);

                var own = new EntityId[workers];
                for (var i = 0; i < workers; i++)
                {
                    own[i] = match.Bodies.EntityAt((player * workers) + i);
                }

                match.Simulation.Commands.Enqueue(0, new Command(CommandType.Gather, player, own, targetEntity: held[0]));
            }

            // Long enough for the walk, and then one whole minute of work.
            match.Simulation.Run(600);
            Assert.Equal(0, match.Simulation.DroppedCommandCount);
            for (var index = 0; index < match.Workers.Count; index++)
            {
                Assert.Equal(WorkerState.Working, match.Workers.ValueAt(index).State);
            }

            long[] before = [match.Stockpiles.Materials(0), match.Stockpiles.Materials(1)];
            match.Simulation.Run(60 * SimInfo.TicksPerSecond);

            var aMinute = workers * 45 * Stockpiles.PerResource;
            Assert.Equal(aMinute, match.Stockpiles.Materials(0) - before[0]);
            Assert.Equal(aMinute, match.Stockpiles.Materials(1) - before[1]);
            Assert.Equal(data.Rules.Start.Energy * Stockpiles.PerResource, match.Stockpiles.Energy(0));
        }
    }
}

/// <summary>
/// Reads <c>game/data</c> from the repository, found from wherever the tests run. Files and folders
/// whose names start with a dot are not data, as for the tools.
/// </summary>
internal static class GameDataOnDisk
{
    public static InMemoryDataFiles Files()
    {
        var root = Path.Combine(FindRepositoryRoot(), "game", "data");
        var files = new InMemoryDataFiles();
        Collect(new DirectoryInfo(root), "", files);
        return files;
    }

    private static void Collect(DirectoryInfo folder, string prefix, InMemoryDataFiles files)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            if (entry.Name.StartsWith('.'))
            {
                continue;
            }

            if (entry is DirectoryInfo subfolder)
            {
                Collect(subfolder, $"{prefix}{entry.Name}/", files);
            }
            else
            {
                files.Set(prefix + entry.Name, File.ReadAllBytes(entry.FullName));
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "global.json")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"No global.json above {AppContext.BaseDirectory}.");
    }
}
