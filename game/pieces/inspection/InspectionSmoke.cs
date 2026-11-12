using System.Globalization;
using Sim;
using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Perks;
using Sim.Setup;
using Sim.Tech;

namespace Game;

/// <summary>
/// The smoke test's inspection: three seconds of play in which the local player is given one
/// fighting unit of each line, with the development spawn, and the inspection panel is asked
/// what it shows for one unit, for units of one kind, for units of several kinds, for a unit of
/// another player and for nothing.
/// </summary>
/// <remarks>
/// <para>
/// The piece gives no order of its own. The spawn is here because no match has a fighting unit
/// until units can be trained, and a panel that has only workers to show is not tested. Before
/// the first tick the part also words every chassis under a range of tiers, since a match of the
/// smoke test reaches one tier above the start and so shows no accent.
/// </para>
/// <para>
/// For the same reason no unit of the match carries a perk. So the part also asks the panel
/// what it would show for a unit of every chassis under each of those tiers, with the counts of
/// its perks at each stage: charged and not, damaged a moment ago and regaining, paid for and
/// not. It asks the same of a unit of another player, which must show none of that.
/// </para>
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

    private const int MostRowsAway = 12;

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

    // One unit of the oldest chassis of each line, in a row beside the player's start: in the
    // nearest row of tiles that is open all along, because the start itself may be built on.
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
        if (OpenRow(match, start.X, start.Y, lines.Count) is not { } row)
        {
            orders.Fail("no row of open tiles near the local player's start has room for a unit of each line.");
            return;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Chassis.Count == 0)
            {
                continue;
            }

            var point = new FixedVector2(Fixed.FromInt(start.X + i) + Fixed.Half, Fixed.FromInt(row) + Fixed.Half);
            orders.Submit(new Command(CommandType.DebugSpawn, orders.Player, targetX: point.X, targetY: point.Y, contentId: lines[i].Chassis[0].Index));
            _spawned++;
        }
    }

    // The y of the row of tiles nearest to (x, y) in which a run of tiles from x on is open
    // ground, looking further away on either side in turn, or null when there is none close by.
    private static int? OpenRow(Match match, int x, int y, int length)
    {
        for (var away = 0; away <= MostRowsAway; away++)
        {
            foreach (var row in new[] { y + away, y - away })
            {
                var open = true;
                for (var i = 0; i < length && open; i++)
                {
                    open = match.Grid.IsPassable(x + i, row);
                }

                if (open)
                {
                    return row;
                }
            }
        }

        return null;
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
                Expect(
                    orders,
                    panel,
                    InspectionMode.One,
                    rows: 1,
                    InspectionWords.Name(one),
                    "Firepower",
                    "Protection",
                    "Mobility",
                    "Manufacturing",
                    "Logistics",
                    "An order makes",
                    one.Armor is { } armor ? InspectionWords.Armor(armor) : "Armor");
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
                // Under the fog of war the panel says nothing of a unit the player does not see
                // (design 5.3). The part then looks at it as a tester alone does, with the fog
                // lifted by the development switch.
                var fogWasOff = view.Sight.FogOff;
                if (!view.Sight.ShowsUnit(other))
                {
                    panel.Inspect(other);
                    Expect(orders, panel, InspectionMode.Nothing, rows: 0);
                    view.Sight.FogOff = true;
                }

                panel.Inspect(other);
                view.Sight.FogOff = fogWasOff;
                UnitFacts.TryRead(match, other, out var theirs);
                Expect(orders, panel, InspectionMode.Enemy, rows: 1, InspectionWords.Name(theirs), InspectionWords.Owner(theirs.Owner));

                // What an opponent may not know of a unit is not in the panel (design 3.2).
                if (Hidden(panel.ShownText()) is { } hidden)
                {
                    orders.Fail($"the panel shows '{hidden}' of another player's unit.");
                }

                panel.Inspect(EntityId.None);
                Expect(orders, panel, InspectionMode.Nothing, rows: 0);
            }

            CheckBadges(orders);
            ShowPerks(orders, panel);
            Expect(orders, panel, InspectionMode.Nothing, rows: 0);
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

    // What the panel must not say of another player's unit: its price and supply, how long an
    // order for it takes, and where it stands with its perks. Returns the first of them it says.
    // What an accent does is not among them: an opponent may know a unit's accents (design 3.2).
    private static string? Hidden(string shown)
    {
        foreach (var word in new[] { "Price", "Supply", "· An order makes", "Now:" })
        {
            if (shown.Contains(word, StringComparison.Ordinal))
            {
                return word;
            }
        }

        return null;
    }

    // Asks the panel what it would show for a unit of every chassis under each of the tier
    // mixes, at each stage of its perks, as the player's own and as another player's.
    private static void ShowPerks(SmokeOrders orders, InspectionPart panel)
    {
        var data = orders.Match.Data;
        var protection = data.TrackRoles.Protection;
        var shown = new HashSet<PerkKind>();
        var hardened = 0;
        foreach (var mix in TierMixes)
        {
            if (mix.Length != data.Tracks.Count || mix.Any(tier => tier > data.TierCount))
            {
                continue;
            }

            var tech = TechResolver.Resolve(data, mix);
            var combat = new CombatRules(data, 1, _ => tech);
            var kinds = new List<UnitGroup>();
            foreach (var blueprint in tech.Blueprints)
            {
                var perks = blueprint.Perks;
                var who = $"{blueprint.Chassis.Id} under tiers {string.Join(',', mix)}";
                var stages = Stages(blueprint, combat);

                // Every unit: its armor, and what an order makes.
                kinds.Add(UnitGroup.Of([stages.Ready, stages.Spent, stages.Regaining])[0]);
                var fresh = Shown(orders, panel, who, UnitGroup.OfOne(stages.Ready), enemy: false);
                Says(orders, who, fresh, perks.AreaDamageReduction is null ? "full area damage" : "less area damage");
                Says(orders, who, fresh, perks.Plating is null ? "no plating" : $"from attackers below Firepower {blueprint.EffectiveTiers[protection]}");
                Says(orders, who, fresh, blueprint.BatchSize == 2 ? "An order makes a pair in" : "An order makes one in");
                hardened += perks.AreaDamageReduction is null ? 0 : 1;

                var spent = Shown(orders, panel, who, UnitGroup.OfOne(stages.Spent), enemy: false);
                var regaining = Shown(orders, panel, who, UnitGroup.OfOne(stages.Regaining), enemy: false);
                var three = Shown(orders, panel, who, UnitGroup.Of([stages.Ready, stages.Spent, stages.Regaining])[0], enemy: false);

                if (perks.Charge is not null)
                {
                    shown.Add(PerkKind.Charge);
                    Says(orders, who, fresh, "Now: charged. Its next attack deals +");
                    Says(orders, who, spent, "Now: not charged. It has walked ");
                    Says(orders, who, three, "Now: 1 of 3 charged for their next attack.");
                    if (stages.Spent.IsCharged || !stages.Ready.IsCharged)
                    {
                        orders.Fail($"{who}: a unit a step short of the distance of Charge reads as charged, or one that has walked it does not.");
                    }
                }

                if (perks.Regeneration is { } regeneration)
                {
                    shown.Add(PerkKind.Regeneration);
                    Says(orders, who, fresh, "Now: at full hit points.");
                    Says(orders, who, spent, $"Now: damaged {Seconds(regeneration.DelayTicks - 1)} s ago. It regains hit points in 0.1 s.");
                    var rate = (regeneration.SharePerSecond.ToFraction() * blueprint.HitPoints).RoundHalfUp() / 1000d;
                    Says(orders, who, regaining, $"Now: damaged {Seconds(regeneration.DelayTicks)} s ago. It regains {rate.ToString("0.#", CultureInfo.InvariantCulture)} hit points a second.");
                    Says(orders, who, three, "Now: 1 regaining hit points and 1 damaged too lately to regain any.");
                }

                if (perks.DeathRefund is { } refund)
                {
                    shown.Add(PerkKind.DeathRefund);
                    var back = (refund.Share.ToFraction() * blueprint.Materials).RoundHalfUp();
                    Says(orders, who, fresh, back > 0
                        ? $"Now: {back} M would come back if it died, of the {blueprint.Materials} M paid for it."
                        : $"Now: nothing would come back, of the {blueprint.Materials} M paid for it.");
                    Says(orders, who, spent, "Now: nothing would come back, because nothing was paid for it.");
                }

                if (perks.Charge is null && perks.Regeneration is null && perks.DeathRefund is null
                    && (fresh + spent + regaining).Contains("Now:", StringComparison.Ordinal))
                {
                    orders.Fail($"{who}: the panel says where a unit stands with a perk it does not carry. It says: {fresh.ReplaceLineEndings(" / ")}");
                }

                // Another player's unit: its armor, which follows from what an opponent may see,
                // and nothing of what its owner alone knows.
                foreach (var stage in new[] { stages.Ready, stages.Spent, stages.Regaining })
                {
                    var theirs = Shown(orders, panel, who, UnitGroup.OfOne(stage), enemy: true);
                    Says(orders, who, theirs, InspectionWords.Armor(stage.Armor!));
                    if (Hidden(theirs) is { } hidden)
                    {
                        orders.Fail($"{who}: the panel shows '{hidden}' of another player's unit. It says: {theirs.ReplaceLineEndings(" / ")}");
                    }
                }
            }

            // A summary of several kinds, with every chassis under this mix as one of them.
            var summary = panel.WidthOfSummary(kinds);
            if (summary > InspectionPart.WidestShown)
            {
                orders.Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"under tiers {string.Join(',', mix)}, a summary of several kinds needs a width of {summary:F0} and may have {InspectionPart.WidestShown:F0}."));
            }
        }

        // The mixes are there to reach every perk the panel says something live about.
        foreach (var kind in new[] { PerkKind.Charge, PerkKind.Regeneration, PerkKind.DeathRefund })
        {
            if (!shown.Contains(kind) && data.Accents.Any(accent => accent.PerksAt(2).Kinds.Contains(kind)))
            {
                orders.Fail($"none of the tier mixes gives a chassis {kind}, so the panel was not asked about it.");
            }
        }

        if (hardened == 0 && data.Accents.Any(accent => accent.PerksAt(2).AreaDamageReduction is not null))
        {
            orders.Fail("none of the tier mixes gives a chassis less area damage, so the panel was not asked about that armor.");
        }
    }

    // What the panel shows for units of one kind. It may need no more width than the window
    // was laid out for: a longer headline or a wider row of cards pushes the bottom panel into
    // the minimap in the narrowest window.
    private static string Shown(SmokeOrders orders, InspectionPart panel, string who, UnitGroup group, bool enemy)
    {
        var shown = panel.ShownFor(group, enemy);
        if (panel.WidthNeeded > InspectionPart.WidestShown)
        {
            orders.Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"{who}: the detail of {(enemy ? "another player's unit" : "a unit")} needs a width of {panel.WidthNeeded:F0} and may have {InspectionPart.WidestShown:F0}. It says: {shown.ReplaceLineEndings(" / ")}"));
        }

        return shown;
    }

    /// <summary>
    /// The units of one kind, of the player's own, for which the panel shows the most text of
    /// all chassis under the tier mixes: the tallest detail there is, or null when the data has
    /// no chassis. The part that tests where the interface stands holds it in the panel.
    /// </summary>
    internal static UnitGroup? LongestDetail(GameData data, InspectionPart panel)
    {
        UnitGroup? longest = null;
        var most = 0;
        foreach (var mix in TierMixes)
        {
            if (mix.Length != data.Tracks.Count || mix.Any(tier => tier > data.TierCount))
            {
                continue;
            }

            var tech = TechResolver.Resolve(data, mix);
            var combat = new CombatRules(data, 1, _ => tech);
            foreach (var blueprint in tech.Blueprints)
            {
                var stages = Stages(blueprint, combat);
                foreach (var group in new[] { UnitGroup.OfOne(stages.Spent), UnitGroup.Of([stages.Ready, stages.Spent, stages.Regaining])[0] })
                {
                    var length = panel.ShownFor(group, enemy: false).Length;
                    if (length > most)
                    {
                        most = length;
                        longest = group;
                    }
                }
            }
        }

        return longest;
    }

    /// <summary>
    /// One kind more than a summary has rows for, each of three units of a chassis under the
    /// last of the tier mixes: the tallest summary there is.
    /// </summary>
    internal static List<UnitGroup> MostKinds(GameData data)
    {
        var kinds = new List<UnitGroup>();
        var mix = TierMixes[^1];
        if (mix.Length != data.Tracks.Count || mix.Any(tier => tier > data.TierCount))
        {
            return kinds;
        }

        var tech = TechResolver.Resolve(data, mix);
        var combat = new CombatRules(data, 1, _ => tech);
        foreach (var blueprint in tech.Blueprints.Take(InspectionPart.MostRows + 1))
        {
            var stages = Stages(blueprint, combat);
            kinds.Add(UnitGroup.Of([stages.Ready, stages.Spent, stages.Regaining])[0]);
        }

        return kinds;
    }

    // A unit of a chassis at three stages of its perks. Ready: it has walked the distance of
    // Charge, is undamaged and was paid for at today's price. Spent: one step short of that
    // distance, damaged one tick too lately to regain anything, and not paid for. Regaining:
    // damaged, and exactly the wait of Regeneration ago.
    private static (UnitFacts Ready, UnitFacts Spent, UnitFacts Regaining) Stages(ChassisBlueprint blueprint, CombatRules combat)
    {
        const int Tick = 100_000;
        var distance = combat.ProfileOf(0, blueprint.Chassis.Index).Charge?.Distance ?? Fixed.One;
        var delay = blueprint.Perks.Regeneration?.DelayTicks ?? SimInfo.TicksPerSecond;
        var maximum = blueprint.HitPoints;
        var damaged = Math.Max(1, maximum - 1000);

        UnitFacts Unit(long hitPoints, Fixed walked, int since, int paid) => UnitFacts.OfChassis(
            EntityId.None,
            owner: 0,
            blueprint,
            combat,
            hitPoints,
            maximum,
            blueprint.SpeedPerTick,
            new PerkCounts { DamagedAt = Tick - since, Walked = walked, Paid = paid },
            Tick);

        return (
            Unit(maximum, distance, since: 0, blueprint.Materials),
            Unit(damaged, Fixed.FromRaw(distance.Raw - 1), delay - 1, paid: 0),
            Unit(damaged, Fixed.Zero, delay, blueprint.Materials));
    }

    private static string Seconds(int ticks) =>
        (ticks / (double)SimInfo.TicksPerSecond).ToString("0.0", CultureInfo.InvariantCulture);

    private static void Says(SmokeOrders orders, string who, string shown, string words)
    {
        if (!shown.Contains(words, StringComparison.Ordinal))
        {
            orders.Fail($"{who}: the inspection panel does not say '{words}'. It says: {shown.ReplaceLineEndings(" / ")}");
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
