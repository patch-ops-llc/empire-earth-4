using System.Globalization;
using System.Text.Json;

namespace Sim.Data;

internal sealed class ErrorList
{
    private readonly List<DataError> _errors = [];

    public int Count => _errors.Count;

    public void Add(string file, string location, string message) => _errors.Add(new DataError(file, location, message));

    public IReadOnlyList<DataError> ToList() => [.. _errors];
}

/// <summary>
/// One JSON value together with where it came from. Every reader records an error and returns a
/// harmless default when the value has the wrong shape, so a file reader can read the whole file,
/// report everything wrong with it at once and then discard the result if anything was reported.
/// A value that is absent (its key was missing, or its parent had the wrong shape) reads as the
/// default without a second error.
/// </summary>
internal readonly struct JsonValue
{
    private readonly JsonElement _element;
    private readonly bool _present;
    private readonly ErrorList _errors;

    public JsonValue(JsonElement element, string file, string path, ErrorList errors)
        : this(element, present: true, file, path, errors)
    {
    }

    private JsonValue(JsonElement element, bool present, string file, string path, ErrorList errors)
    {
        _element = element;
        _present = present;
        _errors = errors;
        File = file;
        Path = path;
    }

    public string File { get; }

    public string Path { get; }

    /// <summary>Errors recorded so far in the whole load, for skipping checks that would only repeat one.</summary>
    public int ErrorCount => _errors.Count;

    public static JsonValue Absent(string file, string path, ErrorList errors) => new(default, present: false, file, path, errors);

    public void Error(string message) => _errors.Add(File, Path, message);

    /// <summary>Records an error at another location in the same file.</summary>
    public void ErrorAt(string location, string message) => _errors.Add(File, location, message);

    public JsonObject Object()
    {
        if (!_present)
        {
            return JsonObject.Absent(File, Path, _errors);
        }

        if (_element.ValueKind != JsonValueKind.Object)
        {
            Error($"expected an object, found {Describe()}");
            return JsonObject.Absent(File, Path, _errors);
        }

        return new JsonObject(_element, File, Path, _errors);
    }

    public IReadOnlyList<JsonValue> Array(int minCount = 0, int maxCount = int.MaxValue)
    {
        if (!_present)
        {
            return [];
        }

        if (_element.ValueKind != JsonValueKind.Array)
        {
            Error($"expected a list, found {Describe()}");
            return [];
        }

        var items = new List<JsonValue>(_element.GetArrayLength());
        foreach (var item in _element.EnumerateArray())
        {
            items.Add(new JsonValue(item, File, $"{Path}[{items.Count}]", _errors));
        }

        if (items.Count < minCount || items.Count > maxCount)
        {
            Error(minCount == maxCount
                ? $"must have exactly {minCount} entries, found {items.Count}"
                : maxCount == int.MaxValue
                    ? $"must have at least {minCount} entries, found {items.Count}"
                    : $"must have between {minCount} and {maxCount} entries, found {items.Count}");
        }

        return items;
    }

    /// <summary>Text that is not empty and has no leading or trailing space.</summary>
    public string String()
    {
        if (!_present)
        {
            return "";
        }

        if (_element.ValueKind != JsonValueKind.String)
        {
            Error($"expected text, found {Describe()}");
            return "";
        }

        if (!TryGetText(out var text))
        {
            Error("is not valid Unicode text");
            return "";
        }

        if (text.Length == 0 || text.Trim().Length != text.Length)
        {
            Error("must not be empty or start or end with a space");
            return "";
        }

        return text;
    }

    /// <summary>An ID: lowercase letters, digits and underscores, starting with a letter.</summary>
    public string Id()
    {
        var text = String();
        if (text.Length > 0 && !DataRules.IsId(text))
        {
            Error($"'{text}' is not an ID: use lowercase letters, digits and underscores, starting with a letter");
            return "";
        }

        return text;
    }

    public int Integer(int min, int max)
    {
        if (!_present)
        {
            return min;
        }

        if (_element.ValueKind != JsonValueKind.Number)
        {
            Error($"expected a whole number, found {Describe()}");
            return min;
        }

        var raw = _element.GetRawText();
        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            Error($"expected a whole number, found {raw}");
            return min;
        }

        if (value < min || value > max)
        {
            Error($"must be between {min} and {max}, found {value}");
            return min;
        }

        return (int)value;
    }

    /// <summary>
    /// A number that may have a fraction. A whole number may be written as a JSON number; anything with
    /// a fraction must be written as text, such as <c>"2.6"</c>.
    /// </summary>
    public DataNumber Number(DataNumber min, DataNumber max, bool aboveMin = false)
    {
        if (!_present)
        {
            return min;
        }

        DataNumber value;
        if (_element.ValueKind == JsonValueKind.Number)
        {
            var raw = _element.GetRawText();
            if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)
                || !DataNumber.TryParse(whole.ToString(CultureInfo.InvariantCulture), out value))
            {
                Error($"write {raw} as text in plain decimal notation, such as \"2.6\"; only whole numbers may be bare");
                return min;
            }
        }
        else if (_element.ValueKind == JsonValueKind.String)
        {
            if (!TryGetText(out var text) || !DataNumber.TryParse(text, out value))
            {
                Error($"{_element.GetRawText()} is not a decimal number such as \"2.6\" "
                    + $"(at most {DataNumber.MaxScale} decimal places and {DataNumber.MaxDigits} digits, no exponent)");
                return min;
            }
        }
        else
        {
            Error($"expected a number, found {Describe()}");
            return min;
        }

        if (value < min || value > max || (aboveMin && value <= min))
        {
            Error(aboveMin
                ? $"must be above {min} and at most {max}, found {value}"
                : $"must be between {min} and {max}, found {value}");
            return min;
        }

        return value;
    }

    private bool TryGetText(out string text)
    {
        try
        {
            text = _element.GetString() ?? "";
            return true;
        }
        catch (InvalidOperationException)
        {
            text = "";
            return false;
        }
    }

    private string Describe() => _element.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        JsonValueKind.String => "text",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        _ => "null",
    };
}

/// <summary>A JSON object that remembers which keys were read, so the rest can be reported as unknown.</summary>
internal sealed class JsonObject
{
    private readonly SortedDictionary<string, JsonElement> _members = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _unread = new(StringComparer.Ordinal);
    private readonly List<string> _keysInFileOrder = [];
    private readonly bool _present;
    private readonly string _file;
    private readonly string _path;
    private readonly ErrorList _errors;

    public JsonObject(JsonElement element, string file, string path, ErrorList errors)
    {
        _present = true;
        _file = file;
        _path = path;
        _errors = errors;
        foreach (var property in element.EnumerateObject())
        {
            _members[property.Name] = property.Value;
            _unread.Add(property.Name);
            _keysInFileOrder.Add(property.Name);
        }
    }

    private JsonObject(string file, string path, ErrorList errors)
    {
        _present = false;
        _file = file;
        _path = path;
        _errors = errors;
    }

    public static JsonObject Absent(string file, string path, ErrorList errors) => new(file, path, errors);

    public JsonValue Required(string key)
    {
        if (Optional(key) is { } value)
        {
            return value;
        }

        if (_present)
        {
            _errors.Add(_file, _path, $"missing key \"{key}\"");
        }

        return JsonValue.Absent(_file, ChildPath(key), _errors);
    }

    public JsonValue? Optional(string key)
    {
        if (!_members.TryGetValue(key, out var element))
        {
            return null;
        }

        _unread.Remove(key);
        return new JsonValue(element, _file, ChildPath(key), _errors);
    }

    /// <summary>Every member, in the order the file lists them, for objects whose keys are data.</summary>
    public IReadOnlyList<(string Key, JsonValue Value)> Members()
    {
        var members = new List<(string, JsonValue)>(_keysInFileOrder.Count);
        foreach (var key in _keysInFileOrder)
        {
            members.Add((key, new JsonValue(_members[key], _file, ChildPath(key), _errors)));
        }

        _unread.Clear();
        return members;
    }

    /// <summary>Reports every key that no reader asked for. Call it once the object has been read.</summary>
    public void End()
    {
        foreach (var key in _unread)
        {
            _errors.Add(_file, ChildPath(key), $"unknown key \"{key}\"");
        }

        _unread.Clear();
    }

    private string ChildPath(string key) => $"{_path}.{key}";
}
