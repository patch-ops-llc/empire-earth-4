using Sim.Core;
using Sim.Setup;

namespace Game;

/// <summary>
/// Says of what kind a unit is, for "select the same kind": two units are of one kind when this
/// gives the same word for both.
/// </summary>
/// <remarks>
/// Today a match holds workers and nothing else that moves, so there are two words. The piece
/// that puts fighting units into a match (11v) gives them theirs here, from the line and chassis
/// each was built as: this is the one place that has to learn of a new kind.
/// </remarks>
public static class UnitKinds
{
    public const string Worker = "worker";
    public const string Other = "unit";

    public static string Of(Match match, EntityId entity) => match.Workers.Contains(entity) ? Worker : Other;
}
