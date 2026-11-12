using System.Buffers.Binary;

namespace Game.Tests.Sound;

/// <summary>
/// The sounds as rows of samples (<see cref="SoundRecipes"/>). Nobody listens to a test, so
/// these hold what can be measured: every cue has a sound of a sensible length, none is silent
/// or clipped, none starts or ends with a click, and the bytes handed to the engine or written
/// to a file are the samples. No sample value is pinned: the sine of one machine may differ
/// from another's in its last digit.
/// </summary>
public class SoundRecipesTests
{
    private const float Tolerance = 0.001f;

    public static TheoryData<SoundCue> Cues => [.. SoundCues.All];

    [Theory]
    [MemberData(nameof(Cues))]
    public void ASoundIsShortAndIsAllNumbers(SoundCue cue)
    {
        var samples = SoundRecipes.Make(cue);
        var seconds = samples.Length / (double)SoundRecipes.SampleRate;

        Assert.InRange(seconds, 0.05, 2.0);
        Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void ASoundReachesItsPeakAndNeverPassesIt(SoundCue cue)
    {
        var samples = SoundRecipes.Make(cue);

        Assert.Equal(SoundRecipes.Peak, samples.Max(sample => Math.Abs(sample)), Tolerance);
        Assert.True(SoundRecipes.Peak < 1f);
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void ASoundIsNotSilence(SoundCue cue)
    {
        var samples = SoundRecipes.Make(cue);
        var loudness = Math.Sqrt(samples.Sum(sample => (double)sample * sample) / samples.Length);

        Assert.True(loudness > 0.03, $"The root mean square of the samples is {loudness}.");
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void ASoundStartsAndEndsAtNothingSoThatItDoesNotClick(SoundCue cue)
    {
        var samples = SoundRecipes.Make(cue);

        Assert.Equal(0f, samples[0]);
        Assert.InRange(samples[^1], -0.01f, 0.01f);

        // The last thousandth of a second is quiet all the way.
        var tail = samples[^(SoundRecipes.SampleRate / 1000)..];
        Assert.All(tail, sample => Assert.InRange(sample, -0.1f, 0.1f));
    }

    [Theory]
    [MemberData(nameof(Cues))]
    public void TheSameCueIsTheSameSoundEveryTimeItIsMade(SoundCue cue)
    {
        Assert.Equal(SoundRecipes.Make(cue), SoundRecipes.Make(cue));
    }

    [Fact]
    public void NoTwoCuesAreTheSameSound()
    {
        var sounds = SoundCues.All.Select(SoundRecipes.Make).ToArray();
        for (var a = 0; a < sounds.Length; a++)
        {
            for (var b = a + 1; b < sounds.Length; b++)
            {
                Assert.False(sounds[a].AsSpan().SequenceEqual(sounds[b]), $"{SoundCues.All[a]} and {SoundCues.All[b]} are the same samples.");
            }
        }
    }

    [Fact]
    public void TheSoundsHeardMostOftenAreTheShortest()
    {
        static double Seconds(SoundCue cue) => SoundRecipes.Make(cue).Length / (double)SoundRecipes.SampleRate;

        Assert.True(Seconds(SoundCue.OrderGiven) <= 0.12);
        Assert.True(Seconds(SoundCue.MeleeHit) <= 0.25);
        Assert.True(Seconds(SoundCue.ShotHit) <= 0.25);
        Assert.True(Seconds(SoundCue.EpochReached) > Seconds(SoundCue.ResearchFinished));
    }

    [Fact]
    public void SamplesBecomeSixteenBitNumbersLowByteFirstAndStayInRange()
    {
        var bytes = SoundRecipes.ToPcm16([0f, 1f, -1f, 0.25f, 3f, -3f]);

        short[] values = [.. Enumerable.Range(0, bytes.Length / 2).Select(i => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2)))];
        Assert.Equal([0, short.MaxValue, -short.MaxValue, 8192, short.MaxValue, -short.MaxValue], values);
    }

    [Fact]
    public void AWaveFileSaysWhatItHoldsAndHoldsTheSamples()
    {
        var samples = SoundRecipes.Make(SoundCue.OrderGiven);
        using var file = new MemoryStream();

        SoundRecipes.WriteWave(file, samples);

        var bytes = file.ToArray();
        var data = SoundRecipes.ToPcm16(samples);
        Assert.Equal(44 + data.Length, bytes.Length);
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal(36 + data.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal("WAVEfmt "u8.ToArray(), bytes[8..16]);
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(SoundRecipes.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)));
        Assert.Equal(SoundRecipes.SampleRate * 2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(32)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)));
        Assert.Equal("data"u8.ToArray(), bytes[36..40]);
        Assert.Equal(data.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
        Assert.Equal(data, bytes[44..]);
    }
}
