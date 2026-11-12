using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The search for a target is written to be quick: it takes its candidates unsorted and asks of
/// each first what rules out most. These tests hold it to the search as design 2.4 words it,
/// written the slow way: every candidate in ascending ID order, everything asked of each, and
/// the first of the nearest kept. Both must pick the same target for every unit, in crowded
/// scenes where many enemies are exactly equally near.
/// </summary>
public class TargetSearchTests
{
    private const int Width = 48;
    private const int Height = 36;
    private const int Players = 3;

    private static readonly string[] StructureIds = [Capitol, Barracks, Generator, Academy, Wall, Wall, Tower, Tower];

    /// <summary>
    /// Units of every chassis and workers of three players on the centers of tiles, so that many
    /// are equally far from one another, among structures of every kind that picking tells
    /// apart, on a map with ground that cannot be walked on. Every unit picks as the slow search
    /// does: idle and on an attack-move, from its post and from elsewhere, and stopped by enemies.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void EveryUnitPicksWhatTheSlowSearchPicks(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        var tally = new Tally();

        // Crowded, where there is always a unit to pick, and one player's units among the
        // others' structures, where a structure is often all there is.
        Compare(Scene(random, units: 260, StructureIds, onTileCenters: seed % 2 == 1), random, tally);
        Compare(Scene(random, units: 80, [Barracks, Generator, Generator, Wall, Tower], onTileCenters: seed % 2 == 1, oneSided: true), random, tally);

        // Not an empty promise: units, Towers and structures picked for want of them, and nothing at all.
        Assert.True(tally.Units > 500, $"{tally.Units} picks of a unit");
        Assert.True(tally.Towers > 100, $"{tally.Towers} picks of a Tower");
        Assert.True(tally.OtherStructures > 10, $"{tally.OtherStructures} picks of another structure");
        Assert.True(tally.Nothing > 10, $"{tally.Nothing} searches that found nothing");
        Assert.True(tally.InReach > 100, $"{tally.InReach} picks in reach");

        // On the centers of tiles, many enemies are exactly equally near.
        Assert.True(tally.Ties > (seed % 2 == 1 ? 100 : 10), $"{tally.Ties} picks among equally near enemies");
        Assert.True(tally.StructureTies > (seed % 2 == 1 ? 10 : -1), $"{tally.StructureTies} picks among equally near structures");
    }

    /// <summary>
    /// The index is as old as the last rebuild. Entities destroyed since, with or without having
    /// been removed, bodies that have moved or changed hands, and bodies taken out of the store,
    /// which moves the ones after them: the search still picks what the slow one picks.
    /// </summary>
    [Theory]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void TheSearchPicksTheSameFromAnIndexThatIsOutOfDate(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        var match = Scene(random, units: 260, StructureIds, onTileCenters: true);
        var entities = Enumerable.Range(0, match.Bodies.Count).Select(match.Bodies.EntityAt).ToArray();

        foreach (var entity in entities)
        {
            switch (random.NextInt(12))
            {
                case 0:
                    match.World.Entities.Destroy(entity);
                    break;
                case 1:
                    match.Bodies.Get(entity).Owner = random.NextInt(Players);
                    break;
                case 2 when match.Combatants.Contains(entity):
                    match.Bodies.Get(entity).Position = MapGrid.TileCenter(random.NextInt(Width), random.NextInt(Height));
                    break;
                case 3 when match.Combatants.Contains(entity):
                    match.Health.Remove(entity);
                    break;
            }
        }

        var tally = new Tally();
        Compare(match, random, tally);

        // The destroyed are removed and the index is still the old one: every place it noted is now wrong.
        match.World.FlushDestroyedEntities();
        Compare(match, random, tally);

        Assert.True(tally.Units > 200, $"{tally.Units} picks of a unit");
        Assert.True(tally.Ties > 100, $"{tally.Ties} picks among equally near enemies");
    }

    /// <summary>
    /// Equally near enemies in different squares of the index, which lists square by square: the
    /// lowest ID is picked wherever it stands, among units, among Towers, and among the
    /// structures a unit on an attack-move picks for want of either.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheLowestIdOfEquallyNearEnemiesIsPickedWhereverItStands(int first)
    {
        // Four places 4 tiles from (14.5, 14.5), each in a square of its own; the one spawned first has the lowest ID.
        (int X, int Y)[] places = [(14, 10), (10, 14), (18, 14), (14, 18)];
        var order = Enumerable.Range(0, 4).Select(i => places[(first + i) % 4]).ToArray();

        var units = Field();
        var peltast = units.Unit(0, Peltast, "14.5", "14.5");
        var workers = order.Select(place => units.SpawnWorker(1, MapGrid.TileCenter(place.X, place.Y))).ToArray();
        units.RefreshDerivedState();
        units.Vision.Refresh();
        Assert.Equal(workers[0], Pick(units, peltast, UnitOrder.Idle));
        Assert.Equal(workers[0], Pick(units, peltast, UnitOrder.AttackMove));

        // A footprint of 2 tiles whose nearest edge is 3 tiles from the unit, on each of the four sides.
        (int X, int Y)[] sites = [(14, 10), (10, 14), (18, 14), (14, 18)];
        var sitesInOrder = Enumerable.Range(0, 4).Select(i => sites[(first + i) % 4]).ToArray();
        foreach (var (structure, isPickedIdle) in new[] { (Tower, true), (Generator, false) })
        {
            var match = Field();
            var unit = match.Unit(0, Peltast, "15", "15");
            var placed = sitesInOrder.Select(site => match.PlaceStructure(1, structure, site.X, site.Y)).ToArray();
            match.RefreshDerivedState();
            match.Vision.Refresh();
            var distances = placed.Select(entity => match.Targeting().MarkOf(entity, match.Bodies.Get(entity)).DistanceSquaredRawTo(match.PositionOf(unit))).ToArray();
            Assert.Single(distances.Distinct());

            Assert.Equal(isPickedIdle ? placed[0] : EntityId.None, Pick(match, unit, UnitOrder.Idle));
            Assert.Equal(placed[0], Pick(match, unit, UnitOrder.AttackMove));
        }
    }

    private static EntityId Pick(Match match, EntityId unit, UnitOrder order)
    {
        var body = match.Bodies.Get(unit);
        var profile = match.Combat.ProfileOf(body.Owner, match.Combatants.Get(unit).Chassis);
        var picked = match.Targeting().Pick(match.World, unit, in body, profile, order, body.Position);
        Assert.Equal(Slow(match, match.Targeting(), unit, in body, profile, order, body.Position, inReachOnly: false), picked);
        return picked;
    }

    /// <summary>Has every fighting unit pick, each way a unit can, and compares each pick with the slow search's.</summary>
    private static void Compare(Match match, DeterministicRandom random, Tally tally)
    {
        var targeting = match.Targeting();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var self = match.Combatants.EntityAt(i);
            if (!match.Bodies.TryGet(self, out var body))
            {
                continue;
            }

            var profile = match.Combat.ProfileOf(body.Owner, match.Combatants.ValueAt(i).Chassis);
            var elsewhere = body.Position + FixedVector2.FromInts(random.NextInt(9) - 4, random.NextInt(9) - 4);
            foreach (var order in new[] { UnitOrder.Idle, UnitOrder.AttackMove })
            {
                foreach (var post in new[] { body.Position, elsewhere })
                {
                    var expected = Slow(match, targeting, self, in body, profile, order, post, inReachOnly: false);
                    var picked = targeting.Pick(match.World, self, in body, profile, order, post);
                    Assert.True(expected == picked, $"{self} at {body.Position}, {order} with its post at {post}: picked {picked}, and the slow search {expected}.");
                    tally.Count(match, targeting, in body, picked);
                }
            }

            var expectedInReach = Slow(match, targeting, self, in body, profile, UnitOrder.Idle, body.Position, inReachOnly: true);
            var inReach = targeting.PickInReach(match.World, self, in body, profile);
            Assert.True(expectedInReach == inReach, $"{self} at {body.Position}, stopped: picked {inReach}, and the slow search {expectedInReach}.");
            tally.InReach += inReach != EntityId.None ? 1 : 0;
        }
    }

    /// <summary>
    /// The search as it was before it was made quick, and as design 2.4 words it: every candidate
    /// in ascending ID order, everything asked of each, and the first of the nearest kept.
    /// </summary>
    private static EntityId Slow(
        Match match, Targeting targeting, EntityId self, in Body body, AttackProfile profile, UnitOrder order, FixedVector2 post, bool inReachOnly)
    {
        var rules = targeting.Rules;
        var position = body.Position;
        var region = match.Grid.RegionOf(position);
        var best = EntityId.None;
        var bestDistance = long.MaxValue;
        var bestStructure = EntityId.None;
        var bestStructureDistance = long.MaxValue;

        var found = new List<EntityId>();
        match.SpatialIndex.QueryCircle(position, profile.Acquisition + rules.FurthestEdge, found);
        foreach (var candidate in found)
        {
            if (candidate == self || !targeting.IsEnemy(match.World, body.Owner, candidate, out var other))
            {
                continue;
            }

            var mark = targeting.MarkOf(candidate, in other);
            var last = false;
            if (mark.IsStructure)
            {
                var pick = rules.PickOf(mark.Structure);
                last = pick == StructurePick.Last;
                if (pick == StructurePick.Never || (last && order != UnitOrder.AttackMove))
                {
                    continue;
                }
            }

            var distance = mark.DistanceSquaredRawTo(position);
            if (distance > profile.AcquisitionSquared
                || distance >= (last ? bestStructureDistance : bestDistance)
                || distance < profile.MinRangeSquared)
            {
                continue;
            }

            if (!rules.CanHit(profile.Line, mark.Line) || (profile.Melee && !IsBesideRegion(match.Grid, in mark, region)))
            {
                continue;
            }

            if ((inReachOnly || order == UnitOrder.Idle)
                && !targeting.Reaches(profile, position, in mark)
                && (inReachOnly || !targeting.TryFindStand(profile, position, post, in mark, out _)))
            {
                continue;
            }

            if (last)
            {
                bestStructure = candidate;
                bestStructureDistance = distance;
            }
            else
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best != EntityId.None ? best : bestStructure;
    }

    private static bool IsBesideRegion(MapGrid grid, in Mark mark, int region)
    {
        if (!mark.IsStructure)
        {
            return grid.RegionOf(mark.Center) == region;
        }

        var tiles = mark.Tiles;
        for (var y = tiles.Y - 1; y <= tiles.Y + tiles.Size; y++)
        {
            for (var x = tiles.X - 1; x <= tiles.X + tiles.Size; x++)
            {
                if (!tiles.Covers(x, y) && grid.RegionOf(x, y) == region)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A map with blocks of ground that cannot be walked on, one of them closing off a corner,
    /// 40 structures of the kinds named, and units of every chassis and workers of three players.
    /// One-sided, nearly all the units are the first player's and the structures are the others'.
    /// </summary>
    private static Match Scene(DeterministicRandom random, int units, string[] structures, bool onTileCenters, bool oneSided = false)
    {
        var rows = Enumerable.Range(0, Height).Select(_ => new string('.', Width).ToCharArray()).ToArray();
        for (var block = 0; block < 6; block++)
        {
            var (left, top, wide, high) = (random.NextInt(Width - 6), random.NextInt(Height - 6), 1 + random.NextInt(5), 1 + random.NextInt(5));
            for (var y = top; y < top + high; y++)
            {
                for (var x = left; x < left + wide; x++)
                {
                    rows[y][x] = '#';
                }
            }
        }

        // A corner no unit outside it can walk to.
        for (var i = 0; i < 9; i++)
        {
            rows[8][i] = '#';
            rows[i][8] = '#';
        }

        var match = Create(TestMaps.FromRows([.. rows.Select(row => new string(row))]), players: Players);
        // A player has one Capitol and one Academy at most: more would be more research slots than there are.
        var single = new HashSet<(int Player, string Structure)>();
        for (var placed = 0; placed < 40;)
        {
            var structure = match.Data.StructureOf(structures[random.NextInt(structures.Length)])!;
            var (x, y, player) = (random.NextInt(Width), random.NextInt(Height), oneSided ? 1 + random.NextInt(Players - 1) : random.NextInt(Players));
            if (match.HasRoomFor(structure, x, y) && (structure.Id is not (Capitol or Academy) || single.Add((player, structure.Id))))
            {
                match.PlaceStructure(player, structure, x, y);
                placed++;
            }
        }

        var quarter = Fixed.One / 4;
        for (var placed = 0; placed < units;)
        {
            var (x, y) = (random.NextInt(Width), random.NextInt(Height));
            if (!match.Grid.IsPassable(x, y))
            {
                continue;
            }

            var point = onTileCenters
                ? MapGrid.TileCenter(x, y)
                : FixedVector2.FromInts(x, y) + new FixedVector2(quarter * random.NextInt(4), quarter * random.NextInt(4));
            var player = oneSided && random.NextInt(16) > 0 ? 0 : random.NextInt(Players);
            if (random.NextInt(8) == 0)
            {
                match.SpawnWorker(player, point);
            }
            else
            {
                match.SpawnChassis(player, random.NextInt(match.Data.Chassis.Count), point);
            }

            placed++;
        }

        match.RefreshDerivedState();
        match.Vision.Refresh();
        return match;
    }

    /// <summary>What the searches of a test picked, to show that the test compared what it is there to compare.</summary>
    private sealed class Tally
    {
        public int Units { get; set; }

        public int Towers { get; set; }

        public int OtherStructures { get; set; }

        public int Nothing { get; set; }

        public int InReach { get; set; }

        /// <summary>Picks where another enemy of the same kind was exactly as near as the one picked.</summary>
        public int Ties { get; set; }

        /// <summary>Those of them where the two were structures.</summary>
        public int StructureTies { get; set; }

        public void Count(Match match, Targeting targeting, in Body body, EntityId picked)
        {
            if (picked == EntityId.None)
            {
                Nothing++;
                return;
            }

            var isStructure = match.Structures.TryGet(picked, out var structure);
            if (!isStructure)
            {
                Units++;
            }
            else if (targeting.Rules.PickOf(structure.Type) == StructurePick.LikeAUnit)
            {
                Towers++;
            }
            else
            {
                OtherStructures++;
            }

            var position = body.Position;
            var distance = targeting.MarkOf(picked, match.Bodies.Get(picked)).DistanceSquaredRawTo(position);
            for (var i = 0; i < match.Bodies.Count; i++)
            {
                var other = match.Bodies.EntityAt(i);
                if (other != picked
                    && match.Bodies.ValueAt(i).Owner != body.Owner
                    && match.Structures.Contains(other) == isStructure
                    && targeting.MarkOf(other, match.Bodies.ValueAt(i)).DistanceSquaredRawTo(position) == distance)
                {
                    Ties++;
                    StructureTies += isStructure ? 1 : 0;
                    return;
                }
            }
        }
    }
}
