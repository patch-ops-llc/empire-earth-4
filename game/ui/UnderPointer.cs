using Godot;
using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The kinds of thing the pointer can be on. They are flags, so that a row of the
/// <see cref="OrderTable"/> or a question to the <see cref="UnderPointer"/> can name several.
/// </summary>
[Flags]
public enum PointerTargets
{
    /// <summary>Nothing: the pointer is not over the world, as when it is over the sky.</summary>
    None = 0,

    /// <summary>The ground, with nothing on it that can be pointed at.</summary>
    Ground = 1 << 0,

    /// <summary>A unit of the local player's.</summary>
    OwnUnit = 1 << 1,

    /// <summary>A unit of another player's.</summary>
    EnemyUnit = 1 << 2,

    /// <summary>A deposit of a resource.</summary>
    Deposit = 1 << 3,

    /// <summary>A structure of the local player's.</summary>
    OwnStructure = 1 << 4,

    /// <summary>A structure of another player's.</summary>
    EnemyStructure = 1 << 5,

    /// <summary>What a click selects: the local player's own units and structures.</summary>
    Own = OwnUnit | OwnStructure,

    /// <summary>Every kind.</summary>
    Any = Ground | OwnUnit | EnemyUnit | Deposit | OwnStructure | EnemyStructure,
}

/// <summary>What is under the pointer: the one answer everything that reacts to a click works from.</summary>
/// <param name="Kind">The kind of thing: exactly one flag, or <see cref="PointerTargets.None"/>.</param>
/// <param name="Entity">The unit, deposit or structure. <see cref="EntityId.None"/> for the ground.</param>
/// <param name="Point">
/// The point of the map under the pointer, whatever stands there. A pointer beyond the edge of
/// the map gives the nearest point on it.
/// </param>
public readonly record struct PointerTarget(PointerTargets Kind, EntityId Entity, FixedVector2 Point)
{
    /// <summary>True when the pointer is over the world at all.</summary>
    public bool IsOnWorld => Kind != PointerTargets.None;

    /// <summary>True when the kind is one of these.</summary>
    public bool Is(PointerTargets kinds) => (Kind & kinds) != 0;
}

/// <summary>What a <see cref="PointerPicker"/> is asked: a point of the screen, and where that is in the world.</summary>
/// <param name="View">The match and its view.</param>
/// <param name="Camera">The camera the player looks through.</param>
/// <param name="Screen">The point of the screen, in viewport pixels.</param>
/// <param name="Ground">The point of the ground under it, as map x and y. It can lie off the map.</param>
/// <param name="Point">That point as the simulation has it, brought onto the map.</param>
/// <param name="Wanted">The kinds the asker wants. A picker passes over everything else.</param>
public readonly record struct PointerProbe(
    MatchView View,
    Camera3D Camera,
    Vector2 Screen,
    Vector2 Ground,
    FixedVector2 Point,
    PointerTargets Wanted);

/// <summary>
/// Finds one kind of thing under the pointer. The piece that draws a kind of thing adds the picker
/// for it with <see cref="UnderPointer.AddPicker"/>: units have one from the start, deposits and
/// structures get theirs with the pieces that draw them.
/// </summary>
public abstract class PointerPicker
{
    /// <summary>
    /// Says what of this picker's kind is at the probe's point of the screen, where the player
    /// sees it drawn. False when there is nothing, or nothing of a kind the probe wants.
    /// </summary>
    public abstract bool TryPick(in PointerProbe probe, out PointerTarget target);
}

/// <summary>
/// Answers "what is under the pointer": a unit of the player's, an enemy unit, a deposit, a
/// structure, or the ground. Selection, orders and anything else that reacts to where the pointer
/// is ask here, so that they all agree.
/// </summary>
/// <remarks>
/// Nothing here uses physics. The point of the screen is turned into a point of the ground, and
/// each <see cref="PointerPicker"/> is asked in the order the pickers were added, units first. The
/// first that finds something gives the answer. When none does, the answer is the ground.
/// </remarks>
public sealed class UnderPointer
{
    private readonly List<PointerPicker> _pickers = [];
    private readonly MatchView _view;

    public UnderPointer(MatchView view) => _view = view;

    /// <summary>Adds a picker, which is asked after those added before it.</summary>
    public void AddPicker(PointerPicker picker) => _pickers.Add(picker);

    /// <summary>What is under the pointer now.</summary>
    /// <param name="wanted">The kinds to look for. The ground is the answer when none of them is there.</param>
    public PointerTarget Current(PointerTargets wanted = PointerTargets.Any) =>
        At(_view.World.GetViewport().GetMousePosition(), wanted);

    /// <summary>What is under a point of the screen.</summary>
    /// <param name="screen">The point, in viewport pixels.</param>
    /// <param name="wanted">The kinds to look for. The ground is the answer when none of them is there.</param>
    public PointerTarget At(Vector2 screen, PointerTargets wanted = PointerTargets.Any)
    {
        var camera = _view.World.GetViewport().GetCamera3D();
        if (camera is null || !TryGroundPoint(camera, screen, out var ground))
        {
            return default;
        }

        var point = ToMapPoint(ground);
        var probe = new PointerProbe(_view, camera, screen, ground, point, wanted);
        foreach (var picker in _pickers)
        {
            if (picker.TryPick(probe, out var target) && target.Is(wanted))
            {
                return target;
            }
        }

        return new PointerTarget(PointerTargets.Ground, EntityId.None, point);
    }

    /// <summary>
    /// The point of the ground under a point of the screen, as map x and y. False when the ray
    /// from the camera through that point never comes down.
    /// </summary>
    public static bool TryGroundPoint(Camera3D camera, Vector2 screen, out Vector2 ground)
    {
        var origin = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        if (direction.Y > -0.0001f)
        {
            ground = default;
            return false;
        }

        var hit = origin + (direction * (-origin.Y / direction.Y));
        ground = new Vector2(hit.X, hit.Z);
        return true;
    }

    /// <summary>
    /// A point of the ground as the simulation has it. Floats end here: this is the one place a
    /// point from the screen becomes a point of the simulation. The point is brought onto the map
    /// on the way, because the simulation drops an order whose target is off the map (design 5.3).
    /// </summary>
    public FixedVector2 ToMapPoint(Vector2 ground)
    {
        var map = _view.Match.Map;
        return new FixedVector2(ToMapCoordinate(ground.X, map.Width), ToMapCoordinate(ground.Y, map.Height));
    }

    /// <summary>A distance in tiles as the simulation has it, rounded to the nearest distance unit.</summary>
    public static Fixed ToFixed(float tiles) => Fixed.FromRaw(Mathf.RoundToInt(tiles * Fixed.RawOne));

    // The last point of a map 96 tiles wide is one distance unit short of 96.
    private static Fixed ToMapCoordinate(float value, int tiles)
    {
        if (float.IsNaN(value))
        {
            return default;
        }

        // In double, because a float cannot hold every distance unit of a large map.
        var last = ((double)tiles * Fixed.RawOne) - 1.0;
        return Fixed.FromRaw((int)Math.Clamp(Math.Round((double)value * Fixed.RawOne), 0.0, last));
    }
}
