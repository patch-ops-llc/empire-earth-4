namespace Game;

/// <summary>One sound to start: what is left of a tick's requests for a cue.</summary>
/// <param name="Cue">The sound.</param>
/// <param name="Gain">How loud, from 0 to 1, before the player's setting.</param>
/// <param name="Count">The number of requests it stands for.</param>
public readonly record struct SoundPlay(SoundCue Cue, float Gain, int Count);

/// <summary>
/// Keeps many of one sound from piling into noise, in two steps. First, all the requests of one
/// tick for the same cue become one sound (<see cref="Merge"/>). Second, a cue that started less
/// than its gap ago is not started again (<see cref="TryStart"/>), which also holds when a frame
/// runs several ticks at once. It reads no clock: the caller says what time it is.
/// </summary>
public sealed class SoundMix
{
    /// <summary>A sound of the world this far from where the camera looks, or farther, is at its quietest.</summary>
    public const float Floor = 0.2f;

    // The loudest that many requests in one tick make a sound, beside a single one.
    private const float MostTogether = 1.4f;

    private readonly double[] _lastStart = new double[SoundCues.All.Count];
    private readonly double[] _gap = new double[SoundCues.All.Count];
    private readonly int[] _starts = new int[SoundCues.All.Count];
    private readonly float[] _loudest = new float[SoundCues.All.Count];
    private readonly int[] _count = new int[SoundCues.All.Count];

    public SoundMix() => Array.Fill(_lastStart, double.NegativeInfinity);

    /// <summary>
    /// How loud a sound of the world is by its place: full while it is about on screen, then
    /// falling evenly to <see cref="Floor"/>, where it stays. A fight elsewhere on the map is
    /// heard faintly and not lost.
    /// </summary>
    /// <param name="tilesAway">The distance on the ground from the point the camera looks at.</param>
    /// <param name="cameraDistance">How far the camera is from that point: the zoom. Farther out, more ground is near.</param>
    public static float Nearness(float tilesAway, float cameraDistance)
    {
        var near = 0.6f * cameraDistance;
        var far = near + 28f;
        if (tilesAway <= near)
        {
            return 1f;
        }

        return tilesAway >= far ? Floor : 1f - ((1f - Floor) * (tilesAway - near) / (far - near));
    }

    /// <summary>
    /// What the player's volume setting, from 0 to 1, does to every sound, as a factor from 0 to
    /// 1. The ear hears loudness in ratios, so the factor is the square of the setting: half way
    /// down the slider sounds about half as loud, where a plain factor of one half would not.
    /// </summary>
    public static float VolumeGain(float volume)
    {
        var clamped = Math.Clamp(volume, 0f, 1f);
        return clamped * clamped;
    }

    /// <summary>
    /// The pitch of a play of a cue, as a factor round 1: each play of a cue of the world is a
    /// little higher or lower than the last, within <see cref="SoundCues.PitchSpread"/>. The
    /// factor depends only on which play it is, so it needs no random numbers.
    /// </summary>
    /// <param name="play">The number of this play of the cue, counted from 0.</param>
    public static float Pitch(SoundCue cue, int play)
    {
        var spread = SoundCues.PitchSpread(cue);
        if (spread == 0f)
        {
            return 1f;
        }

        // A small hash of the two numbers, spread over -1 to 1.
        var mixed = unchecked((((uint)play + 1u) * 2654435761u) ^ (((uint)cue + 1u) * 40503u));
        mixed ^= mixed >> 15;
        var part = ((mixed & 0xFFFF) / 32767.5f) - 1f;
        return 1f + (spread * part);
    }

    /// <summary>
    /// Makes one sound of all the requests of a tick for the same cue: as loud as the nearest of
    /// them, and a little louder when there are many, never more than <see cref="MostTogether"/>
    /// times. The plays are added in the order of <see cref="SoundCues.All"/>.
    /// </summary>
    /// <param name="requests">The requests of one tick.</param>
    /// <param name="nearness">How loud a request is by its place, from 0 to 1. It is asked for cues of the world only.</param>
    public void Merge(IReadOnlyList<SoundRequest> requests, Func<SoundRequest, float> nearness, List<SoundPlay> plays)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(nearness);
        ArgumentNullException.ThrowIfNull(plays);

        Array.Clear(_loudest);
        Array.Clear(_count);
        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var index = (int)request.Cue;
            var loudness = SoundCues.IsOfTheWorld(request.Cue) ? Math.Clamp(nearness(request), 0f, 1f) : 1f;
            _loudest[index] = Math.Max(_loudest[index], loudness);
            _count[index]++;
        }

        foreach (var cue in SoundCues.All)
        {
            var count = _count[(int)cue];
            if (count > 0)
            {
                var together = Math.Min(MostTogether, 1f + (0.1f * MathF.Log2(count)));
                plays.Add(new SoundPlay(cue, Math.Min(1f, SoundCues.Gain(cue) * _loudest[(int)cue] * together), count));
            }
        }
    }

    /// <summary>
    /// How long a cue stays silent after a start of it, in seconds. For a notice it is
    /// <see cref="SoundCues.Gap"/>. For a sound of the world it is between that and twice that,
    /// different after every start, so that a long fight is not a drumbeat: with one gap its
    /// hits would come at one even pace for as long as it lasts. Like <see cref="Pitch"/>, it
    /// depends only on which start it is.
    /// </summary>
    /// <param name="start">The number of the start the gap follows, counted from 0.</param>
    public static double GapAfter(SoundCue cue, int start)
    {
        var gap = SoundCues.Gap(cue);
        if (!SoundCues.IsOfTheWorld(cue))
        {
            return gap;
        }

        var mixed = unchecked((((uint)start + 1u) * 2246822519u) ^ (((uint)cue + 1u) * 3266489917u));
        mixed ^= mixed >> 13;
        return gap * (1.0 + ((mixed & 0xFFFF) / 65536.0));
    }

    /// <summary>
    /// True when a cue asked for now would start: its gap since its last start has passed. It
    /// starts nothing and changes nothing.
    /// </summary>
    /// <param name="now">The time, in seconds, on the clock <see cref="TryStart"/> is given.</param>
    public bool IsFree(SoundCue cue, double now) => now - _lastStart[(int)cue] >= _gap[(int)cue];

    /// <summary>
    /// Asks to start a cue now. False when it last started less than its gap ago
    /// (<see cref="GapAfter"/>): the sound is then left out.
    /// </summary>
    /// <param name="now">The time, in seconds, on any clock that never runs backward.</param>
    public bool TryStart(SoundCue cue, double now)
    {
        var index = (int)cue;
        if (!IsFree(cue, now))
        {
            return false;
        }

        _lastStart[index] = now;
        _gap[index] = GapAfter(cue, _starts[index]);
        _starts[index] = unchecked(_starts[index] + 1);
        return true;
    }
}
