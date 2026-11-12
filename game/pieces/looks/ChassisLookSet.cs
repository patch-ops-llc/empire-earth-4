using System.Globalization;
using System.Text.Json;
using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// The look of every chassis that has one (design 3.2, "which chassis you field"): read once
/// from the presentation data, <c>game/view-data/chassis-looks.jsonc</c>, which names for each
/// chassis a shape file under <c>game/assets/units/</c>. A chassis the file does not name keeps
/// the stand-in shape and its mark, so new content can be played before it has a look.
/// </summary>
/// <remarks>
/// <para>
/// The files are presentation only. They are not part of <c>game/data</c>, the simulation never
/// reads them, and the content hash does not cover them: two players with different looks play
/// the same match.
/// </para>
/// <para>
/// <b>The looks file</b> has two keys. <c>tones</c> names the paints: each is a color as
/// <c>"#rrggbb"</c>, or <c>{ "owner": share }</c> for the owner's color, darkened to that share.
/// <c>looks</c> lists <c>{ "chassis": id, "shape": file }</c>, the file relative to
/// <c>game/assets/units/</c>.
/// </para>
/// <para>
/// <b>A shape file</b> has one key, <c>parts</c>. A part has a <c>shape</c> (<c>box</c>,
/// <c>cylinder</c> or <c>ball</c>), the <c>size</c> of the box it fills as <c>[x, y, z]</c> in
/// tiles, the place of its middle <c>at</c> <c>[x, y, z]</c>, and a <c>tone</c>. It may have a
/// <c>turn</c> of <c>[x, y, z]</c> degrees about its middle, <c>sides</c> for a cylinder or a
/// ball and a <c>spin</c> in degrees about its own axis before the turn, which matters when
/// the sides are few, <c>top</c> for a cylinder that narrows (0 is a point), and <c>mirror</c>,
/// which adds the same part on the other side of the unit. The unit stands on the ground at the origin and
/// faces -Z; +X is its right.
/// </para>
/// </remarks>
public sealed class ChassisLookSet
{
    public const string LooksFile = "res://view-data/chassis-looks.jsonc";
    public const string ShapesFolder = "res://assets/units";

    private const int DefaultSides = 10;

    private static readonly JsonDocumentOptions Reading = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly string[] LooksKeys = ["$schema", "tones", "looks"];
    private static readonly string[] LookKeys = ["chassis", "shape"];
    private static readonly string[] ShapeKeys = ["$schema", "parts"];
    private static readonly string[] PartKeys = ["shape", "size", "at", "tone", "turn", "spin", "sides", "top", "mirror"];

    private static GameData? _loadedFor;
    private static ChassisLookSet _loaded = new([], []);

    private readonly ChassisLook?[] _byChassis;

    private ChassisLookSet(ChassisLook?[] byChassis, IReadOnlyList<LookTone> tones)
    {
        _byChassis = byChassis;
        Looks = [.. byChassis.OfType<ChassisLook>()];
        Tones = tones;
    }

    /// <summary>The tones the looks are painted in, each at the place of its <see cref="LookTone.Index"/>.</summary>
    public IReadOnlyList<LookTone> Tones { get; }

    /// <summary>The looks, in the order of the data's chassis.</summary>
    public IReadOnlyList<ChassisLook> Looks { get; }

    /// <summary>The look of a chassis, by its index in the data, or null for a chassis that has none.</summary>
    public ChassisLook? Of(int chassis) => chassis >= 0 && chassis < _byChassis.Length ? _byChassis[chassis] : null;

    /// <summary>
    /// The looks for a match's data. They are read once and kept. A file that cannot be read
    /// is reported as an error, which fails the smoke test, and leaves every chassis the
    /// stand-in shape: the match is still shown.
    /// </summary>
    public static ChassisLookSet For(GameData data)
    {
        if (ReferenceEquals(_loadedFor, data))
        {
            return _loaded;
        }

        try
        {
            _loaded = Read(data, ReadFile);
        }
        catch (Exception failed) when (failed is InvalidDataException or IOException or JsonException)
        {
            GD.PushError($"The looks of the chassis could not be read, so every unit keeps the stand-in shape. {failed.Message}");
            _loaded = new ChassisLookSet(new ChassisLook?[data.Chassis.Count], []);
        }

        _loadedFor = data;
        return _loaded;
    }

    /// <summary>Reads the looks through a function that hands out the text of a file by its path.</summary>
    /// <exception cref="InvalidDataException">A file says something the looks cannot be built from. The message names the file and the place.</exception>
    public static ChassisLookSet Read(GameData data, Func<string, string> readFile)
    {
        using var document = Parse(LooksFile, readFile(LooksFile));
        var root = document.RootElement;
        RequireKeys(root, LooksKeys, LooksFile);

        var tones = new Dictionary<string, LookTone>(StringComparer.Ordinal);
        foreach (var tone in ObjectAt(root, "tones", LooksFile).EnumerateObject())
        {
            tones.Add(tone.Name, ReadTone(tone.Name, tones.Count, tone.Value));
        }

        var chassisByName = data.Chassis.ToDictionary(chassis => chassis.Id, StringComparer.Ordinal);
        var looks = new ChassisLook?[data.Chassis.Count];
        var entry = 0;
        foreach (var look in ListAt(root, "looks", LooksFile).EnumerateArray())
        {
            var where = $"{LooksFile}, look {entry++}";
            RequireKeys(look, LookKeys, where);
            var id = Text(look, "chassis", where);
            var file = Text(look, "shape", where);
            if (!chassisByName.TryGetValue(id, out var chassis))
            {
                throw new InvalidDataException($"{where}: the data has no chassis '{id}'.");
            }

            if (looks[chassis.Index] is not null)
            {
                throw new InvalidDataException($"{where}: the chassis '{id}' is named twice.");
            }

            var path = $"{ShapesFolder}/{file}";
            var shape = ReadShape(path, readFile(path), tones);
            looks[chassis.Index] = new ChassisLook(chassis, path, shape);
        }

        return new ChassisLookSet(looks, [.. tones.Values.OrderBy(tone => tone.Index)]);
    }

    private static string ReadFile(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read)
            ?? throw new IOException($"Cannot read {path}: {Godot.FileAccess.GetOpenError()}.");
        return file.GetAsText();
    }

    private static JsonDocument Parse(string path, string text)
    {
        try
        {
            return JsonDocument.Parse(text, Reading);
        }
        catch (JsonException failed)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {failed.Message}", failed);
        }
    }

    private static LookTone ReadTone(string name, int index, JsonElement value)
    {
        var where = $"{LooksFile}, tone '{name}'";
        if (value.ValueKind == JsonValueKind.Object)
        {
            RequireKeys(value, ["owner"], where);
            var share = Number(value, "owner", where);
            return share is > 0f and <= 1f
                ? new LookTone(name, index, Owner: true, share, Colors.White)
                : throw new InvalidDataException($"{where}: the owner's share is {share.ToString(CultureInfo.InvariantCulture)} and must be above 0 and at most 1.");
        }

        if (value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: 7 } text
            && text[0] == '#'
            && int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            return new LookTone(name, index, Owner: false, 1f, new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f));
        }

        throw new InvalidDataException($"{where}: a tone is a color as \"#rrggbb\" or {{ \"owner\": share }}.");
    }

    private static LookShape ReadShape(string path, string text, Dictionary<string, LookTone> tones)
    {
        using var document = Parse(path, text);
        RequireKeys(document.RootElement, ShapeKeys, path);

        var shape = new LookShape();
        var number = 0;
        foreach (var part in ListAt(document.RootElement, "parts", path).EnumerateArray())
        {
            var where = $"{path}, part {number++}";
            RequireKeys(part, PartKeys, where);

            var kind = Text(part, "shape", where);
            var size = Triple(part, "size", where);
            var at = Triple(part, "at", where);
            var turn = part.TryGetProperty("turn", out _) ? Triple(part, "turn", where) : Vector3.Zero;
            var spin = part.TryGetProperty("spin", out _) ? Number(part, "spin", where) : 0f;
            var sides = part.TryGetProperty("sides", out _) ? (int)Number(part, "sides", where) : DefaultSides;
            var top = part.TryGetProperty("top", out _) ? Number(part, "top", where) : 1f;
            var mirror = part.TryGetProperty("mirror", out var mirrored) && mirrored.ValueKind == JsonValueKind.True;
            var toneName = Text(part, "tone", where);
            if (!tones.TryGetValue(toneName, out var tone))
            {
                throw new InvalidDataException($"{where}: there is no tone '{toneName}'. The tones are {string.Join(", ", tones.Keys)}.");
            }

            if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
            {
                throw new InvalidDataException($"{where}: every side of its size must be above 0.");
            }

            if (sides is < 3 or > 32 || top is < 0f or > 4f)
            {
                throw new InvalidDataException($"{where}: sides must be from 3 to 32 and top from 0 to 4.");
            }

            AddPart(shape, kind, size, top, sides, spin, at, turn, tone, where);
            if (mirror)
            {
                // The same part on the other side of the unit: its place and its turn seen in a mirror.
                AddPart(shape, kind, size, top, sides, -spin, new Vector3(-at.X, at.Y, at.Z), new Vector3(turn.X, -turn.Y, -turn.Z), tone, where);
            }
        }

        if (shape.Triangles.Count == 0)
        {
            throw new InvalidDataException($"{path}: the shape has no part.");
        }

        return shape;
    }

    private static void AddPart(LookShape shape, string kind, Vector3 size, float top, int sides, float spin, Vector3 at, Vector3 turn, LookTone tone, string where)
    {
        var radians = new Vector3(Mathf.DegToRad(turn.X), Mathf.DegToRad(turn.Y), Mathf.DegToRad(turn.Z));
        var place = new Transform3D(Basis.FromEuler(radians), at);
        switch (kind)
        {
            case "box":
                shape.AddBox(size, place, tone);
                break;
            case "cylinder":
                shape.AddCylinder(size, top, sides, Mathf.DegToRad(spin), place, tone);
                break;
            case "ball":
                shape.AddBall(size, sides, Mathf.DegToRad(spin), place, tone);
                break;
            default:
                throw new InvalidDataException($"{where}: there is no shape '{kind}'. A part is a box, a cylinder or a ball.");
        }
    }

    private static void RequireKeys(JsonElement element, string[] known, string where)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{where}: expected an object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(known, property.Name) < 0)
            {
                throw new InvalidDataException($"{where}: the key '{property.Name}' is not known. The keys are {string.Join(", ", known)}.");
            }
        }
    }

    private static JsonElement ObjectAt(JsonElement element, string key, string where) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"{where}: '{key}' is missing or is not an object.");

    private static JsonElement ListAt(JsonElement element, string key, string where) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException($"{where}: '{key}' is missing or is not a list.");

    private static string Text(JsonElement element, string key, string where) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException($"{where}: '{key}' is missing or is not a text.");

    private static float Number(JsonElement element, string key, string where) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number) && float.IsFinite(number)
            ? number
            : throw new InvalidDataException($"{where}: '{key}' is missing or is not a number.");

    private static Vector3 Triple(JsonElement element, string key, string where)
    {
        if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 3)
        {
            var numbers = new float[3];
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetSingle(out numbers[i]) || !float.IsFinite(numbers[i]))
                {
                    break;
                }

                i++;
            }

            if (i == 3)
            {
                return new Vector3(numbers[0], numbers[1], numbers[2]);
            }
        }

        throw new InvalidDataException($"{where}: '{key}' is missing or is not a list of three numbers.");
    }
}

/// <summary>The look of one chassis.</summary>
/// <param name="Chassis">The chassis.</param>
/// <param name="File">The shape file it was read from.</param>
/// <param name="Shape">Its shape.</param>
public sealed record ChassisLook(ChassisDef Chassis, string File, LookShape Shape)
{
    private ArrayMesh? _mesh;

    /// <summary>
    /// The shape as a mesh, made the first time it is asked for. The renderer is told a box
    /// round it that holds the look at its widest, since far from the camera it is drawn
    /// wider than the mesh is (<see cref="ChassisLooks.FarWidth"/>).
    /// </summary>
    public ArrayMesh Mesh => _mesh ??= WithRoom(Shape.ToMesh());

    private static ArrayMesh WithRoom(ArrayMesh mesh)
    {
        var box = mesh.GetAabb();
        var grown = new Vector3(ChassisLooks.FarWidth, 1f, ChassisLooks.FarWidth);
        mesh.CustomAabb = new Aabb(box.Position * grown, box.Size * grown).Merge(box);
        return mesh;
    }
}
