using Sim.Data;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Setup;

namespace Sim.Tests.Serialization;

public class MatchDescriptionTests
{
    private const string Hash = "00112233445566778899aabbccddeeff0123456789abcdef0123456789abcdef";

    private static GameData Data => TestMatches.Data;

    private static string MapId => Data.Maps[0].Id;

    private static MatchDescription Sample() =>
        new("0.3.1", Hash, "ridge", 0x0102030405060708, [new PlayerSetup(1), PlayerSetup.WithoutStart, new PlayerSetup(0)]);

    private static byte[] BytesOf(MatchDescription description)
    {
        var writer = new ByteWriter();
        description.Write(writer);
        return writer.ToArray();
    }

    /// <summary>The layout, byte for byte. A change to it is a new <see cref="MatchDescription.Version"/>.</summary>
    [Fact]
    public void TheBytesOfADescriptionArePinned()
    {
        const string Expected =
            "0100" // version 1
            + "05000000" + "302E332E31" // "0.3.1"
            + "00112233445566778899AABBCCDDEEFF0123456789ABCDEF0123456789ABCDEF" // content hash
            + "05000000" + "7269646765" // "ridge"
            + "0807060504030201" // seed
            + "03000000" // three players
            + "01000000" + "FFFFFFFF" + "00000000"; // starts 1, none, 0

        Assert.Equal(Expected, Convert.ToHexString(BytesOf(Sample())));
    }

    [Fact]
    public void ADescriptionReadBackIsTheOneWritten()
    {
        var writer = new ByteWriter();
        Sample().Write(writer);
        writer.WriteByte(0xEE);
        var reader = new ByteReader(writer.ToArray());

        var read = MatchDescription.Read(reader);

        Assert.Equal("0.3.1", read.GameVersion);
        Assert.Equal(Hash, read.ContentHash);
        Assert.Equal("ridge", read.MapId);
        Assert.Equal(0x0102030405060708UL, read.Seed);
        Assert.Equal([new PlayerSetup(1), PlayerSetup.WithoutStart, new PlayerSetup(0)], read.Players);
        Assert.Equal(1, reader.Remaining);
        Assert.Equal(BytesOf(Sample()), BytesOf(read));
    }

    [Fact]
    public void TextOutsideAsciiAndTheLargestValuesSurvive()
    {
        var players = Enumerable.Range(0, MatchSetup.MaxPlayers).Select(start => new PlayerSetup(int.MaxValue - start)).ToArray();
        var description = new MatchDescription("v\u00e9rsion \U0001F600", Hash, new string('m', MatchDescription.MaxTextLength), ulong.MaxValue, players);

        var read = MatchDescription.Read(new ByteReader(BytesOf(description)));

        Assert.Equal("v\u00e9rsion \U0001F600", read.GameVersion);
        Assert.Equal(description.MapId, read.MapId);
        Assert.Equal(ulong.MaxValue, read.Seed);
        Assert.Equal(players, read.Players);
    }

    [Fact]
    public void TheGameVersionMayBeEmpty()
    {
        var read = MatchDescription.Read(new ByteReader(BytesOf(new MatchDescription("", Hash, "ridge", 1, [new PlayerSetup(0)]))));

        Assert.Equal("", read.GameVersion);
    }

    [Fact]
    public void TheListOfPlayersIsCopied()
    {
        var players = new List<PlayerSetup> { new(0), new(1) };
        var description = new MatchDescription("", Hash, "ridge", 1, players);

        players[0] = new PlayerSetup(5);
        players.Add(new PlayerSetup(6));

        Assert.Equal([new PlayerSetup(0), new PlayerSetup(1)], description.Players);
    }

    public static TheoryData<string, string, string, int[]> Refused => new()
    {
        { new string('v', MatchDescription.MaxTextLength + 1), Hash, "ridge", [0] },
        { "half a pair \ud83d", Hash, "ridge", [0] },
        { "", Hash[..^1], "ridge", [0] },
        { "", Hash + "0", "ridge", [0] },
        { "", Hash[..^2], "ridge", [0] },
        { "", Hash + "00", "ridge", [0] },
        { "", "", "ridge", [0] },
        { "", Hash.ToUpperInvariant(), "ridge", [0] },
        { "", "sha256:" + Hash[7..], "ridge", [0] },
        { "", Hash, "", [0] },
        { "", Hash, new string('m', MatchDescription.MaxTextLength + 1), [0] },
        { "", Hash, "\udc00", [0] },
        { "", Hash, "ridge", [] },
        { "", Hash, "ridge", [0, 1, 2, 3, 4, 5, 6, 7, 8] },
        { "", Hash, "ridge", [0, -2] },
        { "", Hash, "ridge", [0, 1, 0] },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void ADescriptionThatCannotBeAMatchIsRefused(string gameVersion, string contentHash, string mapId, int[] starts)
    {
        var players = starts.Select(start => new PlayerSetup(start)).ToArray();

        Assert.Throws<ArgumentException>(() => new MatchDescription(gameVersion, contentHash, mapId, 1, players));
    }

    [Fact]
    public void SeveralPlayersMayHaveNoStart()
    {
        var description = new MatchDescription("", Hash, "ridge", 1, [PlayerSetup.WithoutStart, new PlayerSetup(0), PlayerSetup.WithoutStart]);

        Assert.Equal(3, description.Players.Count);
    }

    [Fact]
    public void ArgumentsAreNeeded()
    {
        PlayerSetup[] players = [new(0)];

        Assert.Throws<ArgumentNullException>(() => new MatchDescription(null!, Hash, "ridge", 1, players));
        Assert.Throws<ArgumentNullException>(() => new MatchDescription("", null!, "ridge", 1, players));
        Assert.Throws<ArgumentNullException>(() => new MatchDescription("", Hash, null!, 1, players));
        Assert.Throws<ArgumentNullException>(() => new MatchDescription("", Hash, "ridge", 1, null!));
        Assert.Throws<ArgumentException>(() => new MatchDescription("", Hash, "ridge", 1, [new PlayerSetup(0), null!]));
        Assert.Throws<ArgumentNullException>(() => MatchDescription.Read(null!));
        Assert.Throws<ArgumentNullException>(() => Sample().Write(null!));
        Assert.Throws<ArgumentNullException>(() => Sample().FindProblem(null!));
    }

    /// <summary>Bytes that are a description in form and name a match that cannot be: each is refused when read.</summary>
    [Fact]
    public void BytesThatDescribeNoMatchAreRefused()
    {
        var good = BytesOf(Sample());
        const int VersionAt = 0;
        const int GameVersionAt = 2;
        const int MapIdLengthAt = 2 + 4 + 5 + 32;
        const int PlayerCountAt = MapIdLengthAt + 4 + 5 + 8;

        Refuses(good, VersionAt, [2, 0], "layout version 2");
        Refuses(good, VersionAt, [0, 0], "layout version 0");
        Refuses(good, GameVersionAt + 4, [0xFF], "UTF-8");
        Refuses(good, GameVersionAt, [0xFF, 0xFF, 0xFF, 0xFF], "count");
        Refuses(good, PlayerCountAt, [0xFF, 0xFF, 0xFF, 0x7F], "count");
        Refuses(good, PlayerCountAt + 4, [0xFE, 0xFF, 0xFF, 0xFF], "start -2");
        Refuses(good, PlayerCountAt + 8, [1, 0, 0, 0], "share start 1");

        Refuses(Written(mapId: "", playerCount: 1), 0, [1], "map ID has 0 characters");
        Refuses(Written(mapId: new string('m', MatchDescription.MaxTextLength + 1), playerCount: 1), 0, [1], "map ID has 129 characters");
        Refuses(Written(mapId: "ridge", playerCount: 0), 0, [1], "not 0");
        Refuses(Written(mapId: "ridge", playerCount: MatchSetup.MaxPlayers + 1), 0, [1], "not 9");

        static byte[] Written(string mapId, int playerCount)
        {
            var writer = new ByteWriter();
            writer.WriteUInt16(MatchDescription.Version);
            writer.WriteString("");
            writer.WriteBytes(Convert.FromHexString(Hash));
            writer.WriteString(mapId);
            writer.WriteUInt64(1);
            writer.WriteCount(playerCount);
            for (var player = 0; player < playerCount; player++)
            {
                writer.WriteInt32(player);
            }

            return writer.ToArray();
        }

        static void Refuses(byte[] good, int at, byte[] replacement, string expected)
        {
            var bytes = (byte[])good.Clone();
            replacement.CopyTo(bytes, at);
            var exception = Assert.Throws<InvalidDataException>(() => MatchDescription.Read(new ByteReader(bytes)));
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ADescriptionCutShortAnywhereIsRefused()
    {
        var bytes = BytesOf(Sample());

        for (var length = 0; length < bytes.Length; length++)
        {
            var cut = bytes.AsMemory(0, length);
            Assert.Throws<InvalidDataException>(() => MatchDescription.Read(new ByteReader(cut)));
        }
    }

    [Fact]
    public void AMatchThatTheDataCanBuildHasNoProblem()
    {
        var description = new MatchDescription("any", Data.ContentHash, MapId, 5, [new PlayerSetup(1), new PlayerSetup(0), PlayerSetup.WithoutStart]);

        Assert.Null(description.FindProblem(Data));
    }

    [Fact]
    public void OtherDataIsAProblem()
    {
        var description = new MatchDescription("", Hash, MapId, 5, [new PlayerSetup(0)]);

        var problem = description.FindProblem(Data);

        Assert.NotNull(problem);
        Assert.Contains(Hash, problem, StringComparison.Ordinal);
        Assert.Contains(Data.ContentHash, problem, StringComparison.Ordinal);
        Assert.Null(description.WithContentHash(Data.ContentHash).FindProblem(Data));
    }

    [Fact]
    public void AMapTheDataDoesNotHaveIsAProblem()
    {
        var description = new MatchDescription("", Data.ContentHash, MapId.ToUpperInvariant(), 5, [new PlayerSetup(0)]);

        Assert.Contains($"no map '{MapId.ToUpperInvariant()}'", description.FindProblem(Data), StringComparison.Ordinal);
    }

    [Fact]
    public void AStartTheMapDoesNotHaveIsAProblem()
    {
        var starts = Data.Maps[0].Starts.Count;
        var description = new MatchDescription("", Data.ContentHash, MapId, 5, [new PlayerSetup(0), new PlayerSetup(starts)]);

        Assert.Contains($"Player 1 has start {starts}", description.FindProblem(Data), StringComparison.Ordinal);
    }

    [Fact]
    public void AMatchThatCannotBeBuiltIsInvalidData()
    {
        var otherData = new MatchDescription("", Hash, MapId, 5, [new PlayerSetup(0)]);
        var noMap = new MatchDescription("", Data.ContentHash, "nowhere", 5, [new PlayerSetup(0)]);

        Assert.Contains("other data", Assert.Throws<InvalidDataException>(() => otherData.CreateMatch(Data)).Message, StringComparison.Ordinal);
        Assert.Contains("nowhere", Assert.Throws<InvalidDataException>(() => noMap.CreateMatch(Data)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => otherData.CreateMatch(null!));
    }

    /// <summary>The description builds the match that the same values build when given to the match setup directly.</summary>
    [Fact]
    public void TheMatchBuiltFromADescriptionIsTheOneTheSetupBuilds()
    {
        PlayerSetup[] players = [new(1), PlayerSetup.WithoutStart, new(0)];
        var description = MatchDescription.Read(new ByteReader(BytesOf(new MatchDescription("x", Data.ContentHash, MapId, 99, players))));

        var built = description.CreateMatch(Data);
        var direct = MatchSetup.Create(Data, Data.Maps[0], 99, players);

        Assert.Equal(direct.World.ComputeHash(), built.World.ComputeHash());
        Assert.Equal(direct.Bodies.Count, built.Bodies.Count);
        Assert.True(built.Bodies.Count > 0);
        Assert.Equal(3, built.Players.Count);
        Assert.Same(Data.Maps[0], built.Map);
        Assert.NotEqual(built.World.ComputeHash(), new MatchDescription("x", Data.ContentHash, MapId, 100, players).CreateMatch(Data).World.ComputeHash());
    }

    [Fact]
    public void ChangingTheContentHashKeepsTheRest()
    {
        var other = new string('a', 64);

        var changed = Sample().WithContentHash(other);

        Assert.Equal(other, changed.ContentHash);
        Assert.Equal(Sample().GameVersion, changed.GameVersion);
        Assert.Equal(Sample().MapId, changed.MapId);
        Assert.Equal(Sample().Seed, changed.Seed);
        Assert.Equal(Sample().Players, changed.Players);
        Assert.Throws<ArgumentException>(() => Sample().WithContentHash("abc"));
    }
}
