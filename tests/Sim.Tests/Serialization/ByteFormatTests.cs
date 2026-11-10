using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;

namespace Sim.Tests.Serialization;

public class ByteFormatTests
{
    /// <summary>
    /// One value of every kind, and the bytes they must become. This is the format: if a change
    /// here is intended, every replay and snapshot written before it stops being readable.
    /// </summary>
    [Fact]
    public void EveryKindOfValueHasItsRecordedBytes()
    {
        var writer = new ByteWriter();
        writer.WriteByte(0xAB);
        writer.WriteBool(true);
        writer.WriteBool(false);
        writer.WriteUInt16(0x1234);
        writer.WriteInt32(-2);
        writer.WriteUInt32(0x89ABCDEF);
        writer.WriteInt64(-0x0102030405060708);
        writer.WriteUInt64(0x1122334455667788);
        writer.WriteInt128(-1);
        writer.WriteInt128((Int128.One << 64) + 5);
        writer.WriteCount(3);
        writer.WriteEntityId(new EntityId(258));
        writer.WriteFixed(Fixed.FromInt(-1) - Fixed.Half);
        writer.WriteAngle(Angle.FromUnits(4095));
        writer.WriteFraction(new Fraction(-5, 4));
        writer.WriteString("aé€😀");
        writer.WriteBytes([0xFE, 0xFF]);

        const string expected =
            "AB" +
            "01" +
            "00" +
            "3412" +
            "FEFFFFFF" +
            "EFCDAB89" +
            "F8F8F9FAFBFCFDFE" +
            "8877665544332211" +
            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF" +
            "05000000000000000100000000000000" +
            "03000000" +
            "02010000" +
            "0080FEFF" +
            "FF0F" +
            "FBFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF" + "04000000000000000000000000000000" +
            "0A000000" + "61" + "C3A9" + "E282AC" + "F09F9880" +
            "FEFF";
        Assert.Equal(expected, Convert.ToHexString(writer.WrittenSpan));
        Assert.Equal(expected.Length / 2, writer.Length);
    }

    [Fact]
    public void EveryKindOfValueReadsBackIncludingItsExtremes()
    {
        Int128[] wide = [0, 1, -1, Int128.MaxValue, Int128.MinValue, (Int128)ulong.MaxValue + 1, -(Int128)ulong.MaxValue];
        Fraction[] fractions = [Fraction.Zero, Fraction.One, new(-5, 4), new(Int128.MaxValue, 1), new(-Int128.MaxValue, 1), new(1, Int128.MaxValue)];
        string[] texts = [string.Empty, "movers", "aé€😀", "\0", new string('x', 1000)];

        var writer = new ByteWriter(capacity: 0);
        foreach (var value in (byte[])[0, 1, 127, 128, 255])
        {
            writer.WriteByte(value);
        }

        foreach (var value in (ushort[])[0, 1, 255, 256, ushort.MaxValue])
        {
            writer.WriteUInt16(value);
        }

        foreach (var value in (int[])[0, 1, -1, int.MaxValue, int.MinValue])
        {
            writer.WriteInt32(value);
            writer.WriteUInt32(unchecked((uint)value));
            writer.WriteEntityId(new EntityId(value));
            writer.WriteFixed(Fixed.FromRaw(value));
        }

        foreach (var value in (long[])[0, 1, -1, long.MaxValue, long.MinValue])
        {
            writer.WriteInt64(value);
            writer.WriteUInt64(unchecked((ulong)value));
        }

        foreach (var value in wide)
        {
            writer.WriteInt128(value);
        }

        foreach (var value in (int[])[0, 1, 1024, 4095])
        {
            writer.WriteAngle(Angle.FromUnits(value));
        }

        foreach (var value in fractions)
        {
            writer.WriteFraction(value);
        }

        foreach (var value in texts)
        {
            writer.WriteString(value);
        }

        var reader = new ByteReader(writer.ToArray());
        foreach (var value in (byte[])[0, 1, 127, 128, 255])
        {
            Assert.Equal(value, reader.ReadByte());
        }

        foreach (var value in (ushort[])[0, 1, 255, 256, ushort.MaxValue])
        {
            Assert.Equal(value, reader.ReadUInt16());
        }

        foreach (var value in (int[])[0, 1, -1, int.MaxValue, int.MinValue])
        {
            Assert.Equal(value, reader.ReadInt32());
            Assert.Equal(unchecked((uint)value), reader.ReadUInt32());
            Assert.Equal(new EntityId(value), reader.ReadEntityId());
            Assert.Equal(Fixed.FromRaw(value), reader.ReadFixed());
        }

        foreach (var value in (long[])[0, 1, -1, long.MaxValue, long.MinValue])
        {
            Assert.Equal(value, reader.ReadInt64());
            Assert.Equal(unchecked((ulong)value), reader.ReadUInt64());
        }

        foreach (var value in wide)
        {
            Assert.Equal(value, reader.ReadInt128());
        }

        foreach (var value in (int[])[0, 1, 1024, 4095])
        {
            Assert.Equal(Angle.FromUnits(value), reader.ReadAngle());
        }

        foreach (var value in fractions)
        {
            Assert.Equal(value, reader.ReadFraction());
        }

        foreach (var value in texts)
        {
            Assert.Equal(value, reader.ReadString());
        }

        reader.ExpectEnd();
    }

    [Fact]
    public void TheReaderKeepsCountOfWhereItIs()
    {
        var reader = new ByteReader(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        Assert.Equal(0, reader.Position);
        Assert.Equal(7, reader.Remaining);
        Assert.False(reader.IsAtEnd);

        Assert.Equal(0x04030201, reader.ReadInt32());
        Assert.Equal(4, reader.Position);
        Assert.Equal(3, reader.Remaining);
        Assert.Throws<InvalidDataException>(reader.ExpectEnd);

        Assert.Equal([5, 6, 7], reader.ReadBytes(3).ToArray());
        Assert.True(reader.IsAtEnd);
        reader.ExpectEnd();
        Assert.Equal(0, reader.ReadBytes(0).Length);
    }

    [Fact]
    public void AReaderOverPartOfAnArrayReadsOnlyThatPart()
    {
        byte[] bytes = [9, 9, 1, 0, 0, 0, 9];
        var reader = new ByteReader(bytes.AsMemory(2, 4));

        Assert.Equal(1, reader.ReadInt32());
        Assert.Throws<InvalidDataException>(() => reader.ReadByte());
    }

    [Fact]
    public void ReadingPastTheEndThrowsAndReadsNothing()
    {
        Action<ByteReader>[] reads =
        [
            reader => reader.ReadByte(),
            reader => reader.ReadBool(),
            reader => reader.ReadUInt16(),
            reader => reader.ReadInt32(),
            reader => reader.ReadUInt32(),
            reader => reader.ReadInt64(),
            reader => reader.ReadUInt64(),
            reader => reader.ReadInt128(),
            reader => reader.ReadCount(1),
            reader => reader.ReadEntityId(),
            reader => reader.ReadFixed(),
            reader => reader.ReadAngle(),
            reader => reader.ReadFraction(),
            reader => reader.ReadString(),
            reader => reader.ReadBytes(1),
        ];

        Assert.All(reads, read => Assert.Throws<InvalidDataException>(() => read(new ByteReader(ReadOnlyMemory<byte>.Empty))));

        var oneByteShort = new ByteReader(new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidDataException>(() => oneByteShort.ReadInt32());
        Assert.Equal(0, oneByteShort.Position);
        Assert.Equal(0x0201, oneByteShort.ReadUInt16());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void ATrueOrFalseByteMustBeZeroOrOne(byte value)
    {
        var reader = new ByteReader(new[] { value });

        Assert.Throws<InvalidDataException>(() => reader.ReadBool());
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(65535)]
    public void AnAngleMustBeLessThanOneTurn(ushort units)
    {
        var writer = new ByteWriter();
        writer.WriteUInt16(units);

        Assert.Throws<InvalidDataException>(() => new ByteReader(writer.ToArray()).ReadAngle());
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    [InlineData(0, 2)]
    public void AFractionMustBeInLowestTermsWithAPositiveDenominator(long numerator, long denominator)
    {
        var writer = new ByteWriter();
        writer.WriteInt128(numerator);
        writer.WriteInt128(denominator);

        Assert.Throws<InvalidDataException>(() => new ByteReader(writer.ToArray()).ReadFraction());
    }

    [Fact]
    public void AFractionTheTypeCannotHoldIsRejected()
    {
        // The most negative 128-bit number has no positive counterpart, so no Fraction holds it.
        var writer = new ByteWriter();
        writer.WriteInt128(Int128.MinValue);
        writer.WriteInt128(1);

        Assert.Throws<InvalidDataException>(() => new ByteReader(writer.ToArray()).ReadFraction());
    }

    [Fact]
    public void ACountIsNeverNegative()
    {
        var writer = new ByteWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteCount(-1));
        Assert.Equal(0, writer.Length);

        writer.WriteInt32(-1);
        Assert.Throws<InvalidDataException>(() => new ByteReader(writer.ToArray()).ReadCount(1));
    }

    [Fact]
    public void ACountMustFitInTheBytesThatAreLeft()
    {
        var writer = new ByteWriter();
        writer.WriteCount(3);
        writer.WriteBytes(new byte[12]);
        var bytes = writer.ToArray();

        Assert.Equal(3, new ByteReader(bytes).ReadCount(4));
        Assert.Equal(3, new ByteReader(bytes).ReadCount(1));
        Assert.Throws<InvalidDataException>(() => new ByteReader(bytes).ReadCount(5));
        Assert.Throws<InvalidDataException>(() => new ByteReader(bytes.AsMemory(0, 15)).ReadCount(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ByteReader(bytes).ReadCount(0));
    }

    [Fact]
    public void AHugeCountIsRejectedBeforeAnythingIsAllocated()
    {
        var writer = new ByteWriter();
        writer.WriteCount(int.MaxValue);
        writer.WriteBytes(new byte[64]);
        var bytes = writer.ToArray();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => new ByteReader(bytes).ReadCount(int.MaxValue));
        Assert.Throws<InvalidDataException>(() => new ByteReader(bytes).ReadString());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 0, 64 * 1024);
    }

    [Theory]
    [InlineData("FF")] // not a start byte
    [InlineData("C3")] // a sequence cut short
    [InlineData("C080")] // overlong form of U+0000
    [InlineData("EDA080")] // a surrogate
    [InlineData("F4908080")] // beyond U+10FFFF
    public void TextMustBeValidUtf8(string hex)
    {
        var text = Convert.FromHexString(hex);
        var writer = new ByteWriter();
        writer.WriteCount(text.Length);
        writer.WriteBytes(text);

        Assert.Throws<InvalidDataException>(() => new ByteReader(writer.ToArray()).ReadString());
    }

    [Fact]
    public void TextThatIsNotValidUnicodeIsNotWritten()
    {
        var writer = new ByteWriter();
        writer.WriteByte(7);

        Assert.Throws<ArgumentException>(() => writer.WriteString("a\uD800b"));
        Assert.Throws<ArgumentNullException>(() => writer.WriteString(null!));
        Assert.Equal(1, writer.Length);
    }

    [Fact]
    public void ClearingAWriterStartsItAgain()
    {
        var writer = new ByteWriter(capacity: 2);
        writer.WriteInt64(1);
        writer.WriteInt64(2);
        var first = writer.ToArray();

        writer.Clear();
        Assert.Equal(0, writer.Length);
        Assert.True(writer.WrittenSpan.IsEmpty);

        writer.WriteUInt16(0x0102);
        Assert.Equal([2, 1], writer.WrittenSpan.ToArray());
        Assert.Equal([2, 1], writer.WrittenMemory.ToArray());
        Assert.Equal(16, first.Length);
        Assert.Equal(1, first[0]);
    }

    /// <summary>
    /// Every value has one encoding, so whatever is read without an error writes back as the bytes
    /// it was read from. Without this, two peers could hold the same value as different bytes.
    /// </summary>
    [Fact]
    public void BytesThatAreAcceptedWriteBackAsTheSameBytes()
    {
        var random = new DeterministicRandom(20261110);
        var accepted = new int[6];
        var rejected = new int[6];
        var bytes = new byte[32];
        for (var round = 0; round < 20_000; round++)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                // Mostly zero and small bytes, so that a fair share of the input is valid.
                bytes[i] = random.NextInt(10) switch
                {
                    < 5 => 0,
                    < 8 => (byte)random.NextInt(1, 3),
                    _ => (byte)random.NextInt(256),
                };
            }

            var kind = round % 6;
            var reader = new ByteReader(bytes);
            var writer = new ByteWriter();
            try
            {
                switch (kind)
                {
                    case 0:
                        writer.WriteBool(reader.ReadBool());
                        break;
                    case 1:
                        writer.WriteAngle(reader.ReadAngle());
                        break;
                    case 2:
                        writer.WriteFraction(reader.ReadFraction());
                        break;
                    case 3:
                        writer.WriteString(reader.ReadString());
                        break;
                    case 4:
                        writer.WriteCount(reader.ReadCount(7));
                        break;
                    default:
                        writer.WriteInt128(reader.ReadInt128());
                        break;
                }
            }
            catch (InvalidDataException)
            {
                rejected[kind]++;
                continue;
            }

            accepted[kind]++;
            Assert.Equal(reader.Position, writer.Length);
            Assert.Equal(bytes.AsSpan(0, reader.Position).ToArray(), writer.ToArray());
        }

        // Both outcomes happened for every kind that can be wrong, or the test proved little.
        Assert.All(accepted, count => Assert.InRange(count, 100, int.MaxValue));
        Assert.All(rejected[..5], count => Assert.InRange(count, 100, int.MaxValue));
        Assert.Equal(0, rejected[5]);
    }
}
