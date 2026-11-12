namespace Game;

/// <summary>
/// Makes every sound of a match from nothing but arithmetic: sine tones that glide and die away,
/// and noise from a small generator of its own, passed through a simple filter. No sound is
/// recorded, bought, licensed or downloaded, and the game ships no sound file: each
/// <see cref="SoundCue"/> is worked out here when a match opens.
/// </summary>
/// <remarks>
/// <para>
/// A sound is a row of samples from -1 to 1, one channel, <see cref="SampleRate"/> of them a
/// second. Every part of a sound rises from nothing and falls back to nothing, so no sound
/// starts or ends with a click, and each is scaled so that its loudest sample is at
/// <see cref="Peak"/>. How loud it is in a match is the business of <see cref="SoundCues.Gain"/>.
/// </para>
/// <para>
/// The noise draws on nothing outside this file, and nothing here touches the simulation or its
/// random numbers. The same build makes the same samples every time. Two machines may differ
/// in the last digit of a sample, as their sine functions do, which nobody can hear.
/// </para>
/// </remarks>
public static class SoundRecipes
{
    /// <summary>The number of samples in a second of sound.</summary>
    public const int SampleRate = 44100;

    /// <summary>The loudest sample of every sound, as a part of full scale.</summary>
    public const float Peak = 0.8f;

    // Every part of a sound falls to nothing over this long at its end.
    private const double Tail = 0.006;

    // How strong the overtones of a note are beside its ground tone, which gives it its color.
    private static readonly double[] Pure = [1.0];
    private static readonly double[] Soft = [1.0, 0.25, 0.08];
    private static readonly double[] Bell = [1.0, 0.4, 0.2, 0.1];
    private static readonly double[] Brass = [1.0, 0.6, 0.45, 0.3, 0.2, 0.12];
    private static readonly double[] Hollow = [1.0, 0.0, 0.5, 0.0, 0.3, 0.0, 0.15];

    /// <summary>The samples of a cue.</summary>
    public static float[] Make(SoundCue cue)
    {
        var take = cue switch
        {
            SoundCue.MeleeHit => MeleeHit(),
            SoundCue.ShotHit => ShotHit(),
            SoundCue.UnitDied => UnitDied(),
            SoundCue.StructureFinished => StructureFinished(),
            SoundCue.StructureLost => StructureLost(),
            SoundCue.ResearchFinished => ResearchFinished(),
            SoundCue.EpochReached => EpochReached(),
            SoundCue.OrderGiven => OrderGiven(),
            SoundCue.OrderRefused => OrderRefused(),
            _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, "There is no recipe for this cue."),
        };
        return take.Finish();
    }

    /// <summary>
    /// The samples as the bytes of 16-bit whole numbers, low byte first, which is what a sound
    /// file and the engine's player hold.
    /// </summary>
    public static byte[] ToPcm16(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(value & 0xFF);
            bytes[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    /// Writes samples as a sound file of the plainest kind: WAV, one channel, 16 bits. The game
    /// reads no such file. It is for listening to the sounds outside the game.
    /// </summary>
    public static void WriteWave(Stream file, ReadOnlySpan<float> samples)
    {
        ArgumentNullException.ThrowIfNull(file);
        var data = ToPcm16(samples);
        using var writer = new BinaryWriter(file, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
    }

    // A clash of metal: a short burst of bright noise, three ringing tones that are not in tune
    // with one another, as struck metal is not, and a low knock under them.
    private static Take MeleeHit()
    {
        var take = new Take(0.16);
        take.Noise(start: 0, length: 0.07, level: 1.0, rise: 0.001, fade: 0.018, below: 7000, above: 1500, seed: 11);
        take.Tone(start: 0, length: 0.16, from: 1870, to: 1850, level: 0.5, rise: 0.001, fade: 0.035, Pure);
        take.Tone(start: 0, length: 0.16, from: 2790, to: 2760, level: 0.35, rise: 0.001, fade: 0.028, Pure);
        take.Tone(start: 0, length: 0.16, from: 4130, to: 4100, level: 0.2, rise: 0.001, fade: 0.02, Pure);
        take.Tone(start: 0, length: 0.09, from: 190, to: 120, level: 0.6, rise: 0.002, fade: 0.025, Pure);
        return take;
    }

    // A dull thud: a low tone that drops fast, with a short smack of muffled noise on its start.
    private static Take ShotHit()
    {
        var take = new Take(0.2);
        take.Tone(start: 0, length: 0.2, from: 260, to: 80, level: 1.0, rise: 0.002, fade: 0.05, [1.0, 0.45, 0.2]);
        take.Noise(start: 0, length: 0.06, level: 0.9, rise: 0.001, fade: 0.014, below: 2500, above: 300, seed: 23);
        return take;
    }

    // A falling tone, from the A above middle C down almost two octaves in half a second, over
    // a soft thump of low noise.
    private static Take UnitDied()
    {
        var take = new Take(0.5);
        take.Tone(start: 0, length: 0.5, from: 440, to: 120, level: 0.8, rise: 0.01, fade: 0.16, [1.0, 0.35, 0.15]);
        take.Noise(start: 0.03, length: 0.4, level: 0.35, rise: 0.02, fade: 0.12, below: 900, above: 0, seed: 37);
        return take;
    }

    // Two bell notes that rise a fifth, C and then G: something is ready.
    private static Take StructureFinished()
    {
        var take = new Take(0.75);
        take.Tone(start: 0, length: 0.45, from: 523.25, to: 523.25, level: 0.8, rise: 0.004, fade: 0.14, Bell);
        take.Tone(start: 0.14, length: 0.6, from: 783.99, to: 783.99, level: 0.9, rise: 0.004, fade: 0.2, Bell);
        return take;
    }

    // A collapse: a crack of noise, a rumble under it that lasts, a tone that sinks, and three
    // small bursts after it, as rubble that comes down late. The rumble is in two bands, because
    // the small speakers of a laptop give back little below 150 hertz: the low band is the
    // weight of it on speakers that can, the middle band is what every speaker plays.
    private static Take StructureLost()
    {
        var take = new Take(1.1);
        take.Noise(start: 0, length: 1.1, level: 0.6, rise: 0.005, fade: 0.3, below: 420, above: 0, seed: 53);
        take.Noise(start: 0, length: 0.9, level: 0.9, rise: 0.005, fade: 0.22, below: 1400, above: 250, seed: 73);
        take.Noise(start: 0, length: 0.3, level: 1.2, rise: 0.001, fade: 0.09, below: 3000, above: 400, seed: 59);
        take.Tone(start: 0, length: 1.0, from: 150, to: 45, level: 0.5, rise: 0.005, fade: 0.28, [1.0, 0.5, 0.25]);
        take.Noise(start: 0.18, length: 0.07, level: 0.5, rise: 0.001, fade: 0.018, below: 1800, above: 300, seed: 61);
        take.Noise(start: 0.33, length: 0.07, level: 0.42, rise: 0.001, fade: 0.018, below: 1800, above: 300, seed: 67);
        take.Noise(start: 0.52, length: 0.07, level: 0.32, rise: 0.001, fade: 0.018, below: 1800, above: 300, seed: 71);
        return take;
    }

    // Three quick notes up a chord of E, the last one held with a faint octave above it.
    private static Take ResearchFinished()
    {
        var take = new Take(0.8);
        take.Tone(start: 0, length: 0.2, from: 659.25, to: 659.25, level: 0.7, rise: 0.004, fade: 0.1, Soft);
        take.Tone(start: 0.1, length: 0.2, from: 830.61, to: 830.61, level: 0.7, rise: 0.004, fade: 0.1, Soft);
        take.Tone(start: 0.2, length: 0.6, from: 987.77, to: 987.77, level: 0.8, rise: 0.004, fade: 0.25, Soft);
        take.Tone(start: 0.2, length: 0.6, from: 1975.53, to: 1975.53, level: 0.2, rise: 0.004, fade: 0.2, Pure);
        return take;
    }

    // A fanfare with the color of brass: G, G, C, and then a held chord of C with E on top.
    private static Take EpochReached()
    {
        var take = new Take(1.7);
        take.Tone(start: 0, length: 0.16, from: 392.0, to: 392.0, level: 0.8, rise: 0.015, fade: 0.4, Brass);
        take.Tone(start: 0.18, length: 0.16, from: 392.0, to: 392.0, level: 0.8, rise: 0.015, fade: 0.4, Brass);
        take.Tone(start: 0.36, length: 0.3, from: 523.25, to: 523.25, level: 0.85, rise: 0.015, fade: 0.5, Brass);
        take.Tone(start: 0.68, length: 1.0, from: 659.25, to: 659.25, level: 0.9, rise: 0.015, fade: 0.6, Brass);
        take.Tone(start: 0.68, length: 1.0, from: 523.25, to: 523.25, level: 0.5, rise: 0.015, fade: 0.6, Brass);
        take.Tone(start: 0.68, length: 1.0, from: 783.99, to: 783.99, level: 0.4, rise: 0.015, fade: 0.6, Brass);
        return take;
    }

    // One short high note that bends upward: yes.
    private static Take OrderGiven()
    {
        var take = new Take(0.09);
        take.Tone(start: 0, length: 0.09, from: 880, to: 1040, level: 1.0, rise: 0.003, fade: 0.03, [1.0, 0.2]);
        return take;
    }

    // Two hollow notes, the second lower: no. They are the G and the D above middle C, low
    // beside the click of an order taken and still high enough for a small speaker to play.
    private static Take OrderRefused()
    {
        var take = new Take(0.26);
        take.Tone(start: 0, length: 0.1, from: 392.0, to: 392.0, level: 1.0, rise: 0.004, fade: 0.2, Hollow);
        take.Tone(start: 0.13, length: 0.13, from: 293.66, to: 293.66, level: 1.0, rise: 0.004, fade: 0.2, Hollow);
        return take;
    }

    // The samples of one sound while it is put together.
    private sealed class Take(double seconds)
    {
        private readonly float[] _samples = new float[(int)Math.Ceiling(seconds * SampleRate)];

        /// <summary>
        /// Adds a tone with overtones. Its pitch glides from one frequency to the other, evenly
        /// to the ear, and it is loudest just after its start: it rises over <paramref name="rise"/>
        /// seconds and then falls to about a third in every <paramref name="fade"/> seconds.
        /// </summary>
        /// <param name="partials">The strength of the ground tone and of each overtone above it, in order.</param>
        public void Tone(double start, double length, double from, double to, double level, double rise, double fade, ReadOnlySpan<double> partials)
        {
            var first = (int)(start * SampleRate);
            var count = Math.Min((int)(length * SampleRate), _samples.Length - first);
            var phase = 0.0;
            for (var i = 0; i < count; i++)
            {
                var time = (double)i / SampleRate;
                var pitch = from * Math.Pow(to / from, time / length);
                phase += Math.Tau * pitch / SampleRate;

                var sample = 0.0;
                for (var partial = 0; partial < partials.Length; partial++)
                {
                    // An overtone the sample rate cannot carry would come back as a false tone.
                    if (pitch * (partial + 1) < SampleRate * 0.45)
                    {
                        sample += partials[partial] * Math.Sin(phase * (partial + 1));
                    }
                }

                _samples[first + i] += (float)(sample * level * Envelope(time, length, rise, fade));
            }
        }

        /// <summary>
        /// Adds noise, shaped like a tone is. Only what lies below one frequency is kept, and,
        /// when <paramref name="above"/> is not 0, above another: a low limit alone gives a
        /// rumble, the two together a hiss or a crack.
        /// </summary>
        /// <param name="seed">Where the generator starts. Two bursts with different seeds are different noise.</param>
        public void Noise(double start, double length, double level, double rise, double fade, double below, double above, uint seed)
        {
            var first = (int)(start * SampleRate);
            var count = Math.Min((int)(length * SampleRate), _samples.Length - first);
            var keepBelow = 1.0 - Math.Exp(-Math.Tau * below / SampleRate);
            var keepAbove = above > 0 ? 1.0 - Math.Exp(-Math.Tau * above / SampleRate) : 0.0;

            // The narrower the band that is kept, the quieter the noise comes out. This brings
            // it back to about the strength of a tone of the same level, whatever the band.
            var kept = (keepBelow / (2.0 - keepBelow)) - (keepAbove / (2.0 - keepAbove));
            level *= 0.6 / Math.Sqrt(Math.Max(kept, 0.0001));

            var low = 0.0;
            var lower = 0.0;
            var state = seed;
            for (var i = 0; i < count; i++)
            {
                // A linear congruential generator. It wraps on purpose.
                state = unchecked((state * 1664525u) + 1013904223u);
                var white = ((state >> 8) / (double)(1 << 24) * 2.0) - 1.0;
                low += keepBelow * (white - low);
                lower += keepAbove * (low - lower);

                var time = (double)i / SampleRate;
                _samples[first + i] += (float)((low - lower) * level * Envelope(time, length, rise, fade));
            }
        }

        /// <summary>The samples, scaled so that the loudest is at <see cref="Peak"/>.</summary>
        public float[] Finish()
        {
            var loudest = 0f;
            foreach (var sample in _samples)
            {
                loudest = Math.Max(loudest, Math.Abs(sample));
            }

            if (loudest > 0f)
            {
                var scale = Peak / loudest;
                for (var i = 0; i < _samples.Length; i++)
                {
                    _samples[i] *= scale;
                }
            }

            return _samples;
        }

        private static double Envelope(double time, double length, double rise, double fade) =>
            Math.Min(1.0, time / rise) * Math.Exp(-time / fade) * Math.Clamp((length - time) / Tail, 0.0, 1.0);
    }
}
