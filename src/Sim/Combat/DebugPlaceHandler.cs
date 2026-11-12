using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;
using Sim.Structures;

namespace Sim.Combat;

/// <summary>
/// Executes <see cref="CommandType.DebugPlace"/>, the development placement: it puts one finished
/// structure on the map from nothing for the player who sends the command, with its lowest tile
/// on the tile of the command's target point. The design has no such order. It is there so that
/// Towers and fights over structures can be seen, tested and recorded apart from construction.
/// </summary>
/// <remarks>
/// <para>
/// It shares its switch with the development spawn (<see cref="DebugRules.Spawn"/>): the match
/// setup gives the command this handler only while the rules say so, and otherwise the command
/// has no handler and is dropped.
/// </para>
/// <para>
/// The structure is the command's <see cref="Command.ContentId"/>, its place in the roster. The
/// command is valid when its player is in the match, the data has a file for the structure, every
/// tile of the footprint is open ground under no other structure, no unit stands on one of those
/// tiles, and a structure that gives a research slot still has one to give. Nothing else is
/// asked: the structure need not be unlocked or stand in territory, and it costs nothing. It is
/// finished at once, as <see cref="StructureSetup.PlaceStructure(Match, int, StructureDef, int, int)"/>
/// places it, so a Tower placed this way attacks.
/// </para>
/// <para>
/// A command that is not valid changes nothing and is reported as a
/// <see cref="SimEventKind.OrderRefused"/>.
/// </para>
/// </remarks>
public sealed class DebugPlaceHandler : ICommandHandler
{
    private readonly Match _match;
    private readonly SimEventList _events;

    public DebugPlaceHandler(Match match, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(events);
        _match = match;
        _events = events;
    }

    public bool Execute(World world, in Command command)
    {
        ArgumentNullException.ThrowIfNull(world);

        var x = command.TargetX.FloorToInt();
        var y = command.TargetY.FloorToInt();
        if (command.Player < 0
            || command.Player >= _match.Players.Count
            || _match.Data.StructureOf(command.ContentId) is not { } structure
            || !_match.HasRoomFor(structure, x, y)
            || IsStoodOn(world, new TileSquare(x, y, structure.Footprint))
            || !HasSlotToGive(command.Player, structure))
        {
            OrderCommandHandler.Refuse(_events, in command);
            return false;
        }

        _match.PlaceStructure(command.Player, structure, x, y);
        return true;
    }

    /// <summary>
    /// True when a unit stands on one of the tiles. Every mover is asked, and not the spatial
    /// index: a unit made by a command earlier in this tick is not in the index yet.
    /// </summary>
    private bool IsStoodOn(World world, TileSquare tiles)
    {
        var movers = _match.Movers;
        for (var i = 0; i < movers.Count; i++)
        {
            var entity = movers.EntityAt(i);
            if (world.Entities.IsAlive(entity)
                && _match.Bodies.TryGet(entity, out var body)
                && tiles.Covers(body.Position))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// False for a structure whose research slot the player has no room for: one more Academy than
    /// a player may have. The starting structure's slot is the player's own and is always there.
    /// </summary>
    private bool HasSlotToGive(int player, StructureDef structure)
    {
        if (structure.ResearchSlots == 0 || structure.Id == DataRules.StartStructure)
        {
            return true;
        }

        for (var number = ResearchState.PlayerSlot + 1; number < ResearchState.SlotsPerPlayer; number++)
        {
            if (!_match.Research.State.Slot(player, number).IsOpen)
            {
                return true;
            }
        }

        return false;
    }
}
