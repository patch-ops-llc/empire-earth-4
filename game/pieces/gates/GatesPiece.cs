using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// The view piece for gates (18b): the gate in the build menu, placed with one click like any
/// structure a worker builds, and shown before the click as it will stand in its wall.
/// </summary>
/// <remarks>
/// <para>
/// A gate is a wall tile that its owner's units walk through once it is finished, so nearly
/// all of it is the walls piece's: the piece for structures draws it with a shape that the
/// wall tiles round it and the units by it decide (<see cref="GateShapes"/>,
/// <see cref="WallJoins"/>), it is selected, cancelled, remembered under the fog and shown on
/// the minimap as a wall tile is, and the panel for walls says what it is. Its orders are build
/// orders, and the build menu's messages are theirs.
/// </para>
/// <para>
/// The gate is not in the roster of structures: the game makes it from the wall's file. So
/// the menu, which lists the roster, is given it here.
/// </para>
/// </remarks>
public sealed class GatesPiece : ViewPiece
{
    public override string Name => "Gates";

    public override void AddTo(MatchView view)
    {
        if (view.Match.Data.Gate is not { } gate)
        {
            return;
        }

        var structures = view.World.GetChildren().OfType<StructureLayer>().FirstOrDefault()
            ?? throw new InvalidOperationException("Gates are added after the piece that draws structures.");
        var menu = view.Interface.Parts.OfType<BuildMenu>().FirstOrDefault()
            ?? throw new InvalidOperationException("Gates are added after the build menu.");
        var preview = view.World.GetChildren().OfType<PlacementPreview>().FirstOrDefault()
            ?? throw new InvalidOperationException("Gates are added after the build menu's preview.");
        var aid = view.Interface.Parts.OfType<WallDevelopmentAid>().FirstOrDefault();

        menu.AddStructure(gate);

        // The gate under the pointer is drawn as it would stand: turned the way the wall
        // beside the tile runs, or the way the player's wall orders will bring one.
        var meshes = new Dictionary<int, ArrayMesh>();
        preview.ShapeAt = (structure, site) =>
        {
            if (!structure.IsGate)
            {
                return null;
            }

            var owner = aid is { ForOtherSeat: true } ? aid.OtherSeat : view.Player;
            var joins = structures.Walls.JoinsOf(owner, site.X, site.Y);
            var shape = GateShapes.ShapeOf(joins, WallShapes.Whole, structures.Walls.RunsAlongY(owner, site.X, site.Y, joins), open: false);
            if (!meshes.TryGetValue(shape, out var mesh))
            {
                mesh = GateShapes.MeshOf(shape);
                meshes.Add(shape, mesh);
            }

            return mesh;
        };

        if (aid is not null)
        {
            menu.PlacedInstead = (StructureDef structure, int x, int y) => structure.IsGate && aid.ForOtherSeat && aid.PlaceGate(x, y);
        }
    }
}
