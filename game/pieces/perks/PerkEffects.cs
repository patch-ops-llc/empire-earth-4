using Godot;
using Sim.Core;
using Sim.Numerics;

namespace Game;

/// <summary>
/// What the perks of a fight look like (design 2.3): the splash of an Overgunned unit's hit, and
/// the attack a Rapid unit makes with Charge. Each is drawn from the events of a tick that the
/// player may know of (design 5.3), in the color of the track the perk belongs to.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// A splash: a flat wash in the color of Firepower that spreads over the ground to the reach of
/// the splash and is gone in under half a second, with drops thrown outward from the middle. A
/// shell's blast is a pale ring that stays at its edge round a burst of fire. So the two differ
/// in color, in shape and in how long they last. The hit itself is drawn as any hit is, by the
/// combat piece.
/// </item>
/// <item>Each unit a splash reached flashes in the color of Firepower, where any other hit flashes white, with a spark on it.</item>
/// <item>
/// A charged attack: a charged melee hit is a thick streak in the color of Mobility from the
/// attacker to its target and a star on the target, inside a ring that spreads over the ground.
/// A charged shot leaves with two arrowheads in that color that leap from the attacker the way
/// it faces, flies with a tail in that color, and lands with the same star and ring.
/// </item>
/// </list>
/// <para>
/// A hit is shown as charged only when the player knew of the attack: one made on ground the
/// player does not see lands like any other, as #69 has it, whatever the machine knows.
/// </para>
/// <para>
/// Nothing here is thinner or smaller on the screen than a few pixels, however far away the
/// camera is. Effects are for the eye only: nothing reads them, and a match runs the same with
/// or without them.
/// </para>
/// </remarks>
public partial class PerkEffects : WorldLayer
{
    // The most effects alive at once. A fight larger than this drops its oldest effects first.
    private const int Limit = 800;

    /// <summary>The number of drops a splash throws.</summary>
    public const int Drops = 9;

    // The part of a splash's life in which its wash grows to its full reach.
    private const float WashGrowth = 0.45f;

    private const float MinDropPixels = 3f;
    private const float MinStreakPixels = 3f;
    private const float MinRingPixels = 10f;

    private readonly ShotLayer? _shots;
    private readonly List<Effect> _effects = [];

    // The attackers whose charged attack the player was told of in this tick, with where they stood.
    private readonly Dictionary<EntityId, Vector3> _attacks = [];

    // The shots in flight that the player knows were fired with Charge.
    private readonly HashSet<EntityId> _chargedShots = [];
    private readonly List<EntityId> _gone = [];

    private InstancedShapes _washes = null!;
    private InstancedShapes _rings = null!;
    private InstancedShapes _bodies = null!;
    private InstancedShapes _streaks = null!;
    private InstancedShapes _balls = null!;
    private PerkGlyphs _glyphs = null!;
    private Color _splash;
    private Color _charge;
    private int _turn;

    /// <param name="shots">The layer that draws the shots, for the tail of a charged one, or null when there is none.</param>
    public PerkEffects(ShotLayer? shots)
    {
        _shots = shots;
    }

    /// <summary>The number of splashes shown since the match began.</summary>
    public int SplashesShown { get; private set; }

    /// <summary>The number of units shown reached by a splash since the match began.</summary>
    public int SplashHitsShown { get; private set; }

    /// <summary>The number of charged attacks shown being made since the match began.</summary>
    public int ChargesShown { get; private set; }

    /// <summary>The number of charged attacks shown landing since the match began: a melee hit, a shot or a shell.</summary>
    public int ChargedHitsShown { get; private set; }

    /// <summary>The number of shots in flight that are drawn with the tail of a charged shot.</summary>
    public int ChargedShotCount { get; private set; }

    /// <summary>The number of effects alive now.</summary>
    public int EffectCount => _effects.Count;

    protected override void Attach()
    {
        var match = View.Match;
        _splash = TrackColors.Of(match.Data, match.Data.TrackRoles.Firepower);
        _charge = TrackColors.Of(match.Data, match.Data.TrackRoles.Mobility);

        var area = InstancedShapes.MapArea(match.Map);
        var fading = InstancedShapes.Material(seeThrough: true);
        _washes = InstancedShapes.Under(this, "Washes", new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.02f, RadialSegments = 32, Rings = 1 }, fading, area);
        _rings = InstancedShapes.Under(this, "ChargeRings", PlaceholderMeshes.Ring(1f, Colors.White), fading, area);
        _bodies = InstancedShapes.Under(this, "SplashFlashes", PlaceholderMeshes.Unit(), fading, area);
        _streaks = InstancedShapes.Under(this, "Streaks", new BoxMesh { Size = Vector3.One }, fading, area);
        _balls = InstancedShapes.Under(this, "Drops", new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 }, fading, area);
        _glyphs = PerkGlyphs.Under(this, "Signs", area);
    }

    public override void AfterTick()
    {
        var match = View.Match;
        _attacks.Clear();
        foreach (var happened in match.Simulation.Events)
        {
            if (!View.Sight.Knows(happened))
            {
                continue;
            }

            var place = WorldView.ToWorld(happened.Position);
            switch (happened.Kind)
            {
                case SimEventKind.Splash:
                    SplashesShown++;
                    var reach = happened.Amount / (float)Fixed.RawOne;
                    Add(new Effect(EffectKind.Wash, place, 0.45) { Radius = reach });

                    // The drops of one splash are a turn apart from those of the last, so that two on one place differ.
                    _turn++;
                    for (var drop = 0; drop < Drops; drop++)
                    {
                        Add(new Effect(EffectKind.Drop, place, 0.4) { Radius = reach, Heading = ((drop * Mathf.Tau) + (_turn * 2.4f)) / Drops });
                    }

                    break;

                case SimEventKind.SplashHit:
                    SplashHitsShown++;
                    Add(new Effect(EffectKind.Spark, place, 0.3) { Color = _splash });
                    Add(new Effect(EffectKind.Flash, place, 0.22) { Follows = happened.Entity });
                    break;

                case SimEventKind.ChargedAttack:
                    ChargesShown++;
                    _attacks[happened.Entity] = place;
                    break;

                case SimEventKind.MeleeHit:
                    if (_attacks.Remove(happened.Other, out var from))
                    {
                        ChargedHitsShown++;
                        Add(new Effect(EffectKind.Lunge, place, 0.32) { From = from });
                        AddImpact(place);
                    }

                    break;

                case SimEventKind.ShotFired:
                    if (_attacks.Remove(happened.Other, out var stood))
                    {
                        _chargedShots.Add(happened.Entity);
                        if (match.Movers.TryGet(happened.Other, out var mover))
                        {
                            Add(new Effect(EffectKind.Leap, stood, 0.35) { Heading = CombatEffects.HeadingOf(mover.Facing) });
                        }
                    }

                    break;

                // A shell's blast comes before the hits of those it reached, and is where it landed.
                case SimEventKind.Blast:
                    if (_chargedShots.Remove(happened.Entity))
                    {
                        ChargedHitsShown++;
                        AddImpact(place);
                    }

                    break;

                case SimEventKind.ShotHit:
                    if (_chargedShots.Remove(happened.Other))
                    {
                        ChargedHitsShown++;
                        AddImpact(place);
                    }

                    break;

                default:
                    break;
            }
        }

        // A charged shot that is gone without the player having been told how, as one that
        // landed out of sight, is forgotten.
        foreach (var shot in _chargedShots)
        {
            if (!match.Shots.Contains(shot) || !match.World.Entities.IsAlive(shot))
            {
                _gone.Add(shot);
            }
        }

        foreach (var shot in _gone)
        {
            _chargedShots.Remove(shot);
        }

        _gone.Clear();
        CountChargedShots();
    }

    public override void EveryFrame(double delta)
    {
        _washes.Begin();
        _rings.Begin();
        _bodies.Begin();
        _streaks.Begin();
        _balls.Begin();
        _glyphs.Begin();

        if (GetViewport().GetCamera3D() is { } camera)
        {
            var basis = camera.GlobalBasis;
            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                var effect = _effects[i];
                effect.Age += delta;
                if (effect.Age >= effect.Life)
                {
                    _effects.RemoveAt(i);
                    continue;
                }

                Draw(effect, (float)(effect.Age / effect.Life), camera, basis);
            }

            DrawChargedShots(camera);
        }

        _washes.End();
        _rings.End();
        _bodies.End();
        _streaks.End();
        _balls.End();
        _glyphs.End();
    }

    private void Add(Effect effect)
    {
        if (_effects.Count >= Limit)
        {
            _effects.RemoveAt(0);
        }

        _effects.Add(effect);
    }

    // Where a charged attack landed: a star on what was hit, and a ring over the ground.
    private void AddImpact(Vector3 place)
    {
        Add(new Effect(EffectKind.Shock, place, 0.45));
        Add(new Effect(EffectKind.Star, place, 0.45));
    }

    // Draws one effect that is this far through its life, from 0 to 1.
    private void Draw(Effect effect, float gone, Camera3D camera, Basis basis)
    {
        var left = 1f - gone;
        var middle = effect.At + (Vector3.Up * ShotLayer.FlightHeight);
        switch (effect.Kind)
        {
            case EffectKind.Wash:
                // It grows fast and slows down as it reaches its size, and fades all the while.
                var grown = 1f - Mathf.Pow(1f - Mathf.Min(gone / WashGrowth, 1f), 2f);
                var reach = effect.Radius * Mathf.Lerp(0.3f, 1f, grown);
                _washes.Add(new Transform3D(Basis.FromScale(new Vector3(reach, 1f, reach)), effect.At + (Vector3.Up * 0.05f)), WithAlpha(_splash, 0.5f * left));
                break;

            case EffectKind.Drop:
                // Thrown outward from near the middle to the reach of the splash, over a low arc.
                var outward = new Vector3(Mathf.Cos(effect.Heading), 0f, Mathf.Sin(effect.Heading));
                var flown = 1f - Mathf.Pow(1f - gone, 2f);
                var drop = effect.At + (outward * effect.Radius * Mathf.Lerp(0.25f, 1f, flown)) + (Vector3.Up * (0.15f + (0.6f * Mathf.Sin(Mathf.Pi * gone))));
                var size = ScreenSize.AtLeast(Mathf.Lerp(0.2f, 0.1f, gone), MinDropPixels, ScreenSize.PixelsPerTile(camera, drop));
                _balls.Add(new Transform3D(Basis.FromScale(Vector3.One * size), drop), WithAlpha(_splash.Lightened(0.25f), Mathf.Min(1f, 2f * left)));
                break;

            case EffectKind.Spark:
                var spark = ScreenSize.AtLeast(Mathf.Lerp(0.16f, 0.55f, gone), 2f * MinDropPixels, ScreenSize.PixelsPerTile(camera, middle));
                _balls.Add(new Transform3D(Basis.FromScale(Vector3.One * spark), middle), WithAlpha(effect.Color.Lightened(0.35f), left));
                break;

            case EffectKind.Flash:
                // A shell in the color of Firepower just outside the unit, where the unit is drawn now.
                if (View.World.TryGetDrawnPosition(effect.Follows, out var position) && View.Match.Movers.TryGet(effect.Follows, out var mover))
                {
                    var shell = new Basis(Vector3.Up, CombatEffects.HeadingOf(mover.Facing)).Scaled(new Vector3(1.16f, 1.08f, 1.16f));
                    _bodies.Add(new Transform3D(shell, position), WithAlpha(_splash.Lightened(0.15f), 0.8f * left));
                }

                break;

            case EffectKind.Leap:
                // The arrowheads of the run-up leave the unit the way it faces, and grow as they go.
                var way = new Basis(Vector3.Up, effect.Heading) * Vector3.Forward;
                var leapt = 1f - Mathf.Pow(1f - gone, 2f);
                _glyphs.Add(
                    PerkGlyph.Chevrons,
                    effect.At + (way * (PlaceholderMeshes.UnitRadius + 0.3f + (0.9f * leapt))) + (Vector3.Up * 0.3f),
                    ChargeMarks.Side * Mathf.Lerp(1f, 1.7f, leapt),
                    WithAlpha(_charge.Lightened(0.2f), left),
                    PerkGlyphs.TurnOnScreen(basis, way));
                break;

            case EffectKind.Lunge:
                // A thick streak that runs out toward the target: its tail follows its head there.
                var tail = (effect.From + (Vector3.Up * ShotLayer.FlightHeight)).Lerp(middle, gone * 0.7f);
                var span = middle - tail;
                if (span.LengthSquared() > 0.0001f)
                {
                    var along = span.Normalized();
                    var across = along.Cross(Vector3.Up).Normalized();
                    var thick = ScreenSize.AtLeast(0.16f, MinStreakPixels, ScreenSize.PixelsPerTile(camera, middle));
                    _streaks.Add(new Transform3D(new Basis(across * thick, Vector3.Up * thick, span), tail + (span / 2f)), WithAlpha(_charge.Lightened(0.3f), 0.95f * left));
                }

                break;

            case EffectKind.Shock:
                // A ring over the ground that spreads from what was hit.
                var spread = ScreenSize.AtLeast(Mathf.Lerp(0.3f, 1.1f, 1f - Mathf.Pow(1f - gone, 2f)), MinRingPixels * Mathf.Lerp(0.5f, 1f, gone), ScreenSize.PixelsPerTile(camera, effect.At));
                _rings.Add(new Transform3D(Basis.FromScale(new Vector3(spread, 0.6f, spread)), effect.At + (Vector3.Up * 0.07f)), WithAlpha(_charge, left));
                break;

            case EffectKind.Star:
                _glyphs.Add(PerkGlyph.Burst, middle + (Vector3.Up * 0.35f), Mathf.Lerp(0.6f, 1.1f, gone), WithAlpha(_charge.Lightened(0.25f), Mathf.Min(1f, 1.6f * left)));
                break;

            default:
                break;
        }
    }

    // The tail of every charged shot in flight: a streak in the color of Mobility behind the shot.
    private void DrawChargedShots(Camera3D camera)
    {
        if (_shots is null)
        {
            return;
        }

        foreach (var shot in _chargedShots)
        {
            if (!_shots.TryGetDrawn(shot, out var head, out var along))
            {
                continue;
            }

            var across = along.Cross(Vector3.Up);
            if (across.LengthSquared() < 0.0001f)
            {
                continue;
            }

            across = across.Normalized();
            var thick = ScreenSize.AtLeast(0.14f, MinStreakPixels, ScreenSize.PixelsPerTile(camera, head));
            const float Length = 1.3f;
            _streaks.Add(
                new Transform3D(new Basis(across * thick, across.Cross(along) * thick, along * Length), head - (along * (Length / 2f))),
                WithAlpha(_charge.Lightened(0.2f), 0.75f));
        }
    }

    private void CountChargedShots()
    {
        ChargedShotCount = 0;
        if (_shots is null)
        {
            return;
        }

        foreach (var shot in _chargedShots)
        {
            ChargedShotCount += _shots.TryGetDrawn(shot, out _, out _) ? 1 : 0;
        }
    }

    private static Color WithAlpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);

    private enum EffectKind
    {
        Wash,
        Drop,
        Spark,
        Flash,
        Leap,
        Lunge,
        Shock,
        Star,
    }

    private sealed class Effect(EffectKind kind, Vector3 at, double life)
    {
        public EffectKind Kind { get; } = kind;

        /// <summary>Where it is, on the ground.</summary>
        public Vector3 At { get; } = at;

        /// <summary>How many seconds it lasts.</summary>
        public double Life { get; } = life;

        public double Age { get; set; }

        /// <summary>Where a lunge starts, on the ground.</summary>
        public Vector3 From { get; init; }

        /// <summary>The unit a flash stays on.</summary>
        public EntityId Follows { get; init; }

        public Color Color { get; init; }

        /// <summary>The way a leap goes, as the turn of a unit, or the way a drop is thrown, as an angle on the ground.</summary>
        public float Heading { get; init; }

        /// <summary>How far a splash reaches from its middle, in tiles.</summary>
        public float Radius { get; init; }
    }
}
