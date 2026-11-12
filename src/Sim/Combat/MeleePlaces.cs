using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// Finds a free place by a target for a melee unit that its own side leaves no room (design
/// 2.4): a point from which the target is in reach with a clear line, where the unit can stand,
/// and where it keeps its spacing from every unit that will not make way for it. The numbers of
/// the design, 7 Guards in reach of one infantry unit and 10 in reach of one Cavalry/Armor unit,
/// are how many such places there are round one target.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the places are.</b> On the circle round the target that lies <see cref="Inside"/>
/// within the unit's reach: as far out as the unit can stand and still hit, which is where the
/// most units fit. A unit takes the place nearest to where it stands, of these: the point of the
/// circle that faces it, and the two points of the circle beside each unit in its way, as close
/// to that unit as their spacing and <see cref="Gap"/> allow. So units that come from one side
/// stand shoulder to shoulder round the target and leave no gap that is too small to use.
/// </para>
/// <para>
/// <b>Round a structure</b> the places lie on the line that is <see cref="Inside"/> within the
/// unit's reach of the footprint's edge: a square with rounded corners. A unit takes the place
/// nearest to where it stands, of these: the point of that line that faces it, which is straight
/// out from the point of the footprint nearest to it, and the points of the line beside each unit
/// in its way. So the first units stand where they came up, and those behind them fill the side
/// they came to and then go round the corners to the sides that are free. Ground that is
/// blocked takes its places away: of a wall tile in a line of wall, only the two open faces have
/// places.
/// </para>
/// <para>
/// <b>Who is in the way.</b> Of the unit's own side: a melee unit that is fighting, where it
/// stands; a unit that has a place, at its place, whether it stands there yet or not; and any
/// other unit that holds its ground (<see cref="IGroundHolders"/>). Other units of its side step
/// aside for a unit on its place and are not counted. Of the enemy: every unit but the target,
/// because no unit may stand closer to an enemy than their spacing.
/// </para>
/// <para>
/// <b>What it does not do.</b> It moves nobody who already has a place. Units that come from
/// several sides each take the place that faces them, and what is left between them may be less
/// than one place wide, so fewer units than the design's numbers may fit. A stretch of the
/// circle, or of the line round a footprint, that begins and ends at blocked ground and does not
/// face the unit is looked at only where a unit in the way stands by it: the far face of a wall
/// tile in a line has a place for a unit only beside a unit of its side that is there already,
/// and only when the unit can walk round the line.
/// </para>
/// <para>
/// The places are searched in the order of the units in the store, and a place given out is in
/// the state at once (<see cref="Combatant.Place"/>), so two units that look in the same tick
/// never get the same one. Everything here is integer arithmetic, and a mirrored fight finds the
/// mirrored places, except that of two places equally near the one looked at first is taken.
/// </para>
/// </remarks>
public sealed class MeleePlaces
{
    private readonly MapGrid _grid;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Mover> _movers;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly SpatialIndex _spatialIndex;
    private readonly Targeting _targeting;
    private readonly Fighters _fighters;
    private readonly IGroundHolders[] _holders;
    private readonly Fixed _widestSpacing;

    // Scratch for one search, emptied at its start. Not state.
    private readonly List<EntityId> _found = [];
    private readonly List<Taken> _taken = [];

    /// <param name="grid">The map grid.</param>
    /// <param name="bodies">The store of bodies.</param>
    /// <param name="movers">The store of movers.</param>
    /// <param name="combatants">The store of combatants.</param>
    /// <param name="spatialIndex">The spatial index, which must hold where the units stand now.</param>
    /// <param name="targeting">Says what is in reach.</param>
    /// <param name="fighters">The melee units that are fighting.</param>
    /// <param name="holders">Each names other standing units that hold their ground.</param>
    public MeleePlaces(
        MapGrid grid,
        ComponentStore<Body> bodies,
        ComponentStore<Mover> movers,
        ComponentStore<Combatant> combatants,
        SpatialIndex spatialIndex,
        Targeting targeting,
        Fighters fighters,
        params IGroundHolders[] holders)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(spatialIndex);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(fighters);
        ArgumentNullException.ThrowIfNull(holders);
        _grid = grid;
        _bodies = bodies;
        _movers = movers;
        _combatants = combatants;
        _spatialIndex = spatialIndex;
        _targeting = targeting;
        _fighters = fighters;
        _holders = [.. holders];
        _widestSpacing = fighters.WidestSpacing;
    }

    /// <summary>
    /// How far inside the unit's reach a place lies: 1/128 of a tile. The arithmetic that finds a
    /// place is exact to a few 65,536ths of a tile, so the target is in reach from every place.
    /// </summary>
    public static Fixed Inside => Fixed.FromRaw(Fixed.RawOne / 128);

    /// <summary>
    /// How much further than their spacing a place is put from a unit in the way: 1/128 of a
    /// tile. A place counts as free when it is half of that further than the spacing, which is
    /// what standing units step apart to (<see cref="SeparationSystem.Slack"/>).
    /// </summary>
    public static Fixed Gap => Fixed.FromRaw(Fixed.RawOne / 128);

    /// <summary>
    /// Finds the free place by a target that is nearest to a unit. False when there is none.
    /// </summary>
    /// <param name="unit">The melee unit that looks.</param>
    /// <param name="body">Its body.</param>
    /// <param name="profile">Its attack.</param>
    /// <param name="order">Its order. An idle unit takes only a place it can walk straight to, no further from its post than its acquisition range.</param>
    /// <param name="post">Its post, for an idle unit.</param>
    /// <param name="target">The target, a unit or a structure.</param>
    /// <param name="targetPosition">Where the target stands: for a structure, the center of its footprint.</param>
    /// <param name="direct">
    /// True to find only a place the unit can walk straight to with every enemy standing where it
    /// does, the target included: for a unit that enemy units have stopped on its way.
    /// </param>
    /// <param name="place">The place, which may be where the unit stands.</param>
    /// <param name="crowded">True when units of its own side have places by the target or stand by it and will not make way.</param>
    public bool TryFind(
        EntityId unit,
        in Body body,
        AttackProfile profile,
        UnitOrder order,
        FixedVector2 post,
        EntityId target,
        FixedVector2 targetPosition,
        bool direct,
        out FixedVector2 place,
        out bool crowded)
    {
        ArgumentNullException.ThrowIfNull(profile);
        place = default;
        crowded = false;

        var moverIndex = _movers.IndexOf(unit);
        if (moverIndex < 0 || !TryMark(target, targetPosition, out var mark))
        {
            return false;
        }

        var position = body.Position;
        var reach = ReachOf(profile, in mark);
        var seeker = new Seeker(
            profile,
            position,
            _grid.RegionOf(position),
            order,
            post,
            mark,
            direct,
            direct && !mark.IsStructure ? SpacingRule.Between(_movers.ValueAt(moverIndex), _movers.Get(target)) : Fixed.Zero);
        crowded = Gather(unit, body.Owner, target, in mark, reach, ownSideOnly: false);

        long radius = reach.Raw - Inside.Raw;
        if (mark.IsStructure)
        {
            // A structure has no circle round it: its reach is measured from its footprint.
            return TryFindRoundFootprint(in seeker, mark.Tiles, radius, out place);
        }

        var found = false;
        var nearest = long.MaxValue;

        // The point of the circle that faces the unit.
        var towardX = (long)position.X.Raw - targetPosition.X.Raw;
        var towardY = (long)position.Y.Raw - targetPosition.Y.Raw;
        long distance = IntegerMath.Sqrt((ulong)((towardX * towardX) + (towardY * towardY)));
        if (distance > 0)
        {
            Consider(in seeker, Offset(targetPosition, towardX * radius / distance, towardY * radius / distance), ref place, ref nearest, ref found);
        }

        // The two points of the circle beside each unit in the way: where the circle round the
        // target meets the circle round that unit which no place may be inside.
        for (var i = 0; i < _taken.Count; i++)
        {
            var taken = _taken[i];
            var acrossX = (long)taken.Point.X.Raw - targetPosition.X.Raw;
            var acrossY = (long)taken.Point.Y.Raw - targetPosition.Y.Raw;
            var apartSquared = (acrossX * acrossX) + (acrossY * acrossY);
            long apart = IntegerMath.Sqrt((ulong)apartSquared);
            long keep = taken.Spacing.Raw + Gap.Raw;
            if (apart == 0 || apart > radius + keep || apart < Math.Abs(radius - keep))
            {
                continue;
            }

            // Along the line from the target to that unit, then to either side of it.
            var along = ((radius * radius) - (keep * keep) + apartSquared) / (2 * apart);
            var besideSquared = (radius * radius) - (along * along);
            if (besideSquared < 0)
            {
                continue;
            }

            long beside = IntegerMath.Sqrt((ulong)besideSquared);
            Consider(
                in seeker,
                Offset(targetPosition, ((along * acrossX) - (beside * acrossY)) / apart, ((along * acrossY) + (beside * acrossX)) / apart),
                ref place,
                ref nearest,
                ref found);
            Consider(
                in seeker,
                Offset(targetPosition, ((along * acrossX) + (beside * acrossY)) / apart, ((along * acrossY) - (beside * acrossX)) / apart),
                ref place,
                ref nearest,
                ref found);
        }

        return found;
    }

    /// <summary>
    /// True when a unit that stands in reach of its target may take where it stands as its
    /// place: no unit of its own side that will not make way stands or has its place closer to
    /// it than their spacing.
    /// </summary>
    /// <param name="unit">The melee unit, which stands in reach of the target.</param>
    /// <param name="body">Its body.</param>
    /// <param name="profile">Its attack.</param>
    /// <param name="target">The target, a unit or a structure.</param>
    /// <param name="targetPosition">Where the target stands: for a structure, the center of its footprint.</param>
    public bool IsFreeWhereItStands(EntityId unit, in Body body, AttackProfile profile, EntityId target, FixedVector2 targetPosition)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!_movers.Contains(unit) || !TryMark(target, targetPosition, out var mark))
        {
            return false;
        }

        Gather(unit, body.Owner, target, in mark, ReachOf(profile, in mark), ownSideOnly: true);
        foreach (var taken in _taken)
        {
            if (FixedVector2.DistanceSquaredRaw(body.Position, taken.Point) < FixedVector2.SquareRaw(taken.Spacing))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What a place is measured to: a unit that can walk, at its center, or a structure, at its
    /// footprint. False for anything else.
    /// </summary>
    private bool TryMark(EntityId target, FixedVector2 targetPosition, out Mark mark)
    {
        mark = default;
        if (!_bodies.TryGet(target, out var targetBody))
        {
            return false;
        }

        mark = _targeting.MarkOf(target, in targetBody);
        if (mark.IsStructure)
        {
            return true;
        }

        mark = mark with { Center = targetPosition };
        return _movers.Contains(target);
    }

    /// <summary>The melee reach of a unit against its target: to the center of a unit, to the nearest edge of a structure.</summary>
    private Fixed ReachOf(AttackProfile profile, in Mark mark) =>
        mark.IsStructure ? _targeting.Rules.StructureReach : _targeting.Rules.Reach(profile.Line, mark.Line);

    /// <summary>
    /// Finds the free place round a footprint that is nearest to the seeker, on the line that
    /// lies <paramref name="radius"/> from the footprint's edge: four straight sides and a quarter
    /// circle round each corner.
    /// </summary>
    private bool TryFindRoundFootprint(in Seeker seeker, TileSquare tiles, long radius, out FixedVector2 place)
    {
        place = default;
        var found = false;
        var nearest = long.MaxValue;
        long left = (long)tiles.X * Fixed.RawOne;
        long top = (long)tiles.Y * Fixed.RawOne;
        var right = left + ((long)tiles.Size * Fixed.RawOne);
        var bottom = top + ((long)tiles.Size * Fixed.RawOne);

        // The point of the line that faces the unit: straight out from the nearest point of the footprint.
        var edge = tiles.NearestPointTo(seeker.Position);
        var towardX = (long)seeker.Position.X.Raw - edge.X.Raw;
        var towardY = (long)seeker.Position.Y.Raw - edge.Y.Raw;
        long distance = IntegerMath.Sqrt((ulong)((towardX * towardX) + (towardY * towardY)));
        if (distance > 0)
        {
            Consider(in seeker, Offset(edge, towardX * radius / distance, towardY * radius / distance), ref place, ref nearest, ref found);
        }

        // The points of the line beside each unit in the way: where the line meets the circle
        // round that unit which no place may be inside.
        for (var i = 0; i < _taken.Count; i++)
        {
            var taken = _taken[i];
            long keep = taken.Spacing.Raw + Gap.Raw;
            long atX = taken.Point.X.Raw;
            long atY = taken.Point.Y.Raw;

            // The two sides that run along x, then the two that run along y.
            for (var side = 0; side < 2; side++)
            {
                var y = side == 0 ? top - radius : bottom + radius;
                var acrossSquared = (keep * keep) - ((y - atY) * (y - atY));
                if (acrossSquared >= 0)
                {
                    long across = IntegerMath.Sqrt((ulong)acrossSquared);
                    ConsiderOnSide(in seeker, atX - across, y, atX - across, left, right, ref place, ref nearest, ref found);
                    ConsiderOnSide(in seeker, atX + across, y, atX + across, left, right, ref place, ref nearest, ref found);
                }

                var x = side == 0 ? left - radius : right + radius;
                acrossSquared = (keep * keep) - ((x - atX) * (x - atX));
                if (acrossSquared >= 0)
                {
                    long across = IntegerMath.Sqrt((ulong)acrossSquared);
                    ConsiderOnSide(in seeker, x, atY - across, atY - across, top, bottom, ref place, ref nearest, ref found);
                    ConsiderOnSide(in seeker, x, atY + across, atY + across, top, bottom, ref place, ref nearest, ref found);
                }
            }

            // The quarter circle round each corner: where it meets that circle, as round a unit.
            for (var corner = 0; corner < 4; corner++)
            {
                var cornerX = (corner & 1) == 0 ? left : right;
                var cornerY = (corner & 2) == 0 ? top : bottom;
                var acrossX = atX - cornerX;
                var acrossY = atY - cornerY;
                var apartSquared = (acrossX * acrossX) + (acrossY * acrossY);
                long apart = IntegerMath.Sqrt((ulong)apartSquared);
                if (apart == 0 || apart > radius + keep || apart < Math.Abs(radius - keep))
                {
                    continue;
                }

                var along = ((radius * radius) - (keep * keep) + apartSquared) / (2 * apart);
                var besideSquared = (radius * radius) - (along * along);
                if (besideSquared < 0)
                {
                    continue;
                }

                long beside = IntegerMath.Sqrt((ulong)besideSquared);
                for (var turn = -1; turn <= 1; turn += 2)
                {
                    var outX = ((along * acrossX) - (turn * beside * acrossY)) / apart;
                    var outY = ((along * acrossY) + (turn * beside * acrossX)) / apart;

                    // Only the quarter that points away from the footprint is part of the line.
                    if (((corner & 1) == 0 ? outX <= 0 : outX >= 0) && ((corner & 2) == 0 ? outY <= 0 : outY >= 0))
                    {
                        Consider(
                            in seeker,
                            new FixedVector2(Fixed.FromRaw(checked((int)(cornerX + outX))), Fixed.FromRaw(checked((int)(cornerY + outY)))),
                            ref place,
                            ref nearest,
                            ref found);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Considers a point of a straight side of the line round a footprint, when it lies between the side's two ends.</summary>
    private void ConsiderOnSide(
        in Seeker seeker, long x, long y, long along, long from, long to, ref FixedVector2 place, ref long nearest, ref bool found)
    {
        if (along >= from && along <= to)
        {
            Consider(in seeker, new FixedVector2(Fixed.FromRaw(checked((int)x)), Fixed.FromRaw(checked((int)y))), ref place, ref nearest, ref found);
        }
    }

    private static FixedVector2 Offset(FixedVector2 from, long x, long y) =>
        new(Fixed.FromRaw(checked((int)(from.X.Raw + x))), Fixed.FromRaw(checked((int)(from.Y.Raw + y))));

    /// <summary>
    /// Lists what is in the way of a place by a target: the units of the seeker's side that will
    /// not make way, and the enemies other than the target. True when one of its own side is
    /// near enough to the target to take room from it.
    /// </summary>
    private bool Gather(EntityId unit, int owner, EntityId target, in Mark mark, Fixed reach, bool ownSideOnly)
    {
        _taken.Clear();
        var crowded = false;

        // A structure is filed under its center, and its places lie round its footprint.
        var targetPosition = mark.Center;
        var around = mark.IsStructure ? _targeting.Rules.FurthestEdge : Fixed.Zero;

        // A unit matters when it is within its spacing of a place. A unit on its way to a place
        // is not there yet, so the search looks a tile further; one further off than that is
        // missed, and the unit that finds its place taken when it gets there looks again.
        _spatialIndex.QueryCircle(targetPosition, around + reach + _widestSpacing + Gap + Fixed.One, _found);
        foreach (var other in _found)
        {
            if (other == unit
                || other == target
                || !_bodies.TryGet(other, out var otherBody)
                || !SpacingRule.TryBetween(_movers, unit, other, out var spacing))
            {
                continue;
            }

            var point = otherBody.Position;
            if (!SpacingRule.AreEnemyGroundUnits(owner, otherBody.Owner))
            {
                if (_combatants.TryGet(other, out var combatant) && combatant.HasPlace)
                {
                    point = combatant.Place;
                }
                else if (!StandsItsGround(other))
                {
                    continue;
                }

                crowded |= mark.DistanceSquaredRawTo(point) < FixedVector2.SquareRaw(reach + spacing);
            }
            else if (ownSideOnly)
            {
                continue;
            }

            _taken.Add(new Taken(point, spacing, Enemy: otherBody.Owner != owner));
        }

        return crowded;
    }

    private bool StandsItsGround(EntityId unit)
    {
        if (_fighters.IsFighting(unit))
        {
            return true;
        }

        if (_movers.Get(unit).State != MoveState.Idle)
        {
            return false;
        }

        foreach (var holders in _holders)
        {
            if (holders.HoldsGround(unit))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Takes a point as the place so far when it is a free place and nearer than the one before.</summary>
    private void Consider(in Seeker seeker, FixedVector2 point, ref FixedVector2 place, ref long nearest, ref bool found)
    {
        var distance = FixedVector2.DistanceSquaredRaw(seeker.Position, point);
        if (distance >= nearest || !IsFree(in seeker, point))
        {
            return;
        }

        place = point;
        nearest = distance;
        found = true;
    }

    private bool IsFree(in Seeker seeker, FixedVector2 point)
    {
        // Somewhere the unit can walk to and stand, as a walk that ends there would leave it.
        var mark = seeker.Mark;
        if (_grid.RegionOf(point) != seeker.Region
            || seeker.Region == 0
            || _grid.KeepClear(point, Mover.StandOff) != point
            || !_targeting.Reaches(seeker.Profile, point, in mark))
        {
            return false;
        }

        var half = Gap.Raw / 2;
        foreach (var taken in _taken)
        {
            long keep = taken.Spacing.Raw + half;
            if (FixedVector2.DistanceSquaredRaw(point, taken.Point) < keep * keep
                || (seeker.Direct && taken.Enemy && !SpacingRule.AllowsStep(seeker.Position, point, taken.Point, taken.Spacing)))
            {
                return false;
            }
        }

        // The target is in the way of a straight walk too: a unit by its spacing, a structure as
        // the blocked ground it is.
        if (seeker.Direct
            && (mark.IsStructure
                ? !_grid.IsCorridorClear(seeker.Position, point, Mover.Clearance)
                : !SpacingRule.AllowsStep(seeker.Position, point, mark.Center, seeker.TargetSpacing)))
        {
            return false;
        }

        // An idle unit takes no way round blocked ground and stays within reach of its post.
        return seeker.Order != UnitOrder.Idle
            || point == seeker.Position
            || (FixedVector2.DistanceSquaredRaw(seeker.Post, point) <= seeker.Profile.AcquisitionSquared
                && _grid.IsCorridorClear(seeker.Position, point, Mover.Clearance));
    }

    /// <summary>A point no place may be closer to than the spacing between the seeker and the unit that has it.</summary>
    private readonly record struct Taken(FixedVector2 Point, Fixed Spacing, bool Enemy);

    /// <summary>
    /// What one search is for. The spacing to the target is set for a direct search by a target
    /// that is a unit; a structure is kept away from as blocked ground.
    /// </summary>
    private readonly record struct Seeker(
        AttackProfile Profile,
        FixedVector2 Position,
        int Region,
        UnitOrder Order,
        FixedVector2 Post,
        Mark Mark,
        bool Direct,
        Fixed TargetSpacing);
}
