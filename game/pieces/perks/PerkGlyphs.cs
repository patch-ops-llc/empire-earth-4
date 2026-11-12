using System.Globalization;
using Godot;

namespace Game;

/// <summary>The small signs a perk is shown with.</summary>
public enum PerkGlyph
{
    /// <summary>An upright cross: a unit that is healing.</summary>
    Plus,

    /// <summary>Two solid arrowheads, one behind the other, that point the way they are turned: a charge.</summary>
    Chevrons,

    /// <summary>A star of four points: where a charged attack landed.</summary>
    Burst,
}

/// <summary>
/// Many small signs, each with a place in the world, a color and a size of its own, drawn as
/// this one node. A sign faces the camera and is sized on the screen: as large as it is in tiles
/// while the camera is near, and never smaller than <see cref="MinPixels"/>, so that it can
/// still be read at the widest zoom, where a tile is a few pixels. It is never hidden behind a
/// unit or a tree.
/// </summary>
/// <remarks>
/// A layer says again in every frame what there is, as with <see cref="InstancedShapes"/>. The
/// signs are drawn by a shader, the way the accent badges are, because a sign that keeps its
/// size on the screen is one draw call for all of them this way.
/// </remarks>
public partial class PerkGlyphs : MultiMeshInstance3D
{
    /// <summary>The smallest and the largest a sign's side gets on the screen, in pixels.</summary>
    public const float MinPixels = 10f;

    /// <summary>The smallest the side of a <see cref="PerkGlyph.Plus"/> gets on the screen, in pixels.</summary>
    public const float MinPlusPixels = 8f;

    public const float MaxPixels = 40f;

    /// <summary>The dark edge round a sign, which keeps it apart from the ground and from a sign beside it.</summary>
    public static readonly Color Rim = new(0.05f, 0.055f, 0.07f);

    private readonly List<(Transform3D Place, Color Color, Color More)> _frame = [];

    /// <summary>Makes the node for the signs of one layer and puts it under the layer.</summary>
    /// <param name="layer">The layer the signs are drawn under.</param>
    /// <param name="name">The name of the node.</param>
    /// <param name="area">The part of the world the signs can be in, which Godot must be told because they move.</param>
    public static PerkGlyphs Under(Node3D layer, string name, Aabb area)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode() }, RenderPriority = 11 };
        var glyphs = new PerkGlyphs
        {
            Name = name,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = new QuadMesh { Size = Vector2.One, Material = material },
                InstanceCount = 32,
                VisibleInstanceCount = 0,
                CustomAabb = area,
            },
            CastShadow = ShadowCastingSetting.Off,
        };
        layer.AddChild(glyphs);
        return glyphs;
    }

    /// <summary>The number of signs added since <see cref="Begin"/>.</summary>
    public int Count => _frame.Count;

    /// <summary>Starts a frame with no sign in it.</summary>
    public void Begin() => _frame.Clear();

    /// <summary>Adds a sign to the frame.</summary>
    /// <param name="glyph">Which sign.</param>
    /// <param name="at">The point of the world it is drawn at.</param>
    /// <param name="side">The side of the square it is drawn in, in tiles, while that is between the two limits in pixels.</param>
    /// <param name="color">Its color. The alpha is how solid it is.</param>
    /// <param name="turn">For <see cref="PerkGlyph.Chevrons"/>, the way they point on the screen: an angle counted from the right, counterclockwise.</param>
    /// <param name="shift">How far it is moved on the screen from its point, in sides of its own square: x to the right, y up.</param>
    public void Add(PerkGlyph glyph, Vector3 at, float side, Color color, float turn = 0f, Vector2 shift = default) =>
        _frame.Add((new Transform3D(Basis.FromScale(Vector3.One * side), at), color, new Color((float)glyph, turn, shift.X, shift.Y)));

    /// <summary>Shows the signs of the frame, and no others.</summary>
    public void End()
    {
        var signs = Multimesh;
        if (_frame.Count > signs.InstanceCount)
        {
            // Changing the count throws the old signs away, which is fine: all are placed again below.
            signs.VisibleInstanceCount = 0;
            signs.InstanceCount = Math.Max(_frame.Count, signs.InstanceCount * 2);
        }

        for (var i = 0; i < _frame.Count; i++)
        {
            signs.SetInstanceTransform(i, _frame[i].Place);
            signs.SetInstanceColor(i, _frame[i].Color);
            signs.SetInstanceCustomData(i, _frame[i].More);
        }

        signs.VisibleInstanceCount = _frame.Count;
    }

    /// <summary>
    /// The way a direction on the ground points on the screen, as the angle a sign is turned by:
    /// counted from the right of the screen, counterclockwise.
    /// </summary>
    public static float TurnOnScreen(Basis camera, Vector3 along) => Mathf.Atan2(along.Dot(camera.Y), along.Dot(camera.X));

    private static string ShaderCode()
    {
        static string Number(float value) => value.ToString("0.0###", CultureInfo.InvariantCulture);

        return $$"""
            shader_type spatial;
            render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled;

            const float MIN_PIXELS = {{Number(MinPixels)}};
            const float MIN_PLUS_PIXELS = {{Number(MinPlusPixels)}};
            const float MAX_PIXELS = {{Number(MaxPixels)}};
            const vec3 RIM_COLOR = vec3({{Number(Rim.R)}}, {{Number(Rim.G)}}, {{Number(Rim.B)}});

            varying float glyph;
            varying float turn;
            varying float side_pixels;
            varying vec2 local;

            // The colors are given as the interface has them, and the renderer works in linear light.
            vec3 to_linear(vec3 color) {
                return mix(pow((color + vec3(0.055)) / 1.055, vec3(2.4)), color / 12.92, lessThan(color, vec3(0.04045)));
            }

            // How far a point is outside a box round the middle, and how far inside as a negative number.
            float box(vec2 point, vec2 half_size) {
                vec2 outside = abs(point) - half_size;
                return length(max(outside, vec2(0.0))) + min(max(outside.x, outside.y), 0.0);
            }

            // A solid arrowhead that points to the right, with its tip at this x. It is solid
            // so that its color still shows when it is a few pixels wide.
            float arrowhead(vec2 point, float tip) {
                vec2 slope = normalize(vec2(0.32, 0.24));
                return max(dot(vec2(point.x - tip, abs(point.y)), slope), tip - 0.24 - point.x);
            }

            void vertex() {
                // The instance's custom data: x is the sign, y its turn, z and w its shift in sides.
                glyph = INSTANCE_CUSTOM.x;
                turn = INSTANCE_CUSTOM.y;
                local = VERTEX.xy;
                float world_side = length(MODEL_MATRIX[0].xyz);
                vec4 anchor = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
                float pixels_per_tile = abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y / (2.0 * max(-anchor.z, 0.001));
                // The cross of a healing unit may be smaller than the rest: there is one for every unit of a crowd.
                side_pixels = clamp(world_side * pixels_per_tile, glyph < 0.5 ? MIN_PLUS_PIXELS : MIN_PIXELS, MAX_PIXELS);
                anchor.xy += (INSTANCE_CUSTOM.zw + VERTEX.xy) * side_pixels / pixels_per_tile;
                POSITION = PROJECTION_MATRIX * anchor;
            }

            void fragment() {
                // From the middle of the sign, in shares of its side: x to the right, y up.
                vec2 point = local;
                float edge;
                if (glyph < 0.5) {
                    edge = min(box(point, vec2(0.36, 0.13)), box(point, vec2(0.13, 0.36)));
                } else if (glyph < 1.5) {
                    point = mat2(vec2(cos(turn), -sin(turn)), vec2(sin(turn), cos(turn))) * point;
                    edge = min(arrowhead(point, 0.3), arrowhead(point, -0.04));
                } else {
                    // Four points: the sides curve in toward the middle.
                    vec2 folded = abs(point);
                    edge = 0.4 * (pow(folded.x, 0.6) + pow(folded.y, 0.6) - pow(0.46, 0.6));
                }

                // The edges are a pixel soft, whatever the size.
                float pixel = 1.0 / side_pixels;
                float inside = 1.0 - smoothstep(-0.5 * pixel, 0.5 * pixel, edge);
                if (glyph > 1.5) {
                    // A flash has no rim: it is pale in the middle and its own color at the points.
                    if (inside <= 0.0) {
                        discard;
                    }

                    float core = 1.0 - smoothstep(0.02, 0.2, length(point));
                    ALBEDO = to_linear(mix(COLOR.rgb, vec3(1.0), 0.85 * core));
                    ALPHA = inside * COLOR.a;
                } else {
                    // The rim is a little over a pixel wide.
                    float rim = 1.25 * pixel;
                    float solid = 1.0 - smoothstep(rim - (0.5 * pixel), rim + (0.5 * pixel), edge);
                    if (solid <= 0.0) {
                        discard;
                    }

                    ALBEDO = to_linear(mix(RIM_COLOR, COLOR.rgb, inside));
                    ALPHA = solid * COLOR.a;
                }
            }
            """;
    }
}
