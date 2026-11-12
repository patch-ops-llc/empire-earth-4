using System.Globalization;
using Godot;
using Sim.Economy;

namespace Game;

/// <summary>
/// The left of the top bar: the local player's Materials and Energy, and the supply in use beside
/// the cap (design 4.1).
/// </summary>
/// <remarks>
/// A stockpile is counted in thousandths and shown as whole resources rounded down, so the
/// number never promises a price the stockpile does not cover.
/// </remarks>
public partial class ResourceReadout : InterfacePart
{
    /// <summary>The color of the word before a number, quieter than the number.</summary>
    private static readonly Color NameColor = new(0.68f, 0.72f, 0.8f);

    // The number a readout keeps room for, so that the readouts beside it stand still while it
    // grows. A larger number still fits: the readout is then as wide as the number.
    private const string RoomForAmount = "0,000";
    private const string RoomForSupply = "00 / 00";

    private Label _materials = null!;
    private Label _energy = null!;
    private Label _supply = null!;
    private long _shownMaterials = -1;
    private long _shownEnergy = -1;
    private int _shownUsed = -1;
    private int _shownCap = -1;

    protected override void Attach()
    {
        _materials = AddReadout("Materials", RoomForAmount);
        _energy = AddReadout("Energy", RoomForAmount);
        _supply = AddReadout("Supply", RoomForSupply);
    }

    public override void AfterTick()
    {
        var match = View.Match;
        var player = View.Player;

        var materials = match.Stockpiles.Materials(player) / Stockpiles.PerResource;
        if (materials != _shownMaterials)
        {
            _shownMaterials = materials;
            _materials.Text = materials.ToString("N0", CultureInfo.InvariantCulture);
        }

        var energy = match.Stockpiles.Energy(player) / Stockpiles.PerResource;
        if (energy != _shownEnergy)
        {
            _shownEnergy = energy;
            _energy.Text = energy.ToString("N0", CultureInfo.InvariantCulture);
        }

        var used = match.Supply.Used(player);
        var cap = match.Supply.Cap(player);
        if (used != _shownUsed || cap != _shownCap)
        {
            _shownUsed = used;
            _shownCap = cap;
            _supply.Text = string.Create(CultureInfo.InvariantCulture, $"{used} / {cap}");

            // At the cap nothing more can be ordered (design 1.3), which the player should see coming.
            if (used >= cap)
            {
                _supply.AddThemeColorOverride("font_color", UiStyle.Warning);
            }
            else
            {
                _supply.RemoveThemeColorOverride("font_color");
            }
        }
    }

    // A word and the number after it. Returns the label of the number.
    private Label AddReadout(string name, string roomFor)
    {
        var row = UiStyle.Row(6);
        row.Name = name;
        Frame.TopLeft.AddChild(row);

        var word = new Label { Text = name };
        word.AddThemeColorOverride("font_color", NameColor);
        row.AddChild(word);

        var number = new Label { Name = "Number" };
        row.AddChild(number);
        var room = number.GetThemeFont("font").GetStringSize(roomFor, fontSize: number.GetThemeFontSize("font_size"));
        number.CustomMinimumSize = new Vector2(Mathf.Ceil(room.X), 0f);
        return number;
    }
}
