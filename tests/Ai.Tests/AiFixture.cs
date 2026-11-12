using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;

namespace Ai.Tests;

/// <summary>
/// A match on Ridge with the game's own data, and what one part of the computer player needs to
/// make one choice in it: the sight of player 0, a budget and a list for its orders. The tests
/// put the match in the state the choice is about and look at the orders.
/// </summary>
internal sealed class AiFixture
{
    public const int Player = 0;

    /// <summary>More than any test spends.</summary>
    public const int Plenty = 100_000;

    private readonly HashSet<(int X, int Y)> _spawned = [];

    public AiFixture(int players = 1)
    {
        var data = GameFiles.Data;
        var map = data.Maps.Single(candidate => candidate.Id == "ridge");
        Match = MatchSetup.Create(data, map, seed: 1, [.. Enumerable.Range(0, players).Select(start => new PlayerSetup(start))]);
        Sight = new AiSight(Match, Player);
        Sight.Refresh();
    }

    public Match Match { get; }

    public GameData Data => Match.Data;

    public AiSight Sight { get; }

    public AiBudget Budget { get; } = new();

    public List<Command> Commands { get; } = [];

    /// <summary>The middle tile of the player's Capitol and the radius of its territory.</summary>
    public (int X, int Y, int Radius) Home
    {
        get
        {
            var start = Data.StartStructure;
            var capitol = Sight.Structures.Single(structure => structure.Type == start.Index);
            return (capitol.X + (capitol.Size / 2), capitol.Y + (capitol.Size / 2), start.TerritoryRadius!.Value);
        }
    }

    /// <summary>
    /// A personality made for a test. The weights are those of the tracks in the order of the
    /// data: Firepower, Protection, Mobility, Manufacturing, Logistics, Extraction.
    /// </summary>
    public static AiPersonalityDef Personality(int workers = 6, int[]? weights = null, int expandAt = Plenty, params (string Line, int Share)[] lines)
    {
        var data = GameFiles.Data;
        return new AiPersonalityDef(
            "test",
            "Test",
            [new AiWorkerTarget(1, workers)],
            expandAt,
            weights ?? [.. Enumerable.Repeat(1, data.Tracks.Count)],
            [.. lines.Select(line => new AiLineShare(Line(line.Line), line.Share))]);
    }

    public static int Line(string id) => GameFiles.Data.Lines.Single(line => line.Id == id).Index;

    public static int Track(string id) => GameFiles.Data.Tracks.Select((track, index) => (track, index)).Single(pair => pair.track.Id == id).index;

    public static int Chassis(string id) => GameFiles.Data.Chassis.Single(chassis => chassis.Id == id).Index;

    public static StructureDef Structure(string id) => GameFiles.Data.StructureOf(id);

    /// <summary>Starts a round with a stockpile that covers everything.</summary>
    public AiBudget Rich() => With(Plenty, Plenty);

    /// <summary>Starts a round with a stockpile of the test's choosing. The match's own stockpile stays as it is.</summary>
    public AiBudget With(int materials, int energy)
    {
        Budget.Reset(materials, energy);
        return Budget;
    }

    /// <summary>Gives the player's stockpile in the match exactly these whole amounts.</summary>
    public void SetStock(int materials, int energy)
    {
        var stock = Match.Stockpiles;
        Assert.True(stock.TryPay(Player, (int)(stock.Materials(Player) / Stockpiles.PerResource), (int)(stock.Energy(Player) / Stockpiles.PerResource)));
        stock.Add(Player, materials, energy);
        Sight.Refresh();
    }

    /// <summary>
    /// Places a finished structure of the player's near its Capitol, with open ground around it,
    /// and reads the match again.
    /// </summary>
    public EntityId Place(string structure)
    {
        var def = Structure(structure);
        var (homeX, homeY, radius) = Home;
        Assert.True(new AiSites(Sight).TryFind(def, homeX, homeY, radius, homeX, homeY, alsoNeeds: null, out var x, out var y));
        var entity = Match.PlaceStructure(Player, def, x, y);
        Sight.Refresh();
        return entity;
    }

    /// <summary>Places units of a chassis for the player on open tiles far from its Capitol, and reads the match again.</summary>
    public void Spawn(string chassis, int count)
    {
        var index = Chassis(chassis);
        var (homeX, homeY, radius) = Home;
        for (var y = 0; y < Match.Grid.Height && count > 0; y++)
        {
            for (var x = 0; x < Match.Grid.Width && count > 0; x++)
            {
                if (Math.Max(Math.Abs(x - homeX), Math.Abs(y - homeY)) > radius + 4
                    && Match.Grid.IsPassable(x, y)
                    && !_spawned.Contains((x, y)))
                {
                    Match.SpawnChassis(Player, index, MapGrid.TileCenter(x, y));
                    _spawned.Add((x, y));
                    count--;
                }
            }
        }

        Assert.Equal(0, count);
        Sight.Refresh();
    }

    /// <summary>Places workers for the player on open tiles of its territory, and reads the match again.</summary>
    public void SpawnWorkers(int count)
    {
        var (homeX, homeY, radius) = Home;
        for (var y = homeY - radius; y <= homeY + radius && count > 0; y++)
        {
            for (var x = homeX - radius; x <= homeX + radius && count > 0; x++)
            {
                if (Sight.HoldsTiles(x, y, 1) && IsOpenAround(x, y) && _spawned.Add((x, y)))
                {
                    Match.SpawnWorker(Player, MapGrid.TileCenter(x, y));
                    count--;
                }
            }
        }

        Assert.Equal(0, count);
        Sight.Refresh();

        // Away from structures and deposits, so that no worker stands on a place of a site.
        bool IsOpenAround(int x, int y)
        {
            for (var tileY = y - 2; tileY <= y + 2; tileY++)
            {
                for (var tileX = x - 2; tileX <= x + 2; tileX++)
                {
                    if (!Match.Grid.IsPassable(tileX, tileY))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Has the match carry out the orders in <see cref="Commands"/> in one tick, as with a command
    /// delay of 1, then empties the list and reads the match again.
    /// </summary>
    public void Carry()
    {
        foreach (var command in Commands)
        {
            Match.Simulation.Commands.Enqueue(Match.World.Tick, command);
        }

        Commands.Clear();
        Run(1);
    }

    /// <summary>Runs ticks and reads the match again.</summary>
    public void Run(int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            Match.Simulation.Tick();
        }

        Assert.Equal(0, Match.Simulation.DroppedCommandCount);
        Sight.Refresh();
    }

    /// <summary>The orders of one type in <see cref="Commands"/>, in the order they were given.</summary>
    public List<Command> Orders(CommandType type) => [.. Commands.Where(command => command.Type == type)];

    public KnownSite SiteOf(EntityId entity) => Sight.Sites.Single(site => site.Entity == entity);
}
