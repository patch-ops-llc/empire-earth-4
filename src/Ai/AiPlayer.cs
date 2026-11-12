using Sim;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;

namespace Ai;

/// <summary>
/// The computer player of one seat of a match (design 5.5): it reads the match as its player
/// knows it and gives orders as commands, and it changes the match in no other way.
/// </summary>
/// <remarks>
/// <para>
/// <b>How a match uses it.</b> Whoever runs the match makes one of these for every seat a
/// computer plays, and after every tick calls <see cref="Think"/> with a list. The commands it
/// adds are that seat's orders, and go where a person's orders go: to the host's session, which
/// schedules them and sends them to every peer. It runs on the host only. The other peers get
/// its commands and need neither this class nor its personality.
/// </para>
/// <para>
/// <b>When it thinks.</b> <see cref="Think"/> may be called as often as wished. It does something
/// once every <see cref="ThinkInterval"/> ticks and nothing in between, which is longer than the
/// longest command delay, so the orders of one round have been carried out before the next round
/// reads the match.
/// </para>
/// <para>
/// <b>Determinism.</b> What it orders depends only on the state of the match in the tick it
/// thinks in: it uses no clock, no random numbers and no floating point. Two matches with the
/// same state give the same commands.
/// </para>
/// </remarks>
public sealed class AiPlayer
{
    /// <summary>The ticks between two rounds of thinking: half a second.</summary>
    public const int ThinkInterval = SimInfo.TicksPerSecond / 2;

    /// <summary>The most structures of one type it builds to train its army.</summary>
    public const int MostProducers = 3;

    private readonly AiSight _sight;
    private readonly AiBudget _budget = new();
    private readonly AiResearch _research;
    private readonly AiEconomy _economy;
    private readonly AiConstruction _construction;
    private readonly AiArmy _army;
    private readonly StructureDef? _generator;
    private readonly StructureDef? _academy;
    private readonly StructureDef? _outpost;
    private readonly List<EntityId> _withoutRallyPoint = [];
    private readonly List<int> _deposits = [];
    private readonly List<Command> _gathers = [];
    private int _nextThink;

    /// <param name="match">The match, as the host runs it.</param>
    /// <param name="player">The seat it plays: the number of the player in the match.</param>
    /// <param name="personality">Its personality, one of the match's data: <see cref="GameData.Personalities"/>.</param>
    public AiPlayer(Match match, int player, AiPersonalityDef personality)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(personality);
        if (personality.TrackWeights.Count != match.Data.Tracks.Count)
        {
            throw new ArgumentException("The personality is not one of this match's data.", nameof(personality));
        }

        Player = player;
        Personality = personality;
        _sight = new AiSight(match, player);
        _research = new AiResearch(_sight, personality);
        _economy = new AiEconomy(_sight, personality);
        _construction = new AiConstruction(_sight);
        _army = new AiArmy(_sight, personality);

        // What each structure is for is in its file, so none is named here.
        var start = match.Data.StartStructure;
        foreach (var structure in match.Data.Structures)
        {
            if (structure.Wall is not null || structure.Index == start.Index)
            {
                continue;
            }

            if (structure.IsEnergyWorkSite)
            {
                _generator ??= structure;
            }
            else if (structure.ResearchSlots > 0)
            {
                _academy ??= structure;
            }
            else if (structure.TerritoryRadius is not null)
            {
                _outpost ??= structure;
            }
        }
    }

    /// <summary>The seat it plays.</summary>
    public int Player { get; }

    public AiPersonalityDef Personality { get; }

    /// <summary>
    /// Adds the orders it gives now to the end of <paramref name="commands"/>, which may be none.
    /// Call it after a tick has run, never during one.
    /// </summary>
    public void Think(List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (_sight.Tick < _nextThink)
        {
            return;
        }

        _nextThink = _sight.Tick + ThinkInterval;
        _sight.Refresh();
        if (!TryFindHome(out var homeX, out var homeY, out var homeRadius))
        {
            // Without a Capitol or an Outpost it holds no ground to build or work on.
            return;
        }

        _budget.Reset(_sight.Materials, _sight.Energy);
        _construction.BeginRound();

        var first = commands.Count;
        _research.Think(_budget, commands);
        _economy.TrainWorkers(_budget, commands);
        BuildStructures(homeX, homeY, homeRadius, commands);
        _army.Think(Math.Max(0, _sight.SupplyFree - _economy.SupplyKeptForWorkers), _budget, commands);
        _economy.FillSupply(_army.SmallestUnitSupply, _budget, commands);
        SetRallyPoints(homeX, homeY, homeRadius, commands);

        // The workers are divided last, when the budget knows every purchase of the round, and
        // their orders go first. An order to build is taken by the nearest of the workers, which
        // leaves its work for it, and an order to gather ends a build order: in the other order,
        // a structure would not be built whenever the round also sends its builder to work.
        _gathers.Clear();
        _economy.AssignWorkers(_budget, _gathers);
        commands.InsertRange(first, _gathers);
    }

    /// <summary>The middle of its Capitol, or of the first structure that gives it territory, and that territory's radius.</summary>
    private bool TryFindHome(out int x, out int y, out int radius)
    {
        var start = _sight.Data.StartStructure.Index;
        OwnStructure? home = null;
        var homeRadius = 0;
        foreach (var structure in _sight.Structures)
        {
            if (!structure.IsFinished || _sight.Data.StructureOf(structure.Type)?.TerritoryRadius is not { } territory)
            {
                continue;
            }

            if (home is null || (structure.Type == start && home.Value.Type != start))
            {
                home = structure;
                homeRadius = territory;
            }
        }

        x = home is { } found ? found.X + (found.Size / 2) : 0;
        y = home is { } other ? other.Y + (other.Size / 2) : 0;
        radius = homeRadius;
        return home is not null;
    }

    private void BuildStructures(int homeX, int homeY, int homeRadius, List<Command> commands)
    {
        // Energy: a Generator when it wants more workers at Generators than they have places.
        if (_generator is not null)
        {
            var places = _sight.Data.Resources.Energy.WorkersPerGenerator
                * (_construction.FinishedCount(_generator.Index) + _construction.UnderWayCount(_generator.Index));
            if (_economy.EnergyWorkersWanted > places)
            {
                BuildAtHome(_generator, homeX, homeY, homeRadius, commands);
            }
        }

        // The army: the structure that trains each line of the personality.
        for (var i = 0; i < Personality.Lines.Count; i++)
        {
            if (_sight.Data.LineOf(Personality.Lines[i].Line) is not { } line
                || _sight.Data.StructureOf(line.BuiltAt) is not { } producer
                || IsEarlierLineBuiltAt(i, line.BuiltAt))
            {
                continue;
            }

            if (WantsProducer(producer))
            {
                BuildAtHome(producer, homeX, homeY, homeRadius, commands);
            }
        }

        // Research: an Academy when what it lacks is a slot.
        if (_academy is not null && _research.LacksASlot && _construction.UnderWayCount(_academy.Index) == 0)
        {
            BuildAtHome(_academy, homeX, homeY, homeRadius, commands);
        }

        // Expansion: an Outpost beside the nearest deposit it does not hold.
        if (_outpost is not null && _economy.WantsToExpand && _construction.UnderWayCount(_outpost.Index) == 0)
        {
            Expand(_outpost, homeX, homeY, commands);
        }
    }

    private bool IsEarlierLineBuiltAt(int position, int structure)
    {
        for (var i = 0; i < position; i++)
        {
            if (_sight.Data.LineOf(Personality.Lines[i].Line)?.BuiltAt == structure)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when it has none of a structure that trains its army, and when every one it has is
    /// finished and has a queue as long as it keeps them: it has the resources for more units
    /// than they can train.
    /// </summary>
    private bool WantsProducer(StructureDef producer)
    {
        var count = _sight.CountOf(producer.Index);
        if (count == 0)
        {
            return true;
        }

        if (count >= MostProducers || _construction.UnderWayCount(producer.Index) > 0)
        {
            return false;
        }

        foreach (var structure in _sight.Structures)
        {
            if (structure.Type == producer.Index && _sight.OrdersAt(structure.Entity) < AiEconomy.QueueLength)
            {
                return false;
            }
        }

        return true;
    }

    private void BuildAtHome(StructureDef structure, int homeX, int homeY, int homeRadius, List<Command> commands)
    {
        if (_construction.CanOrder(structure, _budget)
            && _construction.Sites.TryFind(structure, homeX, homeY, homeRadius, homeX, homeY, alsoNeeds: null, out var x, out var y))
        {
            _construction.Order(structure, x, y, _budget, commands);
        }
    }

    private void Expand(StructureDef outpost, int homeX, int homeY, List<Command> commands)
    {
        // The deposits it could claim, nearest to home first.
        _deposits.Clear();
        for (var i = 0; i < _sight.Sites.Count; i++)
        {
            var site = _sight.Sites[i];
            if (site.Kind == WorkSiteKind.Deposit
                && !site.IsHeld
                && !_sight.IsClaimedByAnother(site.X, site.Y, site.Size)
                && !_construction.Sites.WasRefusedNear(outpost.Index, site.X, site.Y, outpost.TerritoryRadius ?? 0))
            {
                _deposits.Add(i);
            }
        }

        if (_deposits.Count == 0 || !_construction.CanOrder(outpost, _budget))
        {
            return;
        }

        _deposits.Sort((left, right) =>
        {
            var byDistance = DistanceFromHome(_sight.Sites[left]).CompareTo(DistanceFromHome(_sight.Sites[right]));
            return byDistance != 0 ? byDistance : left.CompareTo(right);
        });

        foreach (var index in _deposits)
        {
            if (_construction.TryFindSiteAt(_sight.Sites[index], outpost, homeX, homeY, out var x, out var y))
            {
                _construction.Order(outpost, x, y, _budget, commands);
                return;
            }
        }

        long DistanceFromHome(KnownSite site) => AiSites.DistanceSquared(site.X, site.Y, site.Size, homeX, homeY);
    }

    /// <summary>
    /// Gives every structure that trains its army a rally point at the edge of its home
    /// territory, on the side of the middle of the map, so that new units do not stand in the
    /// way of the next ones.
    /// </summary>
    private void SetRallyPoints(int homeX, int homeY, int homeRadius, List<Command> commands)
    {
        _withoutRallyPoint.Clear();
        foreach (var structure in _sight.Structures)
        {
            if (structure.IsFinished
                && _sight.Data.StructureOf(structure.Type) is { Trains.Count: > 0 }
                && _sight.OrdersAt(structure.Entity) >= 0
                && !_sight.HasRallyPoint(structure.Entity))
            {
                _withoutRallyPoint.Add(structure.Entity);
            }
        }

        if (_withoutRallyPoint.Count == 0 || !TryFindMusterTile(homeX, homeY, homeRadius, out var x, out var y))
        {
            return;
        }

        var point = MapGrid.TileCenter(x, y);
        commands.Add(new Command(
            CommandType.SetRallyPoint,
            Player,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_withoutRallyPoint),
            point.X,
            point.Y));
    }

    /// <summary>
    /// The tile its army gathers on: from home toward the middle of the map, three tiles inside
    /// the edge of the home territory, or the nearest tile to that on which a unit can stand.
    /// </summary>
    private bool TryFindMusterTile(int homeX, int homeY, int homeRadius, out int x, out int y)
    {
        var reach = Math.Max(1, homeRadius - 3);
        long dx = (_sight.MapWidth / 2) - homeX;
        long dy = (_sight.MapHeight / 2) - homeY;
        long length = IntegerMath.Sqrt((ulong)((dx * dx) + (dy * dy)));
        var aimX = length == 0 ? homeX + reach : homeX + (int)(dx * reach / length);
        var aimY = length == 0 ? homeY : homeY + (int)(dy * reach / length);

        for (var ring = 0; ring <= homeRadius; ring++)
        {
            for (var offsetY = -ring; offsetY <= ring; offsetY++)
            {
                var step = Math.Abs(offsetY) == ring ? 1 : Math.Max(1, 2 * ring);
                for (var offsetX = -ring; offsetX <= ring; offsetX += step)
                {
                    if (_sight.CanStandOn(aimX + offsetX, aimY + offsetY))
                    {
                        x = aimX + offsetX;
                        y = aimY + offsetY;
                        return true;
                    }
                }
            }
        }

        x = 0;
        y = 0;
        return false;
    }
}
