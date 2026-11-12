using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Vision;

namespace Tools.Replay.Scripts;

/// <summary>
/// The fog of war with structures (17b): workers sent out to explore and to look at what the
/// other players have built, Outposts ordered on ground the player has explored and on ground
/// it has not, structures ordered on ground of the player's own territory that it has not
/// explored, orders for sites on which a structure stands that the player remembers, and
/// foundations cancelled while another player remembers them.
/// </summary>
/// <remarks>
/// <para>
/// Fog adds no command of its own. The script gives the commands of movement, gathering and
/// construction in the situations the fog decides. It reads what each player has explored,
/// sees and remembers from the match, as it reads everything else, and it also reads what a
/// player may not know, to order what the player would order in good faith and the true state
/// then refuses.
/// </para>
/// <para>
/// A start gives a player six workers and no fighting unit, and the script makes none: the
/// workers explore, earn and build. It sends some to earn often enough that Outposts can be
/// paid for.
/// </para>
/// </remarks>
[OrderScript("settle")]
internal sealed class SettleOrders : OrderScript
{
    private const int LongestPause = 50;

    /// <summary>How many sites the script tries before it orders the last one it tried.</summary>
    private const int SitesTried = 60;

    /// <summary>How far from a structure a worker sent to look at it is sent, in tiles: within a worker's sight.</summary>
    private const int LookFrom = 3;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D17_B000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<EntityId> _found = [];
    private readonly List<RememberedStructure> _remembered = [];
    private int _nextTick;

    public SettleOrders(ScriptSetup setup)
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

        if (kind < 14)
        {
            // To work at a deposit the player holds, so that there is something to build with.
            var site = DepositOf(player);
            if (site != EntityId.None)
            {
                PickSome(_random);
                commands.Add(new Command(CommandType.Gather, player, [.. Picked], targetEntity: site));
            }
        }
        else if (kind < 24)
        {
            // Out to explore: one or two workers to a point anywhere on the map.
            PickFew();
            commands.Add(Move(player, Picked, AnyPoint(_random)));
        }
        else if (kind < 36)
        {
            // To look at a structure of another player that the player does not see now, and
            // first of all at one that it remembers: it sees it again, or sees that it is gone.
            // The worker nearest to it goes: a structure that is building itself is soon finished.
            if (TryPickUnseenStructure(player, out var tiles))
            {
                PickNearest(tiles.Center);
                commands.Add(Move(player, Picked, Beside(tiles)));
            }
        }
        else if (kind < 44)
        {
            // Home again, so that what was seen out there is seen no longer and is remembered:
            // the workers that stand where they see a structure of another player, or else a few.
            if (TryPickTerritory(player, out var home))
            {
                PickLookers(player);
                commands.Add(Move(player, Picked, new FixedVector2(Fixed.FromInt(home.CenterX2) / Fixed.FromInt(2), Fixed.FromInt(home.CenterY2) / Fixed.FromInt(2))));
            }
        }
        else if (kind < 62)
        {
            // An Outpost on ground the player has explored and knows nothing against. Half the
            // time the script looks for such a site too near the territory of another player,
            // which is judged only when the worker arrives.
            var outpost = Def("outpost");
            PickFew();
            commands.Add(Build(player, Picked, outpost, KnownSite(player, outpost, _random.NextInt(2) == 0 ? Wish.NearEnemyTerritory : Wish.None)));
        }
        else if (kind < 70)
        {
            // An Outpost or a Capitol on ground the player has not explored: it is refused.
            var structure = Def(_random.NextInt(3) == 0 ? "capitol" : "outpost");
            PickFew();
            commands.Add(Build(player, Picked, structure, UnexploredSite(player, structure)));
        }
        else if (kind < 82)
        {
            // A Generator inside the player's territory, on ground it has not explored if there
            // is any, and on ground that is in truth not free if the script finds some: the
            // order is taken, and the worker finds out when it arrives.
            var generator = Def("generator");
            PickFew();
            commands.Add(Build(player, Picked, generator, KnownSite(player, generator, Wish.NotAsItSeems)));
        }
        else if (kind < 90)
        {
            // An Outpost onto a structure the player remembers and does not see now, and first
            // of all onto one that is gone: it is refused, whether the structure is there or not.
            CollectRemembered(player, onlyGone: true);
            if (_remembered.Count == 0)
            {
                CollectRemembered(player, onlyGone: false);
            }

            if (_remembered.Count > 0)
            {
                var seen = _remembered[_random.NextInt(_remembered.Count)];
                var outpost = Def("outpost");
                var x = seen.X - _random.NextInt(outpost.Footprint);
                var y = seen.Y - _random.NextInt(outpost.Footprint);
                PickFew();
                commands.Add(Build(player, Picked, outpost, PointIn(x, y)));
            }
        }
        else
        {
            // A foundation is cancelled: one that another player remembers and does not see now,
            // if the player has one, and otherwise, one time in four, any of its foundations.
            CollectFoundations(player, onlyRemembered: true);
            if (_found.Count == 0 && _random.NextInt(4) == 0)
            {
                CollectFoundations(player, onlyRemembered: false);
            }

            if (_found.Count > 0)
            {
                commands.Add(new Command(CommandType.CancelBuild, player, [_found[_random.NextInt(_found.Count)]]));
            }
        }
    }

    /// <summary>What the script would like of a site, beyond that the order for it is taken.</summary>
    private enum Wish
    {
        None,

        /// <summary>Too near the territory of another player, which the true state refuses on arrival.</summary>
        NearEnemyTerritory,

        /// <summary>In truth not free, or else not wholly explored.</summary>
        NotAsItSeems,
    }

    private static Command Move(int player, List<EntityId> units, FixedVector2 point) =>
        new(CommandType.Move, player, [.. units], point.X, point.Y);

    private static Command Build(int player, List<EntityId> workers, StructureDef structure, FixedVector2 point) =>
        new(CommandType.Build, player, [.. workers], point.X, point.Y, contentId: structure.Index);

    private StructureDef Def(string id) => Match.Data.StructureOf(id);

    /// <summary>Puts one or two of the player's units into <see cref="OrderScript.Picked"/>.</summary>
    private void PickFew()
    {
        Picked.Clear();
        var first = _random.NextInt(Own.Count);
        Picked.Add(Own[first]);
        if (Own.Count > 1 && _random.NextInt(2) == 0)
        {
            Picked.Add(Own[(first + 1 + _random.NextInt(Own.Count - 1)) % Own.Count]);
        }
    }

    /// <summary>Puts the player's unit nearest to a point into <see cref="OrderScript.Picked"/>: of two equally near, the older.</summary>
    private void PickNearest(FixedVector2 point)
    {
        Picked.Clear();
        var nearest = Own[0];
        var least = long.MaxValue;
        foreach (var unit in Own)
        {
            var distance = FixedVector2.DistanceSquaredRaw(Match.Bodies.Get(unit).Position, point);
            if (distance < least)
            {
                least = distance;
                nearest = unit;
            }
        }

        Picked.Add(nearest);
    }

    /// <summary>
    /// Puts the player's units that stand within a worker's sight of a structure of another
    /// player into <see cref="OrderScript.Picked"/>, and one or two of its units when none does.
    /// </summary>
    private void PickLookers(int player)
    {
        Picked.Clear();
        var sight = FixedVector2.SquareRaw(Fixed.FromInt(Match.Data.Resources.Worker.Sight));
        var structures = Match.Structures;
        foreach (var unit in Own)
        {
            var position = Match.Bodies.Get(unit).Position;
            for (var i = 0; i < structures.Count; i++)
            {
                var entity = structures.EntityAt(i);
                if (Match.World.Entities.IsAlive(entity)
                    && Match.Bodies.Get(entity).Owner != player
                    && structures.ValueAt(i).Tiles.DistanceSquaredRawTo(position) <= sight)
                {
                    Picked.Add(unit);
                    break;
                }
            }
        }

        if (Picked.Count == 0)
        {
            PickFew();
        }
    }

    /// <summary>A point somewhere in a tile. The tile is brought onto the map first.</summary>
    private FixedVector2 PointIn(int x, int y)
    {
        x = Math.Clamp(x, 0, Match.Grid.Width - 1);
        y = Math.Clamp(y, 0, Match.Grid.Height - 1);
        return new FixedVector2(
            Fixed.FromRaw((x * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)),
            Fixed.FromRaw((y * Fixed.RawOne) + _random.NextInt(Fixed.RawOne)));
    }

    /// <summary>
    /// A point in the lowest tile of a site that the player's order is taken for, as far as the
    /// player knows: the first of the tiles tried that also has what the script wishes for, or
    /// else the first the order is taken for, or else the last tile tried.
    /// </summary>
    private FixedVector2 KnownSite(int player, StructureDef structure, Wish wish)
    {
        var rules = Match.Construction;
        var hasTerritory = TryPickTerritory(player, out var territory);
        var (x, y) = (0, 0);
        var (takenX, takenY) = (-1, -1);
        for (var i = 0; i < SitesTried; i++)
        {
            if (ConstructionRules.NeedsTerritory(structure) && hasTerritory)
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

            if (rules.CheckSiteAsKnown(player, structure, x, y) != BuildProblem.None)
            {
                continue;
            }

            var wished = wish switch
            {
                Wish.NearEnemyTerritory => rules.IsTooCloseToEnemyTerritory(player, structure, x, y),
                Wish.NotAsItSeems => rules.CheckSite(player, structure, x, y) != BuildProblem.None,
                _ => true,
            };
            if (wished)
            {
                return PointIn(x, y);
            }

            // Of the sites the order is taken for, one that is not wholly explored is kept before one that is.
            var unexplored = !Match.Vision.Grid.AreAllExplored(player, new TileSquare(x, y, structure.Footprint));
            if (takenX < 0 || (wish == Wish.NotAsItSeems && unexplored))
            {
                (takenX, takenY) = (x, y);
            }
        }

        return takenX < 0 ? PointIn(x, y) : PointIn(takenX, takenY);
    }

    /// <summary>A point in the lowest tile of a site on the map that the player has not wholly explored, if a few draws find one.</summary>
    private FixedVector2 UnexploredSite(int player, StructureDef structure)
    {
        var (x, y) = (0, 0);
        for (var i = 0; i < SitesTried; i++)
        {
            x = _random.NextInt(Match.Grid.Width - structure.Footprint + 1);
            y = _random.NextInt(Match.Grid.Height - structure.Footprint + 1);
            if (!Match.Vision.Grid.AreAllExplored(player, new TileSquare(x, y, structure.Footprint)))
            {
                break;
            }
        }

        return PointIn(x, y);
    }

    /// <summary>A point <see cref="LookFrom"/> tiles from the tiles of a structure, to one side of it. It may be on blocked ground.</summary>
    private FixedVector2 Beside(TileSquare tiles)
    {
        var (x, y) = _random.NextInt(4) switch
        {
            0 => (tiles.X - LookFrom, tiles.Y + _random.NextInt(tiles.Size)),
            1 => (tiles.X + tiles.Size - 1 + LookFrom, tiles.Y + _random.NextInt(tiles.Size)),
            2 => (tiles.X + _random.NextInt(tiles.Size), tiles.Y - LookFrom),
            _ => (tiles.X + _random.NextInt(tiles.Size), tiles.Y + tiles.Size - 1 + LookFrom),
        };
        return PointIn(x, y);
    }

    /// <summary>
    /// The tiles of a structure of another player that the player does not see now. One time in
    /// four it is one the player remembers and that is gone, if there is one, and one time in
    /// four any it remembers; otherwise, or when there is none of those, any that stands.
    /// </summary>
    private bool TryPickUnseenStructure(int player, out TileSquare tiles)
    {
        var way = _random.NextInt(4);
        _remembered.Clear();
        if (way < 2)
        {
            CollectRemembered(player, onlyGone: way == 0);
        }

        if (_remembered.Count > 0)
        {
            tiles = _remembered[_random.NextInt(_remembered.Count)].Tiles;
            return true;
        }

        // A foundation first: it can be cancelled, and then it is gone when the player looks again.
        _found.Clear();
        var structures = Match.Structures;
        for (var pass = 0; pass < 2 && _found.Count == 0; pass++)
        {
            for (var i = 0; i < structures.Count; i++)
            {
                var entity = structures.EntityAt(i);
                if (Match.World.Entities.IsAlive(entity)
                    && Match.Bodies.Get(entity).Owner != player
                    && !Match.Vision.Sees(player, entity)
                    && (pass == 1 || Match.Foundations.Contains(entity)))
                {
                    _found.Add(entity);
                }
            }
        }

        if (_found.Count == 0)
        {
            tiles = default;
            return false;
        }

        tiles = structures.Get(_found[_random.NextInt(_found.Count)]).Tiles;
        return true;
    }

    /// <summary>The structures the player remembers and does not see now, in <see cref="_remembered"/>: all of them, or only those that are gone.</summary>
    private void CollectRemembered(int player, bool onlyGone)
    {
        _remembered.Clear();
        var memory = Match.Vision.Memory;
        for (var i = 0; i < memory.Count(player); i++)
        {
            ref readonly var seen = ref memory.At(player, i);
            var gone = !Match.World.Entities.IsAlive(seen.Entity);
            if (onlyGone ? gone : gone || !Match.Vision.Sees(player, seen.Entity))
            {
                _remembered.Add(seen);
            }
        }
    }

    /// <summary>
    /// The player's structures that are building themselves, in <see cref="_found"/>: all of
    /// them, or only those that another player remembers and does not see now.
    /// </summary>
    private void CollectFoundations(int player, bool onlyRemembered)
    {
        _found.Clear();
        var foundations = Match.Foundations;
        for (var i = 0; i < foundations.Count; i++)
        {
            var structure = foundations.EntityAt(i);
            if (!Match.World.Entities.IsAlive(structure) || Match.Bodies.Get(structure).Owner != player)
            {
                continue;
            }

            if (!onlyRemembered || IsRememberedUnseen(player, structure))
            {
                _found.Add(structure);
            }
        }
    }

    private bool IsRememberedUnseen(int owner, EntityId structure)
    {
        for (var other = 0; other < Match.Players.Count; other++)
        {
            if (other != owner && Match.Vision.Memory.TryGet(other, structure, out _) && !Match.Vision.Sees(other, structure))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>The first deposit the player holds, or none.</summary>
    private EntityId DepositOf(int player)
    {
        var sites = Match.WorkSites;
        for (var i = 0; i < sites.Count; i++)
        {
            var site = sites.ValueAt(i);
            if (Match.World.Entities.IsAlive(sites.EntityAt(i))
                && site.Kind == WorkSiteKind.Deposit
                && Territory.Holds(Match.Territories, Match.World.Entities, player, site.X, site.Y, site.Size))
            {
                return sites.EntityAt(i);
            }
        }

        return EntityId.None;
    }
}
