using Sim.Numerics;

namespace Sim.Core;

/// <summary>
/// One order from a player to the simulation, and the only way anything outside the simulation can
/// change it. A command is plain immutable data: a type, the player who gave it, the entities it is
/// given to, and a target position, a target entity or a content ID. Which of those a type uses is
/// up to its handler.
/// </summary>
public readonly struct Command
{
    private readonly EntityId[]? _entities;

    public Command(
        CommandType type,
        int player,
        ReadOnlySpan<EntityId> entities = default,
        Fixed targetX = default,
        Fixed targetY = default,
        EntityId targetEntity = default,
        int contentId = 0)
    {
        Type = type;
        Player = player;
        _entities = entities.IsEmpty ? null : entities.ToArray();
        TargetX = targetX;
        TargetY = targetY;
        TargetEntity = targetEntity;
        ContentId = contentId;
    }

    public CommandType Type { get; }

    /// <summary>The player who gave the command. The handler checks that the player may give it.</summary>
    public int Player { get; }

    /// <summary>The entities the command is given to, in the order the sender listed them.</summary>
    public ReadOnlySpan<EntityId> Entities => _entities;

    public Fixed TargetX { get; }

    public Fixed TargetY { get; }

    public EntityId TargetEntity { get; }

    /// <summary>The dense integer the data loader assigned to a piece of content, such as a chassis.</summary>
    public int ContentId { get; }
}
