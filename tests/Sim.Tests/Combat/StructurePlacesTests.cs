using Sim.Combat;
using Sim.Core;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Movement;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The places round a structure (design 2.4 and 2.5): a melee unit that its own side leaves no
/// room at a structure finds a free place round its footprint, at its reach from the nearest
/// edge, so that the units behind the first few go round to the sides that are free. A unit still
/// stops as soon as the structure is in reach, nobody who has a place moves for a late one, and
/// units with no place left stand behind.
/// </summary>
public class StructurePlacesTests
{
    private const string Capitol = "capitol";
    private const string Generator = "generator";
    private const string Wall = "wall";
    private const string Hoplite = "hoplite";
    private const string Knight = "knight";

    /// <summary>The lowest tile of the structure under attack.</summary>
    private const int TileX = 16;
    private const int TileY = 10;

    /// <summary>
    /// How many of the units sent at a structure attack it. One side of a Capitol had room for 5
    /// of 12 Hoplites before there were places round a footprint, and a wall tile for 3 of 8.
    /// The line round a footprint, at 0.742 of a tile from it, is 20.66 tiles long round a
    /// Capitol, 12.66 round a Generator and 8.66 round one wall tile: room for 20, 12 and 8
    /// Hoplites at 1 tile, or 13, 8 and 5 Knights at 1.5 tiles. Units that come from one side
    /// fill it but for what is left where the two ends meet; units that come from two sides each
    /// take the place that faces them, and leave more than one gap too small to use.
    /// </summary>
    [Theory]
    [InlineData(Capitol, Hoplite, 12, 1, false, 12)]
    [InlineData(Capitol, Hoplite, 12, 1, true, 12)]
    [InlineData(Capitol, Hoplite, 12, 2, false, 12)]
    [InlineData(Capitol, Hoplite, 24, 1, false, 18)]
    [InlineData(Capitol, Hoplite, 24, 2, false, 18)]
    [InlineData(Capitol, Knight, 12, 1, false, 12)]
    [InlineData(Capitol, Knight, 18, 2, false, 13)]
    [InlineData(Generator, Hoplite, 12, 1, false, 11)]
    [InlineData(Generator, Hoplite, 16, 2, false, 12)]
    [InlineData(Wall, Hoplite, 8, 1, false, 8)]
    [InlineData(Wall, Hoplite, 12, 2, false, 7)]
    [InlineData(Wall, Knight, 8, 1, false, 5)]
    public void UnitsSentAtAStructureAttackItFromRoundItsFootprint(string structure, string chassis, int count, int sides, bool attackMove, int attackers)
    {
        var siege = Siege(structure, chassis, count, sides, attackMove);
        var match = siege.Match;
        var hits = siege.RunAndCountHits(800);

        // The units that attack are the units that have a place, and each stands on its place,
        // in reach of the footprint and no nearer to it than the line the places are on.
        var placed = siege.Units.Where(unit => match.UnitOf(unit).HasPlace).ToArray();
        Assert.Equal(attackers, placed.Length);
        Assert.Equal(placed, siege.Units.Where(unit => hits[unit] > 0));
        Assert.All(placed, unit =>
        {
            Assert.Equal(match.UnitOf(unit).Place, match.PositionOf(unit));
            Assert.InRange(siege.GapOf(unit), Line(match).Raw - 2, match.Combat.StructureReach.Raw);
        });
        match.AssertKeepTheirSpacing([.. siege.Units]);

        // The others stand still behind them, out of reach, and have not attacked.
        var before = siege.Units.Select(match.PositionOf).ToArray();
        var later = siege.RunAndCountHits(100);
        Assert.Equal(before, siege.Units.Select(match.PositionOf));
        Assert.All(siege.Units.Except(placed), unit =>
        {
            Assert.True(siege.GapOf(unit) > match.Combat.StructureReach.Raw);
            Assert.Equal(siege.Target, match.UnitOf(unit).Target);
            Assert.Equal(MoveState.Idle, match.Movers.Get(unit).State);
            Assert.Equal(0, later[unit]);
        });
        Assert.All(placed, unit => Assert.True(later[unit] > 0));
    }

    /// <summary>
    /// Twelve Hoplites that come at a Capitol from the west do not all fit on its west side: those
    /// behind the first go round its corners and attack from the north and the south side.
    /// Nobody goes as far as the east side.
    /// </summary>
    [Fact]
    public void UnitsWithNoRoomOnTheSideTheyCameToUseTheFreeSides()
    {
        var siege = Siege(Capitol, Hoplite, 12);
        var hits = siege.RunAndCountHits(800);

        Assert.All(siege.Units, unit => Assert.True(hits[unit] > 0));
        var places = siege.Units.Select(siege.Match.PositionOf).ToArray();
        var west = Fixed.FromInt(TileX);
        var east = Fixed.FromInt(TileX + 4);
        var north = Fixed.FromInt(TileY);
        var south = Fixed.FromInt(TileY + 4);
        Assert.Equal(3, places.Count(place => place.X < west && place.Y >= north && place.Y <= south));
        Assert.Equal(4, places.Count(place => place.Y < north && place.X >= west && place.X <= east));
        Assert.Equal(2, places.Count(place => place.Y > south && place.X >= west && place.X <= east));
        Assert.Equal(3, places.Count(place => (place.X < west || place.X > east) && (place.Y < north || place.Y > south)));
        Assert.DoesNotContain(places, place => place.X > east && place.Y >= north && place.Y <= south);
    }

    /// <summary>
    /// A unit alone stops as soon as the structure is in reach (design 2.4): on the line of the
    /// places, straight out from the point of the footprint that was nearest to it as it came,
    /// and there it attacks.
    /// </summary>
    [Theory]
    [InlineData("4.5", "12.3", false)]
    [InlineData("4.5", "12.3", true)]
    [InlineData("9.5", "3.5", false)]
    [InlineData("26.5", "20.5", true)]
    public void AUnitAloneStopsWhereTheStructureComesIntoReach(string x, string y, bool attackMove)
    {
        var match = Field();
        var capitol = Place(match, Capitol);
        var unit = match.Unit(0, Hoplite, x, y);
        var from = match.PositionOf(unit);
        Spotter(match);
        Order(match, capitol, attackMove, unit);
        var tiles = match.Structures.Get(capitol).Tiles;
        var inReach = -1;
        var stopped = -1;
        for (var tick = 1; tick <= 400 && stopped < 0; tick++)
        {
            match.Run(1);
            if (inReach < 0 && tiles.DistanceSquaredRawTo(match.PositionOf(unit)) <= match.Combat.StructureReachSquared)
            {
                inReach = tick;
            }

            if (inReach >= 0 && match.Movers.Get(unit).State == MoveState.Idle)
            {
                stopped = tick;
            }
        }

        // It stops in the tick in which the structure comes into reach, or the one after.
        Assert.InRange(stopped - inReach, 0, 1);
        var at = match.PositionOf(unit);
        Assert.InRange(Distance(at, tiles.NearestPointTo(at)), Line(match).Raw - 2, match.Combat.StructureReach.Raw);
        Assert.True(match.UnitOf(unit).HasPlace);
        Assert.Equal(at, match.UnitOf(unit).Place);
        Assert.True(FixedVector2.DistanceSquaredRaw(from, at) < FixedVector2.DistanceSquaredRaw(from, tiles.Center));
        match.Run(40);
        Assert.Equal(at, match.PositionOf(unit));
        Assert.Equal(at, match.UnitOf(unit).Fought);
    }

    /// <summary>
    /// Nobody who has a place moves for a unit that comes late (design 2.4: units that stop keep
    /// their spacing and are not shifted along). The late unit takes the nearest place that is
    /// free, beside the others.
    /// </summary>
    [Fact]
    public void UnitsThatHaveTheirPlacesDoNotMoveForALateOne()
    {
        var siege = Siege(Capitol, Hoplite, 8);
        var match = siege.Match;
        siege.RunAndCountHits(500);
        var units = siege.Units.ToArray();
        Assert.All(units, unit => Assert.Equal(match.UnitOf(unit).Place, match.PositionOf(unit)));
        var before = units.Select(match.PositionOf).ToArray();

        var late = match.Unit(0, Hoplite, "5.5", "12.5");
        match.OrderAttack(0, siege.Target, late);
        siege.Units.Add(late);
        var hits = siege.RunAndCountHits(400);

        Assert.Equal(before, units.Select(match.PositionOf));
        Assert.True(match.UnitOf(late).HasPlace);
        Assert.Equal(match.UnitOf(late).Place, match.PositionOf(late));
        Assert.True(hits[late] > 0);
        Assert.All(units, unit => Assert.True(hits[unit] > 0));
        match.AssertKeepTheirSpacing([.. siege.Units]);
    }

    /// <summary>A place that falls free is taken by a unit that was waiting for one.</summary>
    [Fact]
    public void AUnitThatWaitsTakesAPlaceThatFallsFree()
    {
        var siege = Siege(Capitol, Hoplite, 24);
        var match = siege.Match;
        siege.RunAndCountHits(800);
        var placed = siege.Units.Where(unit => match.UnitOf(unit).HasPlace).ToArray();
        var waiting = siege.Units.Except(placed).ToArray();
        Assert.Equal(18, placed.Length);

        // One on the side they came to, where the others wait behind it.
        var fallen = placed.MinBy(unit => match.PositionOf(unit).X.Raw);
        var where = match.PositionOf(fallen);
        match.World.Entities.Destroy(fallen);
        siege.Units.Remove(fallen);
        var hits = siege.RunAndCountHits(200);

        var taker = Assert.Single(waiting, unit => match.UnitOf(unit).HasPlace);
        Assert.True(hits[taker] > 0);
        Assert.True(FixedVector2.DistanceSquaredRaw(where, match.PositionOf(taker)) < FixedVector2.SquareRaw(Fixed.One));
        Assert.All(placed.Where(unit => unit != fallen), unit => Assert.Equal(match.UnitOf(unit).Place, match.PositionOf(unit)));
        match.AssertKeepTheirSpacing([.. siege.Units]);
    }

    /// <summary>
    /// A wall tile in a line of wall has places only on its two open faces: the tiles of the
    /// line on either side of it are blocked ground. Units sent at it from one side fill that
    /// face, with two of them where each took the place that faced it (the design counts at most
    /// 3 Guards or 2 Cavalry/Armor units at one wall tile from one side), and the others wait
    /// behind them. They do not attack another tile of the line, and they do not walk round the
    /// end of the line to the far face: nobody of their side stands there for a place to be
    /// looked for beside.
    /// </summary>
    [Theory]
    [InlineData(Hoplite)]
    [InlineData(Knight)]
    public void AWallTileInALineHasPlacesOnlyOnItsOpenFaces(string chassis)
    {
        var siege = Siege(Wall, chassis, 8, wallTiles: 4);
        var match = siege.Match;
        var hits = siege.RunAndCountHits(800);

        var placed = siege.Units.Where(unit => match.UnitOf(unit).HasPlace).ToArray();
        Assert.Equal(2, placed.Length);
        Assert.Equal(placed, siege.Units.Where(unit => hits[unit] > 0));
        Assert.All(siege.Units, unit =>
        {
            var at = match.PositionOf(unit);
            Assert.Equal(siege.Target, match.UnitOf(unit).Target);
            Assert.True(at.X < Fixed.FromInt(TileX));
            Assert.True(at.Y > Fixed.FromInt(TileY - 4) && at.Y < Fixed.FromInt(TileY + 5));
        });
        match.AssertKeepTheirSpacing([.. siege.Units]);
        var before = siege.Units.Select(match.PositionOf).ToArray();
        match.Run(100);
        Assert.Equal(before, siege.Units.Select(match.PositionOf));
    }

    /// <summary>
    /// Units sent at a wall tile in a line from both sides fill both faces. A line that does not
    /// close the way round is ground like any other: a unit that finds no place on its own face
    /// may take a free one beside the units of its side on the far face, and walks round the end
    /// of the line to it. Here one of the eight does.
    /// </summary>
    [Fact]
    public void UnitsOnBothSidesOfAnOpenLineOfWallFillBothFacesOfTheTile()
    {
        var siege = Siege(Wall, Hoplite, 8, sides: 2, wallTiles: 4);
        var match = siege.Match;
        var hits = siege.RunAndCountHits(800);

        var placed = siege.Units.Where(unit => match.UnitOf(unit).HasPlace).ToArray();
        Assert.Equal(4, placed.Length);
        Assert.Equal(placed, siege.Units.Where(unit => hits[unit] > 0));
        Assert.Equal(2, placed.Count(unit => match.PositionOf(unit).X < Fixed.FromInt(TileX)));
        Assert.Equal(2, placed.Count(unit => match.PositionOf(unit).X > Fixed.FromInt(TileX + 1)));

        // The units were made west and east by turns: one from the east has its place in the west.
        var crossed = Enumerable.Range(0, siege.Units.Count)
            .Where(i => (i % 2 == 0) != (match.PositionOf(siege.Units[i]).X < Fixed.FromInt(TileX)))
            .Select(i => siege.Units[i])
            .ToArray();
        Assert.Equal(placed.Where(unit => crossed.Contains(unit)), crossed);
        Assert.Single(crossed);
        match.AssertKeepTheirSpacing([.. siege.Units]);
    }

    /// <summary>
    /// The places are gone with the structure, and a unit that is given another order has given
    /// its place up: the next unit may take it.
    /// </summary>
    [Fact]
    public void APlaceByAStructureLastsWhileTheUnitAttacksIt()
    {
        var siege = Siege(Wall, Hoplite, 8);
        var match = siege.Match;
        siege.RunAndCountHits(500);
        Assert.All(siege.Units, unit => Assert.True(match.UnitOf(unit).HasPlace));

        var leaver = siege.Units[0];
        var place = match.PositionOf(leaver);
        match.OrderMove(0, TestMatches.Point("3.5", "20.5"), leaver);
        match.Run(100);
        Assert.False(match.UnitOf(leaver).HasPlace);
        var late = match.Unit(0, Hoplite, "8.5", "10.5");
        match.OrderAttack(0, siege.Target, late);
        match.Run(300);
        Assert.True(match.UnitOf(late).HasPlace);
        Assert.True(FixedVector2.DistanceSquaredRaw(place, match.PositionOf(late)) < FixedVector2.SquareRaw(Fixed.One));

        match.SetHitPoints(siege.Target, 1);
        match.Run(40);
        Assert.False(match.IsAlive(siege.Target));
        Assert.All(siege.Units.Append(late), unit => Assert.False(match.UnitOf(unit).HasPlace));
    }

    /// <summary>
    /// The search itself, round a Capitol. With nobody in the way the place is straight out from
    /// the point of the footprint that is nearest to the unit, 1/128 of a tile inside its reach:
    /// on a side for a unit that faces one, on the quarter circle round a corner for a unit that
    /// stands off one.
    /// </summary>
    [Theory]
    [InlineData("12", "11.25", "15.2578125", "11.25")]
    [InlineData("17.5", "5", "17.5", "9.2578125")]
    [InlineData("24", "13", "20.7421875", "13")]
    [InlineData("19", "20", "19", "14.7421875")]
    public void ThePlaceOfAUnitThatFacesASideIsStraightOutFromTheFootprint(string x, string y, string placeX, string placeY)
    {
        var match = Field();
        var capitol = Place(match, Capitol);
        var seeker = match.Unit(0, Hoplite, x, y);
        match.RefreshDerivedState();

        Assert.True(Find(match, seeker, capitol, out var place, out var crowded));
        Assert.False(crowded);
        Assert.Equal(TestMatches.Point(placeX, placeY), place);
    }

    /// <summary>Off a corner the place is on the line from the corner to the unit, at the same distance from the corner.</summary>
    [Theory]
    [InlineData("12", "7", 16, 10)]
    [InlineData("23", "8", 20, 10)]
    [InlineData("13", "19", 16, 14)]
    [InlineData("21", "17.5", 20, 14)]
    public void ThePlaceOfAUnitThatStandsOffACornerIsOnTheQuarterCircleRoundIt(string x, string y, int cornerX, int cornerY)
    {
        var match = Field();
        var capitol = Place(match, Capitol);
        var seeker = match.Unit(0, Hoplite, x, y);
        match.RefreshDerivedState();
        var corner = new FixedVector2(Fixed.FromInt(cornerX), Fixed.FromInt(cornerY));
        var from = match.PositionOf(seeker);

        Assert.True(Find(match, seeker, capitol, out var place, out _));
        Assert.InRange(Distance(place, corner), Line(match).Raw - 2, Line(match).Raw);
        Assert.InRange(FixedVector2.CrossRaw(from - corner, place - corner) / Line(match).Raw, -2, 2);
        Assert.True(FixedVector2.DistanceSquaredRaw(place, from) < FixedVector2.DistanceSquaredRaw(corner, from));
        Assert.Equal(corner, match.Structures.Get(capitol).Tiles.NearestPointTo(place));
    }

    /// <summary>
    /// Beside a unit of its side that has a place, the place is on the line round the footprint
    /// at their spacing and 1/128 of a tile from that unit, on the side of it that is nearer to
    /// the seeker: further along the same side, or round the corner when the side ends first.
    /// The mirrored siege gives the mirrored place.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ThePlaceBesideAUnitInTheWayIsOnTheSameLineRoundTheFootprint(bool mirroredInX, bool mirroredInY)
    {
        FixedVector2 At(string x, string y) => new(
            mirroredInX ? Fixed.FromInt(36) - TestMatches.Point(x, y).X : TestMatches.Point(x, y).X,
            mirroredInY ? Fixed.FromInt(24) - TestMatches.Point(x, y).Y : TestMatches.Point(x, y).Y);
        FixedVector2 Back(FixedVector2 point) => new(
            mirroredInX ? Fixed.FromInt(36) - point.X : point.X,
            mirroredInY ? Fixed.FromInt(24) - point.Y : point.Y);
        var match = Field();
        var capitol = Place(match, Capitol);
        var tiles = match.Structures.Get(capitol).Tiles;
        var hoplite = match.Data.Chassis.Single(chassis => chassis.Id == Hoplite).Index;
        var holder = match.SpawnChassis(0, hoplite, At("15.2578125", "10.75"));
        var seeker = match.SpawnChassis(0, hoplite, At("12", "11.5"));
        match.RefreshDerivedState();
        ref var unit = ref match.Combatants.Get(holder);
        unit.Target = capitol;
        unit.PlaceFor = capitol;
        unit.Place = match.PositionOf(holder);
        var keep = (Fixed.One + MeleePlaces.Gap).Raw;

        // On the same side, toward the seeker.
        Assert.True(Find(match, seeker, capitol, out var beside, out var crowded));
        Assert.True(crowded);
        Assert.Equal(TestMatches.Point("15.2578125", "11.7578125"), Back(beside));

        // The seeker stands on the other side of the holder, where the side ends less than their
        // spacing from it: the place is round the corner, on the quarter circle.
        match.Bodies.Get(seeker).Position = At("12", "9");
        match.RefreshDerivedState();
        Assert.True(Find(match, seeker, capitol, out var round, out _));
        var corner = Back(tiles.NearestPointTo(round));
        Assert.Equal(TestMatches.Point("16", "10"), corner);
        Assert.InRange(Distance(Back(round), corner), Line(match).Raw - 2, Line(match).Raw + 2);
        Assert.InRange(Distance(round, unit.Place), keep - 2, keep + 2);
        Assert.True(Back(round).X < corner.X && Back(round).Y < corner.Y);
        Assert.True(match.Targeting().Reaches(match.Combat.ProfileOf(0, hoplite), round, match.Targeting().MarkOf(capitol, match.Bodies.Get(capitol))));

        // A second unit has that place: the next is further round the corner, toward the next side.
        var second = match.SpawnChassis(0, hoplite, round);
        match.RefreshDerivedState();
        ref var other = ref match.Combatants.Get(second);
        other.Target = capitol;
        other.PlaceFor = capitol;
        other.Place = round;
        Assert.True(Find(match, seeker, capitol, out var past, out _));
        Assert.InRange(Distance(past, round), keep - 2, keep + 2);
        Assert.InRange(Gap(match, capitol, past), Line(match).Raw - 2, Line(match).Raw + 2);
        Assert.True(Back(past).X > Back(round).X && Back(past).Y < Back(round).Y);
    }

    /// <summary>
    /// A side of the line ends where the quarter circle round the corner begins. Beside a unit
    /// that stands less than their spacing from the end of a side, the place is on the quarter
    /// circle, at the same distance from the footprint as every other place, and not on the
    /// side drawn on past its end, which is further from the corner.
    /// </summary>
    [Theory]
    [InlineData("15.2578125", "10.95", "12", "9.9", 16, 10)]
    [InlineData("15.2578125", "13.05", "12", "14.1", 16, 14)]
    [InlineData("16.95", "9.2578125", "15.9", "6", 16, 10)]
    [InlineData("19.05", "9.2578125", "20.1", "6", 20, 10)]
    public void BesideAUnitNearTheEndOfASideThePlaceIsRoundTheCorner(string holderX, string holderY, string x, string y, int cornerX, int cornerY)
    {
        var match = Field();
        var capitol = Place(match, Capitol);
        var holder = match.Unit(0, Hoplite, holderX, holderY);
        var seeker = match.Unit(0, Hoplite, x, y);
        match.RefreshDerivedState();
        ref var unit = ref match.Combatants.Get(holder);
        unit.Target = capitol;
        unit.PlaceFor = capitol;
        unit.Place = match.PositionOf(holder);
        var keep = (Fixed.One + MeleePlaces.Gap).Raw;

        Assert.True(Find(match, seeker, capitol, out var place, out _));
        Assert.Equal(new FixedVector2(Fixed.FromInt(cornerX), Fixed.FromInt(cornerY)), match.Structures.Get(capitol).Tiles.NearestPointTo(place));
        Assert.InRange(Gap(match, capitol, place), Line(match).Raw - 2, Line(match).Raw + 2);
        Assert.InRange(Distance(place, unit.Place), keep - 2, keep + 2);
    }

    /// <summary>
    /// A unit that finds every place taken stops when it is within its reach of the footprint,
    /// its spacing and one step: behind the units that have the places, further from them than
    /// their spacing, and no further out than that.
    /// </summary>
    [Fact]
    public void AUnitWithNoPlaceLeftStopsItsSpacingBehindThePlaces()
    {
        var siege = Siege(Wall, Hoplite, 8);
        var match = siege.Match;
        siege.RunAndCountHits(500);
        Assert.All(siege.Units, unit => Assert.True(match.UnitOf(unit).HasPlace));

        var late = match.Unit(0, Hoplite, "8.5", "10.5");
        match.OrderAttack(0, siege.Target, late);
        siege.Units.Add(late);
        var hits = siege.RunAndCountHits(300);

        Assert.Equal(0, hits[late]);
        Assert.False(match.UnitOf(late).HasPlace);
        Assert.Equal(MoveState.Idle, match.Movers.Get(late).State);
        var behind = (match.Combat.StructureReach + Fixed.One).Raw;
        Assert.InRange(siege.GapOf(late), behind - 2, behind + match.Movers.Get(late).Speed.Raw);
        match.AssertKeepTheirSpacing([.. siege.Units]);
    }

    /// <summary>
    /// What a place by a structure may not be: closer than their spacing to a unit of its side
    /// that has a place or fights, or to an enemy unit; and for an idle unit, further from its
    /// post than it acquires. A unit of its side that only stands there makes way.
    /// </summary>
    [Fact]
    public void APlaceByAStructureIsFreeOfEveryUnitThatWillNotMakeWay()
    {
        var match = Field();
        var capitol = Place(match, Capitol);
        var seeker = match.Unit(0, Hoplite, "12", "12");
        var bystander = match.Unit(0, Hoplite, "15.2578125", "12");
        match.RefreshDerivedState();
        Assert.True(Find(match, seeker, capitol, out var facing, out var crowded));
        Assert.False(crowded);
        Assert.Equal(match.PositionOf(bystander), facing);

        // An enemy unit stands by the structure, closer to that point than their spacing. It
        // takes the point away from a unit that looks for a place, and not from one that stands
        // there in reach: where a unit stands is its place whatever enemies are near.
        var enemy = match.Unit(1, Hoplite, "15", "12.5");
        match.RefreshDerivedState();
        Assert.True(PlacesOf(match).IsFreeWhereItStands(
            bystander, match.Bodies.Get(bystander), match.Combat.ProfileOf(0, match.UnitOf(bystander).Chassis), capitol, match.PositionOf(capitol)));
        Assert.True(Find(match, seeker, capitol, out var clear, out crowded));
        Assert.False(crowded);
        Assert.NotEqual(facing, clear);
        Assert.True(FixedVector2.DistanceSquaredRaw(clear, match.PositionOf(enemy)) >= FixedVector2.SquareRaw(Fixed.One));
        Assert.InRange(Gap(match, capitol, clear), Line(match).Raw - 2, match.Combat.StructureReach.Raw);

        // A unit of its side that fights from where it stands, by the place found.
        var fighter = match.SpawnChassis(0, match.UnitOf(seeker).Chassis, clear);
        match.RefreshDerivedState();
        ref var fighting = ref match.Combatants.Get(fighter);
        fighting.Wait = Fraction.One;
        fighting.Fought = clear;
        Assert.True(Find(match, seeker, capitol, out var third, out crowded));
        Assert.True(crowded);
        Assert.True(FixedVector2.DistanceSquaredRaw(third, clear) >= FixedVector2.SquareRaw(Fixed.One));
        Assert.True(FixedVector2.DistanceSquaredRaw(third, match.PositionOf(enemy)) >= FixedVector2.SquareRaw(Fixed.One));
        Assert.InRange(Gap(match, capitol, third), Line(match).Raw - 2, match.Combat.StructureReach.Raw);

        // Idle at its post, it takes that place when the post is near enough to it and none
        // when the place is further from the post than it acquires.
        var body = match.Bodies.Get(seeker);
        var attack = match.Combat.ProfileOf(0, match.UnitOf(seeker).Chassis);
        var places = PlacesOf(match);
        Assert.True(places.TryFind(seeker, body, attack, UnitOrder.Idle, third, capitol, match.PositionOf(capitol), direct: false, out var near, out _));
        Assert.Equal(third, near);
        Assert.False(places.TryFind(seeker, body, attack, UnitOrder.Idle, TestMatches.Point("2", "22"), capitol, match.PositionOf(capitol), direct: false, out _, out _));
    }

    /// <summary>
    /// A unit that enemy units have stopped on its way looks only for a place it can walk
    /// straight to, and the structure is in the way of a straight walk as the blocked ground it
    /// is: not a place on a side of the footprint that the unit would have to walk round a
    /// corner to.
    /// </summary>
    [Fact]
    public void AUnitThatEnemiesHaveStoppedTakesOnlyAPlaceByAStructureItCanWalkStraightTo()
    {
        var siege = Siege(Capitol, Hoplite, 12);
        var match = siege.Match;
        siege.RunAndCountHits(800);
        Assert.All(siege.Units, unit => Assert.True(match.UnitOf(unit).HasPlace));

        // The side they came to is full. What is left is round a corner from a unit in the west.
        var late = match.Unit(0, Hoplite, "12", "12");
        match.RefreshDerivedState();
        var from = match.PositionOf(late);
        Assert.True(Find(match, late, siege.Target, out var round, out var crowded));
        Assert.True(crowded);
        Assert.False(match.Grid.IsCorridorClear(from, round, Mover.Clearance));
        Assert.False(Find(match, late, siege.Target, out _, out crowded, direct: true));
        Assert.True(crowded);

        // From the north-east the nearest free place is on the side that faces it.
        match.Bodies.Get(late).Position = TestMatches.Point("23", "6");
        match.RefreshDerivedState();
        Assert.True(Find(match, late, siege.Target, out var open, out _));
        Assert.True(match.Grid.IsCorridorClear(match.PositionOf(late), open, Mover.Clearance));
        Assert.True(Find(match, late, siege.Target, out var straight, out _, direct: true));
        Assert.Equal(open, straight);
    }

    /// <summary>
    /// Round a wall tile that stands alone eight Hoplites have places, and there is no ninth,
    /// whoever asks and from wherever. Where a unit stands in reach is its place only when no
    /// place of its side is closer to it than their spacing.
    /// </summary>
    [Fact]
    public void WhenEveryPlaceRoundAStructureIsTakenThereIsNone()
    {
        var siege = Siege(Wall, Hoplite, 8);
        var match = siege.Match;
        siege.RunAndCountHits(500);
        Assert.All(siege.Units, unit => Assert.True(match.UnitOf(unit).HasPlace));
        var attack = match.Combat.ProfileOf(0, match.UnitOf(siege.Units[0]).Chassis);
        var at = match.PositionOf(siege.Target);
        foreach (var (x, y) in new[] { ("12", "10.5"), ("16.5", "6"), ("21", "10.5"), ("16.5", "15"), ("13", "7"), ("20", "14") })
        {
            var late = match.Unit(0, Hoplite, x, y);
            match.RefreshDerivedState();
            Assert.False(Find(match, late, siege.Target, out _, out var crowded));
            Assert.True(crowded);
            match.World.Entities.Destroy(late);
        }

        // In reach of the wall, between two of the places.
        var between = match.Unit(0, Hoplite, "16.5", "9.4");
        match.RefreshDerivedState();
        Assert.True(match.Structures.Get(siege.Target).Tiles.DistanceSquaredRawTo(match.PositionOf(between)) <= match.Combat.StructureReachSquared);
        Assert.False(PlacesOf(match).IsFreeWhereItStands(between, match.Bodies.Get(between), attack, siege.Target, at));
        match.World.Entities.Destroy(between);

        // With the units gone the same point is free.
        foreach (var unit in siege.Units)
        {
            match.World.Entities.Destroy(unit);
        }

        var alone = match.Unit(0, Hoplite, "16.5", "9.4");
        match.RefreshDerivedState();
        Assert.True(PlacesOf(match).IsFreeWhereItStands(alone, match.Bodies.Get(alone), attack, siege.Target, at));
    }

    /// <summary>
    /// Ground that is blocked takes its places away. A wall tile in a line: the point that faces
    /// a unit on the open face is a place, and beside a unit that has it the places are on the
    /// quarter circles at the two ends of that face, no nearer to the tiles of the line than a
    /// unit may stand. There is no place on the far face for a unit beyond a line that closes
    /// the way round.
    /// </summary>
    [Fact]
    public void BlockedGroundTakesThePlacesByAStructureAway()
    {
        var match = Field();
        var wall = Place(match, Wall);
        for (var y = 0; y < 24; y++)
        {
            if (y != TileY)
            {
                match.PlaceStructure(1, SiegeMatches.Data.StructureOf(Wall), TileX, y);
            }
        }

        var seeker = match.Unit(0, Hoplite, "12", "10.5");
        var holder = match.Unit(0, Hoplite, "15.2578125", "10.5");
        match.RefreshDerivedState();
        Assert.True(Find(match, seeker, wall, out var facing, out _));
        Assert.Equal(match.PositionOf(holder), facing);

        ref var unit = ref match.Combatants.Get(holder);
        unit.Target = wall;
        unit.PlaceFor = wall;
        unit.Place = facing;
        match.Bodies.Get(seeker).Position = TestMatches.Point("12", "9");
        match.RefreshDerivedState();
        Assert.True(Find(match, seeker, wall, out var above, out _));
        Assert.InRange(Distance(above, TestMatches.Point("16", "10")), Line(match).Raw - 2, Line(match).Raw + 2);
        Assert.True(above.Y < Fixed.FromInt(TileY));
        Assert.True(above.X <= Fixed.FromInt(TileX) - Mover.StandOff);
        Assert.Equal(above, match.Grid.KeepClear(above, Mover.StandOff));

        // Both ends of the open face are taken too: nothing is left on this side, and the far
        // face is ground the unit cannot walk to.
        var upper = match.SpawnChassis(0, match.UnitOf(seeker).Chassis, above);
        var lower = match.SpawnChassis(0, match.UnitOf(seeker).Chassis, new FixedVector2(above.X, Fixed.FromInt((2 * TileY) + 1) - above.Y));
        match.RefreshDerivedState();
        foreach (var taken in new[] { upper, lower })
        {
            ref var other = ref match.Combatants.Get(taken);
            other.Target = wall;
            other.PlaceFor = wall;
            other.Place = match.PositionOf(taken);
        }

        Assert.False(Find(match, seeker, wall, out _, out var crowded));
        Assert.True(crowded);
        var beyond = match.Unit(0, Hoplite, "20", "10.5");
        match.RefreshDerivedState();
        Assert.NotEqual(match.Grid.RegionOf(match.PositionOf(seeker)), match.Grid.RegionOf(match.PositionOf(beyond)));
        Assert.True(Find(match, beyond, wall, out var far, out _));
        Assert.Equal(TestMatches.Point("17.7421875", "10.5"), far);
    }

    /// <summary>
    /// A match with units on their way to places round a structure, on them, and waiting for
    /// one is saved and restored like any other.
    /// </summary>
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public void ASnapshotTakenWhileUnitsTakeTheirPlacesRoundAStructureRestoresAndContinues(int snapshotTick)
    {
        var first = Siege(Generator, Hoplite, 16, ordered: false);
        (int Tick, Command Command)[] script = [(1, CombatMatches.AttackCommand(0, first.Target, [.. first.Units]))];
        SnapshotAssert.RestoresAndContinues(() => Siege(Generator, Hoplite, 16, ordered: false).Match, script, snapshotTick, 300);

        var match = first.Match;
        match.Simulation.Commands.Enqueue(1, script[0].Command);
        match.Run(snapshotTick);
        var units = first.Units.Select(unit => (Unit: match.UnitOf(unit), At: match.PositionOf(unit), match.Movers.Get(unit).State)).ToArray();
        if (snapshotTick < 200)
        {
            Assert.Contains(units, unit => unit.Unit.HasPlace && unit.At != unit.Unit.Place);
        }

        if (snapshotTick > 75)
        {
            Assert.Contains(units, unit => unit.Unit.HasPlace && unit.At == unit.Unit.Place);
        }

        if (snapshotTick > 75)
        {
            Assert.Contains(units, unit => !unit.Unit.HasPlace && unit.State == MoveState.Idle);
        }
    }

    /// <summary>The distance from the footprint of a structure at which the places of a melee unit lie.</summary>
    private static Fixed Line(Match match) => match.Combat.StructureReach - MeleePlaces.Inside;

    private static long Distance(FixedVector2 a, FixedVector2 b) => (long)IntegerMath.Sqrt((ulong)FixedVector2.DistanceSquaredRaw(a, b));

    private static long Gap(Match match, EntityId structure, FixedVector2 point) =>
        (long)IntegerMath.Sqrt((ulong)match.Structures.Get(structure).Tiles.DistanceSquaredRawTo(point));

    /// <summary>A structure of player 1 that is not destroyed in the time of a test, with its lowest tile at (16, 10).</summary>
    private static EntityId Place(Match match, string structure)
    {
        var placed = match.PlaceStructure(1, SiegeMatches.Data.StructureOf(structure), TileX, TileY);
        match.SetHitPoints(placed, 2_000_000_000);
        return placed;
    }

    /// <summary>
    /// A worker of the attackers' side that stands in sight of the structure, north-east of it
    /// and out of everybody's way: an attack order may name only what its player sees or
    /// remembers (design 5.3), and the attackers start further off than they see. It is made
    /// after the units, so that each of them has the ID it would have without it. Where there
    /// is no fog it changes nothing.
    /// </summary>
    private static void Spotter(Match match) => match.SpawnWorker(0, "19.5", "5.5");

    private static void Order(Match match, EntityId target, bool attackMove, params EntityId[] units)
    {
        if (attackMove)
        {
            match.OrderAttackMove(0, match.PositionOf(target), units);
        }
        else
        {
            match.OrderAttack(0, target, units);
        }
    }

    private static MeleePlaces PlacesOf(Match match)
    {
        var fighters = new Fighters(match.Data, match.Combat, match.Combatants, match.Bodies, match.Movers, match.SpatialIndex);
        return new MeleePlaces(match.Grid, match.Bodies, match.Movers, match.Combatants, match.SpatialIndex, match.Targeting(), fighters);
    }

    /// <summary>The search for a unit of player 0 that has an attack order.</summary>
    private static bool Find(Match match, EntityId seeker, EntityId target, out FixedVector2 place, out bool crowded, bool direct = false) =>
        PlacesOf(match).TryFind(
            seeker,
            match.Bodies.Get(seeker),
            match.Combat.ProfileOf(0, match.UnitOf(seeker).Chassis),
            UnitOrder.Attack,
            default,
            target,
            match.PositionOf(target),
            direct,
            out place,
            out crowded);

    /// <summary>
    /// A structure of player 1 with its lowest tile at (16, 10), and a block of units of player
    /// 0, three files wide, made 11 tiles west of it; with two sides, every second unit is in a
    /// block as far east of it. A wall may have more tiles of wall north and south of it, in one
    /// line. All units are sent at the structure with one order in the first tick.
    /// </summary>
    private static SiegeOf Siege(string structure, string chassis, int count, int sides = 1, bool attackMove = false, int wallTiles = 0, bool ordered = true)
    {
        var match = Field();
        var target = Place(match, structure);
        for (var i = 1; i <= wallTiles; i++)
        {
            match.PlaceStructure(1, SiegeMatches.Data.StructureOf(structure), TileX, TileY - i);
            match.PlaceStructure(1, SiegeMatches.Data.StructureOf(structure), TileX, TileY + i);
        }

        var units = new List<EntityId>();
        for (var i = 0; i < count; i++)
        {
            var number = i / sides;
            var x = i % sides == 0 ? 3 + (number % 3) : 28 - (number % 3);
            var y = 6 + (2 * (number / 3));
            units.Add(match.Unit(0, chassis, FormattableString.Invariant($"{x}.5"), FormattableString.Invariant($"{y}.5")));
        }

        Spotter(match);
        if (ordered)
        {
            Order(match, target, attackMove, [.. units]);
        }

        return new SiegeOf(match, target, units);
    }

    private sealed record SiegeOf(Match Match, EntityId Target, List<EntityId> Units)
    {
        /// <summary>Runs ticks and counts the melee hits of each unit on the structure in them.</summary>
        public Dictionary<EntityId, int> RunAndCountHits(int ticks)
        {
            var hits = Units.ToDictionary(unit => unit, _ => 0);
            for (var tick = 0; tick < ticks; tick++)
            {
                Match.Run(1);
                foreach (var reported in Match.Simulation.Events)
                {
                    if (reported.Kind == SimEventKind.MeleeHit && reported.Entity == Target && hits.TryGetValue(reported.Other, out var count))
                    {
                        hits[reported.Other] = count + 1;
                    }
                }
            }

            return hits;
        }

        /// <summary>How far a unit stands from the footprint of the structure, in raw units.</summary>
        public long GapOf(EntityId unit) => Gap(Match, Target, Match.PositionOf(unit));
    }
}
