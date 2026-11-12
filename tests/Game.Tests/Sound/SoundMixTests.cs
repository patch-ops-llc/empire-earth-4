using Sim.Numerics;

namespace Game.Tests.Sound;

/// <summary>
/// How many of one sound become few (<see cref="SoundMix"/>): the requests of a tick merge, a
/// cue keeps its gap, and a sound of the world is quieter far from where the camera looks.
/// </summary>
public class SoundMixTests
{
    private const float Tolerance = 0.0001f;

    private static SoundRequest At(SoundCue cue, int x = 0, int y = 0) => new(cue, FixedVector2.FromInts(x, y));

    private static List<SoundPlay> Merge(SoundMix mix, Func<SoundRequest, float> nearness, params SoundRequest[] requests)
    {
        var plays = new List<SoundPlay>();
        mix.Merge(requests, nearness, plays);
        return plays;
    }

    [Fact]
    public void AllTheHitsOfOneTickAreOneSound()
    {
        var hits = Enumerable.Repeat(At(SoundCue.MeleeHit), 40).ToArray();

        var plays = Merge(new SoundMix(), _ => 1f, hits);

        var play = Assert.Single(plays);
        Assert.Equal(SoundCue.MeleeHit, play.Cue);
        Assert.Equal(40, play.Count);
    }

    [Fact]
    public void ManyAtOnceAreALittleLouderThanOneAndNeverMuch()
    {
        var one = Assert.Single(Merge(new SoundMix(), _ => 1f, At(SoundCue.MeleeHit)));
        var four = Assert.Single(Merge(new SoundMix(), _ => 1f, [.. Enumerable.Repeat(At(SoundCue.MeleeHit), 4)]));
        var thousand = Assert.Single(Merge(new SoundMix(), _ => 1f, [.. Enumerable.Repeat(At(SoundCue.MeleeHit), 1000)]));

        Assert.Equal(SoundCues.Gain(SoundCue.MeleeHit), one.Gain, Tolerance);
        Assert.True(four.Gain > one.Gain);
        Assert.True(thousand.Gain >= four.Gain);
        Assert.True(thousand.Gain <= one.Gain * 1.4f + Tolerance);
        Assert.True(thousand.Gain <= 1f);
    }

    [Fact]
    public void DifferentSoundsOfOneTickStayApartAndComeInTheOrderOfTheCues()
    {
        var plays = Merge(
            new SoundMix(),
            _ => 1f,
            At(SoundCue.OrderRefused),
            At(SoundCue.UnitDied),
            At(SoundCue.ShotHit),
            At(SoundCue.UnitDied),
            At(SoundCue.MeleeHit));

        Assert.Equal([SoundCue.MeleeHit, SoundCue.ShotHit, SoundCue.UnitDied, SoundCue.OrderRefused], plays.Select(play => play.Cue));
        Assert.Equal([1, 1, 2, 1], plays.Select(play => play.Count));
    }

    [Fact]
    public void AMergedSoundIsAsLoudAsTheNearestOfItsRequests()
    {
        // Nearness by the x of the place: the request at x 9 is the near one.
        var plays = Merge(new SoundMix(), request => request.Position.X.Raw / (float)Fixed.RawOne / 10f, At(SoundCue.UnitDied, x: 2), At(SoundCue.UnitDied, x: 9));

        var play = Assert.Single(plays);
        var two = 1f + (0.1f * MathF.Log2(2));
        Assert.Equal(SoundCues.Gain(SoundCue.UnitDied) * 0.9f * two, play.Gain, Tolerance);
    }

    [Fact]
    public void ANoticeIsAsLoudWhereverTheCameraIs()
    {
        var notices = SoundCues.All.Where(cue => !SoundCues.IsOfTheWorld(cue)).ToArray();

        var plays = Merge(new SoundMix(), _ => throw new InvalidOperationException("A notice has no place."), [.. notices.Select(cue => At(cue))]);

        Assert.Equal(notices, plays.Select(play => play.Cue));
        Assert.All(plays, play => Assert.Equal(SoundCues.Gain(play.Cue), play.Gain, Tolerance));
    }

    [Fact]
    public void ATickWithNothingToPlayGivesNoSound()
    {
        Assert.Empty(Merge(new SoundMix(), _ => 1f));
    }

    [Fact]
    public void ACueThatHasJustStartedIsNotStartedAgainUntilItsGapHasPassed()
    {
        var mix = new SoundMix();
        var gap = SoundCues.Gap(SoundCue.OrderGiven);

        Assert.True(mix.TryStart(SoundCue.OrderGiven, 0.0));
        Assert.False(mix.TryStart(SoundCue.OrderGiven, 0.0));
        Assert.False(mix.TryStart(SoundCue.OrderGiven, gap * 0.9));
        Assert.True(mix.TryStart(SoundCue.OrderGiven, gap));
        Assert.False(mix.TryStart(SoundCue.OrderGiven, gap * 1.5));
        Assert.True(mix.TryStart(SoundCue.OrderGiven, gap * 2.5));
    }

    [Fact]
    public void ASoundOfTheWorldKeepsTheGapThatFollowsEachOfItsStarts()
    {
        var mix = new SoundMix();
        var now = 0.0;
        for (var start = 0; start < 50; start++)
        {
            Assert.True(mix.TryStart(SoundCue.MeleeHit, now));

            var gap = SoundMix.GapAfter(SoundCue.MeleeHit, start);
            Assert.False(mix.TryStart(SoundCue.MeleeHit, now + (gap * 0.5)));
            Assert.False(mix.TryStart(SoundCue.MeleeHit, now + (gap * 0.98)));
            now += gap * 1.01;
        }
    }

    [Fact]
    public void TheGapOfANoticeIsAlwaysTheSameAndTheGapOfASoundOfTheWorldIsNot()
    {
        foreach (var cue in SoundCues.All)
        {
            var least = SoundCues.Gap(cue);
            var gaps = Enumerable.Range(0, 200).Select(start => SoundMix.GapAfter(cue, start)).ToArray();

            Assert.All(gaps, gap => Assert.InRange(gap, least, least * 2.0));
            if (SoundCues.IsOfTheWorld(cue))
            {
                // No even pace: the gaps are spread over the whole range, and no two in a row are equal.
                Assert.True(gaps.Distinct().Count() > 100);
                Assert.Contains(gaps, gap => gap < least * 1.25);
                Assert.Contains(gaps, gap => gap > least * 1.75);
                Assert.InRange(gaps.Average(), least * 1.35, least * 1.65);
                Assert.All(Enumerable.Range(1, gaps.Length - 1), i => Assert.NotEqual(gaps[i - 1], gaps[i]));
            }
            else
            {
                Assert.All(gaps, gap => Assert.Equal(least, gap));
            }
        }

        Assert.Equal(SoundMix.GapAfter(SoundCue.ShotHit, 31), SoundMix.GapAfter(SoundCue.ShotHit, 31));
    }

    [Fact]
    public void EachCueKeepsAGapOfItsOwn()
    {
        var mix = new SoundMix();

        Assert.All(SoundCues.All, cue => Assert.True(mix.TryStart(cue, 0.0)));
        Assert.All(SoundCues.All, cue => Assert.False(mix.TryStart(cue, 0.01)));
    }

    [Fact]
    public void SeveralTicksRunInOneFrameStartACueOnce()
    {
        // A frame that catches up runs its ticks at one moment of the clock.
        var mix = new SoundMix();
        var started = 0;
        for (var tick = 0; tick < 20; tick++)
        {
            started += mix.TryStart(SoundCue.ShotHit, 3.0) ? 1 : 0;
        }

        Assert.Equal(1, started);
    }

    [Fact]
    public void ALongFightWithHitsInEveryTickStartsAFewSoundsOfAHitASecond()
    {
        var mix = new SoundMix();
        var started = 0;
        const int Seconds = 20;
        for (var tick = 0; tick < Seconds * 20; tick++)
        {
            var plays = Merge(mix, _ => 1f, [.. Enumerable.Repeat(At(SoundCue.MeleeHit), 25)]);
            foreach (var play in plays)
            {
                started += mix.TryStart(play.Cue, tick / 20.0) ? 1 : 0;
            }
        }

        // 10,000 hits were called for. The gap is a tenth to a fifth of a second, and a start
        // waits for the next tick, so between four and ten a second start.
        Assert.InRange(started, Seconds * 4, Seconds * 10);
    }

    [Fact]
    public void ASoundOfTheWorldIsFullWhereTheCameraLooksAndFaintFarAway()
    {
        const float Zoom = 36f;

        Assert.Equal(1f, SoundMix.Nearness(0f, Zoom));
        Assert.Equal(1f, SoundMix.Nearness(15f, Zoom));
        Assert.Equal(SoundMix.Floor, SoundMix.Nearness(90f, Zoom));
        Assert.Equal(SoundMix.Floor, SoundMix.Nearness(10_000f, Zoom));
        Assert.True(SoundMix.Floor > 0f);

        var last = 1f;
        for (var tiles = 0f; tiles <= 140f; tiles += 0.5f)
        {
            var now = SoundMix.Nearness(tiles, Zoom);
            Assert.InRange(now, SoundMix.Floor, 1f);
            Assert.True(now <= last);
            last = now;
        }
    }

    [Fact]
    public void ZoomedOutMoreOfTheGroundIsNear()
    {
        for (var tiles = 0f; tiles <= 140f; tiles += 5f)
        {
            Assert.True(SoundMix.Nearness(tiles, 80f) >= SoundMix.Nearness(tiles, 12f));
        }

        Assert.True(SoundMix.Nearness(30f, 80f) > SoundMix.Nearness(30f, 12f));
    }

    [Fact]
    public void ANoticeKeepsItsPitchAndASoundOfTheWorldWandersALittle()
    {
        foreach (var cue in SoundCues.All)
        {
            var spread = SoundCues.PitchSpread(cue);
            var pitches = Enumerable.Range(0, 200).Select(play => SoundMix.Pitch(cue, play)).ToArray();

            Assert.All(pitches, pitch => Assert.InRange(pitch, 1f - spread - Tolerance, 1f + spread + Tolerance));
            if (SoundCues.IsOfTheWorld(cue))
            {
                Assert.True(spread > 0f);
                Assert.True(pitches.Distinct().Count() > 100);
                Assert.Contains(pitches, pitch => pitch > 1f + (spread / 2f));
                Assert.Contains(pitches, pitch => pitch < 1f - (spread / 2f));
            }
            else
            {
                Assert.All(pitches, pitch => Assert.Equal(1f, pitch));
            }
        }
    }

    [Fact]
    public void TheSamePlayOfACueHasTheSamePitchEveryTime()
    {
        Assert.Equal(SoundMix.Pitch(SoundCue.MeleeHit, 17), SoundMix.Pitch(SoundCue.MeleeHit, 17));
        Assert.Equal(SoundMix.Pitch(SoundCue.UnitDied, int.MaxValue), SoundMix.Pitch(SoundCue.UnitDied, int.MaxValue));
    }

    [Fact]
    public void TheVolumeSettingRunsFromSilenceToFullAndHalfWayIsAQuarter()
    {
        Assert.Equal(0f, SoundMix.VolumeGain(0f));
        Assert.Equal(1f, SoundMix.VolumeGain(1f));
        Assert.Equal(0.25f, SoundMix.VolumeGain(0.5f), Tolerance);
        Assert.Equal(0f, SoundMix.VolumeGain(-3f));
        Assert.Equal(1f, SoundMix.VolumeGain(7f));

        var last = 0f;
        for (var step = 0; step <= 20; step++)
        {
            var now = SoundMix.VolumeGain(step / 20f);
            Assert.True(now >= last);
            last = now;
        }
    }

    [Fact]
    public void EveryCueHasANameALoudnessAndAGap()
    {
        Assert.Equal(9, SoundCues.All.Count);
        Assert.Equal(SoundCues.All.Count, SoundCues.All.Select(SoundCues.Name).Distinct().Count());
        foreach (var cue in SoundCues.All)
        {
            Assert.NotEqual(cue.ToString(), SoundCues.Name(cue));
            Assert.InRange(SoundCues.Gain(cue), 0.1f, 1f);
            Assert.InRange(SoundCues.Gap(cue), 0.05, 2.0);
        }

        // The sounds heard most often are the quietest.
        Assert.True(SoundCues.Gain(SoundCue.OrderGiven) < SoundCues.Gain(SoundCue.EpochReached));
        Assert.True(SoundCues.Gain(SoundCue.MeleeHit) < SoundCues.Gain(SoundCue.StructureLost));
    }
}
