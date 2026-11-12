using System.Globalization;
using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The refund of Reserves in the world (design 2.3): where a unit with Reserves fell, the
/// Materials its owner got back for it, as a number in the color of the Manufacturing track that
/// rises a little and fades.
/// </summary>
/// <remarks>
/// <para>
/// A refund is known to the player who gets it and to nobody else (design 5.3), so the number is
/// shown for the local player's own units only: another player's unit dies in sight like any
/// other, and nothing says what its owner got back. With the development switch that lifts the
/// fog, which shows what every player knows, the numbers of every player are shown.
/// </para>
/// <para>
/// The number is as large on the screen at every zoom, since a number that shrinks with the
/// world cannot be read at the widest. Units that fall close together in one tick share one
/// number, the sum, so that a shell into a group gives one number that can be read and not
/// several on top of each other.
/// </para>
/// </remarks>
public partial class RefundNotes : WorldLayer
{
    /// <summary>The most numbers shown at once. A fight that refunds more often drops the oldest first.</summary>
    public const int Limit = 24;

    /// <summary>Units that fall within this many tiles of each other in one tick share a number.</summary>
    public const float SharedWithin = 1.5f;

    private const double Seconds = 1.8;

    // The size of the font the number is drawn with, and the size it has on the screen, in the
    // interface's units: a window 720 of them high.
    private const int FontSize = 40;
    private const float SizeOnScreen = 19f;
    private const float InterfaceHeight = 720f;

    // How far the number rises in its life, in pixels of its own font.
    private const float Rise = 46f;

    private readonly List<Note> _notes = [];
    private Color _color;

    /// <summary>The number of refunds shown since the match began.</summary>
    public int RefundsShown { get; private set; }

    /// <summary>The Materials of all refunds shown since the match began.</summary>
    public long MaterialsShown { get; private set; }

    /// <summary>The number of numbers on the screen now.</summary>
    public int NoteCount => _notes.Count;

    protected override void Attach()
    {
        var data = View.Match.Data;
        _color = TrackColors.Of(data, data.TrackRoles.Manufacturing);
    }

    public override void AfterTick()
    {
        var match = View.Match;
        var tick = match.World.Tick;
        foreach (var happened in match.Simulation.Events)
        {
            if (happened.Kind != SimEventKind.ReservesRefunded || !View.Sight.Knows(happened))
            {
                continue;
            }

            RefundsShown++;
            MaterialsShown += happened.Amount;
            var place = WorldView.ToWorld(happened.Position);
            if (_notes.Find(other => other.Tick == tick && other.At.DistanceTo(place) <= SharedWithin) is { } shared)
            {
                shared.Materials += happened.Amount;
                shared.Label.Text = Words(shared.Materials);
                continue;
            }

            if (_notes.Count >= Limit)
            {
                _notes[0].Label.QueueFree();
                _notes.RemoveAt(0);
            }

            _notes.Add(new Note(NewLabel(place, happened.Amount), place, tick) { Materials = happened.Amount });
        }
    }

    public override void EveryFrame(double delta)
    {
        if (_notes.Count == 0 || GetViewport().GetCamera3D() is not { } camera)
        {
            return;
        }

        // A label of fixed size is drawn as if it stood one tile before the camera, where a tile
        // is this many of the interface's units.
        var pixelSize = SizeOnScreen / FontSize / (InterfaceHeight / (2f * Mathf.Tan(Mathf.DegToRad(camera.Fov) / 2f)));
        for (var i = _notes.Count - 1; i >= 0; i--)
        {
            var note = _notes[i];
            note.Age += delta;
            if (note.Age >= Seconds)
            {
                note.Label.QueueFree();
                _notes.RemoveAt(i);
                continue;
            }

            // It rises fast and slows down, and fades in its second half.
            var gone = (float)(note.Age / Seconds);
            var alpha = Mathf.Min(1f, 2f * (1f - gone));
            note.Label.PixelSize = pixelSize;
            note.Label.Offset = new Vector2(0f, Rise * (1f - Mathf.Pow(1f - gone, 2f)));
            note.Label.Modulate = new Color(_color.R, _color.G, _color.B, alpha);
            note.Label.OutlineModulate = new Color(PerkGlyphs.Rim.R, PerkGlyphs.Rim.G, PerkGlyphs.Rim.B, alpha);
        }
    }

    /// <summary>A refund as the number says it: "+12".</summary>
    public static string Words(long materials) => string.Create(CultureInfo.InvariantCulture, $"+{materials:N0}");

    private Label3D NewLabel(Vector3 place, long materials)
    {
        var label = new Label3D
        {
            Name = "Refund",
            Text = Words(materials),
            Position = place + (Vector3.Up * (PlaceholderMeshes.UnitHeight + 0.3f)),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            NoDepthTest = true,
            Shaded = false,
            FontSize = FontSize,
            OutlineSize = 12,
            RenderPriority = 12,
            OutlineRenderPriority = 11,
            Modulate = _color,
            OutlineModulate = PerkGlyphs.Rim,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(label);
        return label;
    }

    private sealed class Note(Label3D label, Vector3 at, int tick)
    {
        public Label3D Label { get; } = label;

        /// <summary>Where the first unit of the number fell, on the ground.</summary>
        public Vector3 At { get; } = at;

        /// <summary>The tick of the refund.</summary>
        public int Tick { get; } = tick;

        public long Materials { get; set; }

        public double Age { get; set; }
    }
}
