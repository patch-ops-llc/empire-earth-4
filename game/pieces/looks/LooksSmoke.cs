using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;

namespace Game;

/// <summary>
/// The smoke test's chassis looks: five seconds of play in which the local player is given one
/// unit of every chassis that has a look, and a unit of the other seat rides into their sight.
/// After every tick each unit the world draws must wear the look of its chassis in its owner's
/// color, or the stand-in if it has none, and a unit that is not drawn must wear nothing.
/// </summary>
/// <remarks>
/// <para>
/// Before the first tick the part measures the looks themselves, without the renderer:
/// </para>
/// <list type="bullet">
/// <item>Every line of the data has a chassis with a look in each of the first
/// <see cref="Epochs"/> epochs, the ones the first playable is played in.</item>
/// <item>Every look stands on the ground, ends below the health bar, above which the accent
/// badges are, and stays within <see cref="MaxReach"/> tiles of the middle of its unit.</item>
/// <item>At the camera's starting distance, each two looks differ at
/// <see cref="LookReadings.NormalShare"/> of the larger one's pixels or more, at every heading
/// and in every seat's color. At the widest zoom, each two looks of different lines differ in
/// outline at <see cref="LookReadings.WidestShare"/> or more (<see cref="LookReadings"/>).</item>
/// </list>
/// <para>
/// The other seat's unit is made out of the local player's sight and walks toward their units.
/// While it is unseen the world must not draw it; in the tick it is first drawn it must already
/// wear its look, in the other seat's color. The local player's units then fight it, as idle
/// units do.
/// </para>
/// <para>
/// Like the fight, the part needs <c>debug.spawn</c> on in <c>game/data/rules.jsonc</c>.
/// </para>
/// </remarks>
public sealed class LooksSmoke : SmokePart
{
    /// <summary>The epochs of the first playable: a chassis of each line must have a look in each.</summary>
    public const int Epochs = 4;

    /// <summary>The furthest a look may reach from the middle of its unit, in tiles, before it grows with distance.</summary>
    public const float MaxReach = 0.9f;

    private const int Length = 100;

    // A spawn is carried out a few ticks after it is ordered, and sight is worked out again
    // every few ticks.
    private const int LookAfter = 12;

    // The local player's units stand in rows, this many tiles apart, so that none has to make
    // room for another.
    private const int Step = 2;

    // How far east of the last column the other seat's unit is made, in tiles: beyond the
    // sight of any chassis. It is sent to stand this far east of the last column.
    private const int VisitorAway = 14;
    private const int VisitorStops = 3;

    // The place keeps this far from the edge of the map.
    private const int Margin = 2;

    private readonly HashSet<EntityId> _before = [];
    private ChassisLook[] _made = [];
    private ChassisDef? _visitorChassis;
    private EntityId _visitor = EntityId.None;
    private (int X, int Y) _place;
    private int _columns;
    private int _rows;
    private int _visitorUnseenAt = -1;
    private int _visitorSeenAt = -1;
    private int _mostDressed;
    private int _standIns;
    private int _unlooked;
    private string _closest = string.Empty;
    private bool _skipped;

    public override string Name => "Chassis looks";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (_skipped)
        {
            return;
        }

        if (orders.View.World.GetChildren().OfType<ChassisLooks>().FirstOrDefault() is not { } looks)
        {
            orders.Fail("the world view has no layer of chassis looks.");
            return;
        }

        if (tick == 0)
        {
            Begin(orders, looks);
            return;
        }

        CompareWithTheMatch(orders, looks);
        if (orders.Failed)
        {
            return;
        }

        if (tick == LookAfter)
        {
            CountTheNew(orders, looks);
        }

        if (tick >= LookAfter)
        {
            FollowTheVisitor(tick, orders, looks);
        }

        if (tick == Length - 1)
        {
            Conclude(orders, looks);
        }
    }

    private void Begin(SmokeOrders orders, ChassisLooks looks)
    {
        var match = orders.Match;
        if (!match.Data.Rules.Debug.Spawn)
        {
            _skipped = true;
            GD.Print("Smoke test, chassis looks: skipped, because the development spawn is off in the data and no unit can be made to wear a look.");
            return;
        }

        if (!CheckTheLooks(orders, looks) || !CheckTheReadings(orders, looks))
        {
            return;
        }

        _made = [.. looks.Looks.Looks.OrderBy(look => look.Chassis.Line).ThenBy(look => look.Chassis.Epoch).ThenBy(look => look.Chassis.Index)];
        _visitorChassis = _made
            .Select(look => look.Chassis)
            .OrderByDescending(chassis => match.Research.Tech(OtherSeat(orders)).Blueprints[chassis.Index].SpeedPerTick)
            .ThenBy(chassis => chassis.Index)
            .First();
        _columns = Mathf.CeilToInt(Mathf.Sqrt(_made.Length));
        _rows = Mathf.CeilToInt(_made.Length / (float)_columns);
        if (!ChoosePlace(match))
        {
            orders.Fail($"the map has no open ground {((_columns - 1) * Step) + VisitorAway + 1} tiles long and {((_rows - 1) * Step) + 1} deep for one unit of each of {_made.Length} chassis and a unit of the other seat.");
            return;
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            _before.Add(match.Combatants.EntityAt(i));
        }

        for (var i = 0; i < _made.Length; i++)
        {
            orders.Submit(new Command(
                CommandType.DebugSpawn,
                orders.Player,
                targetX: Fixed.FromInt(_place.X + (i % _columns * Step)) + Fixed.Half,
                targetY: Fixed.FromInt(_place.Y + (i / _columns * Step)) + Fixed.Half,
                contentId: _made[i].Chassis.Index));
        }

        var (x, y) = VisitorStart();
        orders.SubmitFor(new Command(
            CommandType.DebugSpawn,
            OtherSeat(orders),
            targetX: Fixed.FromInt(x) + Fixed.Half,
            targetY: Fixed.FromInt(y) + Fixed.Half,
            contentId: _visitorChassis.Index));
    }

    // What the presentation data must hold, and where a look may be: on the ground, below the
    // health bar and close to the middle of its unit.
    private static bool CheckTheLooks(SmokeOrders orders, ChassisLooks looks)
    {
        var data = orders.Match.Data;
        foreach (var line in data.Lines)
        {
            for (var epoch = 1; epoch <= Math.Min(Epochs, data.TierCount); epoch++)
            {
                var chassis = line.Chassis.Where(candidate => candidate.Epoch == epoch).ToArray();
                if (chassis.Length == 0 || chassis.Any(candidate => looks.Looks.Of(candidate.Index) is null))
                {
                    orders.Fail(
                        $"the line {line.Id} has {(chassis.Length == 0 ? "no chassis" : $"a chassis without a look ({string.Join(", ", chassis.Where(candidate => looks.Looks.Of(candidate.Index) is null).Select(candidate => candidate.Id))})")} in epoch {epoch}. " +
                        $"Every chassis of the first {Epochs} epochs needs a look in {ChassisLookSet.LooksFile}.");
                    return false;
                }
            }
        }

        var ceiling = PlaceholderMeshes.UnitHeight + HealthBars.Lift - HealthBars.Height;
        foreach (var look in looks.Looks.Looks)
        {
            var shape = look.Shape;
            if (shape.Triangles.Count == 0 || shape.Lowest < -0.005f || shape.Lowest > 0.005f || shape.Height > ceiling || shape.Reach > MaxReach)
            {
                orders.Fail(
                    $"the look of the {look.Chassis.Id} ({look.File}) has {shape.Triangles.Count} triangles from {shape.Lowest:0.###} to {shape.Height:0.###} tiles above the ground and reaches {shape.Reach:0.###} tiles from its middle. " +
                    $"A look stands on the ground, ends below the health bar at {ceiling:0.###} and reaches no further than {MaxReach:0.###}.");
                return false;
            }
        }

        if (!Mathf.IsEqualApprox(ChassisLooks.GrowthAt(orders.View.Camera.MinDistance, orders.View.Camera.StartDistance, orders.View.Camera.MaxDistance), 1f)
            || !Mathf.IsEqualApprox(ChassisLooks.GrowthAt(orders.View.Camera.StartDistance, orders.View.Camera.StartDistance, orders.View.Camera.MaxDistance), 1f)
            || !Mathf.IsEqualApprox(ChassisLooks.GrowthAt(orders.View.Camera.MaxDistance, orders.View.Camera.StartDistance, orders.View.Camera.MaxDistance), ChassisLooks.FarWidth))
        {
            orders.Fail("a look does not have its true size up to the camera's starting distance, or is not at its widest at the widest zoom.");
            return false;
        }

        return true;
    }

    // How far apart the looks are on the screen, on a window of the project's own size. They
    // are worked out from the camera, because the window of a smoke test is not a window
    // anyone plays in.
    private bool CheckTheReadings(SmokeOrders orders, ChassisLooks looks)
    {
        var rig = orders.View.Camera;
        if (looks.GetViewport()?.GetCamera3D() is not { } camera)
        {
            orders.Fail("the layer of chassis looks has no camera to measure with.");
            return false;
        }

        var all = looks.Looks.Looks;
        var height = ProjectSettings.GetSetting("display/window/size/viewport_height", 648).AsSingle();
        var pitch = Mathf.DegToRad(rig.PitchDegrees);
        var normal = LookReadings.PixelsPerTile(rig.StartDistance, camera.Fov, height);
        var widest = LookReadings.PixelsPerTile(rig.MaxDistance, camera.Fov, height);

        Closest? near = null;
        for (var player = 0; player < PlayerColors.Count; player++)
        {
            if (LookReadings.ClosestOf(all, pitch, normal, 1f, PlayerColors.Of(player), (_, _) => true) is { } found
                && (near is not { } before || found.Share < before.Share))
            {
                near = found;
            }
        }

        var far = LookReadings.ClosestOf(all, pitch, widest, ChassisLooks.FarWidth, null, (a, b) => a.Chassis.Line != b.Chassis.Line);
        if (near is { } nearest && nearest.Share < LookReadings.NormalShare)
        {
            orders.Fail(
                $"at the camera's starting distance, where a tile is {normal:0.0} pixels wide, the {nearest}: less than {LookReadings.NormalShare:P0}. " +
                "Two chassis must not look this much alike.");
            return false;
        }

        if (far is { } furthest && furthest.Share < LookReadings.WidestShare)
        {
            orders.Fail(
                $"at the widest zoom, where a tile is {widest:0.0} pixels wide, the outlines of the {furthest}: less than {LookReadings.WidestShare:P0}. " +
                "Two chassis of different lines must not have outlines this much alike.");
            return false;
        }

        _closest =
            $"At the starting distance the two most alike, the {near}; at the widest zoom the two of different lines most alike in outline, the {far}.";
        return true;
    }

    // After every tick: what each unit the world draws wears, and that nothing else wears a look.
    private void CompareWithTheMatch(SmokeOrders orders, ChassisLooks looks)
    {
        var match = orders.Match;
        var world = orders.View.World;
        var dressed = 0;
        var standIns = 0;
        var unlooked = 0;
        foreach (var entity in world.Units)
        {
            if (!world.TryGetDress(entity, out var mesh, out var material) || !match.Bodies.TryGet(entity, out var body))
            {
                orders.Fail($"the world draws the unit {entity} and cannot say what it looks like, or the match has no such unit.");
                return;
            }

            var fights = match.Combatants.TryGet(entity, out var unit);
            var look = fights ? looks.Looks.Of(unit.Chassis) : null;
            var worn = looks.LookOf(entity);
            if (look is null)
            {
                // A worker, or a chassis without a look: the stand-in, as the world view made it.
                standIns++;
                unlooked += fights ? 1 : 0;
                if (worn is not null || looks.Looks.Looks.Any(other => ReferenceEquals(other.Mesh, mesh)) || material is ShaderMaterial)
                {
                    orders.Fail($"the unit {entity} of player {body.Owner} has no look of its own and is drawn as the {worn?.Chassis.Id ?? "look of another unit"}.");
                    return;
                }

                continue;
            }

            dressed++;
            if (!ReferenceEquals(worn, look) || !ReferenceEquals(mesh, look.Mesh))
            {
                orders.Fail($"the {look.Chassis.Id} {entity} of player {body.Owner} is drawn {(worn is null ? "in the stand-in shape" : $"as the {worn.Chassis.Id}")}, not in its own look.");
                return;
            }

            if (!ReferenceEquals(material, looks.MaterialOf(body.Owner))
                || material is not ShaderMaterial paint
                || !ChassisLooks.OwnerOf(paint).IsEqualApprox(PlayerColors.Of(body.Owner)))
            {
                orders.Fail($"the {look.Chassis.Id} {entity} of player {body.Owner} is not drawn in its owner's color.");
                return;
            }
        }

        if (dressed != looks.Count)
        {
            orders.Fail($"{dressed} units the world draws have a look, and the layer of chassis looks counts {looks.Count} dressed.");
            return;
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (!world.TryGetDrawnPosition(entity, out _) && looks.LookOf(entity) is not null)
            {
                orders.Fail($"the unit {entity} is not drawn, and the layer of chassis looks still has it dressed.");
                return;
            }
        }

        _mostDressed = Math.Max(_mostDressed, dressed);
        _standIns = standIns;
        _unlooked = unlooked;
    }

    // The units ordered at tick 0: one of each chassis for the local player, each in its look.
    // That every drawn unit wears the look of its chassis was checked just before.
    private void CountTheNew(SmokeOrders orders, ChassisLooks looks)
    {
        var match = orders.Match;
        var world = orders.View.World;
        var worn = new HashSet<int>();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (_before.Contains(entity) || !match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            if (body.Owner != orders.Player)
            {
                _visitor = entity;
            }
            else if (world.TryGetDrawnPosition(entity, out _) && looks.LookOf(entity) is { } look)
            {
                worn.Add(look.Chassis.Index);
            }
        }

        var missing = _made.Where(look => !worn.Contains(look.Chassis.Index)).Select(look => look.Chassis.Id).ToArray();
        if (missing.Length > 0)
        {
            orders.Fail($"{LookAfter} ticks after one unit of each of {_made.Length} chassis was ordered for the local player, no unit is drawn in the look of the {string.Join(", ", missing)}.");
        }
        else if (_visitor == EntityId.None)
        {
            orders.Fail($"the unit ordered {LookAfter} ticks ago for player {OtherSeat(orders)} is not in the match.");
        }
        else if (orders.View.Sight.TrulySees(_visitor) || world.TryGetDrawnPosition(_visitor, out _))
        {
            orders.Fail(
                $"the local player sees the {_visitorChassis!.Id} of player {OtherSeat(orders)} that was made {VisitorAway} tiles from their units, or the world draws it. " +
                "If units see further in the data now, this part must make it further away.");
        }
        else
        {
            _visitorUnseenAt = LookAfter;
            orders.SubmitFor(new Command(
                CommandType.Move,
                OtherSeat(orders),
                [_visitor],
                targetX: Fixed.FromInt(VisitorStart().X - VisitorAway + VisitorStops) + Fixed.Half,
                targetY: Fixed.FromInt(VisitorStart().Y) + Fixed.Half));
        }
    }

    // The tick in which the world first draws the other seat's unit. Its look and its color
    // were checked with every other drawn unit's.
    private void FollowTheVisitor(int tick, SmokeOrders orders, ChassisLooks looks)
    {
        if (_visitorUnseenAt < 0 || _visitorSeenAt >= 0 || !orders.View.World.TryGetDrawnPosition(_visitor, out _))
        {
            return;
        }

        _visitorSeenAt = tick;
        if (looks.LookOf(_visitor)?.Chassis.Index != _visitorChassis!.Index)
        {
            orders.Fail($"the {_visitorChassis.Id} of player {OtherSeat(orders)} came into sight and is not drawn in its look.");
        }
    }

    private void Conclude(SmokeOrders orders, ChassisLooks looks)
    {
        var marks = orders.View.World.GetChildren().OfType<ChassisMarks>().FirstOrDefault()?.Count ?? 0;
        if (_visitorSeenAt < 0)
        {
            orders.Fail(
                $"the {_visitorChassis!.Id} of player {OtherSeat(orders)}, sent toward the local player's units at tick {_visitorUnseenAt} of the part, had not come into sight at tick {Length - 1}. " +
                "If units are slower or see less far in the data now, this part needs more ticks.");
        }
        else if (marks > _unlooked)
        {
            orders.Fail($"{marks} units carried the mark of the stand-in shape in the last frame, and only {_unlooked} fighting units that are drawn have no look.");
        }
        else
        {
            GD.Print(
                $"Smoke test, chassis looks: {looks.Looks.Looks.Count} looks; one unit of each was made for the local player and drawn in its look. " +
                $"The other seat's {_visitorChassis!.Id} was not drawn while out of sight, and wore its look in its owner's color from the tick it was first drawn, tick {_visitorSeenAt}. " +
                $"At most {_mostDressed} units wore a look at once; {_standIns} drawn units keep the stand-in shape, {_unlooked} of them fighting units, and {marks} carry its mark. {_closest}");
        }
    }

    private static int OtherSeat(SmokeOrders orders) => orders.Player == 0 ? 1 : 0;

    private (int X, int Y) VisitorStart() => (_place.X + ((_columns - 1) * Step) + VisitorAway, _place.Y + ((_rows - 1) * Step / 2));

    // The north-west corner of the open ground the units are made on: a block of passable
    // tiles that holds the rows and the way of the other seat's unit, east of them. Of all
    // such blocks, the one whose two ends are furthest from every unit and structure in the
    // match, so that nothing else sees the visitor come or stands in its way.
    private bool ChoosePlace(Match match)
    {
        var map = match.Map;
        var bodies = match.Bodies;
        var width = ((_columns - 1) * Step) + VisitorAway + 1;
        var depth = ((_rows - 1) * Step) + 1;
        var best = -1f;
        for (var y = Margin; y + depth <= map.Height - Margin; y++)
        {
            for (var x = Margin; x + width <= map.Width - Margin; x++)
            {
                var nearest = float.MaxValue;
                for (var i = 0; i < bodies.Count && nearest > best; i++)
                {
                    var at = WorldView.ToWorld(bodies.ValueAt(i).Position);
                    nearest = Math.Min(nearest, Away(at, x, y + (depth / 2f), x + width, y + (depth / 2f)));
                }

                if (nearest <= best)
                {
                    continue;
                }

                var open = true;
                for (var row = 0; row < depth && open; row++)
                {
                    for (var column = 0; column < width && open; column++)
                    {
                        open = match.Grid.IsPassable(x + column, y + row);
                    }
                }

                if (open)
                {
                    best = nearest;
                    _place = (x, y);
                }
            }
        }

        return best >= 0f;
    }

    // The distance from a point on the ground to the nearer end of a row of tiles.
    private static float Away(Vector3 point, float fromX, float fromY, float toX, float toY) =>
        Math.Min(new Vector2(point.X - fromX, point.Z - fromY).Length(), new Vector2(point.X - toX, point.Z - toY).Length());
}
