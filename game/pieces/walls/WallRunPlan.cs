using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Structures;
using Sim.Walls;

namespace Game;

/// <summary>What a tile of a run of wall is to the order that would lay the run.</summary>
public enum WallRunTile
{
    /// <summary>The order lays a tile of wall here and is paid for it.</summary>
    Laid,

    /// <summary>A tile of the player's own wall stands here already: nothing is laid and nothing paid.</summary>
    Standing,

    /// <summary>No wall can be laid here now. The order leaves the tile out, and it is not paid for.</summary>
    LeftOut,
}

/// <summary>
/// A run of wall as the order for it would be carried out if it were given now (design 2.5):
/// the tiles from the first to the last, which of them would be laid, what the order costs, and
/// whether it would be taken at all.
/// </summary>
/// <remarks>
/// <para>
/// The tiles that would be laid are those the player's own view of the match names
/// (<see cref="Sim.Vision.PlayerView.WallRun"/>), by the rule the order is judged by, and the
/// price is the price of one tile for each of them, so that the preview shows exactly what the
/// order pays. A tile of ground the player has not explored is taken as free: when something
/// stands there, the worker leaves it out when it comes to it, and its price comes back. Whether the player can pay
/// is worked out as the order works it out: against the stockpile, counting what the order
/// that the chosen worker already has would give back.
/// </para>
/// <para>
/// With the development aid that lays walls for the other seat, the plan is of that: every
/// tile of the run with room for a wall, laid for nothing, in or out of anybody's territory.
/// </para>
/// </remarks>
public sealed class WallRunPlan
{
    private readonly List<(int X, int Y)> _tiles = [];
    private readonly List<WallRunTile> _kinds = [];

    private WallRunPlan(int firstX, int firstY, int lastX, int lastY)
    {
        FirstX = firstX;
        FirstY = firstY;
        LastX = lastX;
        LastY = lastY;
    }

    public int FirstX { get; }

    public int FirstY { get; }

    public int LastX { get; }

    public int LastY { get; }

    /// <summary>Every tile of the run, from the first to the last.</summary>
    public IReadOnlyList<(int X, int Y)> Tiles => _tiles;

    /// <summary>What each tile of <see cref="Tiles"/> is to the order.</summary>
    public IReadOnlyList<WallRunTile> Kinds => _kinds;

    /// <summary>The number of tiles the order would lay and be paid for.</summary>
    public int Laid { get; private set; }

    /// <summary>The number of tiles of the run where the player's own wall stands already.</summary>
    public int Standing { get; private set; }

    /// <summary>The number of tiles of the run the order would leave out because no wall can be laid there.</summary>
    public int LeftOut { get; private set; }

    /// <summary>
    /// Why the order would be refused, or <see cref="BuildProblem.None"/> when it would be
    /// taken. The simulation's own answer, and after it
    /// <see cref="BuildProblem.NoWorker"/> and <see cref="BuildProblem.CannotAfford"/>.
    /// </summary>
    public BuildProblem Problem { get; private set; }

    /// <summary>True when the order would be taken.</summary>
    public bool IsAccepted => Problem == BuildProblem.None;

    /// <summary>The Materials the order costs: one tile's for every tile laid.</summary>
    public int Materials { get; private set; }

    /// <summary>The Energy the order costs.</summary>
    public int Energy { get; private set; }

    /// <summary>The Materials the player lacks for the order, counting what the worker's old order gives back.</summary>
    public int LackingMaterials { get; private set; }

    /// <summary>The Energy the player lacks for the order.</summary>
    public int LackingEnergy { get; private set; }

    /// <summary>The seat the development aid lays the run for, or null for a run of the game.</summary>
    public int? AidFor { get; private set; }

    /// <summary>The run from one tile to another as the local player's order for it would be carried out now.</summary>
    /// <param name="view">The view of the match.</param>
    /// <param name="workers">The workers the order would list.</param>
    /// <param name="firstX">The x of the first tile.</param>
    /// <param name="firstY">The y of the first tile.</param>
    /// <param name="lastX">The x of the last tile.</param>
    /// <param name="lastY">The y of the last tile.</param>
    public static WallRunPlan Of(MatchView view, IReadOnlyList<EntityId> workers, int firstX, int firstY, int lastX, int lastY)
    {
        var match = view.Match;
        var player = view.Player;
        var plan = new WallRunPlan(firstX, firstY, lastX, lastY);
        var toLay = new List<(int X, int Y)>();

        // Judged by what the player may know, as the order is: a tile of ground the player has
        // not explored is taken as free, whatever stands there.
        plan.Problem = view.Sight.Seen.WallRun(firstX, firstY, lastX, lastY, toLay);
        plan.Read(toLay, (x, y) => IsWallOf(match, player, x, y));
        if (match.Walls.Wall is not { } wall || plan.Problem != BuildProblem.None)
        {
            return plan;
        }

        plan.Materials = checked(wall.Materials * toLay.Count);
        plan.Energy = checked(wall.Energy * toLay.Count);
        var worker = PickWorker(match, player, workers, toLay[0], toLay[^1]);
        if (worker == EntityId.None)
        {
            plan.Problem = BuildProblem.NoWorker;
            return plan;
        }

        var held = match.BuildOrders.TryGet(worker, out var order) ? (order.Materials, order.Energy) : match.Walls.HeldBy(worker);
        var materials = (match.Stockpiles.Materials(player) / Stockpiles.PerResource) + held.Materials;
        var energy = (match.Stockpiles.Energy(player) / Stockpiles.PerResource) + held.Energy;
        plan.LackingMaterials = (int)Math.Max(0L, plan.Materials - materials);
        plan.LackingEnergy = (int)Math.Max(0L, plan.Energy - energy);
        if (plan.LackingMaterials > 0 || plan.LackingEnergy > 0)
        {
            plan.Problem = BuildProblem.CannotAfford;
        }

        return plan;
    }

    /// <summary>
    /// DEVELOPMENT ONLY. The run from one tile to another as the development aid would lay it
    /// for a seat: a finished tile, for nothing, wherever a wall has room.
    /// </summary>
    public static WallRunPlan ForAid(MatchView view, int seat, int firstX, int firstY, int lastX, int lastY)
    {
        var match = view.Match;
        var plan = new WallRunPlan(firstX, firstY, lastX, lastY) { AidFor = seat };
        var toLay = new List<(int X, int Y)>();
        if (match.Walls.Wall is { } wall)
        {
            var length = WallRun.LengthOf(firstX, firstY, lastX, lastY);
            for (var step = 0; step < length; step++)
            {
                var (x, y) = WallRun.TileAt(firstX, firstY, lastX, lastY, step);
                if (match.HasRoomFor(wall, x, y))
                {
                    toLay.Add((x, y));
                }
            }
        }

        plan.Problem = match.Walls.Wall is null ? BuildProblem.NoStructure : toLay.Count == 0 ? BuildProblem.SiteNotFree : BuildProblem.None;
        plan.Read(toLay, (x, y) => IsWallOf(match, seat, x, y));
        return plan;
    }

    /// <summary>The tiles the order would lay, in the order of the run.</summary>
    public IEnumerable<(int X, int Y)> LaidTiles()
    {
        for (var i = 0; i < _tiles.Count; i++)
        {
            if (_kinds[i] == WallRunTile.Laid)
            {
                yield return _tiles[i];
            }
        }
    }

    private void Read(List<(int X, int Y)> toLay, Func<int, int, bool> standsAlready)
    {
        var laid = new HashSet<(int X, int Y)>(toLay);
        var length = WallRun.LengthOf(FirstX, FirstY, LastX, LastY);
        for (var step = 0; step < length; step++)
        {
            var tile = WallRun.TileAt(FirstX, FirstY, LastX, LastY, step);
            var kind = laid.Contains(tile) ? WallRunTile.Laid
                : standsAlready(tile.X, tile.Y) ? WallRunTile.Standing
                : WallRunTile.LeftOut;
            _tiles.Add(tile);
            _kinds.Add(kind);
            Laid += kind == WallRunTile.Laid ? 1 : 0;
            Standing += kind == WallRunTile.Standing ? 1 : 0;
            LeftOut += kind == WallRunTile.LeftOut ? 1 : 0;
        }
    }

    private static bool IsWallOf(Sim.Setup.Match match, int player, int x, int y)
    {
        var wall = match.WallMap.WallAt(x, y);
        return wall != EntityId.None && match.Bodies.TryGet(wall, out Body body) && body.Owner == player;
    }

    // The worker that would take the order, as the order itself picks it: the nearest to either
    // end of the tiles to lay, of those without an order of their own if there is one.
    private static EntityId PickWorker(Sim.Setup.Match match, int player, IReadOnlyList<EntityId> workers, (int X, int Y) first, (int X, int Y) last)
    {
        var firstTile = new TileSquare(first.X, first.Y, 1);
        var lastTile = new TileSquare(last.X, last.Y, 1);
        var picked = EntityId.None;
        var pickedIsFree = false;
        long nearest = 0;
        foreach (var entity in workers)
        {
            if (!match.World.Entities.IsAlive(entity)
                || !match.Workers.Contains(entity)
                || !match.Bodies.TryGet(entity, out Body body)
                || body.Owner != player)
            {
                continue;
            }

            var isFree = !match.Construction.HasOrder(entity);
            var distance = Math.Min(firstTile.DistanceSquaredRawTo(body.Position), lastTile.DistanceSquaredRawTo(body.Position));
            if (picked == EntityId.None || (isFree && !pickedIsFree) || (isFree == pickedIsFree && distance < nearest))
            {
                (picked, pickedIsFree, nearest) = (entity, isFree, distance);
            }
        }

        return picked;
    }
}
