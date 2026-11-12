using System.Globalization;
using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The accent badges over the units of every player (design 3.2): for each accent a unit carries,
/// a small square in the track's color, with one upright mark for level I and two for level II.
/// A unit carries two at most, side by side, the stronger on the left. They tell an opponent
/// which tracks a player is ahead in, so they are drawn on everyone's units alike.
/// </summary>
/// <remarks>
/// <para>
/// All badges are one node, a multimesh of squares that face the camera. A badge is sized on the
/// screen and not in the world: as large as <see cref="WorldSize"/> tiles while the camera is
/// near, and never smaller than <see cref="MinPixels"/>, so that color and level can still be
/// read at the widest zoom, where a tile is a few pixels.
/// </para>
/// <para>
/// A badge stands <see cref="LiftPixels"/> above a point <see cref="AnchorHeight"/> over the
/// unit's top, which is the upper edge of the combat view's health bar. So the badges sit on
/// the bar when a unit has one, at every zoom, and a little above the unit when it has none.
/// </para>
/// </remarks>
public partial class AccentBadges : WorldLayer
{
    /// <summary>The side of a badge in tiles, while that is between the two limits in pixels.</summary>
    public const float WorldSize = 0.34f;

    public const float MinPixels = 9f;
    public const float MaxPixels = 22f;

    /// <summary>
    /// How far over the top of a unit the badges are anchored, in tiles: just over the health
    /// bar of the combat view, whose middle is 0.62 above the unit and which is 0.15 tall with
    /// its edge.
    /// </summary>
    public const float AnchorHeight = 0.72f;

    /// <summary>How far above the anchor the lower edge of a badge is, in pixels.</summary>
    public const float LiftPixels = 2f;

    /// <summary>The room between two badges of one unit, in pixels.</summary>
    public const float GapPixels = 2f;

    /// <summary>The width and height of a level mark and the distance between the middles of two, as shares of the badge's side.</summary>
    public const float MarkWidth = 0.14f;
    public const float MarkHeight = 0.5f;
    public const float MarkSpacing = 0.3f;

    /// <summary>The dark edge round a badge, which keeps it apart from the ground and from the badge beside it.</summary>
    public static readonly Color Rim = new(0.05f, 0.055f, 0.07f);

    /// <summary>The color of a level mark.</summary>
    public static readonly Color Mark = new(0.97f, 0.97f, 0.98f);

    private readonly List<Badge> _badges = [];
    private MultiMesh _mesh = null!;

    /// <summary>The number of badges drawn after the last tick.</summary>
    public int Count => _badges.Count;

    protected override void Attach()
    {
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode() } } },
            InstanceCount = 32,
            VisibleInstanceCount = 0,
        };

        // Godot must be told the area the badges can be in, because they move every frame.
        var map = View.Match.Map;
        _mesh.CustomAabb = new Aabb(Vector3.Zero, new Vector3(map.Width, 3f, map.Height));
        AddChild(new MultiMeshInstance3D
        {
            Name = "Badges",
            Multimesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public override void AfterTick()
    {
        var match = View.Match;
        var data = match.Data;
        _badges.Clear();
        foreach (var entity in View.World.Units)
        {
            if (!match.Combatants.TryGet(entity, out var combatant) || !match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            var accents = match.Research.Tech(body.Owner).Blueprints[combatant.Chassis].Accents;
            for (var place = 0; place < accents.Count; place++)
            {
                // Where the badge stands in its unit's row, in badge widths from the middle.
                var along = place - ((accents.Count - 1) / 2f);
                _badges.Add(new Badge(entity, TrackColors.Of(data, accents[place].Track), accents[place].Level, along));
            }
        }

        if (_badges.Count > _mesh.InstanceCount)
        {
            _mesh.InstanceCount = Math.Max(_badges.Count, _mesh.InstanceCount * 2);
        }

        // Color and level change only with a tick. Where a badge stands changes with every frame.
        for (var i = 0; i < _badges.Count; i++)
        {
            _mesh.SetInstanceColor(i, _badges[i].Color);
            _mesh.SetInstanceCustomData(i, new Color(_badges[i].Level, _badges[i].Along, 0f, 0f));
        }

        Place();
    }

    public override void EveryFrame(double delta) => Place();

    private void Place()
    {
        var placed = 0;
        var lift = Vector3.Up * (PlaceholderMeshes.UnitHeight + AnchorHeight);
        foreach (var badge in _badges)
        {
            // The badges of a tick are in the order the mesh has them, so one that cannot be
            // placed is put where nothing shows it, and the others keep their own color.
            var position = View.World.TryGetDrawnPosition(badge.Entity, out var drawn) ? drawn + lift : new Vector3(0f, -100f, 0f);
            _mesh.SetInstanceTransform(placed++, new Transform3D(Basis.Identity, position));
        }

        _mesh.VisibleInstanceCount = placed;
    }

    // The badge is drawn by a shader, because a square that keeps its size on the screen and a
    // color and a level for each of many is one draw call this way. The numbers in it are the
    // constants above.
    private static string ShaderCode()
    {
        static string Number(float value) => value.ToString("0.0###", CultureInfo.InvariantCulture);
        static string Rgb(Color color) => $"vec3({Number(color.R)}, {Number(color.G)}, {Number(color.B)})";

        return $$"""
            shader_type spatial;
            render_mode unshaded, cull_disabled, depth_test_disabled, shadows_disabled, fog_disabled;

            const float WORLD_SIZE = {{Number(WorldSize)}};
            const float MIN_PIXELS = {{Number(MinPixels)}};
            const float MAX_PIXELS = {{Number(MaxPixels)}};
            const float LIFT_PIXELS = {{Number(LiftPixels)}};
            const float GAP_PIXELS = {{Number(GapPixels)}};
            const float MARK_WIDTH = {{Number(MarkWidth)}};
            const float MARK_HEIGHT = {{Number(MarkHeight)}};
            const float MARK_SPACING = {{Number(MarkSpacing)}};
            const vec3 RIM_COLOR = {{Rgb(Rim)}};
            const vec3 MARK_COLOR = {{Rgb(Mark)}};

            varying float level;
            varying float side_pixels;

            // The colors are given as the interface has them, and the renderer works in linear light.
            vec3 to_linear(vec3 color) {
                return mix(pow((color + vec3(0.055)) / 1.055, vec3(2.4)), color / 12.92, lessThan(color, vec3(0.04045)));
            }

            void vertex() {
                // The instance's custom data: x is the level, y the place in the unit's row.
                level = INSTANCE_CUSTOM.x;
                vec4 anchor = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
                float pixels_per_tile = abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y / (2.0 * max(-anchor.z, 0.001));
                side_pixels = clamp(WORLD_SIZE * pixels_per_tile, MIN_PIXELS, MAX_PIXELS);
                vec2 corner = vec2(INSTANCE_CUSTOM.y * (side_pixels + GAP_PIXELS), LIFT_PIXELS + (side_pixels * 0.5)) + (VERTEX.xy * side_pixels);
                anchor.xy += corner / pixels_per_tile;
                POSITION = PROJECTION_MATRIX * anchor;
            }

            void fragment() {
                // From the middle of the badge, in shares of its side.
                vec2 point = UV - vec2(0.5);
                float corner_radius = 0.16;
                vec2 outside = abs(point) - vec2(0.5 - corner_radius);
                float edge = length(max(outside, vec2(0.0))) + min(max(outside.x, outside.y), 0.0) - corner_radius;
                if (edge > 0.0) {
                    discard;
                }

                // The rim is a pixel and a half wide, whatever the size.
                vec3 color = edge > -1.5 / side_pixels ? RIM_COLOR : COLOR.rgb;

                float marks = level > 1.5 ? 2.0 : 1.0;
                float first = -(marks - 1.0) * MARK_SPACING * 0.5;
                for (float mark = 0.0; mark < marks; mark += 1.0) {
                    float middle = first + (mark * MARK_SPACING);
                    if (abs(point.x - middle) <= MARK_WIDTH * 0.5 && abs(point.y) <= MARK_HEIGHT * 0.5) {
                        color = MARK_COLOR;
                    }
                }

                ALBEDO = to_linear(color);
            }
            """;
    }

    private readonly record struct Badge(EntityId Entity, Color Color, int Level, float Along);
}
