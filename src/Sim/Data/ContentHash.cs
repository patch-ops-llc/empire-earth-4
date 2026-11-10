using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sim.Data;

/// <summary>
/// The content hash that goes into the lobby handshake and the replay header. It is SHA-256 over a
/// canonical text of every data file, so two installs agree exactly when their data means the same:
/// comments, spacing, line endings, the order of keys and the editor's <c>$schema</c> key do not
/// count, and every value does.
/// </summary>
/// <remarks>
/// The canonical text starts with a version line. Then, for each file in ordinal order of its path,
/// comes the path, a line feed, the file's JSON and a line feed. The JSON has no spaces, its object
/// keys are in ordinal order and its strings escape only the quote, the backslash and control
/// characters. The text is hashed as UTF-8.
/// </remarks>
internal static class ContentHash
{
    private const string VersionLine = "content-hash-v1\n";

    public static string Compute(IEnumerable<KeyValuePair<string, JsonElement>> filesInPathOrder)
    {
        var text = new StringBuilder(VersionLine);
        foreach (var (path, root) in filesInPathOrder)
        {
            text.Append(path).Append('\n');
            Write(text, root, isFileRoot: true);
            text.Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Write(StringBuilder text, JsonElement element, bool isFileRoot)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var members = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!isFileRoot || property.Name != DataRules.SchemaKey)
                    {
                        members[property.Name] = property.Value;
                    }
                }

                text.Append('{');
                var first = true;
                foreach (var (name, value) in members)
                {
                    if (!first)
                    {
                        text.Append(',');
                    }

                    first = false;
                    WriteString(text, name);
                    text.Append(':');
                    Write(text, value, isFileRoot: false);
                }

                text.Append('}');
                break;

            case JsonValueKind.Array:
                text.Append('[');
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index++ > 0)
                    {
                        text.Append(',');
                    }

                    Write(text, item, isFileRoot: false);
                }

                text.Append(']');
                break;

            case JsonValueKind.String:
                WriteString(text, element.GetString() ?? "");
                break;

            case JsonValueKind.Number:
                // The loader has already rejected every number that is not a whole number.
                var raw = element.GetRawText();
                text.Append(long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)
                    ? whole.ToString(CultureInfo.InvariantCulture)
                    : raw);
                break;

            case JsonValueKind.True:
                text.Append("true");
                break;

            case JsonValueKind.False:
                text.Append("false");
                break;

            default:
                text.Append("null");
                break;
        }
    }

    private static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var character in value)
        {
            if (character is '"' or '\\')
            {
                text.Append('\\').Append(character);
            }
            else if (character < ' ')
            {
                text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(character);
            }
        }

        text.Append('"');
    }
}
