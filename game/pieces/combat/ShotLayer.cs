using Godot;
using Sim.Combat;
using Sim.Core;
using Sim.Vision;

namespace Game;

/// <summary>
/// Draws every shot in flight that the player may see (design 5.3), which is the player's own and
/// another player's while it is over ground the player sees: a short bright streak in a paler shade of its owner's color, and
/// a thicker one for a shell of artillery. Like a unit, a shot is drawn between where it was one
/// tick ago and where it is now.
/// </summary>
/// <remarks>
/// <para>
/// A shot that lands is gone from the match in the tick it lands, so the simulation's event is
/// what says where it ended: the streak flies its last stretch to the place of the hit and is
/// taken away after it. A shell does the same to the middle of its blast, which the simulation
/// reports before the hits of what it reached. A shot that is lost stops where it was. A shot
/// that lands where the player does not see ends where it was last drawn, since the player is
/// not told of the hit.
/// </para>
/// <para>
/// A Tower fires from its top (16b): its shot starts there and comes down to the height of a
/// unit as it nears what it was fired at.
/// </para>
/// </remarks>
public partial class ShotLayer : WorldLayer
{
    /// <summary>How high above the ground a shot flies, in tiles: about where a unit's middle is.</summary>
    public const float FlightHeight = 0.45f;

    private const float Length = 0.55f;
    private const float Thickness = 0.07f;
    private const float ShellThickness = 0.16f;

    // How far under the top of its shape a structure fires from: for a Tower, the middle of its platform.
    private const float UnderTheTop = 0.12f;

    private readonly Dictionary<EntityId, ShotView> _shots = [];
    private readonly List<EntityId> _gone = [];
    private readonly List<SeenShot> _seen = [];

    // The shots of this tick that a structure fired, with how high it fired them from.
    private readonly Dictionary<EntityId, float> _launched = [];
    private InstancedShapes _streaks = null!;
    private int _pass;

    /// <summary>The number of shots drawn.</summary>
    public int ShotCount => _shots.Count;

    /// <summary>The number of shots drawn that started above the height a unit fires from, as a Tower's do.</summary>
    public int HighShotCount
    {
        get
        {
            var high = 0;
            foreach (var view in _shots.Values)
            {
                high += view.Launch > FlightHeight ? 1 : 0;
            }

            return high;
        }
    }

    /// <summary>
    /// How high above the ground an attacker fires from, in tiles: a structure from near the top
    /// of its shape, and a unit from <see cref="FlightHeight"/>.
    /// </summary>
    public static float LaunchHeight(Sim.Setup.Match match, EntityId attacker) =>
        match.Structures.TryGet(attacker, out var placed) && match.Data.StructureOf(placed.Type) is { } structure
            ? Mathf.Max(FlightHeight, StructureShapes.HeightOf(structure) - UnderTheTop)
            : FlightHeight;

    /// <summary>
    /// The last stretch of a shot that landed in the last tick, as it is drawn: from where it
    /// was to where it landed. False for a shot that is not drawn or has not landed.
    /// </summary>
    public bool TryGetLanding(EntityId shot, out Vector3 from, out Vector3 to)
    {
        if (_shots.TryGetValue(shot, out var view) && view.Landed)
        {
            from = view.Previous;
            to = view.Position;
            return true;
        }

        from = default;
        to = default;
        return false;
    }

    protected override void Attach()
    {
        _streaks = InstancedShapes.Under(
            this,
            "Shots",
            new BoxMesh { Size = Vector3.One },
            InstancedShapes.Material(seeThrough: false),
            InstancedShapes.MapArea(View.Match.Map));
    }

    public override void AfterTick()
    {
        var match = View.Match;
        _pass++;

        _launched.Clear();
        foreach (var happened in match.Simulation.Events)
        {
            if (happened.Kind == SimEventKind.ShotFired && match.Structures.Contains(happened.Other))
            {
                _launched[happened.Entity] = LaunchHeight(match, happened.Other);
            }
        }

        View.Sight.CollectShots(_seen);
        foreach (var seen in _seen)
        {
            var entity = seen.Entity;
            if (!match.Shots.TryGet(entity, out var shot))
            {
                continue;
            }

            var ground = WorldView.ToWorld(shot.Position);
            var aim = WorldView.ToWorld(shot.Aim);
            Vector3 position;
            if (_shots.TryGetValue(entity, out var view))
            {
                position = ground + (Vector3.Up * view.HeightAt(ground, aim));
                view.Previous = view.Position;
            }
            else
            {
                // A shot is first seen in the tick it is fired, where it was fired from.
                var launch = _launched.GetValueOrDefault(entity, FlightHeight);
                position = ground + (Vector3.Up * launch);
                view = new ShotView
                {
                    Previous = position,
                    Color = PlayerColors.Of(shot.Owner).Lerp(Colors.White, 0.7f),
                    Thickness = IsShell(shot) ? ShellThickness : Thickness,
                    Launch = launch,
                    Reach = Flat(aim - ground).Length(),
                };

                // Until it has flown, it points at what it was fired at.
                view.Direction = Lifted(aim) - position;
                _shots.Add(entity, view);
            }

            view.Position = position;
            view.Pass = _pass;
        }

        // A shot that landed in this tick flies on to where it landed, and is gone after the next.
        foreach (var happened in match.Simulation.Events)
        {
            var shot = happened.Kind switch
            {
                SimEventKind.Blast => happened.Entity,
                SimEventKind.ShotHit => happened.Other,
                _ => EntityId.None,
            };
            if (shot != EntityId.None && View.Sight.Knows(happened) && _shots.TryGetValue(shot, out var landed) && !landed.Landed)
            {
                landed.Previous = landed.Position;
                landed.Position = Lifted(WorldView.ToWorld(happened.Position));
                landed.Landed = true;
                landed.Pass = _pass;
            }
        }

        foreach (var (entity, view) in _shots)
        {
            if (view.Pass != _pass)
            {
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _shots.Remove(entity);
        }

        _gone.Clear();
    }

    public override void EveryFrame(double delta)
    {
        var fraction = Mathf.Clamp(View.World.TickFraction, 0f, 1f);
        _streaks.Begin();
        foreach (var view in _shots.Values)
        {
            var step = view.Position - view.Previous;
            if (step.LengthSquared() > 0.0001f)
            {
                view.Direction = step;
            }

            // A shot never flies straight up or down: one with no way to go but that is drawn level.
            if (Flat(view.Direction).LengthSquared() < 0.0001f)
            {
                view.Direction = Vector3.Forward;
            }

            // The box is one tile long along its own Z, which is turned to lie along the flight.
            var along = view.Direction.Normalized();
            var across = along.Cross(Vector3.Up).Normalized();
            var basis = new Basis(across * view.Thickness, across.Cross(along) * view.Thickness, along * Length);
            _streaks.Add(new Transform3D(basis, view.Previous.Lerp(view.Position, fraction)), view.Color);
        }

        _streaks.End();
    }

    private static Vector3 Lifted(Vector3 ground) => ground + (Vector3.Up * FlightHeight);

    private static Vector3 Flat(Vector3 vector) => new(vector.X, 0f, vector.Z);

    // A shell is a shot that lands as a blast.
    private static bool IsShell(in Shot shot) => shot.Blast > Sim.Numerics.Fixed.Zero;

    private sealed class ShotView
    {
        public Vector3 Previous { get; set; }

        public Vector3 Position { get; set; }

        public Vector3 Direction { get; set; }

        public Color Color { get; init; }

        public float Thickness { get; init; }

        /// <summary>How high above the ground it was fired from, in tiles.</summary>
        public float Launch { get; init; } = FlightHeight;

        /// <summary>How far over the ground it was from what it was fired at, in tiles.</summary>
        public float Reach { get; init; }

        /// <summary>
        /// How high it flies over a point of the ground: from where it was fired down to the
        /// height of a unit, by how much of its way to what it is aimed at is left.
        /// </summary>
        public float HeightAt(Vector3 ground, Vector3 aim) =>
            Launch <= FlightHeight || Reach < 0.01f
                ? FlightHeight
                : Mathf.Lerp(FlightHeight, Launch, Mathf.Clamp(Flat(aim - ground).Length() / Reach, 0f, 1f));

        public bool Landed { get; set; }

        public int Pass { get; set; }
    }
}
