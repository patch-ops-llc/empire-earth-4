using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Production;
using Sim.Research;

namespace Tools.Replay.Scripts;

/// <summary>
/// The perks that count something for each unit (19c): Regeneration, Reserves and Charge. Random
/// orders do not reach them, because each needs researches in a row that have to be earned, so
/// this script plays toward them: every player builds a Generator and puts workers to it, and
/// researches what its part needs.
/// </summary>
/// <remarks>
/// <para>
/// The players take two parts in turn, by their numbers. A <b>raider</b> researches Mobility to
/// tier 3 and then Logistics to tier 2, keeps a small force made with the development spawn at a
/// point in front of its base, and once its units carry Charge sends them out and calls them
/// back in turns: an attack-move to the point in front of the next player's base, then a move
/// home, each a walk of far more than 4 tiles. A <b>defender</b> researches Logistics to tier 2
/// and Manufacturing to tier 3, builds the cheapest structure that trains a chassis, and trains
/// units there with the rally point in front of its base, where the raiders come. So paid units
/// die with Reserves and without, raiders charge, and the survivors of both sides regenerate.
/// </para>
/// <para>
/// It gives only orders the simulation accepts when they are given, at its own pace and from a
/// generator of its own. The force needs <c>debug.spawn</c> in the rules of the data, as the
/// script <c>fight</c> does.
/// </para>
/// </remarks>
[OrderScript("perks")]
internal sealed class PerkOrders : OrderScript
{
    private const int LongestPause = 20;

    /// <summary>How many sites the script tries for a structure before it lets the order be.</summary>
    private const int SitesTried = 24;

    /// <summary>The fighting units a raider has before the script stops making more for it.</summary>
    private const int Force = 6;

    /// <summary>The ticks a raider's force is out, and then the ticks it is at home.</summary>
    private const int HalfARaid = 400;

    /// <summary>The workers a player has before the script stops training more for it.</summary>
    private const int Workers = 8;

    /// <summary>How far in front of the edge of its territory a player's point lies, in tiles.</summary>
    private const int BeyondTheEdge = 2;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D19_C000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<EntityId> _free = [];
    private readonly List<int> _chassis = [];
    private readonly List<int> _allowed = [];
    private readonly int[] _ordered;
    private int _nextTick;

    public PerkOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
        _ordered = new int[Match.Players.Count];
        Array.Fill(_ordered, -1);
        for (var chassis = 0; chassis < Match.Data.Chassis.Count; chassis++)
        {
            if (Match.Data.Chassis[chassis].Epoch == Match.Data.Rules.Match.StartEpoch)
            {
                _chassis.Add(chassis);
            }
        }
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        if (!TryFindTerritory(player, out var territory))
        {
            return;
        }

        var raider = player % 2 == 0;
        var roles = Match.Data.TrackRoles;
        ReadOnlySpan<(int Track, int Tier)> wishes = raider
            ? [(roles.Mobility, 2), (roles.Mobility, 3), (roles.Logistics, 2)]
            : [(roles.Logistics, 2), (roles.Manufacturing, 2), (roles.Manufacturing, 3)];
        var saving = AddResearchFor(player, wishes, commands);
        AddWorkFor(player, territory, raider, saving, commands);
        if (raider)
        {
            AddRaidFor(player, territory, tick, commands);
        }
        else
        {
            AddUnitsFor(player, territory, saving, commands);
        }
    }

    /// <summary>
    /// Starts the first research of the list that the player has not got, when it can. True when
    /// such a research is still to come and is not under way, so the player is saving for it.
    /// </summary>
    private bool AddResearchFor(int player, ReadOnlySpan<(int Track, int Tier)> wishes, List<Command> commands)
    {
        foreach (var (track, tier) in wishes)
        {
            if (Match.Research.State.Tier(player, track) >= tier)
            {
                continue;
            }

            if (Match.Research.State.SlotResearching(player, track) >= 0)
            {
                return false;
            }

            if (Match.Research.CheckStart(player, track) == ResearchStartCheck.Allowed)
            {
                commands.Add(new Command(CommandType.StartResearch, player, contentId: track));
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The player's economy: a Generator, then for a defender a structure that trains a chassis,
    /// a few more workers, and every worker without work sent to the Generator while it has
    /// places and to a deposit the player holds otherwise.
    /// </summary>
    private void AddWorkFor(int player, Territory territory, bool raider, bool saving, List<Command> commands)
    {
        CollectWorkersOf(player);
        var generator = EnergySite();
        if (generator is not null && Match.Construction.CountOf(player, generator.Index) == 0)
        {
            AddBuildFor(player, territory, generator, commands);
        }
        else if (!raider && !saving && TrainingStructure() is { } barracks && Match.Construction.CountOf(player, barracks.Index) == 0)
        {
            AddBuildFor(player, territory, barracks, commands);
        }

        if (!saving && Own.Count < Workers && TryFindProducer(player, TrainOrder.Worker, out var capitol)
            && Match.Production.Producers.Get(capitol).Count == 0
            && Match.Production.CheckTrain(player, capitol, TrainOrder.Worker) == TrainCheck.Allowed)
        {
            commands.Add(new Command(CommandType.Train, player, [capitol], contentId: TrainOrder.Worker));
        }

        var sites = Match.WorkSites;
        var ownGenerator = EntityId.None;
        var deposit = EntityId.None;
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites.ValueAt(i);
            var entity = sites.EntityAt(i);
            if (!Match.World.Entities.IsAlive(entity))
            {
                continue;
            }

            if (site.Kind == WorkSiteKind.Generator && site.Owner == player && Match.Construction.IsFinished(entity))
            {
                ownGenerator = ownGenerator == EntityId.None ? entity : ownGenerator;
            }
            else if (site.Kind == WorkSiteKind.Deposit && deposit == EntityId.None
                && Territory.Holds(Match.Territories, Match.World.Entities, player, site.X, site.Y, site.Size))
            {
                deposit = entity;
            }
        }

        var places = ownGenerator == EntityId.None ? 0 : Match.Data.Resources.Energy.WorkersPerGenerator;
        var fromDeposit = EntityId.None;
        foreach (var worker in Own)
        {
            var work = Match.Workers.Get(worker);
            if (work.State == WorkerState.Idle)
            {
                continue;
            }

            if (work.Site == ownGenerator)
            {
                places--;
            }
            else
            {
                fromDeposit = worker;
            }
        }

        Picked.Clear();
        foreach (var worker in _free)
        {
            if (places > 0)
            {
                places--;
                Picked.Add(worker);
            }
        }

        if (Picked.Count == 0 && places > 0 && fromDeposit != EntityId.None)
        {
            // Nobody is without work: one comes over from the deposit in each turn until the Generator is full.
            Picked.Add(fromDeposit);
        }

        if (Picked.Count > 0)
        {
            commands.Add(new Command(CommandType.Gather, player, [.. Picked], targetEntity: ownGenerator));
        }

        _free.RemoveAll(Picked.Contains);
        if (_free.Count > 0 && deposit != EntityId.None)
        {
            commands.Add(new Command(CommandType.Gather, player, [.. _free], targetEntity: deposit));
        }
    }

    /// <summary>Has a worker without work build the structure, when the player can pay for it and a site is found.</summary>
    private void AddBuildFor(int player, Territory territory, StructureDef def, List<Command> commands)
    {
        if (_free.Count == 0 || Match.Construction.CheckStructure(player, def.Index) != BuildProblem.None)
        {
            return;
        }

        var reach = (2 * territory.Radius) + 1;
        for (var i = 0; i < SitesTried; i++)
        {
            var x = (territory.CenterX2 / 2) - territory.Radius + _random.NextInt(reach);
            var y = (territory.CenterY2 / 2) - territory.Radius + _random.NextInt(reach);
            if (Match.Construction.CheckSite(player, def, x, y) == BuildProblem.None)
            {
                var point = MapGrid.TileCenter(x, y);
                commands.Add(new Command(CommandType.Build, player, [_free[0]], point.X, point.Y, contentId: def.Index));
                _free.RemoveAt(0);
                return;
            }
        }
    }

    /// <summary>
    /// A defender's units: the rally point of its structure that trains a chassis is set to the
    /// point in front of its base, and a unit is ordered whenever the queue is empty and the
    /// player is not saving for a research: any of the units it may order there.
    /// </summary>
    private void AddUnitsFor(int player, Territory territory, bool saving, List<Command> commands)
    {
        if (TrainingStructure() is not { } def || !TryFindProducer(player, FirstChassisOf(def), out var structure))
        {
            return;
        }

        var producer = Match.Production.Producers.Get(structure);
        var front = FrontOf(player, territory);
        if (!producer.RallySet || producer.RallyPoint != front)
        {
            commands.Add(new Command(CommandType.SetRallyPoint, player, [structure], front.X, front.Y));
        }

        if (saving || producer.Count > 0)
        {
            return;
        }

        _allowed.Clear();
        foreach (var offered in Match.Production.TrainedAt(def.Index))
        {
            if (offered != TrainOrder.Worker && Match.Production.CheckTrain(player, structure, offered) == TrainCheck.Allowed)
            {
                _allowed.Add(offered);
            }
        }

        if (_allowed.Count > 0)
        {
            commands.Add(new Command(CommandType.Train, player, [structure], contentId: _allowed[_random.NextInt(_allowed.Count)]));
        }
    }

    /// <summary>
    /// A raider's force: made up to its number at the point in front of the raider's base while
    /// it is at home, and, once its units carry Charge, sent to the point in front of the next
    /// player's base and called back in turns.
    /// </summary>
    private void AddRaidFor(int player, Territory territory, int tick, List<Command> commands)
    {
        CollectUnitsOf(player);
        Own.RemoveAll(unit => !Match.Combatants.Contains(unit));
        var home = FrontOf(player, territory);
        var phase = tick / HalfARaid;
        var charges = Match.Research.Tech(player).Blueprints[_chassis[0]].Perks.Charge is not null;
        var isOut = charges && phase % 2 == 1 && TryFindTarget(player, out _);
        if (!isOut && Own.Count < Force && _chassis.Count > 0)
        {
            var place = new FixedVector2(
                home.X + Fixed.FromInt((Own.Count % 3) - 1),
                home.Y + Fixed.FromInt((Own.Count / 3 % 3) - 1));
            var point = Match.Grid.Contains(place) && Match.Grid.IsPassable(place) ? place : home;
            commands.Add(new Command(
                CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: _chassis[_random.NextInt(_chassis.Count)]));
        }

        if (_ordered[player] == phase || Own.Count == 0)
        {
            return;
        }

        _ordered[player] = phase;
        if (isOut && TryFindTarget(player, out var target))
        {
            commands.Add(new Command(CommandType.AttackMove, player, [.. Own], target.X, target.Y));
        }
        else
        {
            commands.Add(new Command(CommandType.Move, player, [.. Own], home.X, home.Y));
        }
    }

    /// <summary>The point in front of the base of the next player after this one that has a territory.</summary>
    private bool TryFindTarget(int player, out FixedVector2 target)
    {
        for (var step = 1; step < Match.Players.Count; step++)
        {
            var other = (player + step) % Match.Players.Count;
            if (TryFindTerritory(other, out var territory))
            {
                target = FrontOf(other, territory);
                return true;
            }
        }

        target = default;
        return false;
    }

    /// <summary>
    /// The point in front of a player's base: on the line from the center of its territory to
    /// the middle of the map, a little beyond the edge of the territory, on the nearest tile
    /// there that a unit can stand on.
    /// </summary>
    private FixedVector2 FrontOf(int player, Territory territory)
    {
        var x = territory.CenterX2 / 2;
        var y = territory.CenterY2 / 2;
        long dx = (Match.Grid.Width / 2) - x;
        long dy = (Match.Grid.Height / 2) - y;
        var length = (long)IntegerMath.Sqrt((ulong)((dx * dx) + (dy * dy)));
        if (length > 0)
        {
            var reach = territory.Radius + BeyondTheEdge + (player % 2);
            x += (int)(dx * reach / length);
            y += (int)(dy * reach / length);
        }

        for (var ring = 0; ring < 8; ring++)
        {
            for (var offsetY = -ring; offsetY <= ring; offsetY++)
            {
                for (var offsetX = -ring; offsetX <= ring; offsetX++)
                {
                    var point = MapGrid.TileCenter(x + offsetX, y + offsetY);
                    if (Match.Grid.Contains(point) && Match.Grid.IsPassable(point))
                    {
                        return point;
                    }
                }
            }
        }

        return MapGrid.TileCenter(territory.CenterX2 / 2, territory.CenterY2 / 2);
    }

    /// <summary>The structure workers earn Energy at, if the data has one.</summary>
    private StructureDef? EnergySite()
    {
        foreach (var structure in Match.Data.Structures)
        {
            if (structure.IsEnergyWorkSite)
            {
                return structure;
            }
        }

        return null;
    }

    /// <summary>The cheapest structure that trains a chassis, if the data has one.</summary>
    private StructureDef? TrainingStructure()
    {
        StructureDef? cheapest = null;
        foreach (var structure in Match.Data.Structures)
        {
            if (FirstChassisOf(structure) >= 0 && (cheapest is null || structure.Materials + structure.Energy < cheapest.Materials + cheapest.Energy))
            {
                cheapest = structure;
            }
        }

        return cheapest;
    }

    private int FirstChassisOf(StructureDef structure)
    {
        foreach (var unit in Match.Production.TrainedAt(structure.Index))
        {
            if (unit != TrainOrder.Worker)
            {
                return unit;
            }
        }

        return -1;
    }

    /// <summary>The first living, finished structure of the player that trains this unit.</summary>
    private bool TryFindProducer(int player, int unit, out EntityId structure)
    {
        var producers = Match.Production.Producers;
        for (var i = 0; i < producers.Count; i++)
        {
            var entity = producers.EntityAt(i);
            if (Match.World.Entities.IsAlive(entity)
                && Match.Bodies.Get(entity).Owner == player
                && Match.Production.TrainedAt(Match.Structures.Get(entity).Type).Contains(unit))
            {
                structure = entity;
                return true;
            }
        }

        structure = EntityId.None;
        return false;
    }

    /// <summary>
    /// Puts the living workers of a player into <see cref="OrderScript.Own"/> and those of them
    /// without work and without a build order into the list of the free.
    /// </summary>
    private void CollectWorkersOf(int player)
    {
        Own.Clear();
        _free.Clear();
        var workers = Match.Workers;
        for (var i = 0; i < workers.Count; i++)
        {
            var entity = workers.EntityAt(i);
            if (!Match.World.Entities.IsAlive(entity) || Match.Bodies.Get(entity).Owner != player)
            {
                continue;
            }

            Own.Add(entity);
            if (workers.ValueAt(i).State == WorkerState.Idle && !Match.BuildOrders.Contains(entity))
            {
                _free.Add(entity);
            }
        }
    }

    /// <summary>The first living territory of the player, in the order of the entities.</summary>
    private bool TryFindTerritory(int player, out Territory territory)
    {
        var territories = Match.Territories;
        for (var i = 0; i < territories.Count; i++)
        {
            if (territories.ValueAt(i).Owner == player && Match.World.Entities.IsAlive(territories.EntityAt(i)))
            {
                territory = territories.ValueAt(i);
                return true;
            }
        }

        territory = default;
        return false;
    }
}
