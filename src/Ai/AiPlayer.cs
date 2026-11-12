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
/// schedules them and sends them to every peer. In the game it runs on the host only. The other
/// peers get its commands and need neither this class nor its personality.
/// </para>
/// <para>
/// <b>When it thinks.</b> <see cref="Think"/> may be called as often as wished. It does something
/// once every <see cref="ThinkInterval"/> ticks and nothing in between, which is longer than the
/// command delay a host chooses, so the orders of one round have been carried out before the next
/// round reads the match. Where its orders take longer, as from a machine that is not the host,
/// it finds that out from the first worker it sends to work and thinks as much later
/// (<see cref="AiPace"/>).
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

    /// <summary>The tiles its army stands behind a passage it holds, on its own side.</summary>
    internal const int HoldBehind = 2;

    private readonly AiSight _sight;
    private readonly AiBudget _budget = new();
    private readonly AiResearch _research;
    private readonly AiEconomy _economy;
    private readonly AiConstruction _construction;
    private readonly AiArmy _army;
    private readonly AiSearch _search;
    private readonly AiExplorer _explorer;
    private readonly AiFight _fight;
    private readonly AiIntel _intel;
    private readonly AiResponses _responses;
    private readonly AiScout _scout;
    private readonly AiWay _way;
    private readonly StructureDef? _generator;
    private readonly StructureDef? _academy;
    private readonly StructureDef? _outpost;
    private readonly List<EntityId> _withoutRallyPoint = [];
    private readonly List<int> _deposits = [];
    private readonly List<Command> _gathers = [];
    private readonly AiPace _pace;
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
        _intel = new AiIntel(_sight);
        _responses = new AiResponses(_sight, _intel, personality);
        _research = new AiResearch(_sight, personality, _responses);
        _economy = new AiEconomy(_sight, personality);
        _construction = new AiConstruction(_sight);
        _pace = new AiPace(_sight);
        _army = new AiArmy(_sight, personality, _responses, _pace);
        _search = new AiSearch(_sight);
        _explorer = new AiExplorer(_sight, _search);
        _fight = new AiFight(_sight, _search, personality.Fight, _intel, _responses);
        _scout = new AiScout(_sight, personality.Scouting);
        _way = new AiWay(_sight);

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

    /// <summary>How its army stands, for the tests of this project and the tools that report on a match.</summary>
    internal AiFight Fight => _fight;

    /// <summary>Its scout, for the same.</summary>
    internal AiScout Scout => _scout;

    internal AiWay Way => _way;

    /// <summary>What it remembers of the enemy, for the same.</summary>
    internal AiIntel Intel => _intel;

    /// <summary>How long its orders take, for the same.</summary>
    internal AiPace Pace => _pace;

    /// <summary>The rows of its response table, for the same.</summary>
    internal AiResponses Responses => _responses;

    /// <summary>
    /// Adds the orders it gives now to the end of <paramref name="commands"/>, which may be none.
    /// Call it after a tick has run, never during one.
    /// </summary>
    public void Think(List<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        // What it saw happen is read after every tick: the events of a tick are gone after the next.
        _intel.Watch();
        _pace.Watch();
        if (_sight.Tick < _nextThink || _pace.IsWaiting)
        {
            return;
        }

        _nextThink = _sight.Tick + _pace.Interval;
        _sight.Refresh();
        _search.Update();
        _intel.Update();
        _responses.Update();
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
        var hasMuster = TryFindMusterTile(homeX, homeY, homeRadius, out var musterX, out var musterY);
        if (hasMuster)
        {
            SetRallyPoints(musterX, musterY, commands);
            HoldAtPassage(homeX, homeY, ref musterX, ref musterY);
        }

        // The scout is chosen before the army is sorted, so that no force takes it along.
        _scout.Think(_fight, hasMuster, musterX, musterY, commands);
        _fight.Think(hasMuster, musterX, musterY, _army.IsAtSupplyCap, commands);
        _way.Think(_pace.Interval, commands, first);

        // The workers are divided last, when the budget knows every purchase of the round, and
        // their orders go first. An order to build is taken by the nearest of the workers, which
        // leaves its work for it, and an order to gather ends a build order: in the other order,
        // a structure would not be built whenever the round also sends its builder to work.
        _gathers.Clear();
        _economy.AssignWorkers(_budget, _gathers);
        commands.InsertRange(first, _gathers);
        _pace.Note(_gathers);
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

        // And the structure that trains a line a row of the response table raises, when no line above is built there.
        for (var i = 0; i < _responses.ArmySupply.Count; i++)
        {
            if (_sight.Data.LineOf(_responses.ArmySupply[i].Line) is { } line
                && _sight.Data.StructureOf(line.BuiltAt) is { } producer
                && !IsEarlierLineBuiltAt(Personality.Lines.Count, line.BuiltAt)
                && !IsEarlierRaisedLineBuiltAt(i, line.BuiltAt)
                && WantsProducer(producer))
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
        if (_outpost is not null)
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

    private bool IsEarlierRaisedLineBuiltAt(int position, int structure)
    {
        for (var i = 0; i < position; i++)
        {
            if (_sight.Data.LineOf(_responses.ArmySupply[i].Line)?.BuiltAt == structure)
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

    /// <summary>
    /// Orders an Outpost at the nearest deposit it could claim when it wants to expand and knows
    /// a place for one. When it knows none, a worker goes to look: an Outpost stands on ground
    /// its player has explored.
    /// </summary>
    private void Expand(StructureDef outpost, int homeX, int homeY, List<Command> commands)
    {
        var wants = _economy.WantsToExpand && _construction.UnderWayCount(outpost.Index) == 0;
        var knowsAPlace = false;
        KnownSite? toLookAt = null;
        if (wants)
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

            _deposits.Sort((left, right) =>
            {
                var byDistance = DistanceFromHome(_sight.Sites[left]).CompareTo(DistanceFromHome(_sight.Sites[right]));
                return byDistance != 0 ? byDistance : left.CompareTo(right);
            });

            foreach (var index in _deposits)
            {
                if (_construction.TryFindSiteAt(_sight.Sites[index], outpost, homeX, homeY, out var x, out var y))
                {
                    knowsAPlace = true;
                    if (_construction.CanOrder(outpost, _budget))
                    {
                        _construction.Order(outpost, x, y, _budget, commands);
                    }

                    break;
                }

                if (toLookAt is null && !_explorer.HasLookedAt(_sight.Sites[index].Entity))
                {
                    toLookAt = _sight.Sites[index];
                }
            }
        }

        _explorer.Think(wants, knowsAPlace, toLookAt, homeX, homeY, commands);

        long DistanceFromHome(KnownSite site) => AiSites.DistanceSquared(site.X, site.Y, site.Size, homeX, homeY);
    }

    /// <summary>
    /// Gives every structure that trains its army a rally point at the edge of its home
    /// territory, on the side of the middle of the map, so that new units do not stand in the
    /// way of the next ones.
    /// </summary>
    private void SetRallyPoints(int x, int y, List<Command> commands)
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

        if (_withoutRallyPoint.Count == 0)
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
    /// "Fights at chokepoints" (design 5.5, item 4, and 5.3: the markers say where to hold):
    /// while a row of the response table names a kind of passage, its army gathers at the marker
    /// of that kind on the way to the nearest other start, <see cref="HoldBehind"/> tiles on its
    /// own side of it, where the enemy comes out of the passage a few at a time. The map file
    /// names the passage, so it need not have seen the ground. Its new units still come out at
    /// home, and walk there.
    /// </summary>
    private void HoldAtPassage(int homeX, int homeY, ref int musterX, ref int musterY)
    {
        if (_responses.Passage is not { } kind)
        {
            return;
        }

        var best = long.MaxValue;
        var toX = 0;
        var toY = 0;
        foreach (var (startX, startY) in _sight.OtherStarts)
        {
            var distance = AiSearch.DistanceSquared(startX, startY, homeX, homeY);
            if (distance < best)
            {
                best = distance;
                toX = startX;
                toY = startY;
            }
        }

        if (best == long.MaxValue || !AiOrders.TryFindMarker(_sight, kind, homeX, homeY, toX, toY, out var marker) || marker is null)
        {
            return;
        }

        // Units pass through along the axis the passage's width does not run along.
        var x = AiOrders.MiddleX(marker);
        var y = AiOrders.MiddleY(marker);
        var middleX = x;
        var middleY = y;
        if (marker.Across == MapAxis.Y)
        {
            x += (homeX < x ? -1 : 1) * ((marker.Area.SizeX / 2) + HoldBehind);
            x = Math.Clamp(x, 0, _sight.MapWidth - 1);
        }
        else
        {
            y += (homeY < y ? -1 : 1) * ((marker.Area.SizeY / 2) + HoldBehind);
            y = Math.Clamp(y, 0, _sight.MapHeight - 1);
        }

        // Ground it has seen and knows to be closed is no place to stand: the passage itself is.
        var isClosed = _sight.IsExplored(x, y) && !_sight.CanStandOn(x, y);
        musterX = isClosed ? middleX : x;
        musterY = isClosed ? middleY : y;
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
