using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Data;
using Sim.Tests.Map;

namespace Sim.Tests.Setup;

public class MatchSetupTests
{
    private static readonly PlayerSetup[] TwoPlayers = [new(0), new(1)];

    /// <summary>The loader's sample map: 40 by 16, mirrored left to right, starts at (4, 7) and (34, 7).</summary>
    private static MapDef SampleMap => TestMatches.Data.Maps[0];

    private static Match SampleMatch(ulong seed = 1) => MatchSetup.Create(TestMatches.Data, SampleMap, seed, TwoPlayers);

    /// <summary>The tiles a player's units stand on, in the order of their IDs. A structure is not a unit.</summary>
    private static (int X, int Y)[] TilesOf(Match match, int player) =>
    [
        .. Enumerable.Range(0, match.Movers.Count)
            .Select(index => match.Bodies.Get(match.Movers.EntityAt(index)))
            .Where(body => body.Owner == player)
            .Select(body => (body.Position.X.FloorToInt(), body.Position.Y.FloorToInt())),
    ];

    [Fact]
    public void AMatchIsReadyForItsFirstTick()
    {
        var match = SampleMatch();

        Assert.Equal(0, match.World.Tick);
        Assert.Same(match.World, match.Simulation.World);
        Assert.Same(TestMatches.Data, match.Data);
        Assert.Same(SampleMap, match.Map);
        Assert.Equal(TwoPlayers, match.Players);
        Assert.Equal(40, match.Grid.Width);
        Assert.Equal(16, match.Grid.Height);
        Assert.Contains(match.World.Stores, store => ReferenceEquals(store, match.Bodies) && store.Name == "bodies");
        Assert.Contains(match.World.Stores, store => ReferenceEquals(store, match.Movers) && store.Name == "movers");

        match.Simulation.Run(20);
        Assert.Equal(20, match.World.Tick);
    }

    /// <summary>
    /// Design 5.2: every player starts with six workers. The sample data's Capitol is 2 by 2, so the
    /// row below it holds four and the row above takes the other two.
    /// </summary>
    [Fact]
    public void EachPlayerStartsWithTheirWorkersBesideTheirStart()
    {
        var match = SampleMatch();

        Assert.Equal(6, TestMatches.Data.Rules.Start.Workers);
        Assert.Equal(12, match.Movers.Count);
        Assert.Equal([(3, 9), (4, 9), (5, 9), (6, 9), (3, 6), (4, 6)], TilesOf(match, 0));
        Assert.Equal([(36, 9), (35, 9), (34, 9), (33, 9), (36, 6), (35, 6)], TilesOf(match, 1));

        for (var i = 0; i < match.Movers.Count; i++)
        {
            var entity = match.Movers.EntityAt(i);
            var body = match.Bodies.Get(entity);
            var mover = match.Movers.ValueAt(i);

            // Player 0's workers are the oldest entities, then player 1's.
            Assert.Equal(i + 1, entity.Value);
            Assert.Equal(i / 6, body.Owner);
            Assert.Equal(MapGrid.TileCenter(body.Position.X.FloorToInt(), body.Position.Y.FloorToInt()), body.Position);
            Assert.Equal(Mover.SpeedPerTick(3), mover.Speed);
            Assert.Equal(MoveState.Idle, mover.State);
        }
    }

    /// <summary>Design 4.1: 3 tiles per second, from the worker profile in the data.</summary>
    [Fact]
    public void TheStartingWorkerSpeedIsTheWorkerProfiles()
    {
        var files = SampleData.With(SampleData.ResourcesPath, "\"speed\": 3", "\"speed\": \"4.5\"");
        var faster = DataAssert.Valid(files);

        Assert.Equal(9830, SampleMatch().Movers.ValueAt(0).Speed.Raw);
        Assert.Equal(Mover.SpeedPerTick(Fraction.Parse("4.5")), MatchSetup.Create(faster, faster.Maps[0], 1, TwoPlayers).Movers.ValueAt(0).Speed);
    }

    /// <summary>Design 5.2: every player starts identically. On a mirrored map that means as mirror images.</summary>
    [Fact]
    public void TheTwoPlayersStartAsMirrorImages()
    {
        var match = SampleMatch();

        for (var i = 0; i < 6; i++)
        {
            var left = match.Bodies.ValueAt(i).Position;
            var right = match.Bodies.ValueAt(i + 6).Position;

            Assert.Equal(Fixed.FromInt(SampleMap.Width) - left.X, right.X);
            Assert.Equal(left.Y, right.Y);
        }
    }

    [Fact]
    public void ThePlayerListDecidesWhoGetsWhichStart()
    {
        var swapped = MatchSetup.Create(TestMatches.Data, SampleMap, 1, [new(1), new(0)]);

        Assert.Equal([(36, 9), (35, 9), (34, 9), (33, 9), (36, 6), (35, 6)], TilesOf(swapped, 0));
        Assert.Equal([(3, 9), (4, 9), (5, 9), (6, 9), (3, 6), (4, 6)], TilesOf(swapped, 1));
        Assert.Equal(0, swapped.Bodies.ValueAt(0).Owner);
    }

    [Fact]
    public void APlayerWithoutAStartGetsNothing()
    {
        var match = MatchSetup.Create(TestMatches.Data, SampleMap, 1, [PlayerSetup.WithoutStart, new(1), PlayerSetup.WithoutStart]);

        Assert.Equal(3, match.Players.Count);
        Assert.Equal(6, match.Movers.Count);
        Assert.Equal(1, match.Structures.Count);
        Assert.Empty(TilesOf(match, 0));
        Assert.Equal(6, TilesOf(match, 1).Length);
        Assert.Empty(TilesOf(match, 2));
        Assert.False(PlayerSetup.WithoutStart.HasStart);
        Assert.True(new PlayerSetup(0).HasStart);
    }

    /// <summary>Workers go to the nearest open tiles when the first choice is off the map or blocked.</summary>
    [Fact]
    public void WorkersAreNeverPlacedOnGroundTheyCannotStandOn()
    {
        var map = TestMaps.FromRows(
            [
                "........",
                "..F.....",
                ".#......",
                "........",
                "........",
                "........",
            ],
            MapSymmetry.MirrorX,
            [new MapStart(0, 0)]);

        var match = MatchSetup.Create(TestMatches.Data, map, 1, [new(0)]);

        // Around the 2 by 2 footprint at (0, 0): the row below loses (-1, 2), off the map, and
        // (1, 2), a cliff; the row above is off the map; the column to the right loses (2, 1), a
        // forest. The rest come from the row one tile further out.
        Assert.Equal([(0, 2), (2, 2), (2, 0), (0, 3), (1, 3), (2, 3)], TilesOf(match, 0));
    }

    [Fact]
    public void OnAMapMirroredTopToBottomTheFarPlayerIsTurnedAroundTheOtherWay()
    {
        var map = TestMaps.FromRows(
            [.. Enumerable.Repeat("............", 20)],
            MapSymmetry.MirrorY,
            [new MapStart(5, 3), new MapStart(5, 15)]);

        var match = MatchSetup.Create(TestMatches.Data, map, 1, TwoPlayers);

        Assert.Equal([(4, 5), (5, 5), (6, 5), (7, 5), (4, 2), (5, 2)], TilesOf(match, 0));
        Assert.Equal([(4, 14), (5, 14), (6, 14), (7, 14), (4, 17), (5, 17)], TilesOf(match, 1));
    }

    [Fact]
    public void AMapWithNoRoomForTheWorkersIsRejected()
    {
        var map = TestMaps.FromRows(
            [
                "..#",
                "..#",
                "###",
            ],
            MapSymmetry.MirrorX,
            [new MapStart(0, 0)]);

        Assert.Throws<InvalidOperationException>(() => MatchSetup.Create(TestMatches.Data, map, 1, [new(0)]));
    }

    [Fact]
    public void ThePlayerListIsChecked()
    {
        var data = TestMatches.Data;

        Assert.Throws<ArgumentException>(() => MatchSetup.Create(data, SampleMap, 1, []));
        Assert.Throws<ArgumentException>(() => MatchSetup.Create(data, SampleMap, 1, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, MatchSetup.MaxPlayers + 1)]));
        Assert.Throws<ArgumentException>(() => MatchSetup.Create(data, SampleMap, 1, [new(0), new(2)]));
        Assert.Throws<ArgumentException>(() => MatchSetup.Create(data, SampleMap, 1, [new(0), new(-2)]));
        Assert.Throws<ArgumentException>(() => MatchSetup.Create(data, SampleMap, 1, [new(1), new(1)]));
        Assert.Throws<ArgumentException>(() => MatchSetup.Create(data, SampleMap, 1, [new(0), null!]));
        Assert.Throws<ArgumentNullException>(() => MatchSetup.Create(null!, SampleMap, 1, TwoPlayers));
        Assert.Throws<ArgumentNullException>(() => MatchSetup.Create(data, null!, 1, TwoPlayers));
        Assert.Throws<ArgumentNullException>(() => MatchSetup.Create(data, SampleMap, 1, null!));

        Assert.Equal(MatchSetup.MaxPlayers, MatchSetup.Create(data, SampleMap, 1, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, MatchSetup.MaxPlayers)]).Players.Count);
    }

    [Fact]
    public void TheMatchKeepsItsOwnCopyOfThePlayerList()
    {
        var players = new List<PlayerSetup> { new(0), new(1) };
        var match = MatchSetup.Create(TestMatches.Data, SampleMap, 1, players);

        players.Clear();

        Assert.Equal(2, match.Players.Count);
    }

    [Fact]
    public void TheSameArgumentsGiveTheSameWorld()
    {
        Assert.Equal(SampleMatch().World.ComputeHash(), SampleMatch().World.ComputeHash());
        Assert.NotEqual(SampleMatch(seed: 1).World.ComputeHash(), SampleMatch(seed: 2).World.ComputeHash());
        Assert.NotEqual(
            SampleMatch().World.ComputeHash(),
            MatchSetup.Create(TestMatches.Data, SampleMap, 1, [new(1), new(0)]).World.ComputeHash());
    }

    [Fact]
    public void SpawningAUnitChecksWhereAndForWhom()
    {
        var match = TestMatches.Create(TestMaps.FromRows("..#", "..."));
        var position = MapGrid.TileCenter(1, 1);

        var unit = match.SpawnUnit(1, position, Fixed.Half);

        Assert.Equal(new EntityId(1), unit);
        Assert.Equal(1, match.Bodies.Get(unit).Owner);
        Assert.Equal(position, match.Bodies.Get(unit).Position);
        Assert.Equal(Fixed.Half, match.Movers.Get(unit).Speed);
        Assert.Throws<ArgumentException>(() => match.SpawnUnit(0, MapGrid.TileCenter(2, 0), Fixed.Half));
        Assert.Throws<ArgumentException>(() => match.SpawnUnit(0, MapGrid.TileCenter(3, 0), Fixed.Half));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.SpawnUnit(2, position, Fixed.Half));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.SpawnUnit(-1, position, Fixed.Half));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.SpawnUnit(0, position, -Fixed.Epsilon));
        Assert.Equal(1, match.Bodies.Count);
    }

    /// <summary>
    /// The whole path a session uses: a match from the setup, move orders through the command queue,
    /// ticks, and the state hash. The same script twice gives the same hash on every tick.
    /// </summary>
    [Fact]
    public void MoveOrdersRunThroughTheMatch()
    {
        ulong[] Play(out Match match)
        {
            match = SampleMatch(seed: 20261110);
            EntityId[] workersOfPlayer0 = [new(1), new(2), new(3)];
            EntityId[] workersOfPlayer1 = [new(7), new(8), new(9)];
            var queue = match.Simulation.Commands;
            queue.Enqueue(0, Move(0, 10, 12, workersOfPlayer0));
            queue.Enqueue(0, Move(1, 29, 12, workersOfPlayer1));

            // Player 1 may not move player 0's workers. This one is dropped.
            queue.Enqueue(5, Move(1, 20, 3, workersOfPlayer0));
            queue.Enqueue(40, Move(0, 3, 3, [new(1)]));

            var hashes = new ulong[160];
            for (var tick = 0; tick < hashes.Length; tick++)
            {
                match.Simulation.Tick();
                hashes[tick] = match.World.ComputeHash();
            }

            return hashes;
        }

        var hashes = Play(out var match);

        Assert.Equal(hashes, Play(out _));
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(MapGrid.TileCenter(3, 3), match.PositionOf(new EntityId(1)));
        Assert.Equal(MapGrid.TileCenter(10, 12), match.PositionOf(new EntityId(2)));
        Assert.Equal(MapGrid.TileCenter(29, 12), match.PositionOf(new EntityId(8)));
        Assert.Equal(MapGrid.TileCenter(6, 9), match.PositionOf(new EntityId(4)));

        static Command Move(int player, int tileX, int tileY, EntityId[] entities)
        {
            var target = MapGrid.TileCenter(tileX, tileY);
            return new Command(CommandType.Move, player, entities, target.X, target.Y);
        }
    }
}
