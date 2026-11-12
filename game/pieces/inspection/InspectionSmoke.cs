using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Tech;

namespace Game;

/// <summary>
/// The smoke test's inspection: three seconds of play in which the local player is given one
/// fighting unit of each line, with the development spawn, and the inspection panel is asked
/// what it shows for one unit, for units of one kind, for units of several kinds, for a unit of
/// another player and for nothing.
/// </summary>
/// <remarks>
/// The piece gives no order of its own. The spawn is here because no match has a fighting unit
/// until units can be trained, and a panel that has only workers to show is not tested. Before
/// the first tick the part also words every chassis under a range of tiers, since a match of the
/// smoke test reaches one tier above the start and so shows no accent.
/// </remarks>
public sealed class InspectionSmoke : SmokePart
{
    // The spawn orders are carried out a few ticks after they are given.
    private const int LookAt = 40;
    private const int Length = 60;

    // Tiers for the six tracks, in the order of the data, that between them give every level of
    // every accent, tracks behind a chassis, and a chassis that cannot use all of a track.
    private static readonly int[][] TierMixes =
    [
        [1, 1, 1, 1, 1, 1],
        [2, 1, 1, 1, 1, 1],
        [3, 3, 1, 1, 1, 1],
        [1, 1, 3, 2, 1, 1],
        [1, 1, 1, 3, 3, 1],
        [4, 4, 4, 2, 2, 2],
        [4, 1, 2, 3, 4, 2],
    ];

    private readonly HashSet<EntityId> _before = [];
    private int _spawned;

    public override string Name => "Inspection";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        switch (tick)
        {
            case 0:
                WordEveryChassis(orders);
                Spawn(orders);
                break;
            case LookAt:
                Look(orders);
                break;
        }
    }

    // One unit of the oldest chassis of each line, in a row across the player's start.
    private void Spawn(SmokeOrders orders)
    {
        var match = orders.Match;
        if (!match.Data.Rules.Debug.Spawn)
        {
            return;
        }

        foreach (var entity in orders.OwnUnits())
        {
            _before.Add(entity);
        }

        var setup = match.Players[orders.Player];
        if (!setup.HasStart)
        {
            orders.Fail("the local player has no start to place units at.");
            return;
        }

        var start = match.Map.Starts[setup.Start];
        var lines = match.Data.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Chassis.Count == 0)
            {
                continue;
            }

            var point = new FixedVector2(Fixed.FromInt(start.X + i) + Fixed.Half, Fixed.FromInt(start.Y) + Fixed.Half);
            orders.Submit(new Command(CommandType.DebugSpawn, orders.Player, targetX: point.X, targetY: point.Y, contentId: lines[i].Chassis[0].Index));
            _spawned++;
        }
    }

    private void Look(SmokeOrders orders)
    {
        var view = orders.View;
        var match = orders.Match;
        if (view.Interface.Parts.OfType<InspectionPart>().FirstOrDefault() is not { } panel)
        {
            orders.Fail("the interface has no inspection panel.");
            return;
        }

        var fighting = new List<EntityId>();
        var workers = new List<EntityId>();
        foreach (var entity in orders.OwnUnits())
        {
            if (match.Workers.Contains(entity))
            {
                workers.Add(entity);
            }
            else if (!_before.Contains(entity) && match.Combatants.Contains(entity))
            {
                fighting.Add(entity);
            }
        }

        if (fighting.Count != _spawned)
        {
            orders.Fail($"{_spawned} fighting units were ordered {LookAt} ticks ago and {fighting.Count} are in the match.");
            return;
        }

        try
        {
            if (fighting.Count > 0)
            {
                view.Selection.Set([fighting[0]]);
                UnitFacts.TryRead(match, fighting[0], out var one);
                Expect(orders, panel, InspectionMode.One, rows: 1, InspectionWords.Name(one), "Firepower", "Protection", "Mobility", "Manufacturing", "Logistics");
            }

            if (fighting.Count > 1)
            {
                view.Selection.Set(fighting);
                Expect(orders, panel, InspectionMode.Mixed, rows: Math.Min(fighting.Count, InspectionPart.MostRows), "Tiers in use");
            }

            if (workers.Count > 1)
            {
                view.Selection.Set(workers);
                Expect(orders, panel, InspectionMode.Group, rows: 1, $"{workers.Count} × Worker", "not a chassis");
            }

            if (workers.Count > 0 && fighting.Count > 0)
            {
                view.Selection.Set([.. workers, .. fighting]);
                Expect(orders, panel, InspectionMode.Mixed, rows: Math.Min(fighting.Count + 1, InspectionPart.MostRows), "Worker");
            }

            view.Selection.Clear();
            Expect(orders, panel, InspectionMode.Nothing, rows: 0);

            var other = OtherPlayersUnit(orders);
            if (other != EntityId.None)
            {
                panel.Inspect(other);
                UnitFacts.TryRead(match, other, out var theirs);
                Expect(orders, panel, InspectionMode.Enemy, rows: 1, InspectionWords.Name(theirs), InspectionWords.Owner(theirs.Owner));

                // What an opponent may not know of a unit is not in the panel (design 3.2).
                var shown = panel.ShownText();
                if (shown.Contains("Price", StringComparison.Ordinal) || shown.Contains("Supply", StringComparison.Ordinal))
                {
                    orders.Fail("the panel shows the price or the supply of another player's unit.");
                }

                panel.Inspect(EntityId.None);
                Expect(orders, panel, InspectionMode.Nothing, rows: 0);
            }

            CheckBadges(orders);
        }
        finally
        {
            view.Selection.Clear();
            panel.Inspect(EntityId.None);
        }
    }

    private static void Expect(SmokeOrders orders, InspectionPart panel, InspectionMode mode, int rows, params string[] words)
    {
        if (panel.Mode != mode || panel.Groups.Count < rows)
        {
            orders.Fail($"the inspection panel is showing {panel.Mode} with {panel.Groups.Count} kinds, and {mode} with {rows} was expected.");
            return;
        }

        var shown = panel.ShownText();
        if (mode == InspectionMode.Nothing && shown.Length > 0)
        {
            orders.Fail("the inspection panel shows text with nothing to inspect.");
        }

        foreach (var word in words)
        {
            if (!shown.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                orders.Fail($"the inspection panel, showing {mode}, does not say '{word}'. It says: {shown.ReplaceLineEndings(" / ")}");
            }
        }
    }

    // The layer of the badges draws one for every accent of every fighting unit, and no other.
    private static void CheckBadges(SmokeOrders orders)
    {
        var match = orders.Match;
        if (orders.View.World.GetChildren().OfType<AccentBadges>().FirstOrDefault() is not { } badges)
        {
            orders.Fail("the world view has no layer of accent badges.");
            return;
        }

        var expected = 0;
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (match.Bodies.TryGet(entity, out var body) && match.World.Entities.IsAlive(entity))
            {
                expected += match.Research.Tech(body.Owner).Blueprints[match.Combatants.ValueAt(i).Chassis].Accents.Count;
            }
        }

        if (badges.Count != expected)
        {
            orders.Fail($"the world shows {badges.Count} accent badges and the fighting units carry {expected} accents.");
        }
    }

    private static EntityId OtherPlayersUnit(SmokeOrders orders)
    {
        var match = orders.Match;
        var best = EntityId.None;
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            var entity = match.Bodies.EntityAt(i);
            if (match.Bodies.ValueAt(i).Owner != orders.Player
                && match.Movers.Contains(entity)
                && match.World.Entities.IsAlive(entity)
                && (best == EntityId.None || entity.CompareTo(best) < 0))
            {
                best = entity;
            }
        }

        return best;
    }

    // Words every chassis of the data as the panel would, under each of the tier mixes, and
    // fails on a chassis or an accent the panel would have nothing to say about.
    private static void WordEveryChassis(SmokeOrders orders)
    {
        var data = orders.Match.Data;
        var accents = 0;
        foreach (var mix in TierMixes)
        {
            if (mix.Length != data.Tracks.Count || mix.Any(tier => tier > data.TierCount))
            {
                continue;
            }

            var tech = TechResolver.Resolve(data, mix);
            foreach (var blueprint in tech.Blueprints)
            {
                if (blueprint.Accents.Count > data.Rules.AccentSlots)
                {
                    orders.Fail($"{blueprint.Chassis.Id} carries {blueprint.Accents.Count} accents under tiers {string.Join(',', mix)}.");
                }

                if (InspectionWords.LineAndEpoch(data, blueprint).Length == 0 || InspectionWords.Attack(blueprint).Length == 0)
                {
                    orders.Fail($"the inspection panel has no words for {blueprint.Chassis.Id}.");
                }

                foreach (var accent in blueprint.Accents)
                {
                    accents++;
                    if (InspectionWords.AccentPerks(data, blueprint, accent).Length == 0)
                    {
                        orders.Fail($"the inspection panel cannot say what {InspectionWords.Accent(data, accent)} does on {blueprint.Chassis.Id}.");
                    }
                }
            }

            for (var player = 0; player < 2; player++)
            {
                if (InspectionWords.EpochSentence(data, player, local: 0, tech.Epoch, tech.EpochCap).Length == 0)
                {
                    orders.Fail("the epoch announcement has no sentence.");
                }
            }
        }

        if (accents == 0 && data.Accents.Count > 0)
        {
            orders.Fail("none of the tier mixes gives a chassis an accent, so no accent was worded.");
        }
    }
}
