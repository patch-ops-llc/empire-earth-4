using Sim.Core;
using Sim.Map;

namespace Ai;

/// <summary>
/// The worker it sends to look at ground it has not seen, when it wants to expand and knows no
/// place for an Outpost (design 5.5: it plays under the same fog as a person). One worker at a
/// time: to a deposit it knows of whose surroundings it has not seen, or else to the nearest
/// ground it has not explored. The worker goes back to work when a place is known.
/// </summary>
internal sealed class AiExplorer
{
    /// <summary>A worker this many tiles from a deposit, or nearer, has seen the ground around it.</summary>
    public const int NearDeposit = 3;

    private readonly AiSight _sight;
    private readonly AiSearch _search;
    private readonly List<EntityId> _lookedAt = [];
    private readonly EntityId[] _one = new EntityId[1];
    private bool _hasGoal;
    private int _goalX;
    private int _goalY;
    private int _goalSize;
    private EntityId _goalDeposit;
    private int _deadline;

    public AiExplorer(AiSight sight, AiSearch search)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(search);
        _sight = sight;
        _search = search;
    }

    /// <summary>True for a deposit a worker has been to, or was sent to and did not reach.</summary>
    public bool HasLookedAt(EntityId deposit) => _lookedAt.Contains(deposit);

    /// <param name="wantsToExpand">True while it wants another deposit.</param>
    /// <param name="knowsAPlace">True when it knows where an Outpost can go, so that nothing is left to look for.</param>
    /// <param name="deposit">A deposit it knows of and could claim, whose surroundings it has not been to, if there is one.</param>
    /// <param name="homeX">The middle of its home, from which the nearest unexplored ground is measured.</param>
    /// <param name="homeY">The middle of its home.</param>
    /// <param name="commands">The list its orders are added to.</param>
    public void Think(bool wantsToExpand, bool knowsAPlace, KnownSite? deposit, int homeX, int homeY, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var worker = FindWorker(_sight.Explorer);
        if (worker is null)
        {
            _sight.Explorer = EntityId.None;
            _hasGoal = false;
        }

        if (knowsAPlace)
        {
            Release();
            return;
        }

        if (_hasGoal && worker is { } walking)
        {
            // A walk that was begun is finished, also when the stockpile has dropped below its mark meanwhile:
            // what it sees on the way stays explored, and half a walk would be walked again.
            var reached = _goalDeposit == EntityId.None
                ? _sight.IsExplored(_goalX, _goalY)
                : AiSearch.DistanceToSquare(_goalX, _goalY, _goalSize, walking.TileX, walking.TileY) <= NearDeposit * NearDeposit;
            if (!reached && _sight.Tick < _deadline)
            {
                return;
            }

            if (_goalDeposit != EntityId.None)
            {
                _lookedAt.Add(_goalDeposit);
            }
            else if (!reached)
            {
                _search.GiveUp(_goalX, _goalY);
            }

            _hasGoal = false;
        }

        if (!wantsToExpand)
        {
            Release();
            return;
        }

        if (deposit is { } known)
        {
            _goalDeposit = known.Entity;
            _goalX = known.X;
            _goalY = known.Y;
            _goalSize = known.Size;
        }
        else if (_search.TryFindUnexplored(homeX, homeY, out _goalX, out _goalY))
        {
            _goalDeposit = EntityId.None;
            _goalSize = 1;
        }
        else
        {
            Release();
            return;
        }

        worker ??= FindNearestWorker(_goalX + (_goalSize / 2), _goalY + (_goalSize / 2));
        if (worker is not { } sent)
        {
            return;
        }

        _hasGoal = true;
        _sight.Explorer = sent.Entity;
        _deadline = AiSearch.DeadlineFor(_sight.Tick, AiSearch.DistanceToSquare(_goalX, _goalY, _goalSize, sent.TileX, sent.TileY));
        _one[0] = sent.Entity;
        var point = MapGrid.TileCenter(_goalX + (_goalSize / 2), _goalY + (_goalSize / 2));
        commands.Add(new Command(CommandType.Move, _sight.Player, _one, point.X, point.Y));
    }

    private void Release()
    {
        _sight.Explorer = EntityId.None;
        _hasGoal = false;
    }

    private OwnWorker? FindWorker(EntityId entity)
    {
        if (entity == EntityId.None)
        {
            return null;
        }

        foreach (var worker in _sight.Workers)
        {
            if (worker.Entity == entity)
            {
                // A build order it was given since is carried out: the nearest worker to a site is often this one.
                return worker.IsBuilding ? null : worker;
            }
        }

        return null;
    }

    private OwnWorker? FindNearestWorker(int x, int y)
    {
        OwnWorker? best = null;
        var bestDistance = long.MaxValue;
        foreach (var worker in _sight.Workers)
        {
            var distance = AiSearch.DistanceSquared(worker.TileX, worker.TileY, x, y);
            if (!worker.IsBuilding && distance < bestDistance)
            {
                best = worker;
                bestDistance = distance;
            }
        }

        return best;
    }
}
