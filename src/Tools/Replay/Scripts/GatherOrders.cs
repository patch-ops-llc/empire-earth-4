using Sim.Core;
using Sim.Economy;

namespace Tools.Replay.Scripts;

/// <summary>
/// Gather orders (M13): workers sent to the deposit their owner holds, to any deposit, every
/// player's to one deposit in the same tick, which is more than it has places for, and gather
/// orders the simulation must drop.
/// </summary>
[OrderScript("gather")]
internal sealed class GatherOrders : OrderScript
{
    private const int LongestPause = 200;

    /// <summary>What the seed of the gather orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D13_0000_0000_0000;

    private readonly DeterministicRandom _random;
    private int _nextTick;

    public GatherOrders(ScriptSetup setup)
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
        var sites = Match.WorkSites;
        CollectUnitsOf(player);
        var open = OpenSites();
        if (Own.Count == 0 || open == 0)
        {
            return;
        }

        var anySite = OpenSite(_random.NextInt(open));
        if (kind < 40)
        {
            // To a deposit the player holds, where the workers earn. Any deposit when it holds none.
            var held = anySite;
            for (var i = 0; i < sites.Count; i++)
            {
                var site = sites.ValueAt(i);
                if (site.Kind == WorkSiteKind.Deposit && Territory.Holds(Match.Territories, Match.World.Entities, player, site.X, site.Y, site.Size))
                {
                    held = sites.EntityAt(i);
                    break;
                }
            }

            if (_random.NextInt(2) == 0)
            {
                PickSome(_random);
                commands.Add(Gather(player, Picked, held));
            }
            else
            {
                commands.Add(Gather(player, Own, held));
            }
        }
        else if (kind < 60)
        {
            // To any deposit, also one outside the player's territory, where they stand and earn nothing.
            PickSome(_random);
            commands.Add(Gather(player, Picked, anySite));
        }
        else if (kind < 80)
        {
            // Every player's units to one deposit in the same tick: more than it has places for.
            for (var other = 0; other < Match.Players.Count; other++)
            {
                CollectUnitsOf(other);
                if (Own.Count > 0)
                {
                    commands.Add(Gather(other, Own, anySite));
                }
            }
        }
        else if (kind < 88)
        {
            // Units of every player and one that never existed. Only the player's own are assigned.
            Picked.Clear();
            for (var i = 0; i < Match.Bodies.Count; i++)
            {
                Picked.Add(Match.Bodies.EntityAt(i));
            }

            Picked.Add(new EntityId(1_000_000 + _random.NextInt(1000)));
            commands.Add(Gather(player, Picked, anySite));
        }
        else if (kind < 94)
        {
            // At something that is no work site: a unit, or an entity that never existed.
            var target = _random.NextInt(2) == 0 ? Own[0] : new EntityId(1_000_000 + _random.NextInt(1000));
            commands.Add(Gather(player, Own, target));
        }
        else
        {
            commands.Add(Gather(Match.Players.Count + _random.NextInt(5), Own, anySite));
        }
    }

    /// <summary>
    /// The number of work sites the script sends workers to: the deposits and the finished
    /// Generators. A Generator is a work site while it builds itself too (design 4.1); the script
    /// leaves those out, as it did when only finished ones were work sites, so that the matches
    /// it records are the ones it recorded then.
    /// </summary>
    private int OpenSites()
    {
        var count = 0;
        for (var i = 0; i < Match.WorkSites.Count; i++)
        {
            if (IsOpen(i))
            {
                count++;
            }
        }

        return count;
    }

    private EntityId OpenSite(int pick)
    {
        for (var i = 0; i < Match.WorkSites.Count; i++)
        {
            if (IsOpen(i) && pick-- == 0)
            {
                return Match.WorkSites.EntityAt(i);
            }
        }

        throw new InvalidOperationException("There are fewer work sites than were counted.");
    }

    private bool IsOpen(int index) =>
        Match.WorkSites.ValueAt(index).Kind == WorkSiteKind.Deposit || !Match.Foundations.Contains(Match.WorkSites.EntityAt(index));

    private static Command Gather(int player, List<EntityId> units, EntityId site) =>
        new(CommandType.Gather, player, [.. units], targetEntity: site);
}
