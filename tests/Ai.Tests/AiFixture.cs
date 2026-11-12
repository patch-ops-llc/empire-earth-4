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

    /// <summary>The tile the army gathers on, for the tests of how it fights.</summary>
    public (int X, int Y) Muster { get; init; }

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
            [.. lines.Select(line => new AiLineShare(Line(line.Line), line.Share))],
            Fight());
    }

    /// <summary>
    /// How a personality made for a test fights: the numbers of design 5.5, and what the test
    /// changes of them. Without a reaction delay it acts in the round it finds a reason.
    /// </summary>
    public static AiFightDef Fight(
        string attackRatio = "1.3",
        int assumedEnemyValue = 400,
        string retreatLosses = "0.4",
        int reinforceUnits = 6,
        MarkerKind passage = MarkerKind.OpenField,
        string raidLine = "cavalry_armor",
        int raidParty = 4,
        int reactionTicks = 0)
    {
        Assert.True(DataNumber.TryParse(attackRatio, out var ratio));
        Assert.True(DataNumber.TryParse(retreatLosses, out var losses));
        return new AiFightDef(ratio, assumedEnemyValue, losses, reinforceUnits, passage, Line(raidLine), raidParty, reactionTicks);
    }

    /// <summary>
    /// A response table of one row, in force: the row is of an enemy one epoch ahead, and the
    /// other seat is given the tiers for it, so nothing has to stand on the map. The fixture
    /// needs two players.
    /// </summary>
    public AiTable Responding(AiPersonalityDef personality, AiDoesDef does)
    {
        ArgumentNullException.ThrowIfNull(personality);
        var table = WithRow(personality, does);
        var ahead = Sight.Tech.Epoch + 1;
        Match.Research.SetTiers(1, [ahead, ahead, ahead, 1, 1, 1]);
        Sight.Refresh();
        var intel = new AiIntel(Sight);
        var responses = new AiResponses(Sight, intel, table);
        var made = new AiTable(table, intel, responses);
        made.Judge();
        Assert.True(responses.IsInForce(0));
        return made;
    }

    /// <summary>A personality with a response table of one row, which is of an enemy one epoch ahead.</summary>
    public static AiPersonalityDef WithRow(AiPersonalityDef personality, AiDoesDef does)
    {
        ArgumentNullException.ThrowIfNull(personality);
        var sees = new AiSeesDef([], OnMainLine: false, Line: 0, Units: 0, ArmyCountRatio: null, EpochsAhead: 1);
        return personality with { Scouting = new AiScoutingDef(180 * Sim.SimInfo.TicksPerSecond, [new AiResponseDef([sees], does)]) };
    }

    /// <summary>What a row does that only has it research a track first.</summary>
    public static AiDoesDef Researching(string track, AiResearchUntil until = AiResearchUntil.Tiers, int amount = 1, bool catchUpFirst = false) =>
        new([], new AiResearchResponse(Track(track), until, amount), Passage: null, catchUpFirst);

    /// <summary>What a row does that only raises a line to a share of its army's supply.</summary>
    public static AiDoesDef Raising(string line, string share)
    {
        Assert.True(DataNumber.TryParse(share, out var number));
        return new([new AiSupplyShare(Line(line), number)], Research: null, Passage: null, CatchUpFirst: false);
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
    /// Shows the player the whole map once, as if it had walked all of it: every tile is
    /// explored, and what stands on it now is remembered. Sight is worked out anew after it, so
    /// the player then sees what its units and structures see.
    /// </summary>
    public void ExploreAll()
    {
        var grid = Match.Vision.Grid;
        grid.BeginRefresh();
        grid.Reveal(Player, MapGrid.TileCenter(grid.Width / 2, grid.Height / 2), Sim.Numerics.Fixed.FromInt(grid.Width + grid.Height));
        grid.EndRefresh();
        Match.Vision.Refresh();
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

    /// <summary>Places one unit of a chassis for a player on a tile, has sight worked out anew and reads the match again.</summary>
    public EntityId SpawnAt(int player, string chassis, int x, int y)
    {
        var entity = Match.SpawnChassis(player, Chassis(chassis), MapGrid.TileCenter(x, y));
        Match.Vision.Refresh();
        Sight.Refresh();
        return entity;
    }

    /// <summary>Places units of a chassis for a player on a row of tiles that starts at a tile.</summary>
    public List<EntityId> SpawnRow(int player, string chassis, int count, int x, int y)
    {
        var units = new List<EntityId>();
        for (var i = 0; i < count; i++)
        {
            units.Add(Match.SpawnChassis(player, Chassis(chassis), MapGrid.TileCenter(x + i, y)));
        }

        Match.Vision.Refresh();
        Sight.Refresh();
        return units;
    }

    /// <summary>Places a worker for a player on a tile.</summary>
    public EntityId SpawnWorkerAt(int player, int x, int y)
    {
        var entity = Match.SpawnWorker(player, MapGrid.TileCenter(x, y));
        Match.Vision.Refresh();
        Sight.Refresh();
        return entity;
    }

    /// <summary>Places a finished structure for a player with its lowest tile on a tile.</summary>
    public EntityId PlaceAt(int player, string structure, int x, int y)
    {
        var entity = Match.PlaceStructure(player, Structure(structure), x, y);
        Match.Vision.Refresh();
        Sight.Refresh();
        return entity;
    }

    /// <summary>Takes hit points from something, to none at most. What has none left dies in the next tick.</summary>
    public void Damage(EntityId entity, long hitPoints)
    {
        ref var health = ref Match.Health.ValueAt(Match.Health.IndexOf(entity));
        health.HitPoints = Math.Max(0, health.HitPoints - hitPoints);
        Sight.Refresh();
    }

    /// <summary>Kills units: they die in a tick that runs here, and whoever sees them die is told.</summary>
    public void Kill(params EntityId[] entities)
    {
        foreach (var entity in entities)
        {
            ref var health = ref Match.Health.ValueAt(Match.Health.IndexOf(entity));
            health.HitPoints = 0;
        }

        Run(1);
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

/// <summary>A personality with a response table, what it remembers of the enemy, and the table as it is judged.</summary>
internal sealed record AiTable(AiPersonalityDef Personality, AiIntel Intel, AiResponses Responses)
{
    /// <summary>One round: the memory is brought up to date and the table is judged.</summary>
    public void Judge()
    {
        Intel.Watch();
        Intel.Update();
        Responses.Update();
    }
}
