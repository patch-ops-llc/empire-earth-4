using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;
using Sim.Tech;

namespace Game;

/// <summary>
/// The smoke test's accent badges: a minute and a half of play in which the local player earns
/// the Energy for a research of a unit track, researches it, and the badges in the world are
/// compared, after every tick, with what the units carry and with what the player may see.
/// </summary>
/// <remarks>
/// <para>
/// The steps, in ticks of the part:
/// </para>
/// <list type="bullet">
/// <item>At 0 the local player gets a unit, the watcher, on the open ground furthest from
/// everything else in the match. A player who cannot pay for a research sends workers to their
/// Generator until it has all it can hold. The other seat starts a research too, if it can pay.</item>
/// <item>In the first tick the local player can pay, the research is ordered.</item>
/// <item>In the tick the research finishes, the watcher, made long before, must show the badges
/// of its new accents: a research changes the units that live (design 2.3).</item>
/// <item>Then the other seat gets a faster unit, the visitor, a few tiles from the watcher.
/// <see cref="LookAfter"/> ticks on, the local player must see it, and it must show a badge for
/// each accent its owner's tiers give it, which is none while that seat has researched nothing.
/// It is then sent away.</item>
/// <item>Once the visitor is out of the player's sight, the world must show nothing of it
/// (design 3.2 and 5.3).</item>
/// </list>
/// <para>
/// After every tick the badges drawn must be exactly those of the fighting units the world
/// draws, each unit's in the order of its accents, and the world must draw no unit of another
/// player's that the local player does not see. Before the first tick the part also checks the
/// sizes of the badges over the camera's whole range: full badges near, and at the widest zoom
/// small ones that fit over one unit.
/// </para>
/// <para>
/// Like the fight, the part needs <c>debug.spawn</c> on in <c>game/data/rules.jsonc</c>. It also
/// needs the Generator that the construction part builds, or a player who can pay as it is.
/// </para>
/// </remarks>
public sealed class BadgesSmoke : SmokePart
{
    // With a full Generator the Energy of a research is earned in 1000 ticks from nothing, and
    // a research of the first tier above the start takes 900 in the data. The rest is for the
    // workers to walk, and for the visitor to come, be looked at and ride out of sight.
    private const int Length = 2200;

    // The research must be ordered by this tick for the rest to fit.
    private const int StartBy = 1100;

    // A spawn is carried out a few ticks after it is ordered, and sight is worked out again
    // every few ticks.
    private const int LookAfter = 12;

    // How far from the watcher the visitor is made, in tiles: within the sight of any chassis.
    private const int VisitorAway = 5;

    // How far from the watcher the visitor is sent, in tiles: beyond the sight of any chassis.
    private const int VisitorLeaves = 24;

    // The watcher's place keeps this far from the edge of the map.
    private const int Margin = 2;

    private readonly HashSet<EntityId> _before = [];
    private bool _skipped;
    private int _ownTrack = -1;
    private int _otherTrack = -1;
    private int _ownTier;
    private int _otherTier;
    private int _startedAt = -1;
    private int _sentToWork;
    private ChassisDef? _watcherChassis;
    private ChassisDef? _visitorChassis;
    private (int X, int Y) _place;
    private (int X, int Y) _toward;
    private EntityId _watcher = EntityId.None;
    private EntityId _visitor = EntityId.None;
    private Accent[] _watcherBefore = [];
    private int _ownFinishedAt = -1;
    private int _otherFinishedAt = -1;
    private int _visitorOrderedAt = -1;
    private int _visitorSeenAt = -1;
    private int _visitorUnseenAt = -1;
    private int _visitorBadges;
    private int _mostBadges;

    public override string Name => "Accent badges";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (_skipped)
        {
            return;
        }

        if (orders.View.World.GetChildren().OfType<AccentBadges>().FirstOrDefault() is not { } badges)
        {
            orders.Fail("the world view has no layer of accent badges.");
            return;
        }

        if (tick == 0)
        {
            Begin(orders, badges);
            return;
        }

        CompareWithTheMatch(orders, badges);
        FollowTheResearches(tick, orders, badges);
        FollowTheVisitor(tick, orders, badges);
        if (tick == Length - 1)
        {
            Conclude(orders);
        }
    }

    private void Begin(SmokeOrders orders, AccentBadges badges)
    {
        var match = orders.Match;
        if (!match.Data.Rules.Debug.Spawn)
        {
            _skipped = true;
            GD.Print("Smoke test, accent badges: left out, because the development spawn is off and the part has no other way to a fighting unit.");
            return;
        }

        if (orders.View.Sight.FogOff)
        {
            orders.Fail("the fog is lifted from the screen, so the part cannot tell what the player sees.");
            return;
        }

        CheckSizes(orders, badges);

        var other = OtherSeat(orders);
        _ownTrack = TrackToResearch(match, orders.Player, not: -1, paid: false);
        if (_ownTrack < 0)
        {
            orders.Fail("the local player has no track left whose next tier gives the oldest chassis an accent and may be researched.");
            return;
        }

        if (!ChooseChassis(match) || !ChoosePlace(match, orders.Player))
        {
            orders.Fail("the data has no two chassis of the first epoch of which one is faster, or the map has no open ground away from everything for them.");
            return;
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            _before.Add(match.Combatants.EntityAt(i));
        }

        _ownTier = match.Research.State.Tier(orders.Player, _ownTrack);
        if (match.Research.CheckStart(orders.Player, _ownTrack) != ResearchStartCheck.Allowed && !SendToTheGenerator(orders))
        {
            return;
        }

        // The other seat has no Generator in this match. It researches if it can pay as it is.
        _otherTrack = TrackToResearch(match, other, not: _ownTrack, paid: true);
        if (_otherTrack >= 0)
        {
            _otherTier = match.Research.State.Tier(other, _otherTrack);
            orders.SubmitFor(new Command(CommandType.StartResearch, other, contentId: _otherTrack));
        }

        orders.Submit(new Command(
            CommandType.DebugSpawn,
            orders.Player,
            targetX: Fixed.FromInt(_place.X) + Fixed.Half,
            targetY: Fixed.FromInt(_place.Y) + Fixed.Half,
            contentId: _watcherChassis!.Index));
    }

    // After every tick: the badges in the world are those of the fighting units the world draws,
    // each unit's in the order of its accents, and the world draws no unit of another player's
    // that the local player does not see.
    private void CompareWithTheMatch(SmokeOrders orders, AccentBadges badges)
    {
        var match = orders.Match;
        var view = orders.View;
        var expected = new List<ShownBadge>();
        foreach (var entity in view.World.Units)
        {
            if (!match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            if (body.Owner != orders.Player && !view.Sight.TrulySees(entity))
            {
                orders.Fail($"the world draws {entity} of player {body.Owner}, which the local player does not see.");
                return;
            }

            if (!match.Combatants.TryGet(entity, out var combatant))
            {
                continue;
            }

            var accents = match.Research.Tech(body.Owner).Blueprints[combatant.Chassis].Accents;
            for (var place = 0; place < accents.Count; place++)
            {
                expected.Add(new ShownBadge(entity, accents[place].Track, accents[place].Level, place, accents.Count));
            }
        }

        _mostBadges = Math.Max(_mostBadges, badges.Count);
        if (!badges.Shown.SequenceEqual(expected))
        {
            orders.Fail(
                $"the world shows {badges.Count} accent badges and the units it draws carry {expected.Count} accents, or not the same ones. " +
                $"Shown: {string.Join(' ', badges.Shown)}. Carried: {string.Join(' ', expected)}.");
        }
    }

    private void FollowTheResearches(int tick, SmokeOrders orders, AccentBadges badges)
    {
        var match = orders.Match;
        var state = match.Research.State;
        if (_watcher == EntityId.None)
        {
            _watcher = NewUnit(orders, orders.Player, _watcherChassis!);
            if (_watcher != EntityId.None)
            {
                _watcherBefore = [.. AccentsOf(match, _watcher)];
            }
            else if (tick >= LookAfter)
            {
                orders.Fail($"the unit ordered {tick} ticks ago for the local player is not in the match.");
            }
        }

        if (_startedAt < 0)
        {
            if (match.Research.CheckStart(orders.Player, _ownTrack) == ResearchStartCheck.Allowed)
            {
                _startedAt = tick;
                orders.Submit(new Command(CommandType.StartResearch, orders.Player, contentId: _ownTrack));
            }
            else if (tick >= StartBy)
            {
                orders.Fail(
                    $"the local player still cannot start the research of track {_ownTrack} at tick {tick} of the part: {match.Research.CheckStart(orders.Player, _ownTrack)}, " +
                    $"with {match.Stockpiles.Energy(orders.Player) / 1000} Energy and {_sentToWork} workers sent to the Generator. If Energy comes slower in the data now, this part needs more ticks.");
            }

            return;
        }

        if (_otherTrack >= 0 && _otherFinishedAt < 0 && state.Tier(OtherSeat(orders), _otherTrack) > _otherTier)
        {
            _otherFinishedAt = tick;
        }

        if (_ownFinishedAt >= 0 || state.Tier(orders.Player, _ownTrack) <= _ownTier)
        {
            return;
        }

        // This is the first look after the tick in which the research finished.
        _ownFinishedAt = tick;
        if (_watcher == EntityId.None || !match.World.Entities.IsAlive(_watcher))
        {
            orders.Fail("the local player's unit did not live to see the research finish.");
            return;
        }

        var now = AccentsOf(match, _watcher);
        var shown = badges.Shown.Where(badge => badge.Entity == _watcher).Select(badge => new Accent(badge.Track, badge.Level)).ToArray();
        if (now.SequenceEqual(_watcherBefore) || !now.Any(accent => accent.Track == _ownTrack))
        {
            orders.Fail($"the research of track {_ownTrack} gave the {_watcherChassis!.Id} no accent: it carries {string.Join(' ', now)}.");
        }
        else if (!shown.SequenceEqual(now))
        {
            orders.Fail($"in the tick its owner's research finished, the {_watcherChassis!.Id} carries {string.Join(' ', now)} and shows {string.Join(' ', shown)}.");
        }
    }

    private void FollowTheVisitor(int tick, SmokeOrders orders, AccentBadges badges)
    {
        var match = orders.Match;
        var view = orders.View;
        var other = OtherSeat(orders);
        if (_ownFinishedAt < 0 || (_otherTrack >= 0 && _otherFinishedAt < 0) || orders.Failed)
        {
            return;
        }

        if (_visitorOrderedAt < 0)
        {
            _visitorOrderedAt = tick;
            orders.SubmitFor(new Command(
                CommandType.DebugSpawn,
                other,
                targetX: Fixed.FromInt(_place.X + (_toward.X * VisitorAway)) + Fixed.Half,
                targetY: Fixed.FromInt(_place.Y + (_toward.Y * VisitorAway)) + Fixed.Half,
                contentId: _visitorChassis!.Index));
            return;
        }

        if (tick < _visitorOrderedAt + LookAfter)
        {
            return;
        }

        if (tick == _visitorOrderedAt + LookAfter)
        {
            _visitor = NewUnit(orders, other, _visitorChassis!);
            var carried = _visitor == EntityId.None ? [] : AccentsOf(match, _visitor);
            _visitorBadges = badges.Shown.Count(badge => badge.Entity == _visitor);
            if (_visitor == EntityId.None)
            {
                orders.Fail($"the unit ordered {LookAfter} ticks ago for player {other} is not in the match.");
            }
            else if (!view.Sight.TrulySees(_visitor) || !view.World.TryGetDrawnPosition(_visitor, out _))
            {
                orders.Fail($"the local player does not see the {_visitorChassis!.Id} of player {other} that was made {VisitorAway} tiles from their own unit, or the world does not draw it.");
            }
            else if ((_otherTrack >= 0 && !carried.Any(accent => accent.Track == _otherTrack)) || _visitorBadges != carried.Count)
            {
                orders.Fail($"the {_visitorChassis!.Id} of player {other} carries {carried.Count} accents ({string.Join(' ', carried)}) and shows {_visitorBadges} badges. Its owner researched track {_otherTrack}.");
            }
            else
            {
                _visitorSeenAt = tick;

                // Further the way it came from, away from the watcher and from everything else
                // of the local player's.
                orders.SubmitFor(new Command(
                    CommandType.Move,
                    other,
                    [_visitor],
                    targetX: Fixed.FromInt(_place.X + (_toward.X * VisitorLeaves)) + Fixed.Half,
                    targetY: Fixed.FromInt(_place.Y + (_toward.Y * VisitorLeaves)) + Fixed.Half));
            }

            return;
        }

        // Out of sight the visitor is what it was, and the world shows nothing of it.
        if (_visitorSeenAt >= 0
            && _visitorUnseenAt < 0
            && match.World.Entities.IsAlive(_visitor)
            && !view.Sight.TrulySees(_visitor))
        {
            _visitorUnseenAt = tick;
            if (view.World.TryGetDrawnPosition(_visitor, out _) || badges.Shown.Any(badge => badge.Entity == _visitor))
            {
                orders.Fail($"the world still shows the {_visitorChassis!.Id} of player {other}, or its badges, after the local player lost sight of it.");
            }
        }
    }

    private void Conclude(SmokeOrders orders)
    {
        if (orders.Failed)
        {
            return;
        }

        if (_ownFinishedAt < 0 || (_otherTrack >= 0 && _otherFinishedAt < 0))
        {
            orders.Fail(
                $"the research of track {_ownTrack}, ordered at tick {_startedAt} of the part, or the other seat's of track {_otherTrack}, had not finished at tick {Length - 1}. " +
                "If a research takes longer in the data now, this part needs more ticks.");
        }
        else if (_visitorSeenAt < 0 || _visitorUnseenAt < 0)
        {
            orders.Fail($"the other seat's unit was {(_visitorSeenAt < 0 ? "never seen" : "still in sight")} when the part ended, {Length - 1 - _visitorOrderedAt} ticks after it was ordered.");
        }
        else
        {
            var paid = _sentToWork > 0 ? $"with {_sentToWork} more workers at the Generator the research was paid for at tick {_startedAt} of the part" : "the research was paid for at once";
            var theirs = _otherTrack >= 0 ? $"showed {_visitorBadges} badges after its owner's research" : "carried no accent, since its owner could pay for no research, and showed no badge";
            GD.Print(
                $"Smoke test, accent badges: {paid}, and the {_watcherChassis!.Id} made before it showed its new accent in the tick it finished, tick {_ownFinishedAt}. " +
                $"The other seat's {_visitorChassis!.Id} {theirs} while it was seen, from tick {_visitorSeenAt}; from tick {_visitorUnseenAt} it was out of sight and not drawn. " +
                $"At most {_mostBadges} badges were drawn at once.");
        }
    }

    // The sizes of the badges over the camera's range, on a window of the project's own size
    // and on larger ones. They are worked out from the camera, because the window of a smoke
    // test is not a window anyone plays in.
    private static void CheckSizes(SmokeOrders orders, AccentBadges badges)
    {
        var rig = orders.View.Camera;
        if (badges.GetViewport()?.GetCamera3D() is not { } camera)
        {
            orders.Fail("the layer of accent badges has no camera to measure with.");
            return;
        }

        var height = ProjectSettings.GetSetting("display/window/size/viewport_height", 648).AsSingle();
        var depth = Mathf.Sin(Mathf.DegToRad(rig.PitchDegrees));

        // The width of a tile at the point the camera looks at, in units of the interface.
        float Tile(float distance) => height / (2f * distance * Mathf.Tan(Mathf.DegToRad(camera.Fov) / 2f));

        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f, 3f })
        {
            var tile = Tile(rig.MinDistance) * scale;
            var near = BadgeLayout.For(tile, tile * depth, scale);
            if (near.Compact || near.MarkWidth < 1 || near.MarkSpacing <= near.MarkWidth || near.PairWidth > tile)
            {
                orders.Fail($"at the nearest zoom, with {scale} pixels to a unit of the interface, the badges are {near}.");
            }

            tile = Tile(rig.MaxDistance) * scale;
            var far = BadgeLayout.For(tile, tile * depth, scale);
            if (!far.Compact
                || far.Width < (int)(AccentBadges.PipPixels * scale)
                || far.Width - (2 * far.Rim) < 2
                || far.Height - (2 * far.Rim) < 2
                || far.PairWidth > tile
                || far.HeightOf(2) > tile * depth)
            {
                orders.Fail($"at the widest zoom, with {scale} pixels to a unit of the interface and {tile:0.0} to a tile, the badges are {far}: those of a unit must fit over it.");
            }
        }

        // Nowhere between are the two badges of a unit wider than a tile, so the badges of
        // units that stand a tile apart do not cover each other.
        for (var distance = rig.MinDistance; distance <= rig.MaxDistance; distance += 0.25f)
        {
            var tile = Tile(distance);
            var layout = BadgeLayout.For(tile, tile * depth, 1f);
            if (layout.PairWidth > tile || layout.HeightOf(2) > tile * depth)
            {
                orders.Fail($"with the camera {distance} tiles away a tile is {tile:0.0} pixels wide and the badges of a unit are {layout.PairWidth} by {layout.HeightOf(2)}: {layout}.");
                return;
            }
        }
    }

    // Sends workers of the local player to their Generator until it has all it can hold. False,
    // with the test failed, when the player has no Generator or nobody to send.
    private bool SendToTheGenerator(SmokeOrders orders)
    {
        var match = orders.Match;
        var generator = EntityId.None;
        for (var i = 0; i < match.WorkSites.Count; i++)
        {
            var site = match.WorkSites.ValueAt(i);
            if (site.Kind == WorkSiteKind.Generator && site.Owner == orders.Player && match.World.Entities.IsAlive(match.WorkSites.EntityAt(i)))
            {
                generator = match.WorkSites.EntityAt(i);
                break;
            }
        }

        if (generator == EntityId.None)
        {
            orders.Fail($"the local player cannot pay for a research ({match.Research.CheckStart(orders.Player, _ownTrack)}) and has no Generator to earn the Energy at.");
            return false;
        }

        var there = 0;
        var free = new List<EntityId>();
        foreach (var entity in orders.OwnUnits())
        {
            if (!match.Workers.TryGet(entity, out var worker))
            {
                continue;
            }

            if (worker.Site == generator)
            {
                there++;
            }
            else if (!match.BuildOrders.Contains(entity))
            {
                free.Add(entity);
            }
        }

        var room = match.Data.Resources.Energy.WorkersPerGenerator - there;
        if (room > 0 && free.Count > 0)
        {
            var sent = free.Take(room).ToArray();
            _sentToWork = sent.Length;
            orders.Submit(new Command(CommandType.Gather, orders.Player, sent, targetEntity: generator));
        }

        return true;
    }

    // The first unit track whose next tier will give the oldest chassis an accent, and which
    // the player may research now, or could with more in the stockpile unless it must be paid
    // for. Not the track the other seat takes, where there is another.
    private static int TrackToResearch(Match match, int player, int not, bool paid)
    {
        var roles = match.Data.TrackRoles;
        var found = -1;
        foreach (var track in new[] { roles.Firepower, roles.Protection, roles.Mobility, roles.Manufacturing, roles.Logistics })
        {
            // A chassis of the first epoch uses a track up to the window above it.
            var check = match.Research.CheckStart(player, track);
            if ((check != ResearchStartCheck.Allowed && (paid || check != ResearchStartCheck.TooExpensive))
                || match.Research.State.Tier(player, track) > match.Data.Rules.EffectiveTierWindow)
            {
                continue;
            }

            if (track != not)
            {
                return track;
            }

            found = track;
        }

        return found;
    }

    // The watcher is the slowest chassis of the first epoch that sees at least as far as the
    // visitor is made, and the visitor the fastest, so that it can leave the watcher behind.
    private bool ChooseChassis(Match match)
    {
        var tech = match.Research.Tech(0);
        foreach (var blueprint in tech.Blueprints)
        {
            if (blueprint.Chassis.Epoch != 1 || blueprint.Sight <= VisitorAway)
            {
                continue;
            }

            if (_watcherChassis is null || blueprint.SpeedPerTick < tech.Blueprints[_watcherChassis.Index].SpeedPerTick)
            {
                _watcherChassis = blueprint.Chassis;
            }

            if (_visitorChassis is null || blueprint.SpeedPerTick > tech.Blueprints[_visitorChassis.Index].SpeedPerTick)
            {
                _visitorChassis = blueprint.Chassis;
            }
        }

        return _watcherChassis is not null && _visitorChassis is not null && _watcherChassis.Index != _visitorChassis.Index;
    }

    // The watcher's place and the way the visitor comes from. The place is the open tile
    // furthest from every unit and structure in the match that has, along a row or a column,
    // open ground to where the visitor is made and on to where it is sent. Of the ways with
    // open ground, the visitor takes the one that ends furthest from everything the local
    // player has, so that nothing else of theirs sees it once it has left the watcher.
    private bool ChoosePlace(Match match, int player)
    {
        var map = match.Map;
        var bodies = match.Bodies;
        (int X, int Y)[] ways = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        var best = -1f;
        for (var y = Margin; y < map.Height - Margin; y++)
        {
            for (var x = Margin; x < map.Width - Margin; x++)
            {
                var nearest = float.MaxValue;
                for (var i = 0; i < bodies.Count && nearest > best; i++)
                {
                    nearest = Math.Min(nearest, Away(bodies.ValueAt(i).Position, x, y));
                }

                if (nearest <= best)
                {
                    continue;
                }

                var furthest = -1f;
                foreach (var way in ways)
                {
                    var open = true;
                    for (var step = 0; step <= VisitorLeaves && open; step++)
                    {
                        open = match.Grid.IsPassable(x + (way.X * step), y + (way.Y * step));
                    }

                    if (!open)
                    {
                        continue;
                    }

                    var end = float.MaxValue;
                    for (var i = 0; i < bodies.Count; i++)
                    {
                        if (bodies.ValueAt(i).Owner == player)
                        {
                            end = Math.Min(end, Away(bodies.ValueAt(i).Position, x + (way.X * VisitorLeaves), y + (way.Y * VisitorLeaves)));
                        }
                    }

                    if (end > furthest)
                    {
                        furthest = end;
                        _toward = way;
                    }
                }

                if (furthest >= 0f)
                {
                    best = nearest;
                    _place = (x, y);
                }
            }
        }

        // The way of the place that was kept is worked out last, since a later tile may have
        // set it and then not been kept.
        if (best <= 0f)
        {
            return false;
        }

        var kept = -1f;
        foreach (var way in ways)
        {
            var open = true;
            for (var step = 0; step <= VisitorLeaves && open; step++)
            {
                open = match.Grid.IsPassable(_place.X + (way.X * step), _place.Y + (way.Y * step));
            }

            if (!open)
            {
                continue;
            }

            var end = float.MaxValue;
            for (var i = 0; i < bodies.Count; i++)
            {
                if (bodies.ValueAt(i).Owner == player)
                {
                    end = Math.Min(end, Away(bodies.ValueAt(i).Position, _place.X + (way.X * VisitorLeaves), _place.Y + (way.Y * VisitorLeaves)));
                }
            }

            if (end > kept)
            {
                kept = end;
                _toward = way;
            }
        }

        return kept >= 0f;
    }

    // How far a point of the match is from the middle of a tile, in tiles.
    private static float Away(FixedVector2 point, int x, int y)
    {
        var at = WorldView.ToWorld(point);
        return new Vector2(at.X - (x + 0.5f), at.Z - (y + 0.5f)).Length();
    }

    private static IReadOnlyList<Accent> AccentsOf(Match match, EntityId entity) =>
        match.Combatants.TryGet(entity, out var combatant) && match.Bodies.TryGet(entity, out var body)
            ? match.Research.Tech(body.Owner).Blueprints[combatant.Chassis].Accents
            : [];

    // The unit of this owner and chassis that was not in the match when the part began and
    // stands nearest to the watcher's place, or none.
    private EntityId NewUnit(SmokeOrders orders, int owner, ChassisDef chassis)
    {
        var match = orders.Match;
        var best = EntityId.None;
        var nearest = float.MaxValue;
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (match.Combatants.ValueAt(i).Chassis != chassis.Index
                || _before.Contains(entity)
                || !match.Bodies.TryGet(entity, out var body)
                || body.Owner != owner
                || !match.World.Entities.IsAlive(entity))
            {
                continue;
            }

            var away = Away(body.Position, _place.X, _place.Y);
            if (away < nearest && away <= VisitorAway + 2)
            {
                nearest = away;
                best = entity;
            }
        }

        return best;
    }

    private static int OtherSeat(SmokeOrders orders) => (orders.Player + 1) % orders.Match.Players.Count;
}
