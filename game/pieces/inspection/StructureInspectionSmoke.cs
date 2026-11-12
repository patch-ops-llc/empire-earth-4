using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Tech;
using Sim.Vision;

namespace Game;

/// <summary>
/// The smoke test's inspection of structures: up to forty-five seconds of play in which one of the
/// local player's units walks to a structure of the other seat's, sees it, and walks back. On
/// the way the part clicks on the structure and asks the inspection panel what it shows, and
/// fails when the panel shows a structure the player has never seen, shows no tiers for one
/// they see, or shows one they only remember as anything but what they last saw of it.
/// </summary>
/// <remarks>
/// <para>
/// The steps:
/// </para>
/// <list type="bullet">
/// <item>Before the first tick the part asks the panel what it would show for every structure
/// of the roster under a range of tiers: as another player's that is seen, as one that is
/// remembered, and as the player's own. A match of the smoke test reaches one tier above the
/// start, and has no Tower.</item>
/// <item>It selects one structure of the local player's, and then two when there are two: the
/// panel must show their tiers. It asks for a structure of the other seat's that the player has
/// never seen, when there is one: the panel must show nothing.</item>
/// <item>The nearest unit of the local player is sent to a structure of the other seat's. When
/// the player remembers that structure from an earlier part, a click on it must show the
/// memory first.</item>
/// <item>In the tick the player first sees it, a click on it must show it as it stands, with
/// its owner's Protection tier as the rules have it, and the mark must lie round it. The unit
/// is sent back.</item>
/// <item>In the tick the player first no longer sees it, the panel must have gone over to the
/// memory by itself: the hit points and the look as last seen, no tiers, and the sentence that
/// says so. The development switch must then show it as it stands, with its tiers, and laying
/// the fog back must make it a memory again.</item>
/// <item>A selection of the player's own must take the panel from it, and a click on open
/// ground must let it go.</item>
/// </list>
/// <para>
/// The part gives two move orders and no other, and leaves nothing selected. It holds whatever
/// ran before it: it needs a structure of the other seat's that stands, and a unit of the local
/// player's.
/// </para>
/// </remarks>
public sealed class StructureInspectionSmoke : SmokePart
{
    private const int Length = 900;

    // The age of the memory the panel is asked to word before the first tick.
    private const int WordedAgo = 300;

    // Tiers for the tracks, in the order of the data. Between them they give a Protection tier
    // with and without the fortification rule, tiers that differ from track to track, and a
    // Protection tier from which a Tower hits every line.
    private static readonly int[][] TierMixes =
    [
        [1, 1, 1, 1, 1, 1],
        [2, 3, 1, 4, 2, 3],
        [3, 1, 4, 2, 1, 2],
        [5, 5, 5, 5, 5, 5],
    ];

    private readonly List<SeenStructure> _listed = [];
    private StructureInspection? _panel;
    private InspectedStructureMark? _mark;
    private SeenStructure _target;
    private EntityId _scout = EntityId.None;
    private FixedVector2 _home;
    private int _other;
    private int _worded;
    private int _clicks;
    private int _seenAt = -1;
    private int _rememberedAt = -1;
    private bool _rememberedBefore;
    private bool _ownShown;
    private bool _unknownAsked;
    private bool _tested;

    public override string Name => "Inspection of structures";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (orders.Failed)
        {
            return;
        }

        if (tick == 0)
        {
            Begin(orders);
        }
        else if (tick == Length - 1)
        {
            Judge(orders);
        }
        else if (_panel is not null)
        {
            Follow(orders, tick);
        }
    }

    private void Begin(SmokeOrders orders)
    {
        var view = orders.View;
        var match = orders.Match;
        _panel = view.Interface.Parts.OfType<StructureInspection>().FirstOrDefault();
        _mark = view.World.GetChildren().OfType<InspectedStructureMark>().FirstOrDefault();
        if (_panel is null || _mark is null)
        {
            orders.Fail("the view has no inspection panel for structures, or no mark for the structure it is about.");
            return;
        }

        WordEveryStructure(orders, _panel);
        if (orders.Failed)
        {
            return;
        }

        ShowOwn(orders, _panel);
        if (orders.Failed)
        {
            return;
        }

        // The structures of the other seat's that stand, whoever may see them. One the player
        // knows of already is the one to walk to, so that the memory is shown before and after.
        _other = (orders.Player + 1) % match.Players.Count;
        PlayerSight.CollectStanding(match, _listed);
        var found = false;
        foreach (var standing in _listed)
        {
            if (standing.Owner != _other || match.Data.StructureOf(standing.Type) is not { Wall: null })
            {
                continue;
            }

            var known = view.Sight.Seen.KnowsStructure(standing.Entity);
            if (!known && !_unknownAsked)
            {
                _unknownAsked = true;
                _panel.Inspect(standing.Entity);
                if (_panel.Mode != StructureInspectionMode.Nothing || _mark.Structure != EntityId.None)
                {
                    orders.Fail($"the panel shows a structure of the other seat's that the local player has never seen: {_panel.ShownText().ReplaceLineEndings(" / ")}");
                    return;
                }
            }

            if (!found || (known && !view.Sight.Seen.KnowsStructure(_target.Entity)))
            {
                _target = standing;
                found = true;
            }
        }

        if (match.Players.Count < 2 || !found)
        {
            orders.Fail("the part needs a structure of another seat's to inspect.");
            return;
        }

        if (view.Sight.Seen.TryGetStructure(_target.Entity, out var memory) && memory.IsRemembered)
        {
            _rememberedBefore = true;
            Click(orders, _panel, in memory);
            MustShowMemory(orders, "before the unit is sent, of a structure remembered from an earlier part");
            _panel.Inspect(EntityId.None);
            if (orders.Failed)
            {
                return;
            }
        }

        SendScout(orders);
    }

    private void Follow(SmokeOrders orders, int tick)
    {
        var view = orders.View;
        var panel = _panel!;
        var known = view.Sight.Seen.TryGetStructure(_target.Entity, out var seen);
        if (_seenAt < 0 && known && !seen.IsRemembered)
        {
            _seenAt = tick;
            Click(orders, panel, in seen);
            MustShowStanding(orders, "in the tick the player first sees it, after a click on it");
            orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], _home.X, _home.Y));
        }
        else if (_seenAt >= 0 && _rememberedAt < 0 && known && !seen.IsRemembered)
        {
            MustShowStanding(orders, "while the player sees it");
        }
        else if (_seenAt >= 0 && _rememberedAt < 0 && known && seen.IsRemembered)
        {
            _rememberedAt = tick;
            MustShowMemory(orders, "in the tick the player first no longer sees it");
            view.Sight.FogOff = true;
        }
        else if (_rememberedAt >= 0 && tick == _rememberedAt + 1)
        {
            MustShowStanding(orders, "remembered, with the fog lifted by the development switch");
            view.Sight.FogOff = false;
        }
        else if (_rememberedAt >= 0 && tick == _rememberedAt + 2)
        {
            MustShowMemory(orders, "after the development switch laid the fog back over it");
            if (!orders.Failed)
            {
                MustGiveWay(orders, panel);
            }

            _tested = !orders.Failed;
        }
    }

    // A selection of the player's own takes the panel, the memory is back when it is cleared,
    // and a click that is not on a structure of another player's lets it go.
    private void MustGiveWay(SmokeOrders orders, StructureInspection panel)
    {
        var view = orders.View;
        view.Selection.Set([_scout]);
        if (panel.Mode != StructureInspectionMode.Nothing || _mark!.Structure != EntityId.None)
        {
            orders.Fail($"the panel still shows the other seat's structure, in mode {panel.Mode}, with a unit of the player's own selected.");
            return;
        }

        view.Selection.Clear();
        if (panel.Mode != StructureInspectionMode.Remembered)
        {
            orders.Fail($"the panel is in mode {panel.Mode} once the selection is cleared, and showed a memory before it.");
            return;
        }

        // The click is on the unit that walked there, which is the player's own.
        var at = WorldView.ToWorld(orders.Match.Bodies.Get(_scout).Position);
        var looked = view.Camera.Focus;
        view.Camera.CenterOn(new Vector2(at.X, at.Z));
        if (view.World.GetViewport().GetCamera3D() is { } camera)
        {
            panel.ClickAt(camera.UnprojectPosition(at));
        }
        else
        {
            panel.Inspect(EntityId.None);
        }

        view.Camera.CenterOn(looked);
        if (panel.Mode != StructureInspectionMode.Nothing || panel.ShownText().Length > 0 || _mark.Structure != EntityId.None)
        {
            orders.Fail($"the panel is in mode {panel.Mode} after a click that is not on a structure of another player's.");
        }
    }

    // Looks at the structure and clicks on the middle of it, as the player does. A unit of the
    // other seat's that stands in front of that point is what such a click would be on, so the
    // panel is then asked for the structure directly. The view is put back where it was.
    private void Click(SmokeOrders orders, StructureInspection panel, in SeenStructure structure)
    {
        var view = orders.View;
        var camera = view.World.GetViewport().GetCamera3D();
        if (camera is null)
        {
            panel.Inspect(structure.Entity);
            return;
        }

        var looked = view.Camera.Focus;
        var middle = new Vector2(structure.X + (structure.Size / 2f), structure.Y + (structure.Size / 2f));
        view.Camera.CenterOn(middle);
        var screen = camera.UnprojectPosition(new Vector3(middle.X, 0.3f, middle.Y));
        if (view.UnderPointer.At(screen, PointerTargets.EnemyUnit).Is(PointerTargets.EnemyUnit))
        {
            panel.Inspect(structure.Entity);
        }
        else
        {
            panel.ClickAt(screen);
            _clicks++;
        }

        view.Camera.CenterOn(looked);
    }

    // The panel shows the structure as it stands: the rules' tiers for it, and its hit points now.
    private void MustShowStanding(SmokeOrders orders, string when)
    {
        var match = orders.Match;
        var data = match.Data;
        var panel = _panel!;
        var shown = panel.ShownText();
        var said = shown.ReplaceLineEndings(" / ");
        if (panel.Mode != StructureInspectionMode.Seen || panel.Structures.Count != 1 || panel.Structures[0].Entity != _target.Entity)
        {
            orders.Fail($"the panel is in mode {panel.Mode} and not about the other seat's structure as it stands, {when}: {said}");
            return;
        }

        var facts = panel.Structures[0];
        var built = match.Research.Tech(_other).StructureOf(_target.Type);
        match.Health.TryGet(_target.Entity, out var health);
        if (built is null || !facts.HasTiers || facts.Protection != built.ProtectionTier || facts.Firepower != (built.Attack?.EffectiveFirepower ?? 0))
        {
            orders.Fail($"the panel has Protection {facts.Protection} and Firepower {facts.Firepower} for the other seat's structure, {when}, and its owner's blueprint has {built?.ProtectionTier} and {built?.Attack?.EffectiveFirepower ?? 0}.");
        }
        else if (facts.HitPoints != health.HitPoints || facts.Maximum != health.Maximum)
        {
            orders.Fail($"the panel has {facts.HitPoints} of {facts.Maximum} hit points for the other seat's structure, {when}, and it has {health.HitPoints} of {health.Maximum}.");
        }
        else if (!shown.Contains(StructureInspectionWords.Tier(data, data.TrackRoles.Protection, built.ProtectionTier), StringComparison.Ordinal)
            || !shown.Contains(InspectionWords.Health(health.HitPoints, health.Maximum), StringComparison.Ordinal)
            || !shown.Contains(InspectionWords.Owner(_other), StringComparison.Ordinal)
            || !shown.Contains(StructureInspectionWords.NoMobilityNoAccents, StringComparison.Ordinal))
        {
            orders.Fail($"the panel does not name the owner, the hit points and the Protection tier of the other seat's structure, {when}: {said}");
        }
        else if (shown.Contains("As you last saw it", StringComparison.Ordinal))
        {
            orders.Fail($"the panel calls a structure the player sees a memory, {when}: {said}");
        }
        else
        {
            MarkMustLieRound(orders, when);
        }
    }

    // The panel shows the structure as the player last saw it, and nothing the player has not seen.
    private void MustShowMemory(SmokeOrders orders, string when)
    {
        var view = orders.View;
        var data = orders.Match.Data;
        var panel = _panel!;
        var shown = panel.ShownText();
        var said = shown.ReplaceLineEndings(" / ");
        if (!view.Sight.Seen.TryGetStructure(_target.Entity, out var memory) || !memory.IsRemembered)
        {
            orders.Fail($"the local player does not remember the other seat's structure, {when}.");
            return;
        }

        if (panel.Mode != StructureInspectionMode.Remembered || panel.Structures.Count != 1 || panel.Structures[0].Entity != _target.Entity)
        {
            orders.Fail($"the panel is in mode {panel.Mode} and not about the memory of the other seat's structure, {when}: {said}");
            return;
        }

        var facts = panel.Structures[0];
        if (facts.HasTiers || facts.Attack is not null || shown.Contains(" hit points · ", StringComparison.Ordinal))
        {
            orders.Fail($"the panel shows the tiers of a structure the player only remembers, {when}: {said}");
        }
        else if (facts.HitPoints != memory.HitPoints || facts.Maximum != memory.MaxHitPoints || facts.Built != memory.Built || facts.IsFinished != memory.IsFinished || facts.LookTier != memory.LookTier)
        {
            orders.Fail($"the panel has the other seat's structure otherwise than the player last saw it, {when}: {facts.HitPoints} of {facts.Maximum} hit points, built for {facts.Built} ticks.");
        }
        else if (!shown.Contains("As you last saw it", StringComparison.Ordinal)
            || !shown.Contains(InspectionWords.Health(memory.HitPoints, memory.MaxHitPoints), StringComparison.Ordinal)
            || !shown.Contains(StructureInspectionWords.TiersWhileSeen(in facts), StringComparison.Ordinal))
        {
            orders.Fail($"the panel does not say that it shows a memory, with the hit points last seen, {when}: {said}");
        }
        else if (facts.Structure.Track is { } track
            && memory.LookTier > 0
            && !shown.Contains(StructureInspectionWords.Tier(data, track, memory.LookTier), StringComparison.Ordinal))
        {
            orders.Fail($"the panel does not show the tier the structure's look showed when the player last saw it, {when}: {said}");
        }
        else
        {
            MarkMustLieRound(orders, when);
        }
    }

    private void MarkMustLieRound(SmokeOrders orders, string when)
    {
        if (_mark!.Structure != _target.Entity || _mark.Marked != (_target.X, _target.Y, _target.Size))
        {
            orders.Fail($"the mark does not lie round the other seat's structure the panel is about, {when}.");
        }
    }

    // One structure of the local player's selected, and then two: the tiers of their owner.
    private void ShowOwn(SmokeOrders orders, StructureInspection panel)
    {
        var view = orders.View;
        var match = orders.Match;
        var data = match.Data;
        var roles = data.TrackRoles;
        view.Sight.CollectStructures(_listed);
        var own = _listed.FindAll(structure => structure.Owner == orders.Player);
        if (own.Count == 0)
        {
            return;
        }

        var tech = match.Research.Tech(orders.Player);
        var protection = StructureInspectionWords.Tier(data, roles.Protection, tech.Tiers[roles.Protection]);
        view.Selection.Set([own[0].Entity]);
        var shown = panel.ShownText();
        var structure = data.StructureOf(own[0].Type);
        if (panel.Mode != StructureInspectionMode.Own
            || !shown.Contains(protection, StringComparison.Ordinal)
            || !shown.Contains($"{InspectionWords.Whole(own[0].MaxHitPoints)} hit points", StringComparison.Ordinal))
        {
            orders.Fail($"the panel is in mode {panel.Mode} for a selected structure of the player's own and does not show its Protection tier and hit points: {shown.ReplaceLineEndings(" / ")}");
        }
        else if (structure?.Track is { } track
            && track != roles.Protection
            && !shown.Contains(StructureInspectionWords.Tier(data, track, tech.Tiers[track]), StringComparison.Ordinal))
        {
            orders.Fail($"the panel does not show the tier a selected structure of the player's own shows by its look: {shown.ReplaceLineEndings(" / ")}");
        }
        else if (own.Count > 1)
        {
            view.Selection.Set([own[0].Entity, own[1].Entity]);
            shown = panel.ShownText();
            if (panel.Mode != StructureInspectionMode.Several || !shown.Contains(protection, StringComparison.Ordinal))
            {
                orders.Fail($"the panel is in mode {panel.Mode} for two selected structures of the player's own and does not show their Protection tier: {shown.ReplaceLineEndings(" / ")}");
            }
        }

        _ownShown = !orders.Failed;

        // Another player's structure is shown while nothing of the player's own is selected.
        view.Selection.Clear();
    }

    // Every structure of the roster under each mix of tiers, as the panel words it for another
    // player's that is seen, for one that is remembered, and for the player's own.
    private void WordEveryStructure(SmokeOrders orders, StructureInspection panel)
    {
        var match = orders.Match;
        var data = match.Data;
        var roles = data.TrackRoles;
        var fortification = match.Combat.Fortification;
        var other = (orders.Player + 1) % Math.Max(match.Players.Count, 1);
        foreach (var mix in TierMixes)
        {
            if (mix.Length != data.Tracks.Count)
            {
                continue;
            }

            var tech = TechResolver.Resolve(data, mix);
            foreach (var structure in data.Structures)
            {
                if (tech.StructureOf(structure.Index) is not { } built)
                {
                    continue;
                }

                var look = structure.Track is { } track ? mix[track] : 0;
                var standing = new SeenStructure(
                    EntityId.None,
                    other,
                    structure.Index,
                    0,
                    0,
                    structure.Footprint,
                    built.HitPoints,
                    built.HitPoints,
                    IsFinished: true,
                    structure.BuildTicks,
                    look,
                    SeenStructure.NoTrack,
                    IsRemembered: false,
                    SeenTick: 0);
                var seen = panel.ShownFor(StructureFacts.Of(structure, in standing, built, fortification, WordedAgo), own: false);
                var remembered = panel.ShownFor(
                    StructureFacts.Of(structure, standing with { IsRemembered = true }, null, fortification, WordedAgo),
                    own: false);
                var own = panel.ShownFor(
                    StructureFacts.Of(structure, standing with { Owner = orders.Player }, built, fortification, WordedAgo),
                    own: true);
                _worded += 3;

                var protection = StructureInspectionWords.Tier(data, roles.Protection, mix[roles.Protection]);
                var firepower = StructureInspectionWords.Tier(data, roles.Firepower, mix[roles.Firepower]);
                var fortified = StructureInspectionWords.Fortified(fortification, mix[roles.Protection]);
                var looks = structure.Track is { } shows ? StructureInspectionWords.Tier(data, shows, look) : null;
                string? wrong = null;
                foreach (var (text, whose) in new[] { (seen, "another player's that is seen"), (own, "the player's own") })
                {
                    if (!text.Contains(protection, StringComparison.Ordinal) || !text.Contains(fortified, StringComparison.Ordinal))
                    {
                        wrong = $"as {whose} it does not show {protection} with \"{fortified}\"";
                    }
                    else if (built.Attack is { } attack
                        && (!text.Contains(firepower, StringComparison.Ordinal)
                            || !text.Contains($"{InspectionWords.Whole(attack.DamagePerSecond)} damage a second", StringComparison.Ordinal)
                            || text.Contains("cannot hit", StringComparison.Ordinal) != (attack.CannotHit.Count > 0)))
                    {
                        wrong = $"as {whose} it does not show {firepower} with the damage of its attack and the lines it cannot hit";
                    }
                    else if (built.Attack is null && text.Contains("damage a second", StringComparison.Ordinal))
                    {
                        wrong = $"as {whose} it shows an attack, and it has none";
                    }
                    else if (looks is not null && !text.Contains(looks, StringComparison.Ordinal))
                    {
                        wrong = $"as {whose} it does not show {looks}, the tier of its look";
                    }
                }

                if (wrong is null && !seen.Contains(StructureInspectionWords.NoMobilityNoAccents, StringComparison.Ordinal))
                {
                    wrong = "as another player's that is seen it does not say that a structure has no Mobility tier and no accents";
                }
                else if (wrong is null
                    && (!remembered.Contains(StructureInspectionWords.Memory(WordedAgo), StringComparison.Ordinal)
                        || remembered.Contains(" hit points · ", StringComparison.Ordinal)
                        || remembered.Contains("damage a second", StringComparison.Ordinal)
                        || remembered.Contains(StructureInspectionWords.NoMobilityNoAccents, StringComparison.Ordinal)))
                {
                    wrong = "as a memory it does not say that it is one, or shows a tier the rules do not hand out for a memory";
                }
                else if (wrong is null && looks is not null && !remembered.Contains(looks, StringComparison.Ordinal))
                {
                    wrong = $"as a memory it does not show {looks}, the tier its look showed";
                }

                if (wrong is not null)
                {
                    orders.Fail(
                        $"the panel words a {structure.Id} under tiers {string.Join(",", mix)} wrongly: {wrong}. " +
                        $"Seen: {seen.ReplaceLineEndings(" / ")} Remembered: {remembered.ReplaceLineEndings(" / ")} Own: {own.ReplaceLineEndings(" / ")}");
                    return;
                }
            }
        }
    }

    // The nearest unit of the local player walks to the structure. Nobody can stand on it, so
    // the unit ends beside it.
    private void SendScout(SmokeOrders orders)
    {
        var match = orders.Match;
        var middle = _target.Center;
        var nearest = long.MaxValue;
        foreach (var unit in orders.OwnUnits())
        {
            var at = match.Bodies.Get(unit).Position;
            long x = at.X.Raw - middle.X.Raw;
            long y = at.Y.Raw - middle.Y.Raw;
            if ((x * x) + (y * y) < nearest)
            {
                nearest = (x * x) + (y * y);
                _scout = unit;
                _home = at;
            }
        }

        if (_scout == EntityId.None)
        {
            orders.Fail("the local player has no unit to send to the structure.");
            return;
        }

        orders.Submit(new Command(CommandType.Move, orders.Player, [_scout], middle.X, middle.Y));
    }

    private void Judge(SmokeOrders orders)
    {
        var name = orders.Match.Data.StructureOf(_target.Type)?.Id;
        GD.Print(
            $"Smoke test, inspection of structures: the panel worded {_worded} structures under {TierMixes.Length} mixes of tiers" +
            $"{(_ownShown ? ", showed the tiers of the local player's own" : string.Empty)}" +
            $"{(_unknownAsked ? ", showed nothing of one never seen" : string.Empty)}" +
            $"{(_rememberedBefore ? ", showed the memory of the other seat's " + name + " before the walk" : string.Empty)}, " +
            $"showed the other seat's {name} with its tiers from tick {_seenAt} of the part and as a memory from tick {_rememberedAt}, after {_clicks} clicks.");

        if (_panel is null)
        {
            return;
        }

        if (_seenAt < 0)
        {
            orders.Fail("the local player never saw the other seat's structure, with a unit sent to it.");
        }
        else if (_rememberedAt < 0)
        {
            orders.Fail($"the local player still sees the other seat's structure {Length - 1 - _seenAt} ticks after the unit was sent back from it.");
        }
        else if (!_tested)
        {
            orders.Fail("the part ended before the memory of the structure was tested.");
        }
        else if (_panel.Mode != StructureInspectionMode.Nothing)
        {
            orders.Fail($"the panel is in mode {_panel.Mode} at the end of the part, with nothing selected and nothing clicked.");
        }
    }
}
