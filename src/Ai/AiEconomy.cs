using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Production;

namespace Ai;

/// <summary>
/// The economy (design 5.5, item 1): it keeps workers in production up to the count its
/// personality wants in the epoch, divides them between Materials and Energy by what its next
/// purchases still lack, and says when to expand.
/// </summary>
internal sealed class AiEconomy
{
    /// <summary>The orders it keeps in one queue. A short queue leaves the stockpile free for whatever is wanted next.</summary>
    public const int QueueLength = 2;

    /// <summary>
    /// The ticks between two moves of a worker from one resource to the other. Moving costs the
    /// walk, so the division follows the purchases slowly.
    /// </summary>
    public const int RebalanceInterval = 2 * Sim.SimInfo.TicksPerSecond;

    private readonly AiSight _sight;
    private readonly AiPersonalityDef _personality;
    private readonly EntityId[] _one = new EntityId[1];
    private int _nextRebalance;

    public AiEconomy(AiSight sight, AiPersonalityDef personality)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(personality);
        _sight = sight;
        _personality = personality;
    }

    /// <summary>
    /// The workers it wants at Generators, as the last round of thinking worked it out. Zero
    /// before the first round.
    /// </summary>
    public int EnergyWorkersWanted { get; private set; }

    /// <summary>The supply of the workers it has yet to train to reach the most its personality ever keeps.</summary>
    public int SupplyKeptForWorkers
    {
        get
        {
            var missing = _personality.MostWorkers - (_sight.Workers.Count + _sight.WorkersInTraining);
            return Math.Max(0, missing) * _sight.Data.Resources.Worker.Supply;
        }
    }

    /// <summary>
    /// True when it should claim another deposit (design 5.5): the stockpile holds more Materials
    /// than its personality's mark, or a deposit it holds has dropped to the lower rate and its
    /// other deposits have no place left for the workers there.
    /// </summary>
    public bool WantsToExpand
    {
        get
        {
            if (_sight.Materials > _personality.ExpandAtMaterials)
            {
                return true;
            }

            var depleted = false;
            var freeAtFullRate = 0;
            foreach (var site in _sight.Sites)
            {
                if (site.Kind == WorkSiteKind.Deposit && site.IsHeld)
                {
                    depleted |= site.IsDepleted;
                    freeAtFullRate += site.IsDepleted ? 0 : site.FreePlaces;
                }
            }

            return depleted && freeAtFullRate == 0;
        }
    }

    /// <summary>Orders a worker when it has fewer than its personality wants in this epoch.</summary>
    public void TrainWorkers(AiBudget budget, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(commands);

        var missing = _personality.WorkersAt(_sight.Tech.Epoch) - (_sight.Workers.Count + _sight.WorkersInTraining);
        if (missing > 0)
        {
            var worker = _sight.Data.Resources.Worker;
            budget.Want(Math.Min(missing, QueueLength) * worker.Materials, Math.Min(missing, QueueLength) * worker.Energy);
            TrainWorker(budget, commands);
        }
    }

    /// <summary>
    /// Orders a worker into supply that nothing else can use: when the supply that is free is
    /// less than any unit of its army takes, a worker fills it.
    /// </summary>
    /// <param name="smallestUnitSupply">The least supply a unit of its army takes, or 0 when it can train none.</param>
    /// <param name="budget">The round's budget.</param>
    /// <param name="commands">The list its orders are added to.</param>
    public void FillSupply(int smallestUnitSupply, AiBudget budget, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(commands);

        if (smallestUnitSupply > 0 && SupplyKeptForWorkers == 0 && _sight.SupplyFree > 0 && _sight.SupplyFree < smallestUnitSupply)
        {
            TrainWorker(budget, commands);
        }
    }

    /// <summary>
    /// Sends the workers that have no work, and those that wait for a place, to a site with a
    /// free place, and moves one worker from one resource to the other when the division is off. Call it last in a round, when the budget knows every
    /// purchase of the round.
    /// </summary>
    public void AssignWorkers(AiBudget budget, List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(commands);

        var atDeposits = 0;
        var atGenerators = 0;
        var working = 0;
        foreach (var worker in _sight.Workers)
        {
            if (worker.IsBuilding || worker.Entity == _sight.Explorer)
            {
                continue;
            }

            working++;
            if (worker.State != WorkerState.Idle && SiteOf(worker.Site) is { } site)
            {
                if (site.Kind == WorkSiteKind.Generator)
                {
                    atGenerators++;
                }
                else
                {
                    atDeposits++;
                }
            }
        }

        var depositPlaces = 0;
        var generatorPlaces = 0;
        foreach (var site in _sight.Sites)
        {
            if (site.Kind == WorkSiteKind.Generator)
            {
                generatorPlaces += site.Places;
            }
            else if (site.IsHeld)
            {
                depositPlaces += site.Places;
            }
        }

        EnergyWorkersWanted = EnergyShareOf(working, atGenerators, depositPlaces, budget);
        var energyTarget = Math.Min(EnergyWorkersWanted, generatorPlaces);

        var movedOne = false;
        for (var i = 0; i < _sight.Workers.Count; i++)
        {
            var worker = _sight.Workers[i];
            if (worker.State is not (WorkerState.Idle or WorkerState.Waiting) || worker.IsBuilding || worker.Entity == _sight.Explorer)
            {
                continue;
            }

            // One that waits for a place earns nothing: it goes where a place is free, as one without work does.
            var waitsAt = worker.State == WorkerState.Waiting ? SiteOf(worker.Site) : null;
            if (waitsAt is { Kind: WorkSiteKind.Generator })
            {
                atGenerators--;
            }
            else if (waitsAt is not null)
            {
                atDeposits--;
            }

            var toEnergy = atGenerators < energyTarget;
            var index = FindSite(worker, toEnergy ? WorkSiteKind.Generator : WorkSiteKind.Deposit, not: waitsAt?.Entity);
            if (index < 0)
            {
                toEnergy = !toEnergy;
                index = FindSite(worker, toEnergy ? WorkSiteKind.Generator : WorkSiteKind.Deposit, not: waitsAt?.Entity);
            }

            if (index < 0)
            {
                if (waitsAt is { Kind: WorkSiteKind.Generator })
                {
                    atGenerators++;
                }
                else if (waitsAt is not null)
                {
                    atDeposits++;
                }

                continue;
            }

            Send(worker, index, commands);
            movedOne |= waitsAt is not null;
            if (toEnergy)
            {
                atGenerators++;
            }
            else
            {
                atDeposits++;
            }
        }

        // A worker that waited and was moved is the round's move.
        if (!movedOne && _sight.Tick >= _nextRebalance && Rebalance(atGenerators, energyTarget, commands))
        {
            _nextRebalance = _sight.Tick + RebalanceInterval;
        }
    }

    /// <summary>
    /// The workers that should stand at Generators: the workers divided as the Materials and the
    /// Energy that the round's purchases lack, each over what one worker earns of it. Workers
    /// that no deposit has a place for go to Energy as well.
    /// </summary>
    private int EnergyShareOf(int working, int atGenerators, int depositPlaces, AiBudget budget)
    {
        // Worker-seconds of work each resource still needs, up to a common factor.
        var energyWork = (Int128)budget.MissingEnergy * _sight.PaymentOf(Resource.Materials);
        var materialsWork = (Int128)budget.MissingMaterials * _sight.PaymentOf(Resource.Energy);
        var wanted = atGenerators;
        if (energyWork + materialsWork > 0)
        {
            // Rounded half up.
            wanted = (int)(((2 * working * energyWork) + energyWork + materialsWork) / (2 * (energyWork + materialsWork)));
        }

        return Math.Clamp(Math.Max(wanted, working - depositPlaces), 0, working);
    }

    private bool Rebalance(int atGenerators, int energyTarget, List<Command> commands)
    {
        if (atGenerators < energyTarget)
        {
            return Move(WorkSiteKind.Deposit, WorkSiteKind.Generator, fromDepletedOnly: false, commands);
        }

        // One worker too many at the Generators is left there: moving it back and forth costs more than it earns.
        if (atGenerators > energyTarget + 1)
        {
            return Move(WorkSiteKind.Generator, WorkSiteKind.Deposit, fromDepletedOnly: false, commands);
        }

        return Move(WorkSiteKind.Deposit, WorkSiteKind.Deposit, fromDepletedOnly: true, commands);
    }

    /// <summary>
    /// Moves one worker from a site of one kind to a site of another that has a free place: a
    /// worker that is waiting for a place before one that has one, and of those the nearest.
    /// From deposit to deposit it moves only a worker of a depleted deposit, to one that is not.
    /// </summary>
    private bool Move(WorkSiteKind from, WorkSiteKind to, bool fromDepletedOnly, List<Command> commands)
    {
        var best = -1;
        var bestSite = -1;
        var bestWaits = false;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < _sight.Workers.Count; i++)
        {
            var worker = _sight.Workers[i];
            if (worker.IsBuilding
                || worker.Entity == _sight.Explorer
                || worker.State == WorkerState.Idle
                || SiteOf(worker.Site) is not { } site
                || site.Kind != from)
            {
                continue;
            }

            if (fromDepletedOnly && !site.IsDepleted)
            {
                continue;
            }

            var index = FindSite(worker, to, fullRateOnly: fromDepletedOnly);
            if (index < 0)
            {
                continue;
            }

            var waits = worker.State == WorkerState.Waiting;
            var distance = DistanceSquared(worker, _sight.Sites[index]);
            if (best < 0 || (waits && !bestWaits) || (waits == bestWaits && distance < bestDistance))
            {
                best = i;
                bestSite = index;
                bestWaits = waits;
                bestDistance = distance;
            }
        }

        if (best < 0)
        {
            return false;
        }

        Send(_sight.Workers[best], bestSite, commands);
        return true;
    }

    /// <summary>
    /// The site of a kind that a worker should go to, as a position in the sight's list, or -1:
    /// one that pays the player and has a free place, a deposit at the whole rate before a
    /// depleted one, and of those the nearest.
    /// </summary>
    /// <param name="worker">The worker.</param>
    /// <param name="kind">Deposit or Generator.</param>
    /// <param name="fullRateOnly">True to leave out the depleted deposits.</param>
    /// <param name="not">A site to leave out: the one the worker waits at, where no place is free whatever the count says.</param>
    private int FindSite(OwnWorker worker, WorkSiteKind kind, bool fullRateOnly = false, EntityId? not = null)
    {
        var best = -1;
        var bestDepleted = false;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < _sight.Sites.Count; i++)
        {
            var site = _sight.Sites[i];
            if (site.Kind != kind || !site.IsHeld || site.FreePlaces == 0 || (fullRateOnly && site.IsDepleted) || site.Entity == not)
            {
                continue;
            }

            var distance = DistanceSquared(worker, site);
            if (best < 0 || (bestDepleted && !site.IsDepleted) || (bestDepleted == site.IsDepleted && distance < bestDistance))
            {
                best = i;
                bestDepleted = site.IsDepleted;
                bestDistance = distance;
            }
        }

        return best;
    }

    private KnownSite? SiteOf(EntityId entity)
    {
        foreach (var site in _sight.Sites)
        {
            if (site.Entity == entity)
            {
                return site;
            }
        }

        return null;
    }

    private void Send(OwnWorker worker, int siteIndex, List<Command> commands)
    {
        var site = _sight.Sites[siteIndex];
        _one[0] = worker.Entity;
        commands.Add(new Command(CommandType.Gather, _sight.Player, _one, targetEntity: site.Entity));

        // The place is taken for the rest of this round.
        _sight.Sites[siteIndex] = site with { Holders = site.Holders + 1 };
    }

    private void TrainWorker(AiBudget budget, List<Command> commands)
    {
        var worker = _sight.Data.Resources.Worker;
        if (_sight.SupplyFree < worker.Supply)
        {
            return;
        }

        var producer = EntityId.None;
        var shortest = QueueLength;
        foreach (var structure in _sight.Structures)
        {
            if (structure.IsFinished && _sight.Data.StructureOf(structure.Type) is { TrainsWorkers: true })
            {
                var orders = _sight.OrdersAt(structure.Entity);
                if (orders >= 0 && orders < shortest)
                {
                    producer = structure.Entity;
                    shortest = orders;
                }
            }
        }

        if (producer == EntityId.None)
        {
            return;
        }

        if (!budget.Covers(worker.Materials, worker.Energy))
        {
            budget.Hold(worker.Materials, worker.Energy);
            return;
        }

        budget.Pay(worker.Materials, worker.Energy);
        _one[0] = producer;
        commands.Add(new Command(CommandType.Train, _sight.Player, _one, contentId: TrainOrder.Worker));
    }

    private static long DistanceSquared(OwnWorker worker, KnownSite site) =>
        AiSites.DistanceSquared(site.X, site.Y, site.Size, worker.TileX, worker.TileY);
}
