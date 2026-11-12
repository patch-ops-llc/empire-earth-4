using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;

namespace Tools.Replay.Scripts;

/// <summary>
/// Orders of construction (15c): build orders for sites the player may build on and for sites it
/// may not, orders that cancel a build before its foundation and after it, workers sent to earn
/// at what was built, and workers on their way to a site sent somewhere else.
/// </summary>
/// <remarks>
/// A player has what its start gives it and what its workers earn, so most of what the script
/// orders it cannot pay for. To get structures built all the same, it orders the two cheap ones
/// that the plan's replay must have, a Generator and an Outpost, more often than the rest, and
/// looks for a site that the order accepts before it gives up and orders any.
/// </remarks>
[OrderScript("build")]
internal sealed class BuildOrders : OrderScript
{
    private const int LongestPause = 90;

    /// <summary>How many sites the script tries before it orders one that will be refused.</summary>
    private const int SitesTried = 12;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D15_C000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<EntityId> _found = [];
    private int _nextTick;

    public BuildOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        var kind = _random.NextInt(100);
        CollectUnitsOf(player);
        if (Own.Count == 0)
        {
            return;
        }

        var roster = Match.Data.Rules.Structures;
        if (kind < 34)
        {
            // A structure on a site the order accepts, if the script finds one.
            var pick = _random.NextInt(10);
            var structure = pick < 5 ? IndexOf("generator") : pick < 8 ? IndexOf("outpost") : _random.NextInt(roster.Count);
            PickSome(_random);
            commands.Add(Build(player, Picked, structure, SiteFor(player, structure)));
        }
        else if (kind < 42)
        {
            // Any structure of the roster anywhere on the map, and now and then the number after its
            // last: the gate's (18b) in data that has a wall, and no structure's otherwise.
            PickSome(_random);
            commands.Add(Build(player, Picked, _random.NextInt(roster.Count + 1), AnyPoint(_random)));
        }
        else if (kind < 62)
        {
            // Cancel: a worker's order, a structure that is building itself, or everything the player has.
            var way = _random.NextInt(5);
            CollectWorkersWithOrders(player);
            if (way < 2 && _found.Count > 0)
            {
                commands.Add(Cancel(player, [_found[_random.NextInt(_found.Count)]]));
                return;
            }

            CollectFoundations(player);
            if (way < 4 && _found.Count > 0)
            {
                commands.Add(Cancel(player, [_found[_random.NextInt(_found.Count)]]));
                return;
            }

            CollectFoundations(player);
            CollectWorkersWithOrders(player, keep: true);
            for (var i = 0; i < Match.Structures.Count; i++)
            {
                _found.Add(Match.Structures.EntityAt(i));
            }

            commands.Add(Cancel(player, _found));
        }
        else if (kind < 82)
        {
            // To work: at a Generator of the player's that is finished, or else at a deposit it holds.
            var site = WorkSiteFor(player);
            if (site == EntityId.None)
            {
                return;
            }

            PickSome(_random);
            commands.Add(new Command(CommandType.Gather, player, [.. Picked], targetEntity: site));
        }
        else if (kind < 90)
        {
            // A worker on its way to a site is sent somewhere else, which ends its build order.
            CollectWorkersWithOrders(player);
            var point = AnyPoint(_random);
            EntityId[] sent = _found.Count > 0 ? [_found[_random.NextInt(_found.Count)]] : [.. Own];
            commands.Add(new Command(_random.NextInt(3) == 0 ? CommandType.Stop : CommandType.Move, player, sent, point.X, point.Y));
        }
        else if (kind < 96)
        {
            // Everything in the match and one that never existed: only a worker of the player's can take it.
            Picked.Clear();
            for (var i = 0; i < Match.Bodies.Count; i++)
            {
                Picked.Add(Match.Bodies.EntityAt(i));
            }

            Picked.Add(new EntityId(1_000_000 + _random.NextInt(1000)));
            var structure = IndexOf("generator");
            commands.Add(Build(player, Picked, structure, SiteFor(player, structure)));
        }
        else
        {
            // From a player who is not in the match.
            var structure = IndexOf("generator");
            commands.Add(Build(Match.Players.Count + _random.NextInt(5), Own, structure, SiteFor(player, structure)));
        }
    }

    private static Command Build(int player, List<EntityId> workers, int structure, FixedVector2 point) =>
        new(CommandType.Build, player, [.. workers], point.X, point.Y, contentId: structure);

    private static Command Cancel(int player, List<EntityId> entities) =>
        new(CommandType.CancelBuild, player, [.. entities]);

    private int IndexOf(string id)
    {
        var roster = Match.Data.Rules.Structures;
        for (var i = 0; i < roster.Count; i++)
        {
            if (roster[i] == id)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// A point in the lowest tile of a site for the structure: one the order accepts, if one of
    /// the tiles tried is, and the last tile tried otherwise. A structure that needs the player's
    /// territory is tried in the square round one of the player's territories, and any other
    /// anywhere on the map.
    /// </summary>
    private FixedVector2 SiteFor(int player, int structure)
    {
        var def = Match.Data.StructureOf(structure);
        if (def is null)
        {
            return AnyPoint(_random);
        }

        var hasTerritory = TryPickTerritory(player, out var territory);
        var x = 0;
        var y = 0;
        for (var i = 0; i < SitesTried; i++)
        {
            if (ConstructionRules.NeedsTerritory(def) && hasTerritory)
            {
                var reach = (2 * territory.Radius) + 1;
                x = (territory.CenterX2 / 2) - territory.Radius + _random.NextInt(reach);
                y = (territory.CenterY2 / 2) - territory.Radius + _random.NextInt(reach);
            }
            else
            {
                x = _random.NextInt(Match.Grid.Width);
                y = _random.NextInt(Match.Grid.Height);
            }

            if (Match.Construction.CheckSiteAsKnown(player, def, x, y) == BuildProblem.None)
            {
                break;
            }
        }

        x = Math.Clamp(x, 0, Match.Grid.Width - 1);
        y = Math.Clamp(y, 0, Match.Grid.Height - 1);
        return new FixedVector2(
            Fixed.FromRaw((x * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)),
            Fixed.FromRaw((y * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)));
    }

    private bool TryPickTerritory(int player, out Territory territory)
    {
        var territories = Match.Territories;
        var own = 0;
        for (var i = 0; i < territories.Count; i++)
        {
            if (territories.ValueAt(i).Owner == player && Match.World.Entities.IsAlive(territories.EntityAt(i)))
            {
                own++;
            }
        }

        territory = default;
        if (own == 0)
        {
            return false;
        }

        var pick = _random.NextInt(own);
        for (var i = 0; i < territories.Count; i++)
        {
            if (territories.ValueAt(i).Owner == player && Match.World.Entities.IsAlive(territories.EntityAt(i)) && pick-- == 0)
            {
                territory = territories.ValueAt(i);
            }
        }

        return true;
    }

    /// <summary>The player's first finished Generator, or else the first deposit it holds, or none.</summary>
    private EntityId WorkSiteFor(int player)
    {
        var sites = Match.WorkSites;
        var deposit = EntityId.None;
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites.ValueAt(i);
            if (!Match.World.Entities.IsAlive(sites.EntityAt(i)))
            {
                continue;
            }

            if (site.Kind == WorkSiteKind.Generator && site.Owner == player)
            {
                return sites.EntityAt(i);
            }

            if (deposit == EntityId.None
                && site.Kind == WorkSiteKind.Deposit
                && Territory.Holds(Match.Territories, Match.World.Entities, player, site.X, site.Y, site.Size))
            {
                deposit = sites.EntityAt(i);
            }
        }

        return deposit;
    }

    private void CollectWorkersWithOrders(int player, bool keep = false)
    {
        if (!keep)
        {
            _found.Clear();
        }

        var orders = Match.BuildOrders;
        for (var i = 0; i < orders.Count; i++)
        {
            var worker = orders.EntityAt(i);
            if (Match.World.Entities.IsAlive(worker) && Match.Bodies.Get(worker).Owner == player)
            {
                _found.Add(worker);
            }
        }
    }

    private void CollectFoundations(int player)
    {
        _found.Clear();
        var foundations = Match.Foundations;
        for (var i = 0; i < foundations.Count; i++)
        {
            var structure = foundations.EntityAt(i);
            if (Match.World.Entities.IsAlive(structure) && Match.Bodies.Get(structure).Owner == player)
            {
                _found.Add(structure);
            }
        }
    }
}
