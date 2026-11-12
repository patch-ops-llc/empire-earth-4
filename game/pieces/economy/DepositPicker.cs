using Godot;
using Sim.Core;
using Sim.Economy;

namespace Game;

/// <summary>
/// Finds the deposit under the pointer. A deposit is found where it is drawn: on the block that
/// stands on its tiles, and so also on the side of the block the camera faces, which hides the
/// ground behind it.
/// </summary>
public sealed class DepositPicker : PointerPicker
{
    public override bool TryPick(in PointerProbe probe, out PointerTarget target)
    {
        target = default;
        if ((probe.Wanted & PointerTargets.Deposit) == 0)
        {
            return false;
        }

        var origin = probe.Camera.ProjectRayOrigin(probe.Screen);
        var direction = probe.Camera.ProjectRayNormal(probe.Screen);
        var sites = probe.View.Match.WorkSites;
        var entities = probe.View.Match.World.Entities;

        var best = EntityId.None;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < sites.Count; i++)
        {
            var entity = sites.EntityAt(i);
            var site = sites.ValueAt(i);
            if (site.Kind != WorkSiteKind.Deposit || !entities.IsAlive(entity))
            {
                continue;
            }

            // The box round the block: its tiles on the ground, and as tall as it is drawn.
            var low = new Vector3(site.X, 0f, site.Y);
            var high = new Vector3(site.X + site.Size, DepositLayer.Height, site.Y + site.Size);
            if (TryEnter(origin, direction, low, high, out var distance) && distance < bestDistance)
            {
                best = entity;
                bestDistance = distance;
            }
        }

        if (best == EntityId.None)
        {
            return false;
        }

        target = new PointerTarget(PointerTargets.Deposit, best, probe.Point);
        return true;
    }

    // Where a ray enters a box whose sides lie along the axes, as the distance from the ray's
    // origin. False when it passes the box or the box is behind the origin.
    private static bool TryEnter(Vector3 origin, Vector3 direction, Vector3 low, Vector3 high, out float distance)
    {
        var enter = 0f;
        var leave = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(direction[axis]) < 0.000001f)
            {
                if (origin[axis] < low[axis] || origin[axis] > high[axis])
                {
                    distance = 0f;
                    return false;
                }

                continue;
            }

            var first = (low[axis] - origin[axis]) / direction[axis];
            var second = (high[axis] - origin[axis]) / direction[axis];
            enter = Mathf.Max(enter, Mathf.Min(first, second));
            leave = Mathf.Min(leave, Mathf.Max(first, second));
        }

        distance = enter;
        return enter <= leave;
    }
}
