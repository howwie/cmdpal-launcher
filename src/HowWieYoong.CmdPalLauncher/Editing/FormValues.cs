using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HowWieYoong.CmdPalLauncher.Editing;

/// <summary>Reads the values of a submitted Adaptive Card form, which arrive as a JSON object of strings keyed by input id.</summary>
internal sealed class FormValues
{
    private readonly JsonObject _values;
    private readonly string _prefix;

    private FormValues(JsonObject values, string prefix)
    {
        _values = values;
        _prefix = prefix;
    }

    public static FormValues Parse(string payload)
    {
        try
        {
            return new FormValues(JsonNode.Parse(payload) as JsonObject ?? [], string.Empty);
        }
        catch (JsonException)
        {
            return new FormValues([], string.Empty);
        }
    }

    /// <summary>
    /// The values of one section of a larger form, whose input ids all start with <paramref name="prefix"/>
    /// (e.g. "a2_" for the third app). Ids passed to the returned object leave the prefix out.
    /// </summary>
    public FormValues Section(string prefix) => new(_values, _prefix + prefix);

    /// <summary>Whether the form had this input at all (as opposed to having it empty).</summary>
    public bool Has(string id) => _values.ContainsKey(_prefix + id);

    /// <summary>The trimmed text, or null when the field is empty.</summary>
    public string? Text(string id)
    {
        var node = _values[_prefix + id];
        var text = node is JsonValue value && value.TryGetValue<string>(out var s) ? s : node?.ToJsonString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public bool Toggle(string id) => string.Equals(Text(id), "true", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A whole number, or null when the field is empty. Sets <paramref name="error"/> when it isn't a whole number
    /// or is below <paramref name="min"/>.
    /// </summary>
    public int? Number(string id, string label, ref string? error, int? min = null)
    {
        if (Text(id) is not { } text)
        {
            return null;
        }

        // Number inputs can submit "1200.0".
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number != System.Math.Floor(number) || number is > int.MaxValue or < int.MinValue)
        {
            error ??= $"{label} must be a whole number.";
            return null;
        }

        if (min is int minimum && number < minimum)
        {
            error ??= $"{label} can't be less than {minimum}.";
            return null;
        }

        return (int)number;
    }
}
