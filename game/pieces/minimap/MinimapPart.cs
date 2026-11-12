using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The minimap in the interface: the whole map in the bottom right corner of the window. A press
/// of the look button on it moves the view there, and holding it drags the view along. A press
/// of the order button gives the selection the order it would get on that ground in the world.
/// </summary>
/// <remarks>
/// The frame has no place for a corner, so the minimap is a control of this part's own, drawn
/// over the frame. A full-screen panel covers the world, and the minimap is hidden while one is
/// open, as the bottom panel is.
/// </remarks>
public partial class MinimapPart : InterfacePart
{
    /// <summary>The longer side of the minimap, in pixels of the interface, whose window is 1152 by 648 before it is scaled.</summary>
    public const float LongestSide = 176f;

    // The room inside the box, and from the box to the edges of the window. Together they are
    // the width of the border that scrolls the camera, so the pointer on the map never scrolls it.
    private const float Padding = 6f;
    private const int Margin = 10;

    // The buttons that select and that give orders in the world, as the input map names them.
    private static readonly StringName SelectAction = "unit_select";
    private static readonly StringName OrderAction = "unit_order";

    private MinimapGeometry _geometry;
    private MinimapPicture _picture = null!;
    private PanelContainer _box = null!;

    protected override void Attach()
    {
        var map = View.Match.Map;
        _geometry = MinimapGeometry.Fit(map.Width, map.Height, LongestSide);

        _picture = new MinimapPicture { Name = "Picture" };
        _picture.Begin(View, _geometry, new FogOnMinimap(View));
        _picture.Looked += View.Camera.CenterOn;
        _picture.Ordered += Order;

        // The box takes the presses on its rim, so that a click a little beside the map selects
        // and orders nothing behind it. Everything else goes on to the world: the wheel still
        // zooms, and a selection box that was begun in the world can be dragged across.
        _box = new PanelContainer { Name = "Minimap", MouseFilter = Control.MouseFilterEnum.Pass };
        _box.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(Padding));
        _box.GuiInput += TakeRimPress;
        _box.AddChild(_picture);
        AddChild(_box);
        _box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, Margin);
        _box.GrowHorizontal = Control.GrowDirection.Begin;
        _box.GrowVertical = Control.GrowDirection.Begin;
        Frame.TakeBottomRightCorner(_box);
    }

    public override void EveryFrame(double delta) => _box.Visible = !Frame.IsPanelOpen;

    private void TakeRimPress(InputEvent input)
    {
        if (input.IsActionPressed(SelectAction) || input.IsActionPressed(OrderAction) || input.IsActionPressed(MinimapPicture.LookAction))
        {
            _box.AcceptEvent();
        }
    }

    // The order goes through the order table, as a click on the ground does, so the minimap
    // gives whatever order the pieces have for the ground: today the move order. The point is
    // always the ground, whatever stands there.
    private void Order(Vector2 point)
    {
        var target = new PointerTarget(PointerTargets.Ground, EntityId.None, _geometry.ToMapPoint(point));
        if (View.Orders.Give(View, target) is not { } row)
        {
            return;
        }

        _picture.ShowOrderAt(point);
        if (OS.IsStdOutVerbose())
        {
            GD.Print($"{row.Name} order from the minimap for {View.Selection.Count} to {WorldView.ToWorld(target.Point)} at tick {View.Match.World.Tick}.");
        }
    }
}
