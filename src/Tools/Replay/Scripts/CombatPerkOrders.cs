using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Research;

namespace Tools.Replay.Scripts;

/// <summary>
/// The accents of combat (19a): the attack rate, range, splash and wider blast of Overgunned, the
/// area reduction and plating of Hardened, and the sight of Rapid. Random orders do not reach
/// them, because each needs two researches of one track in a row that have to be earned, so this
/// script plays toward them: every player with a base earns Energy and researches what its part
/// needs, and all players keep a small force fighting in the middle of the map.
/// </summary>
/// <remarks>
/// <para>
/// The players take three parts in turn, by their numbers. A <b>gunner</b> researches Firepower
/// to tier 3 and then Mobility to tier 2: its units of the first epoch come to attack faster or
/// further, then to splash or to fire shells with a wider blast, then to see further. An
/// <b>armored</b> player researches Protection to tier 2, then Mobility to tier 2, then
/// Protection to tier 3: its units come to take less from blasts and splash, then less from
/// every attacker below their Protection. A <b>plain</b> player researches nothing, so that its
/// units and its Tower stay such attackers. A player without a base, which a recording may have,
/// can research nothing whatever its part and fights all the same.
/// </para>
/// <para>
/// The armored player does not start its third Protection while a gunner with a base has yet
/// to reach Firepower 3. A splash on a unit that takes a quarter less and has no plating needs
/// a gunner that splashes and an armored player still at Protection 2 at the same time. Left
/// to when each can pay, the two researches end within a few ticks of each other, in either
/// order; held back, the armored player's units stand without plating under the gunner's
/// splash for as long as that third research takes.
/// </para>
/// <para>
/// A player with a base places Generators beside its first structure and sends its workers to
/// them. Every player keeps a force of the chassis of the first epoch, a Tower and a few walls
/// round the middle of the map, each player on its own side of it, and orders its force about
/// there: attack-moves into the middle, attacks on a unit or a structure of an enemy that it
/// sees, moves back to its side, and stops. A unit that stands beside a wall when it is hit by
/// a unit that splashes has the wall splashed.
/// </para>
/// <para>
/// It places its structures with the development placement and makes its units with the
/// development spawn, as <see cref="SiegeOrders"/> does, and needs <c>debug.spawn</c> in the
/// rules of the data for both. It gives its orders at its own pace and from a generator of its
/// own.
/// </para>
/// </remarks>
[OrderScript("combat-perks")]
internal sealed class CombatPerkOrders : OrderScript
{
    private const int LongestPause = 24;

    /// <summary>The fighting units a player has before the script stops making more for it.</summary>
    private const int Force = 7;

    /// <summary>The Generators a player with a base has before the script stops placing more for it.</summary>
    private const int Generators = 2;

    /// <summary>The walls a player has round the middle before the script stops placing more for it.</summary>
    private const int Walls = 3;

    /// <summary>How far from the middle of the map a player's side of it is, in tiles.</summary>
    private const int SideDistance = 5;

    /// <summary>How many sites the script tries for a structure before it lets the placement be.</summary>
    private const int SitesTried = 16;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D19_A000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<int> _chassis = [];
    private readonly List<EntityId> _fighters = [];
    private readonly List<EntityId> _freeWorkers = [];
    private readonly List<EntityId> _seenUnits = [];
    private readonly List<EntityId> _seenStructures = [];
    private readonly StructureDef? _generator;
    private readonly StructureDef? _tower;
    private readonly StructureDef? _wall;
    private readonly FixedVector2 _middle;
    private int _nextTick;

    public CombatPerkOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
        for (var chassis = 0; chassis < Match.Data.Chassis.Count; chassis++)
        {
            if (Match.Data.Chassis[chassis].Epoch == Match.Data.Rules.Match.StartEpoch)
            {
                _chassis.Add(chassis);
            }
        }

        foreach (var structure in Match.Data.Structures)
        {
            if (structure.IsEnergyWorkSite)
            {
                _generator ??= structure;
            }
            else if (structure.Attack is not null)
            {
                _tower ??= structure;
            }
            else if (structure.Wall is not null)
            {
                _wall ??= structure;
            }
        }

        _middle = OpenGroundNear(Match.Grid.Width / 2, Match.Grid.Height / 2);
    }

    /// <summary>The part a player takes: 0 a gunner, 1 armored, 2 plain.</summary>
    public static int PartOf(int player) => player % 3;

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        var kind = _random.NextInt(100);
        Collect(player);
        AddResearchFor(player, commands);
        AddWorkFor(player, commands);
        AddForceFor(player, commands);
        if (_fighters.Count == 0)
        {
            return;
        }

        if (kind < 34)
        {
            // Into the middle: on the way the units pick for themselves.
            var into = Near(_middle, 3);
            commands.Add(new Command(CommandType.AttackMove, player, [.. Some(_fighters)], into.X, into.Y));
        }
        else if (kind < 60)
        {
            if (_seenUnits.Count > 0)
            {
                commands.Add(new Command(CommandType.Attack, player, [.. Some(_fighters)], targetEntity: Any(_seenUnits)));
            }
        }
        else if (kind < 70)
        {
            if (_seenStructures.Count > 0)
            {
                commands.Add(new Command(CommandType.Attack, player, [.. Some(_fighters)], targetEntity: Any(_seenStructures)));
            }
        }
        else if (kind < 82)
        {
            // Back to its own side: a walk on which nothing is fought.
            var back = Near(SideOf(player), 2);
            commands.Add(new Command(CommandType.Move, player, [.. Some(_fighters)], back.X, back.Y));
        }
        else if (kind < 90)
        {
            commands.Add(new Command(CommandType.Stop, player, [.. Some(_fighters)]));
        }
    }

    /// <summary>Starts the first research of the player's part that it has not got and can start now.</summary>
    private void AddResearchFor(int player, List<Command> commands)
    {
        var roles = Match.Data.TrackRoles;
        ReadOnlySpan<(int Track, int Tier)> wishes = PartOf(player) switch
        {
            0 => [(roles.Firepower, 2), (roles.Firepower, 3), (roles.Mobility, 2)],
            1 => [(roles.Protection, 2), (roles.Mobility, 2), (roles.Protection, 3)],
            _ => [],
        };
        foreach (var (track, tier) in wishes)
        {
            if (Match.Research.State.Tier(player, track) >= tier)
            {
                continue;
            }

            var held = PartOf(player) == 1 && track == roles.Protection && tier == 3 && AGunnerHasYetToSplash();
            if (!held
                && Match.Research.State.SlotResearching(player, track) < 0
                && Match.Research.CheckStart(player, track) == ResearchStartCheck.Allowed)
            {
                commands.Add(new Command(CommandType.StartResearch, player, contentId: track));
            }

            return;
        }
    }

    /// <summary>True while a gunner that can research, one with a base, is below the Firepower that splashes.</summary>
    private bool AGunnerHasYetToSplash()
    {
        var firepower = Match.Data.TrackRoles.Firepower;
        for (var player = 0; player < Match.Players.Count; player++)
        {
            if (PartOf(player) == 0 && Match.Research.State.Tier(player, firepower) < 3 && TryFindBase(player, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The economy of a player with a base: Generators beside its first structure, up to their
    /// number, one worker at a deposit the player holds, for the Materials the researches cost
    /// beyond what a player starts with, and every other worker without work sent to the
    /// Generator with the fewest workers.
    /// </summary>
    private void AddWorkFor(int player, List<Command> commands)
    {
        if (_generator is null || _freeWorkers.Count == 0 || !TryFindBase(player, out var home))
        {
            return;
        }

        var sites = Match.WorkSites;
        var emptiest = EntityId.None;
        var fewest = int.MaxValue;
        var generators = 0;
        var deposit = EntityId.None;
        var atDeposits = 0;
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites.ValueAt(i);
            var entity = sites.EntityAt(i);
            if (!Match.World.Entities.IsAlive(entity))
            {
                continue;
            }

            if (site.Kind == WorkSiteKind.Deposit)
            {
                if (Territory.Holds(Match.Territories, Match.World.Entities, player, site.X, site.Y, site.Size))
                {
                    deposit = deposit == EntityId.None ? entity : deposit;
                    atDeposits += WorkersAt(player, entity);
                }
            }
            else if (site.Owner == player)
            {
                generators++;
                var workers = WorkersAt(player, entity);
                if (workers < fewest)
                {
                    (emptiest, fewest) = (entity, workers);
                }
            }
        }

        if (generators < Generators && TryFindSite(_generator, home, 7, out var forGenerator))
        {
            commands.Add(Place(player, _generator, forGenerator));
        }

        if (deposit != EntityId.None && atDeposits == 0)
        {
            commands.Add(new Command(CommandType.Gather, player, [_freeWorkers[0]], targetEntity: deposit));
            _freeWorkers.RemoveAt(0);
        }

        var places = Match.Data.Resources.Energy.WorkersPerGenerator - fewest;
        if (emptiest != EntityId.None && places > 0 && _freeWorkers.Count > 0)
        {
            Picked.Clear();
            Picked.AddRange(_freeWorkers.Take(places));
            commands.Add(new Command(CommandType.Gather, player, [.. Picked], targetEntity: emptiest));
        }
    }

    /// <summary>
    /// The player's side of the middle: a fighting unit when it has fewer than its force, a Tower
    /// when it has none there, and a wall when it has fewer than its walls.
    /// </summary>
    private void AddForceFor(int player, List<Command> commands)
    {
        var side = SideOf(player);
        if (_fighters.Count < Force && _chassis.Count > 0)
        {
            var point = Near(side, 2);
            commands.Add(new Command(
                CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: _chassis[_random.NextInt(_chassis.Count)]));
        }

        var towers = 0;
        var walls = 0;
        var structures = Match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            if (Match.World.Entities.IsAlive(entity) && Match.Bodies.TryGet(entity, out var body) && body.Owner == player)
            {
                towers += structures.ValueAt(i).Type == _tower?.Index ? 1 : 0;
                walls += structures.ValueAt(i).Type == _wall?.Index ? 1 : 0;
            }
        }

        if (_tower is not null && towers == 0 && TryFindSite(_tower, side, 3, out var forTower))
        {
            commands.Add(Place(player, _tower, forTower));
        }
        else if (_wall is not null && walls < Walls && TryFindSite(_wall, _middle, 4, out var forWall))
        {
            commands.Add(Place(player, _wall, forWall));
        }
    }

    /// <summary>
    /// Sorts what there is for a player: its fighting units, its workers without work, and the
    /// units and structures of the other players that it sees and that can be attacked.
    /// </summary>
    private void Collect(int player)
    {
        _fighters.Clear();
        _freeWorkers.Clear();
        _seenUnits.Clear();
        _seenStructures.Clear();
        var bodies = Match.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var entity = bodies.EntityAt(i);
            var body = bodies.ValueAt(i);
            if (!Match.World.Entities.IsAlive(entity) || !Match.Health.Contains(entity))
            {
                continue;
            }

            if (body.Owner == player)
            {
                if (Match.Combatants.Contains(entity))
                {
                    _fighters.Add(entity);
                }
                else if (Match.Workers.TryGet(entity, out var worker) && worker.State == WorkerState.Idle)
                {
                    _freeWorkers.Add(entity);
                }
            }
            else if (Match.Structures.Contains(entity))
            {
                if (Match.Vision.Sees(player, entity))
                {
                    _seenStructures.Add(entity);
                }
            }
            else if (Match.Movers.Contains(entity) && Match.Vision.Sees(player, in body))
            {
                _seenUnits.Add(entity);
            }
        }
    }

    /// <summary>The workers of a player that hold a place at a work site or wait for one.</summary>
    private int WorkersAt(int player, EntityId site)
    {
        var count = 0;
        var workers = Match.Workers;
        for (var i = 0; i < workers.Count; i++)
        {
            var worker = workers.ValueAt(i);
            var entity = workers.EntityAt(i);
            if (worker.State != WorkerState.Idle && worker.Site == site && Match.World.Entities.IsAlive(entity)
                && Match.Bodies.TryGet(entity, out var body) && body.Owner == player)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The center of the first structure a player has: its Capitol, in a match that starts with one.</summary>
    private bool TryFindBase(int player, out FixedVector2 home)
    {
        var structures = Match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            if (Match.World.Entities.IsAlive(entity) && Match.Bodies.TryGet(entity, out var body) && body.Owner == player
                && Match.Data.StructureOf(structures.ValueAt(i).Type)?.ResearchSlots > 0)
            {
                home = body.Position;
                return true;
            }
        }

        home = default;
        return false;
    }

    /// <summary>
    /// A player's side of the middle: the players stand round it at equal angles, first to the
    /// west, on the nearest open ground to that point.
    /// </summary>
    private FixedVector2 SideOf(int player)
    {
        // Whole steps round the middle for up to four players, and the diagonals after them.
        ReadOnlySpan<(int X, int Y)> steps = [(-1, 0), (1, 0), (0, 1), (0, -1), (-1, -1), (1, 1), (-1, 1), (1, -1)];
        var (stepX, stepY) = steps[player % steps.Length];
        return OpenGroundNear(
            _middle.X.FloorToInt() + (stepX * SideDistance),
            _middle.Y.FloorToInt() + (stepY * SideDistance));
    }

    /// <summary>The center of the open tile nearest to a tile, by rings round it.</summary>
    private FixedVector2 OpenGroundNear(int x, int y)
    {
        for (var ring = 0; ring < 16; ring++)
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

        return MapGrid.TileCenter(x, y);
    }

    /// <summary>A free site for a structure within so many tiles of a point, if one of the sites tried is.</summary>
    private bool TryFindSite(StructureDef structure, FixedVector2 near, int tiles, out FixedVector2 site)
    {
        for (var i = 0; i < SitesTried; i++)
        {
            var x = near.X.FloorToInt() + _random.NextInt(-tiles, tiles + 1);
            var y = near.Y.FloorToInt() + _random.NextInt(-tiles, tiles + 1);
            if (Match.StructureTiles.IsFree(new TileSquare(x, y, structure.Footprint)))
            {
                site = MapGrid.TileCenter(x, y);
                return true;
            }
        }

        site = default;
        return false;
    }

    private static Command Place(int player, StructureDef structure, FixedVector2 site) =>
        new(CommandType.DebugPlace, player, targetX: site.X, targetY: site.Y, contentId: structure.Index);

    private EntityId Any(List<EntityId> entities) => entities[_random.NextInt(entities.Count)];

    /// <summary>Each of the entities with an even chance, and at least one.</summary>
    private List<EntityId> Some(List<EntityId> entities)
    {
        Picked.Clear();
        foreach (var entity in entities)
        {
            if (_random.NextInt(2) == 0)
            {
                Picked.Add(entity);
            }
        }

        if (Picked.Count == 0)
        {
            Picked.Add(Any(entities));
        }

        return Picked;
    }

    /// <summary>A point within so many tiles of another, on the map.</summary>
    private FixedVector2 Near(FixedVector2 point, int tiles)
    {
        var reach = tiles * Fixed.RawOne;
        var x = Math.Clamp(point.X.Raw + _random.NextInt(-reach, reach + 1), 0, (Match.Grid.Width * Fixed.RawOne) - 1);
        var y = Math.Clamp(point.Y.Raw + _random.NextInt(-reach, reach + 1), 0, (Match.Grid.Height * Fixed.RawOne) - 1);
        return new FixedVector2(Fixed.FromRaw(x), Fixed.FromRaw(y));
    }
}
