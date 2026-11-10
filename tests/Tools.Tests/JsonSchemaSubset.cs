using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tools.Tests;

/// <summary>
/// A validator for the part of JSON Schema draft-07 that the files in <c>schemas/</c> use. It exists
/// so that the tests can hold the schemas to the data without a package. A keyword it does not know
/// is an error and never ignored, so a schema cannot pass by using something this class skips.
/// </summary>
internal sealed class JsonSchemaSubset
{
    private readonly JsonElement _root;

    public JsonSchemaSubset(JsonElement root)
    {
        _root = root;
    }

    public IReadOnlyList<string> Validate(JsonElement instance)
    {
        var errors = new List<string>();
        Check(_root, instance, "$", errors);
        return errors;
    }

    private void Check(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        foreach (var keyword in schema.EnumerateObject())
        {
            var rule = keyword.Value;
            switch (keyword.Name)
            {
                case "$schema" or "title" or "description" or "definitions":
                    break;

                case "$ref":
                    Check(Resolve(rule.GetString()!), value, path, errors);
                    break;

                case "allOf":
                    foreach (var part in rule.EnumerateArray())
                    {
                        Check(part, value, path, errors);
                    }

                    break;

                case "anyOf":
                    if (!rule.EnumerateArray().Any(option => Matches(option, value)))
                    {
                        errors.Add($"{path}: matches none of the allowed forms");
                    }

                    break;

                case "type":
                    if (!HasType(value, rule.GetString()!))
                    {
                        errors.Add($"{path}: is not of type {rule.GetString()}");
                    }

                    break;

                case "enum":
                    if (!rule.EnumerateArray().Any(allowed => JsonElement.DeepEquals(allowed, value)))
                    {
                        errors.Add($"{path}: is not one of the allowed values");
                    }

                    break;

                case "pattern":
                    if (value.ValueKind == JsonValueKind.String && !Regex.IsMatch(value.GetString()!, rule.GetString()!, RegexOptions.CultureInvariant))
                    {
                        errors.Add($"{path}: does not match {rule.GetString()}");
                    }

                    break;

                case "minimum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDecimal() < rule.GetDecimal())
                    {
                        errors.Add($"{path}: is below {rule.GetRawText()}");
                    }

                    break;

                case "maximum":
                    if (value.ValueKind == JsonValueKind.Number && value.GetDecimal() > rule.GetDecimal())
                    {
                        errors.Add($"{path}: is above {rule.GetRawText()}");
                    }

                    break;

                case "minItems":
                    if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() < rule.GetInt32())
                    {
                        errors.Add($"{path}: has fewer than {rule.GetInt32()} items");
                    }

                    break;

                case "maxItems":
                    if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > rule.GetInt32())
                    {
                        errors.Add($"{path}: has more than {rule.GetInt32()} items");
                    }

                    break;

                case "uniqueItems":
                    if (rule.GetBoolean() && value.ValueKind == JsonValueKind.Array)
                    {
                        var items = value.EnumerateArray().ToList();
                        if (items.Where((item, index) => items.Take(index).Any(earlier => JsonElement.DeepEquals(earlier, item))).Any())
                        {
                            errors.Add($"{path}: has the same item twice");
                        }
                    }

                    break;

                case "items":
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        var index = 0;
                        foreach (var item in value.EnumerateArray())
                        {
                            Check(rule, item, $"{path}[{index++}]", errors);
                        }
                    }

                    break;

                case "required":
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var name in rule.EnumerateArray())
                        {
                            if (!value.TryGetProperty(name.GetString()!, out _))
                            {
                                errors.Add($"{path}: has no {name.GetString()}");
                            }
                        }
                    }

                    break;

                case "properties":
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in rule.EnumerateObject())
                        {
                            if (value.TryGetProperty(property.Name, out var member))
                            {
                                Check(property.Value, member, $"{path}.{property.Name}", errors);
                            }
                        }
                    }

                    break;

                case "additionalProperties":
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var member in value.EnumerateObject())
                        {
                            if (schema.TryGetProperty("properties", out var declared) && declared.TryGetProperty(member.Name, out _))
                            {
                                continue;
                            }

                            if (rule.ValueKind == JsonValueKind.False)
                            {
                                errors.Add($"{path}: has an unknown key {member.Name}");
                            }
                            else if (rule.ValueKind == JsonValueKind.Object)
                            {
                                Check(rule, member.Value, $"{path}.{member.Name}", errors);
                            }
                        }
                    }

                    break;

                case "propertyNames":
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var member in value.EnumerateObject())
                        {
                            Check(rule, JsonSerializer.SerializeToElement(member.Name), $"{path}.{member.Name} (key)", errors);
                        }
                    }

                    break;

                default:
                    throw new NotSupportedException($"JsonSchemaSubset does not implement the keyword '{keyword.Name}'.");
            }
        }
    }

    private bool Matches(JsonElement schema, JsonElement value)
    {
        var errors = new List<string>();
        Check(schema, value, "$", errors);
        return errors.Count == 0;
    }

    private JsonElement Resolve(string reference)
    {
        const string Prefix = "#/definitions/";
        return reference.StartsWith(Prefix, StringComparison.Ordinal)
            ? _root.GetProperty("definitions").GetProperty(reference[Prefix.Length..])
            : throw new NotSupportedException($"JsonSchemaSubset resolves only {Prefix} references, not '{reference}'.");
    }

    private static bool HasType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number
            && decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number == decimal.Truncate(number),
        _ => throw new NotSupportedException($"JsonSchemaSubset does not implement the type '{type}'."),
    };
}
