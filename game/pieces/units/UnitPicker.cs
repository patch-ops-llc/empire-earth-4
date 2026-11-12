using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// Finds the unit under the pointer, the local player's or another player's. A unit is found
/// where it is drawn, since that is what the player aims at.
/// </summary>
/// <remarks>
/// The simulation's spatial index names the entities round the point of the ground under the
/// pointer, and each is then tested where it appears on screen. The second test is needed because
/// the camera looks down at a slant: a unit is clicked on its body, which stands above the spot
/// of ground behind it.
/// </remarks>
public sealed class UnitPicker : PointerPicker
{
    /// <summary>How many pixels beside a unit a click is still on it.</summary>
    public const float ClickSlack = 5f;

    /// <summary>
    /// How far round a point of the ground, in tiles, the spatial index is asked. It covers the
    /// slant of the camera and the distance a unit is drawn from where it stood at the end of the
    /// last tick.
    /// </summary>
    public const float GroundMargin = 2f;

    private readonly List<EntityId> _candidates = [];

    public override bool TryPick(in PointerProbe probe, out PointerTarget target)
    {
        target = default;
        if ((probe.Wanted & (PointerTargets.OwnUnit | PointerTargets.EnemyUnit)) == 0)
        {
            return false;
        }

        var view = probe.View;
        var camera = probe.Camera;
        var best = EntityId.None;
        var bestKind = PointerTargets.None;
        var bestDistance = float.MaxValue;

        view.Match.SpatialIndex.QueryCircle(probe.Point, UnderPointer.ToFixed(GroundMargin), _candidates);
        foreach (var entity in _candidates)
        {
            if (!view.Match.Bodies.TryGet(entity, out var body) || !view.World.TryGetDrawnPosition(entity, out var position))
            {
                continue;
            }

            var kind = body.Owner == view.Player ? PointerTargets.OwnUnit : PointerTargets.EnemyUnit;
            if ((probe.Wanted & kind) == 0 || camera.IsPositionBehind(position))
            {
                continue;
            }

            // On screen a unit is roughly a capsule: the line from its foot to its top, as thick as it is wide.
            var foot = camera.UnprojectPosition(position);
            var top = camera.UnprojectPosition(position + (Vector3.Up * PlaceholderMeshes.UnitHeight));
            var side = camera.UnprojectPosition(position + (camera.GlobalBasis.X * PlaceholderMeshes.UnitRadius));
            var reach = foot.DistanceTo(side) + ClickSlack;
            var distance = Geometry2D.GetClosestPointToSegment(probe.Screen, foot, top).DistanceTo(probe.Screen);
            if (distance <= reach && distance < bestDistance)
            {
                best = entity;
                bestKind = kind;
                bestDistance = distance;
            }
        }

        if (best == EntityId.None)
        {
            return false;
        }

        target = new PointerTarget(bestKind, best, probe.Point);
        return true;
    }
}
