using Godot;

namespace Game;

/// <summary>
/// Plays the sounds of a match (design 6: minimal sound). After every tick it reads the events
/// of the tick and plays what <see cref="EventSounds"/> makes of them; and it answers the local
/// player's own orders at once, before the match has run them, so that the delay between a click
/// and its tick is not felt.
/// </summary>
/// <remarks>
/// <para>
/// It reads the match and never changes it. Nothing it does is seen by the simulation: a match
/// runs the same with the sound on, off, or on a machine with no sound at all.
/// </para>
/// <para>
/// <b>Where the sounds come from.</b> None is a file. Each is worked out by
/// <see cref="SoundRecipes"/> when the match opens, which takes a few thousandths of a second.
/// </para>
/// <para>
/// <b>Many at once.</b> All the hits of one tick are one sound, and a cue that has just started
/// is not started again (<see cref="SoundMix"/>). A sound of the world is loudest where the
/// camera looks and faint far from it.
/// </para>
/// <para>
/// <b>The fog.</b> A sound is played only for what the local player may know of
/// (<see cref="SightOfTheView"/>).
/// </para>
/// <para>
/// <b>The player's own orders.</b> An order given by a click, on the map or wherever a piece
/// asks the <see cref="OrderTable"/>, and a command for the selection, as Stop, are answered
/// with <see cref="SoundCue.OrderGiven"/>. A click that no row of the table had an order for is
/// answered with <see cref="SoundCue.OrderRefused"/>, as is an order the match refuses a tick or
/// more later.
/// </para>
/// </remarks>
public partial class SoundPart : InterfacePart
{
    // The most sounds at once. With the gaps between two starts of a cue, a match needs fewer.
    private const int VoiceCount = 16;

    private readonly AudioStreamWav[] _streams = new AudioStreamWav[SoundCues.All.Count];
    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[VoiceCount];
    private readonly double[] _voiceStarted = new double[VoiceCount];
    private readonly int[] _asked = new int[SoundCues.All.Count];
    private readonly int[] _played = new int[SoundCues.All.Count];
    private readonly SoundMix _mix = new();
    private readonly List<SoundRequest> _requests = [];
    private readonly List<SoundPlay> _plays = [];

    private Func<SoundRequest, float> _nearness = null!;
    private EventSounds _events = null!;
    private SightOfTheView _sight = null!;
    private double _clock;

    /// <summary>
    /// The sound part of the match that is shown, for the smoke test, whose parts are handed the
    /// match and not its view. Null when no match is shown.
    /// </summary>
    internal static SoundPart? Latest { get; private set; }

    /// <summary>The view of the match this part plays for, for the smoke test.</summary>
    internal MatchView MatchView => View;

    /// <summary>The kinds of event from later pieces that this build has, and those it lacks.</summary>
    public SoundEventKinds Kinds { get; private set; } = null!;

    /// <summary>True when the build has a fog of war and the sounds follow it.</summary>
    public bool FollowsFog => _sight.HasFog;

    /// <summary>The number of times a cue was called for since the match began, by an event or by an order.</summary>
    public int Asked(SoundCue cue) => _asked[(int)cue];

    /// <summary>The number of times a cue was started since the match began. Never more than <see cref="Asked"/>.</summary>
    public int Played(SoundCue cue) => _played[(int)cue];

    /// <summary>How long a cue's sound is, in seconds.</summary>
    public double LengthOf(SoundCue cue) => _streams[(int)cue].GetLength();

    protected override void Attach()
    {
        SoundSettings.Load();

        foreach (var cue in SoundCues.All)
        {
            _streams[(int)cue] = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = SoundRecipes.SampleRate,
                Stereo = false,
                Data = SoundRecipes.ToPcm16(SoundRecipes.Make(cue)),
            };
        }

        for (var i = 0; i < VoiceCount; i++)
        {
            _voices[i] = new AudioStreamPlayer { Name = $"Voice{i}", Bus = SoundSettings.BusName };
            AddChild(_voices[i]);
        }

        _nearness = Nearness;
        Kinds = SoundEventKinds.OfThisBuild();
        _events = new EventSounds(View.Player, Kinds);
        _sight = SightOfTheView.Of(View, View.Player);
        if (_sight.Problem is { } problem)
        {
            GD.PushError($"Sound: {problem}");
        }

        View.Orders.Answered += row => Notice(row is null ? SoundCue.OrderRefused : SoundCue.OrderGiven);
        Frame.CommandGiven += _ => Notice(SoundCue.OrderGiven);
        Latest = this;

        if (OS.IsStdOutVerbose())
        {
            var waiting = Kinds.Missing.Count == 0 ? "none" : string.Join(", ", Kinds.Missing);
            GD.Print($"Sound: {SoundCues.All.Count} sounds made. Fog followed: {FollowsFog}. Kinds of event not in this build: {waiting}.");
        }
    }

    public override void _ExitTree()
    {
        if (Latest == this)
        {
            Latest = null;
        }

        Silence();
    }

    /// <summary>
    /// Stops every sound that is playing. A match that is left, or a game that quits, in the
    /// middle of a sound lets go of it here, so that the engine has less of it to report as
    /// left over at its exit.
    /// </summary>
    internal void Silence()
    {
        foreach (var voice in _voices)
        {
            voice?.Stop();
        }
    }

    public override void AfterTick()
    {
        _requests.Clear();
        _events.Collect(View.Match.Simulation.Events, _sight.Knows, _requests);
        if (_requests.Count == 0)
        {
            return;
        }

        _plays.Clear();
        _mix.Merge(_requests, _nearness, _plays);
        foreach (var play in _plays)
        {
            _asked[(int)play.Cue] += play.Count;
            if (_mix.TryStart(play.Cue, _clock))
            {
                Start(play.Cue, play.Gain);
            }
        }
    }

    public override void EveryFrame(double delta) => _clock += delta;

    /// <summary>Plays a cue once, whatever the match is doing, for a player who wants to hear it or to judge the volume.</summary>
    public void Audition(SoundCue cue) => Begin(cue, SoundCues.Gain(cue), pitch: 1f);

    /// <summary>
    /// Answers an order of the local player's that a piece sent to the match itself, past the
    /// order table and the frame's commands, as a panel with buttons of its own does. The piece
    /// finds this part among <see cref="InterfaceFrame.Parts"/> and calls it as the order goes.
    /// </summary>
    /// <param name="taken">False when the order could not be sent.</param>
    public void Acknowledge(bool taken = true) => Notice(taken ? SoundCue.OrderGiven : SoundCue.OrderRefused);

    // A sound for the local player alone, called for outside the events of a tick.
    private void Notice(SoundCue cue)
    {
        _asked[(int)cue]++;
        if (_mix.TryStart(cue, _clock))
        {
            Start(cue, SoundCues.Gain(cue));
        }
    }

    private void Start(SoundCue cue, float gain)
    {
        Begin(cue, gain, SoundMix.Pitch(cue, _played[(int)cue]));
        _played[(int)cue]++;
    }

    private void Begin(SoundCue cue, float gain, float pitch)
    {
        // A voice that is silent, or else the one that has played longest.
        var voice = 0;
        for (var i = 0; i < VoiceCount; i++)
        {
            if (!_voices[i].Playing)
            {
                voice = i;
                break;
            }

            if (_voiceStarted[i] < _voiceStarted[voice])
            {
                voice = i;
            }
        }

        var player = _voices[voice];
        player.Stream = _streams[(int)cue];
        player.VolumeDb = Mathf.LinearToDb(Mathf.Max(gain, 0.0001f));
        player.PitchScale = pitch;
        player.Play();
        _voiceStarted[voice] = _clock;

        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Sound: {cue} at {gain:0.00} of full, {_clock:0.00} seconds into the match.");
        }
    }

    private float Nearness(SoundRequest request)
    {
        var place = WorldView.ToWorld(request.Position);
        var away = new Vector2(place.X, place.Z).DistanceTo(View.Camera.Focus);
        return SoundMix.Nearness(away, View.Camera.Distance);
    }
}
