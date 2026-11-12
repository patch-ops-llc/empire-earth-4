using Godot;
using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>
/// What a fight looks like beside the units, the bars and the shots: every hit, shot and death the
/// simulation reports for a tick becomes a short effect here.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A melee hit: a pale streak from the attacker to its target, a spark on the target, and the target flashes white.</item>
/// <item>A shot fired: a flash where it was fired from. The shot itself is the <see cref="ShotLayer"/>'s.</item>
/// <item>A shot that lands: a spark on the target, and the target flashes white.</item>
/// <item>A shot that is lost, because its target died first: a grey puff where the shot was.</item>
/// <item>A shell that lands: a burst at the place, inside a pale ring that spreads to the edge of the blast and stays there while it fades, so that the ring shows who was in reach. Each unit the blast reached is a shot that lands.</item>
/// <item>A death: the unit's shape sinks into the ground and fades, inside a ring in its owner's color that spreads.</item>
/// </list>
/// An effect is drawn from what its event says, since the entities an event names may be gone by
/// the time it is read. Effects are for the eye only: nothing reads them, and a match runs the
/// same with or without them.
/// </remarks>
public partial class CombatEffects : WorldLayer
{
    // The most effects alive at once. A fight larger than this drops its oldest effects first.
    private const int Limit = 600;

    private static readonly Color SparkColor = new(1f, 0.95f, 0.75f);
    private static readonly Color MuzzleColor = new(1f, 0.85f, 0.35f);
    private static readonly Color PuffColor = new(0.7f, 0.7f, 0.72f);
    private static readonly Color StreakColor = new(1f, 1f, 1f);

    // The part of a blast's life in which its ring and its burst grow to their full size.
    private const float BlastGrowth = 0.3f;

    private readonly List<Effect> _effects = [];

    // The way each unit that can be damaged faced after the last tick, for the shape it leaves when it dies.
    private readonly Dictionary<EntityId, float> _headings = [];
    private readonly List<EntityId> _gone = [];

    private InstancedShapes _balls = null!;
    private InstancedShapes _streaks = null!;
    private InstancedShapes _bodies = null!;
    private InstancedShapes _rings = null!;

    /// <summary>The number of hits, melee and shot, shown since the match began.</summary>
    public int HitsShown { get; private set; }

    /// <summary>The number of blasts shown since the match began.</summary>
    public int BlastsShown { get; private set; }

    /// <summary>The number of deaths shown since the match began.</summary>
    public int DeathsShown { get; private set; }

    /// <summary>The number of effects alive now.</summary>
    public int EffectCount => _effects.Count;

    protected override void Attach()
    {
        var area = InstancedShapes.MapArea(View.Match.Map);
        var fading = InstancedShapes.Material(seeThrough: true);
        _rings = InstancedShapes.Under(this, "DeathRings", PlaceholderMeshes.Ring(1f, Colors.White), fading, area);
        _bodies = InstancedShapes.Under(this, "FallenAndFlashes", PlaceholderMeshes.Unit(), fading, area);
        _streaks = InstancedShapes.Under(this, "Streaks", new BoxMesh { Size = Vector3.One }, fading, area);
        _balls = InstancedShapes.Under(this, "Sparks", new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 }, fading, area);
    }

    public override void AfterTick()
    {
        var match = View.Match;
        foreach (var happened in match.Simulation.Events)
        {
            var place = WorldView.ToWorld(happened.Position);
            switch (happened.Kind)
            {
                case SimEventKind.MeleeHit:
                    HitsShown++;
                    if (match.Bodies.TryGet(happened.Other, out var attacker))
                    {
                        Add(new Effect(EffectKind.Streak, place, 0.16) { From = WorldView.ToWorld(attacker.Position) });
                    }

                    Add(new Effect(EffectKind.Spark, place, 0.25));
                    Add(new Effect(EffectKind.Flash, place, 0.14) { Follows = happened.Entity });
                    break;

                case SimEventKind.ShotFired:
                    Add(new Effect(EffectKind.Muzzle, place, 0.12));
                    break;

                case SimEventKind.ShotHit:
                    HitsShown++;
                    Add(new Effect(EffectKind.Spark, place, 0.25));
                    Add(new Effect(EffectKind.Flash, place, 0.14) { Follows = happened.Entity });
                    break;

                case SimEventKind.ShotLost:
                    Add(new Effect(EffectKind.Puff, place, 0.45));
                    break;

                case SimEventKind.Blast:
                    BlastsShown++;
                    Add(new Effect(EffectKind.Blast, place, 0.6) { Radius = happened.Amount / (float)Fixed.RawOne });
                    break;

                case SimEventKind.Death:
                    DeathsShown++;
                    var color = PlayerColors.Of(happened.Player);
                    var heading = _headings.GetValueOrDefault(happened.Entity);
                    Add(new Effect(EffectKind.Fallen, place, 1.1) { Color = color, Heading = heading });
                    Add(new Effect(EffectKind.Ring, place, 0.7) { Color = color });
                    break;

                default:
                    break;
            }
        }

        RememberHeadings();
    }

    public override void EveryFrame(double delta)
    {
        _rings.Begin();
        _bodies.Begin();
        _streaks.Begin();
        _balls.Begin();

        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            effect.Age += delta;
            if (effect.Age >= effect.Life)
            {
                _effects.RemoveAt(i);
                continue;
            }

            Draw(effect, (float)(effect.Age / effect.Life));
        }

        _rings.End();
        _bodies.End();
        _streaks.End();
        _balls.End();
    }

    private void Add(Effect effect)
    {
        if (_effects.Count >= Limit)
        {
            _effects.RemoveAt(0);
        }

        _effects.Add(effect);
    }

    // Draws one effect that is this far through its life, from 0 to 1.
    private void Draw(Effect effect, float gone)
    {
        var left = 1f - gone;
        var middle = effect.At + (Vector3.Up * ShotLayer.FlightHeight);
        switch (effect.Kind)
        {
            case EffectKind.Spark:
                AddBall(middle, Mathf.Lerp(0.14f, 0.5f, gone), SparkColor, left);
                break;

            case EffectKind.Muzzle:
                AddBall(middle, Mathf.Lerp(0.3f, 0.12f, gone), MuzzleColor, 0.9f * left);
                break;

            case EffectKind.Puff:
                AddBall(middle + (Vector3.Up * (0.4f * gone)), Mathf.Lerp(0.18f, 0.42f, gone), PuffColor, 0.6f * left);
                break;

            case EffectKind.Streak:
                // The streak runs out toward the target: its tail follows its head there.
                var tail = (effect.From + (Vector3.Up * ShotLayer.FlightHeight)).Lerp(middle, gone);
                var span = middle - tail;
                if (span.LengthSquared() > 0.0001f)
                {
                    var along = span.Normalized();
                    var across = along.Cross(Vector3.Up).Normalized();
                    var basis = new Basis(across * 0.06f, Vector3.Up * 0.06f, span);
                    _streaks.Add(new Transform3D(basis, tail + (span / 2f)), WithAlpha(StreakColor, 0.9f * left));
                }

                break;

            case EffectKind.Flash:
                // A white shell just outside the unit, where the unit is drawn now.
                if (View.World.TryGetDrawnPosition(effect.Follows, out var position))
                {
                    var heading = _headings.GetValueOrDefault(effect.Follows);
                    var shell = new Basis(Vector3.Up, heading).Scaled(new Vector3(1.12f, 1.06f, 1.12f));
                    _bodies.Add(new Transform3D(shell, position), WithAlpha(Colors.White, 0.75f * left));
                }

                break;

            case EffectKind.Fallen:
                var sunk = new Basis(Vector3.Up, effect.Heading).Scaled(new Vector3(1f, Mathf.Lerp(1f, 0.08f, gone), 1f));
                _bodies.Add(new Transform3D(sunk, effect.At), WithAlpha(effect.Color.Darkened(0.35f), 0.85f * left));
                break;

            case EffectKind.Blast:
                // Both grow fast and slow down as they reach their size: the ring to the blast's own radius.
                var grown = 1f - Mathf.Pow(1f - Mathf.Min(gone / BlastGrowth, 1f), 2f);
                var reach = effect.Radius * Mathf.Lerp(0.2f, 1f, grown);
                _rings.Add(new Transform3D(Basis.FromScale(new Vector3(reach, 0.25f, reach)), effect.At + (Vector3.Up * 0.06f)), WithAlpha(SparkColor, left));
                AddBall(middle, effect.Radius * Mathf.Lerp(0.3f, 1.1f, grown), MuzzleColor, 0.6f * left * left);
                break;

            case EffectKind.Ring:
                var size = Mathf.Lerp(0.35f, 1.15f, gone);
                _rings.Add(new Transform3D(Basis.FromScale(new Vector3(size, 0.25f, size)), effect.At + (Vector3.Up * 0.05f)), WithAlpha(effect.Color, left));
                break;

            default:
                break;
        }
    }

    private void AddBall(Vector3 center, float size, Color color, float alpha) =>
        _balls.Add(new Transform3D(Basis.FromScale(Vector3.One * size), center), WithAlpha(color, alpha));

    private static Color WithAlpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);

    private void RememberHeadings()
    {
        var match = View.Match;
        var health = match.Health;
        for (var i = 0; i < health.Count; i++)
        {
            var entity = health.EntityAt(i);
            if (match.Movers.TryGet(entity, out var mover))
            {
                _headings[entity] = HeadingOf(mover.Facing);
            }
        }

        foreach (var entity in _headings.Keys)
        {
            if (!health.Contains(entity))
            {
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _headings.Remove(entity);
        }

        _gone.Clear();
    }

    // The turn about the vertical axis that points the unit shape along a facing, as the world view turns its units.
    private static float HeadingOf(Angle facing)
    {
        var angle = facing.Units * Mathf.Tau / Angle.UnitsPerTurn;
        return Mathf.Atan2(-Mathf.Cos(angle), -Mathf.Sin(angle));
    }

    private enum EffectKind
    {
        Spark,
        Muzzle,
        Puff,
        Streak,
        Flash,
        Fallen,
        Ring,
        Blast,
    }

    private sealed class Effect(EffectKind kind, Vector3 at, double life)
    {
        public EffectKind Kind { get; } = kind;

        /// <summary>Where it is, on the ground.</summary>
        public Vector3 At { get; } = at;

        /// <summary>How many seconds it lasts.</summary>
        public double Life { get; } = life;

        public double Age { get; set; }

        /// <summary>Where a streak starts, on the ground.</summary>
        public Vector3 From { get; init; }

        /// <summary>The unit a flash stays on.</summary>
        public EntityId Follows { get; init; }

        public Color Color { get; init; }

        public float Heading { get; init; }

        /// <summary>How far a blast reaches from its middle, in tiles.</summary>
        public float Radius { get; init; }
    }
}
