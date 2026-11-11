using System.Collections.Immutable;
using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;

namespace Net.Tests;

public class MessageCodecTests
{
    private static readonly Command Plain = new(ToyGame.Spawn, 1, contentId: 7);

    private static readonly Command Full = new(
        ToyGame.Cull,
        3,
        [new EntityId(4), new EntityId(9)],
        Fixed.FromInt(12),
        Fixed.FromInt(-5),
        new EntityId(77),
        contentId: 1234);

    public static TheoryData<string> Kinds() =>
    [
        "JoinRequest", "JoinAccept", "ReadyNotice", "Ping", "Pong", "MatchStart", "CommandRequest",
        "TickBundle", "empty TickBundle", "HashReport", "PeerLeft", "SessionEnd", "MatchNotice", "LeaveNotice",
    ];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryMessageSurvivesTheBytes(string kind)
    {
        var message = Example(kind);

        var bytes = MessageCodec.Encode(message);
        var read = MessageCodec.Read(bytes);

        Assert.Equal(message.GetType(), read.GetType());
        Assert.Equal(bytes, MessageCodec.Encode(read));
        if (message is not (CommandRequest or TickBundle or MatchNotice))
        {
            // Commands and match descriptions compare by their bytes, the other messages by their values.
            Assert.Equal(message, read);
        }
    }

    [Fact]
    public void ThereIsAnExampleOfEveryMessage()
    {
        var kinds = typeof(NetMessage).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(NetMessage))).Select(type => type.Name);
        var examples = Kinds().Select(row => Example(row.Data).GetType().Name).Distinct();

        Assert.Equal(kinds.Order(), examples.Order());
    }

    [Fact]
    public void ABundleKeepsItsTickAndItsCommandsInOrder()
    {
        var bundle = new TickBundle(31, [Plain, Full, Plain]);

        var read = Assert.IsType<TickBundle>(MessageCodec.Read(MessageCodec.Encode(bundle)));

        Assert.Equal(31, read.Tick);
        Assert.Equal(3, read.Commands.Length);
        Assert.Equal(Bytes(Plain), Bytes(read.Commands[0]));
        Assert.Equal(Bytes(Full), Bytes(read.Commands[1]));
        Assert.Equal(Bytes(Plain), Bytes(read.Commands[2]));
        Assert.Equal([new EntityId(4), new EntityId(9)], read.Commands[1].Entities.ToArray());
    }

    /// <summary>
    /// The layout as bytes, so that a change to it cannot go unnoticed: it has to change this test
    /// and <see cref="ProtocolInfo.Version"/> with it.
    /// </summary>
    [Fact]
    public void TheLayoutIsTheOneWrittenDown()
    {
        Assert.Equal("01 01000000 01000000", Hex(new JoinRequest(1, 1), 1, 4, 4));
        Assert.Equal("02 03000000 08000000", Hex(new JoinAccept(3, 8), 1, 4, 4));
        Assert.Equal("03", Hex(new ReadyNotice(), 1));
        Assert.Equal("04 02010000", Hex(new Ping(258), 1, 4));
        Assert.Equal("05 FFFFFFFF", Hex(new Pong(-1), 1, 4));
        Assert.Equal("06 0A000000 02000000 14000000 20A1070000000000", Hex(new MatchStart(10, 2, 20, TimeSpan.FromMilliseconds(50)), 1, 4, 4, 4, 8));
        Assert.Equal("09 14000000 EFCDAB8967452301", Hex(new HashReport(20, 0x0123456789ABCDEF), 1, 4, 8));
        Assert.Equal("0A 02000000 04", Hex(new PeerLeft(2, SessionEndReason.FellBehind), 1, 4, 1));
        Assert.Equal("0B 02 2C010000 28010000", Hex(new SessionEnd(SessionEndReason.Desync, 300, 296), 1, 1, 4, 4));
        Assert.Equal("0B 01 FFFFFFFF FFFFFFFF", Hex(new SessionEnd(SessionEndReason.HostEnded, -1, -1), 1, 1, 4, 4));
        Assert.Equal("0D 07", Hex(new LeaveNotice(SessionEndReason.Left), 1, 1));
        Assert.Equal("0D 08", Hex(new LeaveNotice(SessionEndReason.SetupMismatch), 1, 1));

        // Around commands, which are laid out by the simulation's one encoding of them.
        Assert.Equal([7, .. Bytes(Full)], MessageCodec.Encode(new CommandRequest(Full)));
        Assert.Equal([8, 5, 0, 0, 0, 2, 0, 0, 0, .. Bytes(Plain), .. Bytes(Full)], MessageCodec.Encode(new TickBundle(5, [Plain, Full])));

        // Around the match, which is laid out by the simulation's one encoding of it.
        var description = new ByteWriter();
        TestData.Description().Write(description);
        Assert.Equal([12, .. description.ToArray()], MessageCodec.Encode(new MatchNotice(TestData.Description())));
    }

    [Fact]
    public void AMatchNoticeCarriesTheDescriptionUnchanged()
    {
        var description = new MatchDescription("dev-1234567", new string('a', 64), "ridge", ulong.MaxValue, [new PlayerSetup(1), PlayerSetup.WithoutStart, new PlayerSetup(0)]);

        var read = Assert.IsType<MatchNotice>(MessageCodec.Read(MessageCodec.Encode(new MatchNotice(description)))).Description;

        Assert.Equal("dev-1234567", read.GameVersion);
        Assert.Equal(new string('a', 64), read.ContentHash);
        Assert.Equal("ridge", read.MapId);
        Assert.Equal(ulong.MaxValue, read.Seed);
        Assert.Equal([new PlayerSetup(1), PlayerSetup.WithoutStart, new PlayerSetup(0)], read.Players);
    }

    [Fact]
    public void TheSizeOfABundleIsItsOverheadAndItsCommands()
    {
        Assert.Equal(MessageCodec.BundleOverhead, MessageCodec.Encode(new TickBundle(0, [])).Length);
        Assert.Equal(
            MessageCodec.BundleOverhead + CommandEncoding.SizeOf(Plain) + CommandEncoding.SizeOf(Full),
            MessageCodec.Encode(new TickBundle(0, [Plain, Full])).Length);
        Assert.Equal(MessageCodec.CommandRequestOverhead + CommandEncoding.SizeOf(Full), MessageCodec.Encode(new CommandRequest(Full)).Length);
    }

    /// <summary>The numbers a peer sees must not change when a reason is added.</summary>
    [Fact]
    public void TheReasonsKeepTheirNumbers()
    {
        Assert.Equal(
            [
                (SessionEndReason.None, 0), (SessionEndReason.HostEnded, 1), (SessionEndReason.Desync, 2),
                (SessionEndReason.ConnectionLost, 3), (SessionEndReason.FellBehind, 4), (SessionEndReason.ProtocolError, 5),
                (SessionEndReason.VersionMismatch, 6), (SessionEndReason.Left, 7), (SessionEndReason.SetupMismatch, 8),
            ],
            Enum.GetValues<SessionEndReason>().Select(reason => (reason, (int)reason)));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AMessageCutShortOrWithBytesLeftOverIsRejected(string kind)
    {
        var bytes = MessageCodec.Encode(Example(kind));

        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.Throws<InvalidDataException>(() => MessageCodec.Read(bytes.AsMemory(0, length)));
            Assert.False(MessageCodec.TryRead(bytes.AsMemory(0, length), out _));
        }

        Assert.Throws<InvalidDataException>(() => MessageCodec.Read((byte[])[.. bytes, 0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(255)]
    public void AKindThatDoesNotExistIsRejected(byte kind)
    {
        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(new byte[] { kind }));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(new byte[] { kind, 0, 0, 0, 0 }));
    }

    [Fact]
    public void AReasonThatDoesNotExistIsRejectedBothWays()
    {
        var left = MessageCodec.Encode(new PeerLeft(1, SessionEndReason.Left));
        left[^1] = 9;
        var end = MessageCodec.Encode(new SessionEnd(SessionEndReason.Left, -1, -1));
        end[1] = 200;

        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(left));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(end));
        Assert.Throws<ArgumentException>(() => MessageCodec.Encode(new PeerLeft(1, (SessionEndReason)9)));
        Assert.Throws<ArgumentException>(() => MessageCodec.Encode(new SessionEnd((SessionEndReason)(-1), -1, -1)));
    }

    /// <summary>
    /// A bundle says how many commands follow. A count that the rest of the message cannot hold
    /// is refused before anything is allocated for it.
    /// </summary>
    [Fact]
    public void ABundleThatClaimsMoreCommandsThanItHoldsIsRejected()
    {
        var writer = new ByteWriter();
        writer.WriteByte(8);
        writer.WriteInt32(0);
        writer.WriteCount(int.MaxValue);
        CommandEncoding.Write(writer, Plain);

        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(writer.ToArray()));
    }

    [Fact]
    public void AMessageOrACommandOverTheSizeLimitIsRejected()
    {
        var mostEntities = (ProtocolInfo.MaxCommandSize - CommandEncoding.MinimumSize) / 4;
        var largest = new Command(ToyGame.Spawn, 0, new EntityId[mostEntities]);
        var tooLarge = new Command(ToyGame.Spawn, 0, new EntityId[mostEntities + 1]);

        Assert.IsType<CommandRequest>(MessageCodec.Read(MessageCodec.Encode(new CommandRequest(largest))));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(MessageCodec.Encode(new CommandRequest(tooLarge))));

        // A bundle holds several of the largest commands, and one more than fit makes it no message.
        var perBundle = (ProtocolInfo.MaxMessageSize - MessageCodec.BundleOverhead) / CommandEncoding.SizeOf(largest);
        Assert.True(perBundle >= 7);
        Assert.IsType<TickBundle>(MessageCodec.Read(MessageCodec.Encode(new TickBundle(0, [.. Enumerable.Repeat(largest, perBundle)]))));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(MessageCodec.Encode(new TickBundle(0, [.. Enumerable.Repeat(largest, perBundle + 1)]))));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(new byte[ProtocolInfo.MaxMessageSize + 1]));
    }

    /// <summary>
    /// A host reads no further than the version of a join request from another version, whose
    /// other bytes may be anything.
    /// </summary>
    [Fact]
    public void AJoinRequestOfAnotherProtocolVersionIsReadByItsVersionAlone()
    {
        var writer = new ByteWriter();
        writer.WriteByte(1);
        writer.WriteInt32(ProtocolInfo.Version + 1);
        var bare = writer.ToArray();
        writer.WriteString("whatever the next version adds");

        Assert.Equal(new JoinRequest(ProtocolInfo.Version + 1, 0), MessageCodec.Read(bare));
        Assert.Equal(new JoinRequest(ProtocolInfo.Version + 1, 0), MessageCodec.Read(writer.ToArray()));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Read(MessageCodec.Encode(new JoinRequest(ProtocolInfo.Version, 1)).AsMemory(0, 5)));
    }

    /// <summary>
    /// Whatever bytes arrive, reading them either gives a message or throws the one exception the
    /// sessions catch. Nothing a peer sends can bring the process down.
    /// </summary>
    [Fact]
    public void RandomBytesAreAMessageOrRejectedAndNothingElse()
    {
        var random = new DeterministicRandom(42);
        var examples = Kinds().Select(row => MessageCodec.Encode(Example(row.Data))).ToArray();
        var accepted = 0;

        for (var round = 0; round < 20_000; round++)
        {
            byte[] bytes;
            if (round % 2 == 0)
            {
                // Pure noise behind a kind byte that exists.
                bytes = new byte[random.NextInt(80)];
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)random.NextInt(256);
                }

                if (bytes.Length > 0)
                {
                    bytes[0] = (byte)(1 + random.NextInt(13));
                }
            }
            else
            {
                // A real message with one byte changed.
                bytes = (byte[])examples[random.NextInt(examples.Length)].Clone();
                bytes[random.NextInt(bytes.Length)] = (byte)random.NextInt(256);
            }

            if (MessageCodec.TryRead(bytes, out var message))
            {
                accepted++;

                // Every message has one encoding, but for a join request of another version, which is cut off after the version.
                if (message is not JoinRequest join || join.ProtocolVersion == ProtocolInfo.Version)
                {
                    Assert.Equal(bytes, MessageCodec.Encode(message));
                }
            }
        }

        Assert.InRange(accepted, 1000, 19_000);
    }

    [Fact]
    public void WritingAppendsToWhatTheWriterHolds()
    {
        var writer = new ByteWriter();
        MessageCodec.Write(writer, new Ping(1));
        MessageCodec.Write(writer, new ReadyNotice());

        Assert.Equal([4, 1, 0, 0, 0, 3], writer.ToArray());
    }

    private static NetMessage Example(string kind) => kind switch
    {
        "JoinRequest" => new JoinRequest(ProtocolInfo.Version, CommandEncoding.Version),
        "JoinAccept" => new JoinAccept(3, 8),
        "ReadyNotice" => new ReadyNotice(),
        "Ping" => new Ping(int.MinValue),
        "Pong" => new Pong(int.MaxValue),
        "MatchStart" => new MatchStart(600, 3, 20, TimeSpan.FromMilliseconds(50)),
        "CommandRequest" => new CommandRequest(Full),
        "TickBundle" => new TickBundle(12345, [Plain, Full, Plain]),
        "empty TickBundle" => new TickBundle(0, []),
        "HashReport" => new HashReport(40, ulong.MaxValue),
        "PeerLeft" => new PeerLeft(2, SessionEndReason.ConnectionLost),
        "SessionEnd" => new SessionEnd(SessionEndReason.Desync, 500, 480),
        "MatchNotice" => new MatchNotice(TestData.Description()),
        "LeaveNotice" => new LeaveNotice(SessionEndReason.SetupMismatch),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static byte[] Bytes(Command command)
    {
        var writer = new ByteWriter();
        CommandEncoding.Write(writer, command);
        return writer.ToArray();
    }

    /// <summary>The bytes of a message as hexadecimal, split into values of the given sizes.</summary>
    private static string Hex(NetMessage message, params int[] sizes)
    {
        var bytes = MessageCodec.Encode(message);
        Assert.Equal(bytes.Length, sizes.Sum());
        var parts = new List<string>();
        var at = 0;
        foreach (var size in sizes)
        {
            parts.Add(Convert.ToHexString(bytes, at, size));
            at += size;
        }

        return string.Join(' ', parts);
    }
}
