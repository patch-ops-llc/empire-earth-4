using System.Globalization;
using Godot;
using Sim.Core;
using Sim.Tech;

namespace Game;

/// <summary>
/// The accent badges over the units (design 2.3 and 3.2): for each accent a unit carries, a
/// small square in the track's color, with one upright mark for level I and two for level II.
/// A unit carries two at most, side by side, the stronger on the left. They tell an opponent
/// which tracks a player is ahead in, so they are drawn on everyone's units alike: on the
/// player's own, and on another player's while the player sees it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is read.</b> The badges of a unit are the accents the simulation's
/// <see cref="Sim.Vision.PlayerView.Inspect"/> gives for it, which answers only for a unit the
/// player sees. Nothing else of another player's research is read here. They are read again
/// after every tick, so a research that finishes changes the badges of the units that live in
/// the same tick, and an accent that a research displaces goes as the new one comes.
/// </para>
/// <para>
/// <b>How they are drawn.</b> All badges are one node, a multimesh of squares that face the
/// camera, and so one draw call however many units there are. A badge is sized on the screen
/// and not in the world: <see cref="BadgeLayout"/> says how large at the zoom of the frame, in
/// whole pixels, and the shader puts every badge on whole pixels, so that a mark one pixel wide
/// is drawn sharp. Near, a badge is <see cref="WorldSize"/> tiles. Far, it keeps
/// <see cref="MinPixels"/>, and once two of those no longer fit over one unit it becomes the
/// small badge, whose level is counted in squares: color and level can then still be read at
/// the widest zoom, where a tile is a dozen pixels, and the badges of units that stand a tile
/// apart do not cover each other.
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

    /// <summary>The smallest and the largest side of a full badge, in pixels on a window of the project's own height.</summary>
    public const float MinPixels = 9f;
    public const float MaxPixels = 22f;

    /// <summary>The smallest side of one square of the small badge, in pixels on a window of the project's own height.</summary>
    public const float PipPixels = 5f;

    /// <summary>
    /// The share of a tile's width that the two badges of one unit may take before they become
    /// small, and the share of the room between two rows of units that a small badge may take.
    /// </summary>
    public const float Fit = 0.9f;

    /// <summary>
    /// How far over the top of a unit the badges are anchored, in tiles: just over the health
    /// bar of the combat view, whose middle is 0.62 above the unit and which is 0.15 tall with
    /// its edge.
    /// </summary>
    public const float AnchorHeight = 0.72f;

    /// <summary>How far above the anchor the lower edge of a badge is, in pixels.</summary>
    public const float LiftPixels = 2f;

    /// <summary>The room between two full badges of one unit, in pixels.</summary>
    public const float GapPixels = 2f;

    /// <summary>The width and height of a level mark and the distance between the middles of two, as shares of the badge's side.</summary>
    public const float MarkWidth = 0.14f;
    public const float MarkHeight = 0.5f;
    public const float MarkSpacing = 0.3f;

    /// <summary>The dark edge round a badge, which keeps it apart from the ground and from the badge beside it.</summary>
    public static readonly Color Rim = new(0.05f, 0.055f, 0.07f);

    /// <summary>The color of a level mark.</summary>
    public static readonly Color Mark = new(0.97f, 0.97f, 0.98f);

    private static readonly StringName CompactParameter = "compact";
    private static readonly StringName SizesParameter = "sizes";
    private static readonly StringName MarksParameter = "marks";

    private readonly List<ShownBadge> _badges = [];
    private MultiMesh _mesh = null!;
    private ShaderMaterial _material = null!;
    private BadgeLayout? _handed;

    /// <summary>The number of badges drawn after the last tick.</summary>
    public int Count => _badges.Count;

    /// <summary>The badges drawn after the last tick, unit by unit, and within a unit from the left.</summary>
    public IReadOnlyList<ShownBadge> Shown => _badges;

    /// <summary>The sizes the badges have in this frame.</summary>
    public BadgeLayout Layout { get; private set; } = BadgeLayout.For(100f, 100f, 1f);

    protected override void Attach()
    {
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode() } };
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = _material },
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
        var data = View.Match.Data;
        _badges.Clear();

        // The world draws the units the player may be shown, and only those carry badges.
        foreach (var entity in View.World.Units)
        {
            if (!TryReadAccents(entity, out var accents))
            {
                continue;
            }

            for (var place = 0; place < accents.Count; place++)
            {
                _badges.Add(new ShownBadge(entity, accents[place].Track, accents[place].Level, place, accents.Count));
            }
        }

        if (_badges.Count > _mesh.InstanceCount)
        {
            _mesh.InstanceCount = Math.Max(_badges.Count, _mesh.InstanceCount * 2);
        }

        // Color and level change only with a tick. Where a badge stands changes with every frame.
        for (var i = 0; i < _badges.Count; i++)
        {
            var badge = _badges[i];

            // Where the badge stands in its unit's row, in steps from the middle.
            var along = badge.Place - ((badge.Of - 1) / 2f);
            _mesh.SetInstanceColor(i, TrackColors.Of(data, badge.Track));
            _mesh.SetInstanceCustomData(i, new Color(badge.Level, along, 0f, 0f));
        }

        Place();
    }

    public override void EveryFrame(double delta) => Place();

    /// <summary>
    /// Measures the screen at the point the camera looks at, in pixels of the window: the width
    /// of a tile, how far apart two points are that lie a tile behind each other, and the pixels
    /// to one unit of the interface. False where there is no camera or no window to measure.
    /// </summary>
    public bool TryMeasure(out float pixelsPerTile, out float pixelsPerRow, out float pixelsPerUnit)
    {
        pixelsPerTile = 0f;
        pixelsPerRow = 0f;
        pixelsPerUnit = 1f;
        var viewport = GetViewport();
        if (viewport?.GetCamera3D() is not { } camera)
        {
            return false;
        }

        // The camera answers in units of the interface, which is stretched over the window.
        var units = viewport.GetVisibleRect().Size;
        var pixels = viewport is Window window ? (Vector2)window.Size : units;
        if (units.Y <= 0f || pixels.Y <= 0f)
        {
            return false;
        }

        var focus = new Vector3(View.Camera.Focus.X, 0f, View.Camera.Focus.Y);
        var middle = camera.UnprojectPosition(focus);
        pixelsPerUnit = pixels.Y / units.Y;
        pixelsPerTile = camera.UnprojectPosition(focus + camera.GlobalBasis.X).DistanceTo(middle) * pixelsPerUnit;
        pixelsPerRow = Mathf.Abs(camera.UnprojectPosition(focus + Vector3.Back).Y - middle.Y) * pixelsPerUnit;
        return pixelsPerTile > 0f && pixelsPerRow > 0f;
    }

    // The accents a unit shows. The simulation answers for the player's own units and for those
    // the player sees. A unit the world draws only because a tester has lifted the fog from the
    // screen (DEVELOPMENT ONLY, see PlayerSight.FogOff) is read from the match itself.
    private bool TryReadAccents(EntityId entity, out IReadOnlyList<Accent> accents)
    {
        if (View.Sight.Seen.Inspect(entity, out var inspection))
        {
            accents = inspection.Accents;
            return true;
        }

        var match = View.Match;
        if (View.Sight.FogOff
            && match.Combatants.TryGet(entity, out var combatant)
            && match.Bodies.TryGet(entity, out var body)
            && !match.Structures.Contains(entity))
        {
            accents = match.Research.Tech(body.Owner).Blueprints[combatant.Chassis].Accents;
            return true;
        }

        accents = [];
        return false;
    }

    private void Place()
    {
        if (TryMeasure(out var pixelsPerTile, out var pixelsPerRow, out var pixelsPerUnit))
        {
            Layout = BadgeLayout.For(pixelsPerTile, pixelsPerRow, pixelsPerUnit);
        }

        if (_handed != Layout)
        {
            _handed = Layout;
            _material.SetShaderParameter(CompactParameter, Layout.Compact);
            _material.SetShaderParameter(SizesParameter, new Vector4(Layout.Width, Layout.Height, Layout.Rim, Layout.Step));
            _material.SetShaderParameter(MarksParameter, new Vector4(Layout.MarkWidth, Layout.MarkHeight, Layout.MarkSpacing, Layout.Lift));
        }

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
    // color and a level for each of many is one draw call this way. The sizes come from the
    // layout of the frame, in whole pixels.
    private static string ShaderCode()
    {
        static string Number(float value) => value.ToString("0.0###", CultureInfo.InvariantCulture);
        static string Rgb(Color color) => $"vec3({Number(color.R)}, {Number(color.G)}, {Number(color.B)})";

        return $$"""
            shader_type spatial;
            render_mode unshaded, cull_disabled, depth_test_disabled, shadows_disabled, fog_disabled;

            // The layout of the frame, in pixels. sizes: the width and the height of a box, the
            // rim, the step from one badge of a unit to the next. marks: the width and the height
            // of a level mark, the step from the first to the second, the lift over the anchor.
            uniform bool compact = false;
            uniform vec4 sizes = vec4(22.0, 22.0, 2.0, 24.0);
            uniform vec4 marks = vec4(4.0, 12.0, 8.0, 2.0);

            const vec3 RIM_COLOR = {{Rgb(Rim)}};
            const vec3 MARK_COLOR = {{Rgb(Mark)}};

            varying flat float level;
            varying flat vec2 size;

            // The colors are given as the interface has them, and the renderer works in linear light.
            vec3 to_linear(vec3 color) {
                return mix(pow((color + vec3(0.055)) / 1.055, vec3(2.4)), color / 12.92, lessThan(color, vec3(0.04045)));
            }

            void vertex() {
                // The instance's custom data: x is the level, y the place in the unit's row.
                level = INSTANCE_CUSTOM.x;
                size = vec2(sizes.x, compact && level > 1.5 ? (2.0 * sizes.y) - sizes.z : sizes.y);

                // The anchor on the screen, in pixels counted from the lower left, and from it
                // the lower left corner of the badge, on a whole pixel. Some renderers hand the
                // projection over with its y turned, so up is taken from the projection itself.
                vec4 anchor = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0));
                vec2 up = vec2(1.0, PROJECTION_MATRIX[1][1] < 0.0 ? -1.0 : 1.0);
                vec2 on_screen = ((anchor.xy / anchor.w) * up * 0.5 + 0.5) * VIEWPORT_SIZE;
                vec2 corner = floor(on_screen + vec2((INSTANCE_CUSTOM.y * sizes.w) - (sizes.x * 0.5), marks.w) + 0.5);
                vec2 point = corner + ((VERTEX.xy + 0.5) * size);
                POSITION = vec4(((point / VIEWPORT_SIZE) * 2.0 - 1.0) * up * anchor.w, anchor.z, anchor.w);
            }

            void fragment() {
                // The pixel of the badge, counted from its lower left corner.
                vec2 at = vec2(UV.x, 1.0 - UV.y) * size;
                vec2 pixel = floor(at);
                float side = sizes.x;
                float rim = sizes.z;
                vec3 color = COLOR.rgb;

                if (compact) {
                    // One box, or two with the rim of the lower as the line between them.
                    bool on_rim = pixel.x < rim || pixel.x >= size.x - rim || pixel.y < rim || pixel.y >= size.y - rim;
                    bool between = level > 1.5 && pixel.y >= sizes.y - rim && pixel.y < sizes.y;
                    if (on_rim || between) {
                        color = RIM_COLOR;
                    }
                } else {
                    // A square with round corners, and the rim inside its edge.
                    float corner_radius = 0.16 * side;
                    vec2 outside = abs(at - (size * 0.5)) - (size * 0.5 - corner_radius);
                    float edge = length(max(outside, vec2(0.0))) + min(max(outside.x, outside.y), 0.0) - corner_radius;
                    if (edge > 0.0) {
                        discard;
                    }

                    if (edge > -rim) {
                        color = RIM_COLOR;
                    }

                    // The marks, each on whole pixels.
                    float count = level > 1.5 ? 2.0 : 1.0;
                    float left = floor((side - marks.x - ((count - 1.0) * marks.z)) * 0.5);
                    float bottom = floor((side - marks.y) * 0.5);
                    if (pixel.y >= bottom && pixel.y < bottom + marks.y) {
                        for (float mark = 0.0; mark < count; mark += 1.0) {
                            float from = left + (mark * marks.z);
                            if (pixel.x >= from && pixel.x < from + marks.x) {
                                color = MARK_COLOR;
                            }
                        }
                    }
                }

                ALBEDO = to_linear(color);
            }
            """;
    }
}

/// <summary>One accent badge as the world shows it over a unit.</summary>
/// <param name="Entity">The unit.</param>
/// <param name="Track">The track of the accent, whose color the badge has.</param>
/// <param name="Level">The level of the accent: 1 or 2.</param>
/// <param name="Place">Its place among the unit's badges, from the left: 0 is the stronger accent.</param>
/// <param name="Of">How many badges the unit shows.</param>
public readonly record struct ShownBadge(EntityId Entity, int Track, int Level, int Place, int Of);
