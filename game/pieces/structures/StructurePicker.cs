using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// Finds the structure under the pointer, the local player's or another player's. A structure
/// is found where it is drawn: on the box that stands on its tiles and is as tall as its shape,
/// so that a click on its roof finds it and not the ground behind it.
/// </summary>
/// <remarks>
/// Nothing here uses physics. The line from the camera through the point of the screen is
/// tested against the box of every structure, and the nearest one it enters is the answer. The
/// pickers are asked in the order they were added, so a unit in front of a structure, or on the
/// screen in front of one, is found before it.
/// </remarks>
public sealed class StructurePicker : PointerPicker
{
    public override bool TryPick(in PointerProbe probe, out PointerTarget target)
    {
        target = default;
        if ((probe.Wanted & (PointerTargets.OwnStructure | PointerTargets.EnemyStructure)) == 0)
        {
            return false;
        }

        var view = probe.View;
        var match = view.Match;
        var origin = probe.Camera.ProjectRayOrigin(probe.Screen);
        var direction = probe.Camera.ProjectRayNormal(probe.Screen);

        var best = EntityId.None;
        var bestKind = PointerTargets.None;
        var bestDistance = float.MaxValue;

        var structures = match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var entity = structures.EntityAt(i);
            var placed = structures.ValueAt(i);
            if (!match.World.Entities.IsAlive(entity)
                || !match.Bodies.TryGet(entity, out var body)
                || match.Data.StructureOf(placed.Type) is not { } structure)
            {
                continue;
            }

            var kind = body.Owner == view.Player ? PointerTargets.OwnStructure : PointerTargets.EnemyStructure;
            if ((probe.Wanted & kind) == 0)
            {
                continue;
            }

            var from = new Vector3(placed.X, 0f, placed.Y);
            var to = new Vector3(placed.X + placed.Size, StructureShapes.HeightOf(structure), placed.Y + placed.Size);
            if (TryEnter(origin, direction, from, to, out var distance) && distance < bestDistance)
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

    /// <summary>
    /// How far along a line from a point a box begins. False when the line passes the box by or
    /// the box is behind the point.
    /// </summary>
    /// <param name="origin">Where the line begins.</param>
    /// <param name="direction">The way it goes, of any length but zero.</param>
    /// <param name="from">The corner of the box with the lowest coordinates.</param>
    /// <param name="to">The corner with the highest.</param>
    /// <param name="distance">The distance to the box in lengths of <paramref name="direction"/>, zero from inside it.</param>
    public static bool TryEnter(Vector3 origin, Vector3 direction, Vector3 from, Vector3 to, out float distance)
    {
        var enter = 0f;
        var leave = float.MaxValue;
        distance = 0f;
        for (var axis = 0; axis < 3; axis++)
        {
            var start = origin[axis];
            var step = direction[axis];
            if (Mathf.IsZeroApprox(step))
            {
                // Along this axis the line never gets nearer: it is between the two sides or it is not.
                if (start < from[axis] || start > to[axis])
                {
                    return false;
                }

                continue;
            }

            var a = (from[axis] - start) / step;
            var b = (to[axis] - start) / step;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            leave = Mathf.Min(leave, Mathf.Max(a, b));
            if (enter > leave)
            {
                return false;
            }
        }

        distance = enter;
        return true;
    }
}
