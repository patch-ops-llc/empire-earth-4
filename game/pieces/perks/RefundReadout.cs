using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The refund of Reserves in the top bar (design 2.3): when a unit of the local player's with
/// Reserves dies, the number of Materials turns to the color of the Manufacturing track for a
/// moment, and the Materials that came back are shown under it.
/// </summary>
/// <remarks>
/// It reads the refunds that name the local player and no others: the readout is the player's
/// own stockpile, whatever else the screen shows. Refunds that follow each other closely are
/// added up, so that a lost group reads as one sum.
/// </remarks>
public partial class RefundReadout : InterfacePart
{
    private const double Seconds = 2.4;
    private const double FadeSeconds = 0.8;

    private Label? _number;
    private Label _gain = null!;
    private Color _color;
    private long _shown;
    private double _left;

    /// <summary>The number of ticks in which the readout was made to show a refund since the match began.</summary>
    public int Reactions { get; private set; }

    /// <summary>The Materials shown under the readout now, or 0 while nothing is shown.</summary>
    public long MaterialsShown => _left > 0.0 ? _shown : 0;

    protected override void Attach()
    {
        var data = View.Match.Data;
        _color = TrackColors.Of(data, data.TrackRoles.Manufacturing);
        _number = Frame.Parts.OfType<ResourceReadout>().FirstOrDefault()?.MaterialsNumber;

        // A child of the number that no container lays out: it hangs under the number and moves
        // nothing in the bar.
        _gain = new Label { Name = "Refund", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _gain.AddThemeColorOverride("font_color", _color);
        _gain.AddThemeColorOverride("font_outline_color", PerkGlyphs.Rim);
        _gain.AddThemeConstantOverride("outline_size", 5);
        (_number as Node ?? this).AddChild(_gain);
    }

    public override void AfterTick()
    {
        long refunded = 0;
        foreach (var happened in View.Match.Simulation.Events)
        {
            if (happened.Kind == SimEventKind.ReservesRefunded && happened.Player == View.Player)
            {
                refunded += happened.Amount;
            }
        }

        if (refunded == 0)
        {
            return;
        }

        Reactions++;
        _shown = _left > 0.0 ? _shown + refunded : refunded;
        _left = Seconds;
        _gain.Text = RefundNotes.Words(_shown);
        _gain.Visible = true;
    }

    public override void EveryFrame(double delta)
    {
        if (_left <= 0.0)
        {
            return;
        }

        _left -= delta;
        if (_left <= 0.0)
        {
            _gain.Visible = false;
            if (_number is not null)
            {
                _number.SelfModulate = Colors.White;
            }

            return;
        }

        var strength = (float)Math.Min(1.0, _left / FadeSeconds);
        _gain.Modulate = new Color(1f, 1f, 1f, strength);
        if (_number is not null)
        {
            _gain.Position = new Vector2(0f, _number.Size.Y - 6f);
            _number.SelfModulate = Colors.White.Lerp(_color, strength);
        }
    }
}
