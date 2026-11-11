using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Setup;

namespace Sim.Tests.Serialization;

public class ReplayTests
{
    private const int Interval = ReplayRecorder.DefaultHashInterval;

    /// <summary>The bytes of a replay before its match description.</summary>
    private const int HeaderStart = 10;

    private static GameData Data => TestMatches.Data;

    private static MatchDescription Description(ulong seed = 7) =>
        new("test build", Data.ContentHash, Data.Maps[0].Id, seed, [new PlayerSetup(0), new PlayerSetup(1)]);

    /// <summary>
    /// Orders for both players across 100 ticks: groups, single units, a redirect, a command of
    /// a kind without a handler that uses every field, and two commands in one tick.
    /// </summary>
    private static (int Tick, Command Command)[] Script()
    {
        var match = Description().CreateMatch(Data);
        var units = new[] { new List<EntityId>(), new List<EntityId>() };
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            units[match.Bodies.ValueAt(i).Owner].Add(match.Bodies.EntityAt(i));
        }

        Assert.True(units[0].Count >= 2 && units[1].Count >= 2);
        var width = match.Grid.Width;
        var height = match.Grid.Height;
        var script = new List<(int Tick, Command Command)>
        {
            (1, Move(0, units[0], width - 3, height / 2)),
        };
        for (var i = 0; i < units[1].Count; i++)
        {
            script.Add((1, Move(1, [units[1][i]], 2 + i, 1 + (i % 3))));
        }

        script.Add((25, Move(0, units[0][..2], width / 2, 1)));
        script.Add((44, Move(1, units[1], width / 2, height - 2)));
        script.Add((44, new Command((CommandType)777, 0, [units[0][0], units[1][0]], Fixed.FromRaw(-5), Fixed.FromRaw(99), units[1][1], 31)));
        script.Add((90, Move(0, units[0], 3, height / 2)));
        return [.. script];

        static Command Move(int player, List<EntityId> entities, int x, int y) =>
            new(CommandType.Move, player, [.. entities], Fixed.FromInt(x) + Fixed.Half, Fixed.FromInt(y) + Fixed.Half);
    }

    /// <summary>
    /// Records a match the way a session does: the commands of a tick are known
    /// <paramref name="ahead"/> ticks before it runs.
    /// </summary>
    private static byte[] Record(
        int ticks,
        int ahead = 0,
        bool finish = true,
        (int Tick, Command Command)[]? script = null,
        MatchDescription? description = null)
    {
        description ??= Description();
        script ??= Script();
        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World);
        var scheduled = 0;
        for (var tick = 0; tick < ticks; tick++)
        {
            for (; scheduled <= tick + ahead; scheduled++)
            {
                var due = script.Where(entry => entry.Tick == scheduled).Select(entry => entry.Command).ToArray();
                recorder.RecordCommands(scheduled, due);
                foreach (var command in due)
                {
                    match.Simulation.Commands.Enqueue(scheduled, command);
                }
            }

            match.Simulation.Tick();
            recorder.AfterTick();
        }

        if (finish)
        {
            recorder.Finish();
        }

        return writer.ToArray();
    }

    /// <summary>The state hash after every tick of the scripted match, run without any recording.</summary>
    private static ulong[] HashesOfTheMatch(int ticks, (int Tick, Command Command)[]? script = null)
    {
        var match = Description().CreateMatch(Data);
        foreach (var (tick, command) in script ?? Script())
        {
            if (tick < ticks)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }
        }

        var hashes = new ulong[ticks + 1];
        hashes[0] = match.World.ComputeHash();
        for (var tick = 1; tick <= ticks; tick++)
        {
            match.Simulation.Tick();
            hashes[tick] = match.World.ComputeHash();
        }

        return hashes;
    }

    private static Replay Read(byte[] bytes, bool keepWhatIsWhole = false) => Replay.Read(new ByteReader(bytes), keepWhatIsWhole);

    /// <summary>Where each record of a replay starts, found without the class under test.</summary>
    private static List<(byte Kind, int Tick, int Offset)> Records(byte[] bytes)
    {
        var reader = new ByteReader(bytes);
        reader.ReadBytes(HeaderStart);
        MatchDescription.Read(reader);
        var records = new List<(byte Kind, int Tick, int Offset)>();
        while (!reader.IsAtEnd)
        {
            var offset = reader.Position;
            var kind = reader.ReadByte();
            var tick = reader.ReadInt32();
            if (kind == 1)
            {
                for (var left = reader.ReadCount(1); left > 0; left--)
                {
                    CommandEncoding.Read(reader);
                }
            }
            else
            {
                reader.ReadUInt64();
            }

            records.Add((kind, tick, offset));
        }

        return records;
    }

    private static int OffsetOf(byte[] bytes, byte kind, int tick) =>
        Records(bytes).Single(record => record.Kind == kind && record.Tick == tick).Offset;

    private static byte[] With(byte[] bytes, int offset, params byte[] replacement)
    {
        var changed = (byte[])bytes.Clone();
        replacement.CopyTo(changed, offset);
        return changed;
    }

    /// <summary>The header of a replay of the sample description, followed by records written by the test.</summary>
    private static byte[] Written(Action<ByteWriter> records)
    {
        var writer = new ByteWriter();
        writer.WriteBytes("RPLY"u8);
        writer.WriteUInt16(Replay.Version);
        writer.WriteUInt16(CommandEncoding.Version);
        writer.WriteUInt16(Interval);
        Description().Write(writer);
        records(writer);
        return writer.ToArray();
    }

    private static void WriteRecord(ByteWriter writer, byte kind, int tick, ulong hash)
    {
        writer.WriteByte(kind);
        writer.WriteInt32(tick);
        writer.WriteUInt64(hash);
    }

    private static void WriteCommands(ByteWriter writer, int tick, params Command[] commands)
    {
        writer.WriteByte(1);
        writer.WriteInt32(tick);
        writer.WriteCount(commands.Length);
        foreach (var command in commands)
        {
            CommandEncoding.Write(writer, in command);
        }
    }

    /// <summary>The layout, record for record. A change to it is a new <see cref="Replay.Version"/>.</summary>
    [Fact]
    public void TheBytesOfAReplayAreTheLayout()
    {
        var command = new Command(CommandType.Move, 1, [new EntityId(3)], Fixed.FromInt(4), Fixed.FromInt(5));
        (int Tick, Command Command)[] script = [(3, command)];
        var hashes = HashesOfTheMatch(25, script);

        var expected = Written(writer =>
        {
            WriteRecord(writer, 2, 0, hashes[0]);
            WriteCommands(writer, 3, command);
            WriteRecord(writer, 2, Interval, hashes[Interval]);
            WriteRecord(writer, 3, 25, hashes[25]);
        });

        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(Record(25, script: script)));
        Assert.Equal("52504C59" + "0100" + "0100" + "1400", Convert.ToHexString(expected.AsSpan(0, HeaderStart)));
        Assert.Equal(20, Interval);
        Assert.Equal(SimInfo.TicksPerSecond, Interval);
    }

    [Fact]
    public void AReplayReadBackHasWhatWasRecorded()
    {
        var script = Script();
        var hashes = HashesOfTheMatch(100);

        var replay = Read(Record(100));

        Assert.Equal("test build", replay.Description.GameVersion);
        Assert.Equal(Data.ContentHash, replay.Description.ContentHash);
        Assert.Equal(7UL, replay.Description.Seed);
        Assert.Equal(script.Length, replay.Commands.Count);
        for (var i = 0; i < script.Length; i++)
        {
            Assert.Equal(script[i].Tick, replay.Commands[i].Tick);
            AssertSameCommand(script[i].Command, replay.Commands[i].Command);
        }

        Assert.Equal([0, 20, 40, 60, 80, 100], replay.Hashes.Select(hash => hash.Tick));
        Assert.All(replay.Hashes, hash => Assert.Equal(hashes[hash.Tick], hash.Hash));
        Assert.Equal(new ReplayHash(100, hashes[100]), replay.End);
        Assert.True(replay.IsComplete);
        Assert.Null(replay.DamagedAt);
        Assert.Equal(100, replay.TickCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(100)]
    [InlineData(133)]
    public void ARecordedMatchReproduces(int ticks)
    {
        var hashes = HashesOfTheMatch(ticks);
        var replay = Read(Record(ticks));

        var result = replay.Run(Data);

        Assert.True(result.Reproduced);
        Assert.Null(result.Mismatch);
        Assert.Equal(ticks, result.TicksRun);
        Assert.Equal(hashes[ticks], result.FinalHash);
        Assert.Equal((ticks / Interval) + 1 + (ticks % Interval == 0 ? 0 : 1), result.HashesChecked);
    }

    /// <summary>The match in the replay moves units: the test would mean little if the orders did nothing.</summary>
    [Fact]
    public void TheScriptedMatchChangesWithItsCommands()
    {
        var with = HashesOfTheMatch(100);
        var without = HashesOfTheMatch(100, []);

        Assert.Equal(without[0], with[0]);
        Assert.NotEqual(without[2], with[2]);
        Assert.NotEqual(with[89], with[100]);
    }

    /// <summary>A session knows the commands of a tick before the tick runs, so they are recorded ahead of the hashes.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(30)]
    public void AMatchRecordedWithCommandsKnownAheadReproduces(int ahead)
    {
        var bytes = Record(100, ahead);
        var replay = Read(bytes);

        var result = replay.Run(Data);

        Assert.True(result.Reproduced);
        Assert.Equal(HashesOfTheMatch(100)[100], result.FinalHash);
        Assert.Equal(Script().Length, replay.Commands.Count);

        // Known 30 ticks ahead, the commands of tick 44 are in the file before the hash of tick 20.
        var records = Records(bytes);
        Assert.Equal(ahead == 30, records.IndexOf((1, 44, OffsetOf(bytes, 1, 44))) < records.IndexOf((2, 20, OffsetOf(bytes, 2, 20))));
    }

    /// <summary>Commands that were scheduled for a tick the match never reached are in the file and are not run.</summary>
    [Fact]
    public void CommandsScheduledPastTheEndAreKeptAndNotRun()
    {
        var replay = Read(Record(88, ahead: 5));

        Assert.Equal(90, replay.Commands[^1].Tick);
        Assert.Equal(88, replay.TickCount);
        var result = replay.Run(Data);
        Assert.True(result.Reproduced);
        Assert.Equal(88, result.TicksRun);
        Assert.Equal(HashesOfTheMatch(88)[88], result.FinalHash);
    }

    [Fact]
    public void ARecordingThatStoppedIsAReplayOfWhatItHas()
    {
        var replay = Read(Record(95, finish: false));

        Assert.False(replay.IsComplete);
        Assert.Null(replay.End);
        Assert.Equal(91, replay.TickCount);
        var result = replay.Run(Data);
        Assert.True(result.Reproduced);
        Assert.Equal(91, result.TicksRun);
        Assert.Equal(5, result.HashesChecked);
        Assert.Equal(HashesOfTheMatch(91)[91], result.FinalHash);
    }

    [Fact]
    public void ARecordingThatStoppedAfterAHashRunsToThatHash()
    {
        var replay = Read(Record(125, finish: false, script: []));

        Assert.Equal(120, replay.TickCount);
        Assert.Equal(7, replay.Run(Data).HashesChecked);
    }

    [Fact]
    public void AMatchWithoutATickIsAReplay()
    {
        var description = Description();
        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        _ = new ReplayRecorder(writer, description, match.World);

        var replay = Read(writer.ToArray());

        Assert.Equal(0, replay.TickCount);
        Assert.Equal([new ReplayHash(0, match.World.ComputeHash())], replay.Hashes);
        Assert.Equal(new ReplayResult(0, match.World.ComputeHash(), 1, null), replay.Run(Data));
    }

    [Fact]
    public void AChangedCommandIsFoundAtTheNextHash()
    {
        var bytes = Record(100);
        var firstCommand = OffsetOf(bytes, 1, 1) + 1 + 4 + 4;
        var hashes = HashesOfTheMatch(100);

        // The command's player: 0 becomes 1, who owns none of the units, so the order moves nobody.
        var replay = Read(With(bytes, firstCommand + 2, 1));
        var result = replay.Run(Data);

        Assert.Equal(1, replay.Commands[0].Command.Player);
        Assert.False(result.Reproduced);
        Assert.Equal(20, result.TicksRun);
        Assert.Equal(2, result.HashesChecked);
        Assert.Equal(20, result.Mismatch!.Value.Tick);
        Assert.Equal(hashes[20], result.Mismatch.Value.Recorded);
        Assert.Equal(result.FinalHash, result.Mismatch.Value.Computed);
        Assert.NotEqual(hashes[20], result.FinalHash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(100)]
    public void AChangedHashIsFoundWhereItIs(int tick)
    {
        var bytes = Record(133);
        var hashes = HashesOfTheMatch(133);

        var result = Read(With(bytes, OffsetOf(bytes, 2, tick) + 5, 0xAB)).Run(Data);

        Assert.Equal(tick, result.TicksRun);
        Assert.Equal(hashes[tick], result.FinalHash);
        Assert.Equal((tick / Interval) + 1, result.HashesChecked);
        Assert.Equal(hashes[tick], result.Mismatch!.Value.Computed);
        Assert.NotEqual(hashes[tick], result.Mismatch.Value.Recorded);
    }

    [Fact]
    public void AChangedFinalHashIsFoundAtTheEnd()
    {
        var bytes = Record(133);

        var result = Read(With(bytes, OffsetOf(bytes, 3, 133) + 12, 0xAB)).Run(Data);

        Assert.False(result.Reproduced);
        Assert.Equal(133, result.TicksRun);
        Assert.Equal(133, result.Mismatch!.Value.Tick);
        Assert.Equal(8, result.HashesChecked);
    }

    /// <summary>A match that ended one tick later than recorded has another final state.</summary>
    [Fact]
    public void AChangedLengthIsFoundAtTheEnd()
    {
        var bytes = Record(133);

        var result = Read(With(bytes, OffsetOf(bytes, 3, 133) + 1, 134)).Run(Data);

        Assert.Equal(134, result.Mismatch!.Value.Tick);
    }

    [Fact]
    public void AnotherSeedIsFoundAtTheFirstHash()
    {
        var bytes = Record(60);
        var description = new ByteReader(bytes.AsMemory(HeaderStart));
        description.ReadUInt16();
        description.ReadString();
        description.ReadBytes(32);
        description.ReadString();
        var seedAt = HeaderStart + description.Position;

        var replay = Read(With(bytes, seedAt, 8));
        var result = replay.Run(Data);

        Assert.Equal(8UL, replay.Description.Seed);
        Assert.Equal(0, result.Mismatch!.Value.Tick);
        Assert.Equal(0, result.TicksRun);
        Assert.Equal(1, result.HashesChecked);
    }

    [Fact]
    public void AReplayOfOtherDataDoesNotRun()
    {
        var other = new string('e', 64);
        var bytes = Record(40);
        var hashAt = bytes.AsSpan().IndexOf(Convert.FromHexString(Data.ContentHash));

        var replay = Read(With(bytes, hashAt, Convert.FromHexString(other)));

        Assert.Equal(other, replay.Description.ContentHash);
        var exception = Assert.Throws<InvalidDataException>(() => replay.Run(Data));
        Assert.Contains(other, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReplayRunsOnlyInAMatchThatHasNotRun()
    {
        var replay = Read(Record(40));
        var match = replay.Description.CreateMatch(Data);
        match.Simulation.Tick();

        Assert.Throws<ArgumentException>(() => replay.Run(match));
        Assert.Throws<ArgumentException>(() => replay.RecordAgain(match, replay.Description, new ByteWriter()));
        Assert.Throws<ArgumentNullException>(() => replay.Run((Match)null!));
        Assert.Throws<ArgumentNullException>(() => replay.Run((GameData)null!));
        Assert.Throws<ArgumentNullException>(() => Replay.Read(null!));
    }

    [Fact]
    public void RunningAReplayTwiceGivesTheSameResult()
    {
        var replay = Read(Record(100));

        Assert.Equal(replay.Run(Data), replay.Run(Data));
    }

    public static TheoryData<string, string> NotAReplay => new()
    {
        { "52504C58" + "0100" + "0100", "RPLY" },
        { "534E4150" + "0100" + "0100", "RPLY" },
        { "52504C59" + "0200" + "0100", "layout version 2" },
        { "52504C59" + "0000" + "0100", "layout version 0" },
        { "52504C59" + "0100" + "0200", "encoding version 2" },
        { "52504C59" + "0100" + "0100" + "0000", "hash interval of 0" },
        { "52504C59" + "0100" + "0100" + "1400" + "0200", "match description has layout version 2" },
    };

    [Theory]
    [MemberData(nameof(NotAReplay))]
    public void BytesWithAnotherHeaderAreRefused(string start, string expected)
    {
        var bytes = With(Record(20), 0, Convert.FromHexString(start));

        Assert.Contains(expected, Assert.Throws<InvalidDataException>(() => Read(bytes)).Message, StringComparison.Ordinal);
        Assert.Contains(expected, Assert.Throws<InvalidDataException>(() => Read(bytes, keepWhatIsWhole: true)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsOutOfOrderAreRefused()
    {
        var command = new Command(CommandType.Move, 0, [new EntityId(1)]);

        Refused(w => WriteRecord(w, 9, 0, 0), "unknown kind 9");
        Refused(w => WriteRecord(w, 0, 0, 0), "unknown kind 0");
        Refused(w => WriteRecord(w, 2, -1, 0), "tick -1");
        Refused(w => WriteRecord(w, 3, -5, 0), "tick -5");
        Refused(w => WriteCommands(w, -1, command), "tick -1");
        Refused(w => WriteCommands(w, 4), "without a command");
        Refused(
            w =>
            {
                WriteCommands(w, 4, command);
                WriteCommands(w, 4, command);
            },
            "commands for tick 4 after those for tick 4");
        Refused(
            w =>
            {
                WriteCommands(w, 4, command);
                WriteCommands(w, 3, command);
            },
            "commands for tick 3 after those for tick 4");
        Refused(w => WriteRecord(w, 2, 20, 1), "hash of tick 20 where that of tick 0 is due");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 2, 0, 1);
            },
            "hash of tick 0 where that of tick 20 is due");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 2, 40, 1);
            },
            "hash of tick 40 where that of tick 20 is due");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 2, 19, 1);
            },
            "hash of tick 19 where that of tick 20 is due");
        Refused(w => WriteRecord(w, 3, 0, 1), "without the hash of tick 0");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 2, 20, 1);
                WriteRecord(w, 3, 19, 1);
            },
            "ends at tick 19");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 3, 20, 1);
            },
            "ends at tick 20");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 3, int.MaxValue, 1);
            },
            "ends at tick 2147483647");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 3, 0, 2);
            },
            "another hash");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 3, 5, 1);
                WriteRecord(w, 2, 20, 1);
            },
            "after its end record");
        Refused(
            w =>
            {
                WriteRecord(w, 2, 0, 1);
                WriteRecord(w, 3, 5, 1);
                w.WriteByte(0);
            },
            "after its end record");

        static void Refused(Action<ByteWriter> records, string expected)
        {
            var bytes = Written(records);
            Assert.Contains(expected, Assert.Throws<InvalidDataException>(() => Read(bytes)).Message, StringComparison.Ordinal);

            var kept = Read(bytes, keepWhatIsWhole: true);
            Assert.NotNull(kept.DamagedAt);
        }
    }

    /// <summary>Commands and hashes are each in order, and not sorted against each other.</summary>
    [Fact]
    public void CommandsMayComeBeforeEarlierHashes()
    {
        var command = new Command(CommandType.Move, 0, [new EntityId(1)]);

        var replay = Read(Written(w =>
        {
            WriteCommands(w, 50, command);
            WriteRecord(w, 2, 0, 1);
            WriteCommands(w, 70, command, command);
            WriteRecord(w, 2, 20, 2);
            WriteRecord(w, 3, 39, 2);
        }));

        Assert.Equal([50, 70, 70], replay.Commands.Select(entry => entry.Tick));
        Assert.Equal([0, 20], replay.Hashes.Select(hash => hash.Tick));
        Assert.Equal(39, replay.TickCount);
        Assert.True(replay.IsComplete);
    }

    /// <summary>
    /// Without an end record a replay runs to its last hash, and on while it has commands, but
    /// never a whole interval more: however far ahead its last command is, the run is bounded.
    /// </summary>
    [Theory]
    [InlineData(-1, 40)]
    [InlineData(3, 40)]
    [InlineData(39, 40)]
    [InlineData(40, 41)]
    [InlineData(55, 56)]
    [InlineData(58, 59)]
    [InlineData(59, 59)]
    [InlineData(60, 59)]
    [InlineData(int.MaxValue, 59)]
    public void ARecordingThatStoppedRunsLessThanOneIntervalPastItsLastHash(int lastCommandTick, int expected)
    {
        var command = new Command(CommandType.Move, 0, [new EntityId(1)]);

        var replay = Read(Written(w =>
        {
            WriteRecord(w, 2, 0, 1);
            WriteRecord(w, 2, 20, 2);
            WriteRecord(w, 2, 40, 2);
            if (lastCommandTick >= 0)
            {
                WriteCommands(w, lastCommandTick, command);
            }
        }));

        Assert.Equal(expected, replay.TickCount);
    }

    /// <summary>The interval between hashes is the recorder's choice and is in the file.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(250)]
    public void AReplayWithAnotherHashIntervalReproduces(int interval)
    {
        var description = Description();
        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World, interval);
        var script = Script();
        for (var tick = 0; tick < 100; tick++)
        {
            foreach (var (_, command) in script.Where(entry => entry.Tick == tick))
            {
                recorder.RecordCommands(tick, [command]);
                match.Simulation.Commands.Enqueue(tick, command);
                break;
            }

            match.Simulation.Tick();
            recorder.AfterTick();
        }

        recorder.Finish();
        var replay = Read(writer.ToArray());
        var result = replay.Run(Data);

        Assert.Equal(interval, replay.HashInterval);
        Assert.Equal(Enumerable.Range(0, (100 / interval) + 1).Select(i => i * interval), replay.Hashes.Select(hash => hash.Tick));
        Assert.True(result.Reproduced);
        Assert.Equal(100, result.TicksRun);

        var again = new ByteWriter();
        replay.RecordAgain(description.CreateMatch(Data), description, again);
        Assert.Equal(Convert.ToHexString(writer.WrittenSpan), Convert.ToHexString(again.WrittenSpan));
    }

    /// <summary>
    /// A file cut anywhere after its header is refused by a strict read, unless the cut falls
    /// between two records, and is a replay of its whole records when asked to keep those. What
    /// is kept still reproduces.
    /// </summary>
    [Fact]
    public void AReplayCutShortKeepsItsWholeRecords()
    {
        var bytes = Record(100, ahead: 2);
        var records = Records(bytes);
        var starts = records.Select(record => record.Offset).ToHashSet();
        var headerLength = records[0].Offset;

        for (var length = 0; length < bytes.Length; length++)
        {
            var cut = bytes[..length];
            if (length < headerLength)
            {
                Assert.Throws<InvalidDataException>(() => Read(cut));
                Assert.Throws<InvalidDataException>(() => Read(cut, keepWhatIsWhole: true));
                continue;
            }

            var whole = records.Where(record => record.Offset < length).ToList();
            var lastIsWhole = starts.Contains(length);
            if (!lastIsWhole)
            {
                whole.RemoveAt(whole.Count - 1);
                Assert.Throws<InvalidDataException>(() => Read(cut));
            }
            else
            {
                Assert.Null(Read(cut).DamagedAt);
            }

            var kept = Read(cut, keepWhatIsWhole: true);
            Assert.Equal(lastIsWhole ? null : records.Last(record => record.Offset < length).Offset, kept.DamagedAt);
            Assert.False(kept.IsComplete);
            Assert.Equal(whole.Count(record => record.Kind == 2), kept.Hashes.Count);
            Assert.Equal(whole.Where(record => record.Kind == 1).Select(record => record.Tick), kept.Commands.Select(entry => entry.Tick).Distinct());
        }

        var half = Read(bytes[..(OffsetOf(bytes, 2, 60) + 3)], keepWhatIsWhole: true);
        Assert.Equal([0, 20, 40], half.Hashes.Select(hash => hash.Tick));
        Assert.True(half.Run(Data).Reproduced);
    }

    /// <summary>A commands record cut inside its second command adds none of its commands.</summary>
    [Fact]
    public void ACommandsRecordCutShortAddsNothing()
    {
        var bytes = Record(100);
        var record = OffsetOf(bytes, 1, 44);
        var next = Records(bytes).First(other => other.Offset > record).Offset;

        var kept = Read(bytes[..(next - 1)], keepWhatIsWhole: true);

        Assert.Equal(record, kept.DamagedAt);
        Assert.DoesNotContain(kept.Commands, entry => entry.Tick == 44);
        Assert.Equal(25, kept.Commands[^1].Tick);
    }

    /// <summary>Whatever the bytes after the header are, reading them ends in a replay or in <see cref="InvalidDataException"/>.</summary>
    [Fact]
    public void BytesChangedAtRandomNeverFailAnyOtherWay()
    {
        var good = Record(100);
        var headerLength = Records(good)[0].Offset;
        var random = new DeterministicRandom(2026);
        var refused = 0;
        for (var round = 0; round < 600; round++)
        {
            var bytes = (byte[])good.Clone();
            for (var changes = 1 + random.NextInt(3); changes > 0; changes--)
            {
                bytes[headerLength + random.NextInt(bytes.Length - headerLength)] = (byte)random.NextInt(256);
            }

            var cut = bytes[..(headerLength + random.NextInt(bytes.Length - headerLength + 1))];
            try
            {
                var replay = Read(round % 2 == 0 ? bytes : cut);
                _ = replay.Run(Data);
            }
            catch (InvalidDataException)
            {
                refused++;
            }

            var kept = Read(round % 2 == 0 ? bytes : cut, keepWhatIsWhole: true);
            _ = kept.Run(Data);
        }

        Assert.InRange(refused, 60, 599);
    }

    [Fact]
    public void RecordingAgainGivesTheSameFileForTheSameRules()
    {
        var bytes = Record(133);
        var replay = Read(bytes);
        var writer = new ByteWriter();

        replay.RecordAgain(replay.Description.CreateMatch(Data), replay.Description, writer);

        Assert.Equal(Convert.ToHexString(bytes), Convert.ToHexString(writer.WrittenSpan));
    }

    /// <summary>
    /// Recording again puts right what running the replay finds wrong: hashes that the rules no
    /// longer give. It keeps the commands and the length, drops commands that never ran, and
    /// writes the records in the order a recorder without a session writes them.
    /// </summary>
    [Fact]
    public void RecordingAgainReplacesTheHashesAndKeepsTheCommands()
    {
        var canonical = Record(88);
        var bytes = Record(88, ahead: 5);
        foreach (var record in Records(bytes).Where(record => record.Kind != 1))
        {
            bytes = With(bytes, record.Offset + 5, 0x77, 0x77);
        }

        var replay = Read(bytes);
        Assert.False(replay.Run(Data).Reproduced);
        var description = new MatchDescription("another build", Data.ContentHash, replay.Description.MapId, replay.Description.Seed, replay.Description.Players);
        var writer = new ByteWriter();
        replay.RecordAgain(description.CreateMatch(Data), description, writer);
        var again = Read(writer.ToArray());

        Assert.True(again.Run(Data).Reproduced);
        Assert.Equal("another build", again.Description.GameVersion);
        Assert.Equal(88, again.TickCount);
        Assert.Equal(Read(canonical).Commands.Count, again.Commands.Count);
        Assert.Equal(replay.Commands.Count - 1, again.Commands.Count);
        Assert.Equal(Read(canonical).Hashes, again.Hashes);
        Assert.Equal(Read(canonical).End, again.End);
    }

    [Fact]
    public void RecordingAgainFinishesARecordingThatStopped()
    {
        var replay = Read(Record(95, finish: false));
        var writer = new ByteWriter();

        replay.RecordAgain(replay.Description.CreateMatch(Data), replay.Description, writer);

        Assert.Equal(Convert.ToHexString(Record(91)), Convert.ToHexString(writer.WrittenSpan));
    }

    [Fact]
    public void TheRecorderOnlyAppends()
    {
        var description = Description();
        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World);
        var file = new List<byte>();
        var script = Script();
        for (var tick = 0; tick < 100; tick++)
        {
            var due = script.Where(entry => entry.Tick == tick).Select(entry => entry.Command).ToArray();
            recorder.RecordCommands(tick, due);
            foreach (var command in due)
            {
                match.Simulation.Commands.Enqueue(tick, command);
            }

            match.Simulation.Tick();
            recorder.AfterTick();
            file.AddRange(writer.WrittenSpan);
            writer.Clear();
        }

        recorder.Finish();
        file.AddRange(writer.WrittenSpan);

        Assert.Equal(Convert.ToHexString(Record(100)), Convert.ToHexString(file.ToArray()));
    }

    [Fact]
    public void RecordingChangesNothingInTheWorld()
    {
        var hashes = HashesOfTheMatch(100);
        var replay = Read(Record(100));

        Assert.Equal(hashes[100], replay.End!.Value.Hash);
        Assert.Equal(hashes[60], replay.Hashes[3].Hash);
    }

    [Fact]
    public void TheRecorderRefusesWhatWouldNotBeAReplay()
    {
        var description = Description();
        var command = new Command(CommandType.Move, 0, [new EntityId(1)]);
        var ran = description.CreateMatch(Data);
        ran.Simulation.Tick();
        Assert.Throws<ArgumentException>(() => new ReplayRecorder(new ByteWriter(), description, ran.World));
        Assert.Throws<ArgumentNullException>(() => new ReplayRecorder(null!, description, ran.World));
        Assert.Throws<ArgumentNullException>(() => new ReplayRecorder(new ByteWriter(), null!, ran.World));
        Assert.Throws<ArgumentNullException>(() => new ReplayRecorder(new ByteWriter(), description, null!));

        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World);
        match.Simulation.Run(3);
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.RecordCommands(2, [command]));
        recorder.RecordCommands(5, [command]);
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.RecordCommands(5, [command]));
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.RecordCommands(4, [command]));
        recorder.RecordCommands(6, []);
        Assert.Throws<ArgumentOutOfRangeException>(() => recorder.RecordCommands(6, [command]));
        var before = writer.Length;
        Assert.False(recorder.IsFinished);

        recorder.Finish();
        var finished = writer.Length;
        recorder.Finish();

        Assert.True(recorder.IsFinished);
        Assert.Equal(before + 13, finished);
        Assert.Equal(finished, writer.Length);
        Assert.Throws<InvalidOperationException>(() => recorder.RecordCommands(9, [command]));
        Assert.Throws<InvalidOperationException>(recorder.AfterTick);
        Assert.Equal([5], Read(writer.ToArray()).Commands.Select(entry => entry.Tick));
    }

    /// <summary>A hash is recorded once for a tick however often the recorder is told a tick ended.</summary>
    [Fact]
    public void AHashIsRecordedOnceForATick()
    {
        var description = Description();
        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World);
        recorder.AfterTick();
        match.Simulation.Run(Interval);
        recorder.AfterTick();
        recorder.AfterTick();
        match.Simulation.Run(Interval - 1);
        recorder.AfterTick();
        recorder.Finish();

        var replay = Read(writer.ToArray());

        Assert.Equal([0, Interval], replay.Hashes.Select(hash => hash.Tick));
        Assert.Equal((2 * Interval) - 1, replay.End!.Value.Tick);
        Assert.True(replay.Run(Data).Reproduced);
    }

    /// <summary>The recorder need not be told of every tick, only of those whose hash is due.</summary>
    [Fact]
    public void ARecorderThatMissedADueHashSaysSo()
    {
        var description = Description();
        var match = description.CreateMatch(Data);
        var recorder = new ReplayRecorder(new ByteWriter(), description, match.World);

        match.Simulation.Run(Interval + 1);

        Assert.Contains("tick 20 was not recorded", Assert.Throws<InvalidOperationException>(recorder.AfterTick).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(recorder.Finish);
        Assert.False(recorder.IsFinished);
    }

    /// <summary>A match that ends on a tick whose hash is due gets that hash from <see cref="ReplayRecorder.Finish"/> when nothing else recorded it.</summary>
    [Fact]
    public void FinishingRecordsAHashThatIsDue()
    {
        var description = Description();
        var match = description.CreateMatch(Data);
        var writer = new ByteWriter();
        var recorder = new ReplayRecorder(writer, description, match.World);
        match.Simulation.Run(Interval);

        recorder.Finish();

        Assert.Equal(Convert.ToHexString(Record(Interval, script: [])), Convert.ToHexString(writer.WrittenSpan));
    }

    [Fact]
    public void TheHashIntervalHasLimits()
    {
        var description = Description();
        var match = description.CreateMatch(Data);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayRecorder(new ByteWriter(), description, match.World, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayRecorder(new ByteWriter(), description, match.World, ReplayRecorder.MaxHashInterval + 1));
        var writer = new ByteWriter();
        _ = new ReplayRecorder(writer, description, match.World, ReplayRecorder.MaxHashInterval);
        Assert.Equal(ReplayRecorder.MaxHashInterval, Read(writer.ToArray()).HashInterval);
    }

    private static void AssertSameCommand(Command expected, Command actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Player, actual.Player);
        Assert.Equal(expected.Entities.ToArray(), actual.Entities.ToArray());
        Assert.Equal(expected.TargetX, actual.TargetX);
        Assert.Equal(expected.TargetY, actual.TargetY);
        Assert.Equal(expected.TargetEntity, actual.TargetEntity);
        Assert.Equal(expected.ContentId, actual.ContentId);
    }
}
