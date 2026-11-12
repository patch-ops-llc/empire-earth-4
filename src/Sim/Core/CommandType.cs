namespace Sim.Core;

/// <summary>
/// The command type list. Each milestone that adds a kind of command adds one line here and
/// registers its handler with <see cref="Simulation.SetCommandHandler"/>.
/// </summary>
/// <remarks>
/// Keep the lines in alphabetical order, so that two pull requests rarely touch the same line. Give
/// each new type the lowest number not yet used, from 1 to 65,535, and never change or reuse a
/// number: replays and the network protocol store it. CommandTypeTests fails if two names share a
/// number.
/// </remarks>
public enum CommandType
{
    /// <summary>Not a command. The default value, which no handler can be registered for.</summary>
    None = 0,
    Attack = 4,
    AttackMove = 5,
    Build = 9,
    CancelBuild = 10,
    CancelResearch = 2,
    Concede = 15,
    DebugPlace = 16,
    DebugSpawn = 8,
    Gather = 7,
    Move = 1,
    StartResearch = 3,
    Stop = 6,
}
