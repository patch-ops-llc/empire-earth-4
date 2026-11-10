using System.Buffers.Binary;
using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Tests.Core;

namespace Sim.Tests.Serialization;

public class CommandEncodingTests
{
    /// <summary>
    /// The layout of an encoded command. Replays and the network protocol store these bytes, so a
    /// change here must come with a new <see cref="CommandEncoding.Version"/>.
    /// </summary>
    [Fact]
    public void ACommandHasItsRecordedBytes()
    {
        EntityId[] entities = [new(7), new(300), new(65536)];
        var command = new Command(
            (CommandType)0x0102,
            player: 1,
            entities,
            targetX: Fixed.FromInt(10) + Fixed.Half,
            targetY: Fixed.FromInt(-3),
            targetEntity: new EntityId(42),
            contentId: 0x0A0B0C0D);

        const string expected =
            "0201" + // type
            "01000000" + // player
            "00800A00" + // target x, 10.5
            "0000FDFF" + // target y, -3
            "2A000000" + // target entity
            "0D0C0B0A" + // content ID
            "03000000" + // number of entities
            "07000000" + "2C010000" + "00000100";
        Assert.Equal(expected, Convert.ToHexString(Encode(command)));
        Assert.Equal(1, CommandEncoding.Version);
    }

    [Fact]
    public void TheDefaultCommandIsTwentySixZeroBytes()
    {
        var bytes = Encode(default);

        Assert.Equal(new byte[26], bytes);
        Assert.Equal(26, CommandEncoding.MinimumSize);
        AssertSame(default, Decode(bytes));
    }

    public static TheoryData<int> EntityCounts => [0, 1, 2, 127, 128, 129, 1000];

    [Theory]
    [MemberData(nameof(EntityCounts))]
    public void ACommandReadsBackWithAnyNumberOfEntities(int count)
    {
        var entities = new EntityId[count];
        for (var i = 0; i < count; i++)
        {
            entities[i] = new EntityId((i * 37) + 1);
        }

        var command = new Command((CommandType)3, 1, entities, Fixed.FromInt(4), Fixed.FromInt(5), new EntityId(6), 7);
        var bytes = Encode(command);

        Assert.Equal(26 + (4 * count), bytes.Length);
        Assert.Equal(bytes.Length, CommandEncoding.SizeOf(command));
        AssertSame(command, Decode(bytes));
    }

    [Fact]
    public void EveryValueAFieldCanHoldReadsBack()
    {
        int[] numbers = [0, 1, -1, int.MaxValue, int.MinValue];
        foreach (var type in (int[])[0, 1, 99, ushort.MaxValue])
        {
            foreach (var number in numbers)
            {
                EntityId[] entities = [new(number), EntityId.None, new(number)];
                var command = new Command(
                    (CommandType)type,
                    number,
                    entities,
                    Fixed.FromRaw(number),
                    Fixed.FromRaw(unchecked(-number)),
                    new EntityId(number),
                    number);

                AssertSame(command, Decode(Encode(command)));
            }
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void ATypeNumberOutsideTheListsRangeIsNotWritten(int type)
    {
        var writer = new ByteWriter();
        var command = new Command((CommandType)type, 0);

        Assert.Throws<ArgumentException>(() => CommandEncoding.Write(writer, command));
        Assert.Equal(0, writer.Length);
    }

    [Fact]
    public void CommandsWrittenOneAfterAnotherReadBackInOrder()
    {
        var script = ToyMatch.Script(DeterminismTests.Ticks);
        var writer = new ByteWriter();
        var expectedLength = 0;
        foreach (var (_, command) in script)
        {
            CommandEncoding.Write(writer, command);
            expectedLength += CommandEncoding.SizeOf(command);
        }

        Assert.Equal(expectedLength, writer.Length);

        var reader = new ByteReader(writer.WrittenMemory);
        foreach (var (_, command) in script)
        {
            AssertSame(command, CommandEncoding.Read(reader));
        }

        reader.ExpectEnd();
    }

    /// <summary>
    /// The toy match played from commands that went through bytes ends on the hash that
    /// DeterminismTests records: nothing a handler reads is lost or changed by the encoding.
    /// </summary>
    [Fact]
    public void AMatchPlayedFromEncodedCommandsReachesTheRecordedHash()
    {
        var script = ToyMatch.Script(DeterminismTests.Ticks);
        var decoded = script.Select(entry => (entry.Tick, Decode(Encode(entry.Command)))).ToList();

        var hashes = new ToyMatch(DeterminismTests.Seed).Play(DeterminismTests.Ticks, decoded);

        Assert.Equal(DeterminismTests.ExpectedFinalHash, hashes[^1]);
    }

    [Fact]
    public void ACommandCutShortAnywhereIsRejected()
    {
        EntityId[] entities = [new(1), new(2), new(3)];
        var bytes = Encode(new Command((CommandType)1, 0, entities, Fixed.One, Fixed.One, new EntityId(9), 5));

        for (var length = 0; length < bytes.Length; length++)
        {
            var reader = new ByteReader(bytes.AsMemory(0, length));
            Assert.Throws<InvalidDataException>(() => CommandEncoding.Read(reader));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void AnEntityCountThatIsNegativeOrLargerThanTheBytesLeftIsRejected(int count)
    {
        var writer = new ByteWriter();
        CommandEncoding.Write(writer, new Command((CommandType)1, 0, [new EntityId(5)]));
        var bytes = writer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), count);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => CommandEncoding.Read(new ByteReader(bytes)));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 64 * 1024);
    }

    /// <summary>
    /// Whatever bytes arrive, reading them either fails with the one documented exception or gives
    /// a command that writes back as exactly the bytes that were read.
    /// </summary>
    [Fact]
    public void ArbitraryBytesAreRejectedOrWriteBackAsTheyWere()
    {
        var random = new DeterministicRandom(4);
        var accepted = 0;
        var rejected = 0;
        for (var round = 0; round < 20_000; round++)
        {
            var bytes = new byte[random.NextInt(0, 64)];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)random.NextInt(256);
            }

            if (bytes.Length >= 26 && round % 2 == 0)
            {
                // Half the time, announce a number of entities that could be there.
                bytes[22] = (byte)random.NextInt(0, 12);
                bytes[23] = bytes[24] = bytes[25] = 0;
            }

            var reader = new ByteReader(bytes);
            Command command;
            try
            {
                command = CommandEncoding.Read(reader);
            }
            catch (InvalidDataException)
            {
                rejected++;
                continue;
            }

            accepted++;
            Assert.Equal(reader.Position, CommandEncoding.SizeOf(command));
            Assert.Equal(bytes.AsSpan(0, reader.Position).ToArray(), Encode(command));
        }

        Assert.InRange(accepted, 1000, int.MaxValue);
        Assert.InRange(rejected, 1000, int.MaxValue);
    }

    [Fact]
    public void WritingAndReadingNeedAWriterAndAReader()
    {
        Assert.Throws<ArgumentNullException>(() => CommandEncoding.Write(null!, default));
        Assert.Throws<ArgumentNullException>(() => CommandEncoding.Read(null!));
    }

    private static byte[] Encode(in Command command)
    {
        var writer = new ByteWriter();
        CommandEncoding.Write(writer, command);
        return writer.ToArray();
    }

    private static Command Decode(byte[] bytes)
    {
        var reader = new ByteReader(bytes);
        var command = CommandEncoding.Read(reader);
        reader.ExpectEnd();
        return command;
    }

    private static void AssertSame(in Command expected, in Command actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Player, actual.Player);
        Assert.Equal(expected.TargetX, actual.TargetX);
        Assert.Equal(expected.TargetY, actual.TargetY);
        Assert.Equal(expected.TargetEntity, actual.TargetEntity);
        Assert.Equal(expected.ContentId, actual.ContentId);
        Assert.Equal(expected.Entities.ToArray(), actual.Entities.ToArray());
    }
}
