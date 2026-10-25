using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace HowWieYoong.CmdPalLauncher.Editing;

/// <summary>
/// Builds Adaptive Card forms with the current values already filled in, so no template data binding is needed.
/// </summary>
internal static class Card
{
    public static string Form(string submitTitle, params JsonObject?[] body) =>
        Form(body, Submit(submitTitle, null));

    /// <summary>A form whose buttons at the bottom are <paramref name="actions"/>.</summary>
    public static string Form(IEnumerable<JsonObject?> body, params JsonObject[] actions) =>
        new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.6",
            ["body"] = Array(body),
            ["actions"] = Array(actions),
        }.ToJsonString();

    /// <summary>
    /// A button that submits every input on the form, plus <paramref name="data"/> so the extension can tell which
    /// button was pressed.
    /// </summary>
    public static JsonObject Submit(string title, JsonObject? data, bool destructive = false)
    {
        var action = new JsonObject { ["type"] = "Action.Submit", ["title"] = title };
        if (data is not null)
        {
            action["data"] = data;
        }

        if (destructive)
        {
            action["style"] = "destructive";
        }

        return action;
    }

    /// <summary>A button that shows or hides elements without submitting anything.</summary>
    public static JsonObject ToggleVisibility(string title, params string[] targetIds) => new()
    {
        ["type"] = "Action.ToggleVisibility",
        ["title"] = title,
        ["targetElements"] = new JsonArray([.. targetIds.Select(id => (JsonNode)id)]),
    };

    /// <summary>A row of buttons inside the form.</summary>
    public static JsonObject Buttons(params JsonObject[] actions) => new() { ["type"] = "ActionSet", ["actions"] = Array(actions) };
    public static JsonObject Container(IEnumerable<JsonObject?> items, string? id = null, bool visible = true, string? style = null)
    {
        var container = new JsonObject { ["type"] = "Container", ["items"] = Array(items) };
        if (id is not null)
        {
            container["id"] = id;
        }

        if (!visible)
        {
            container["isVisible"] = false;
        }

        if (style is not null)
        {
            container["style"] = style;
        }

        return container;
    }

    /// <summary>An image at a fixed size, e.g. from a data: URI.</summary>
    public static JsonObject Image(string url, int pixels, string altText) => new()
    {
        ["type"] = "Image",
        ["url"] = url,
        ["width"] = $"{pixels}px",
        ["height"] = $"{pixels}px",
        ["altText"] = altText,
    };

    /// <summary>
    /// Columns laid out side by side; width is "auto", "stretch" or a pixel size like "32px". Clicking anywhere in
    /// the row runs <paramref name="selectAction"/>, if given.
    /// </summary>
    public static JsonObject Row(JsonObject? selectAction, params (string Width, JsonObject?[] Items)[] columns)
    {
        var row = new JsonObject
        {
            ["type"] = "ColumnSet",
            ["columns"] = new JsonArray([.. columns.Select(c => (JsonNode)new JsonObject
            {
                ["type"] = "Column",
                ["width"] = c.Width,
                ["verticalContentAlignment"] = "Center",
                ["items"] = Array(c.Items),
            })]),
        };

        if (selectAction is not null)
        {
            row["selectAction"] = selectAction;
        }

        return row;
    }

    public static JsonObject TextBlock(string text, bool bold = false, bool subtle = false, string? size = null, string? id = null, bool visible = true)
    {
        var block = new JsonObject { ["type"] = "TextBlock", ["text"] = text, ["wrap"] = true };
        if (id is not null)
        {
            block["id"] = id;
        }

        if (!visible)
        {
            block["isVisible"] = false;
        }

        if (bold)
        {
            block["weight"] = "Bolder";
        }

        if (subtle)
        {
            block["isSubtle"] = true;
            block["spacing"] = "None";
        }

        if (size is not null)
        {
            block["size"] = size;
        }

        return block;
    }

    /// <summary>
    /// Makes <paramref name="elements"/> one section of a larger form: input ids get <paramref name="prefix"/>
    /// (read back with <see cref="FormValues.Section"/>). "Required" is dropped, because the client would refuse
    /// every button on the form while any section had an empty required field; the extension checks instead.
    /// </summary>
    public static List<JsonObject> Section(string prefix, IEnumerable<JsonObject?> elements)
    {
        var section = elements.OfType<JsonObject>().ToList();
        foreach (var element in section)
        {
            PrefixInputs(element, prefix);
        }

        return section;
    }

    private static void PrefixInputs(JsonNode? node, string prefix)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["type"]?.GetValue<string>() is { } type && type.StartsWith("Input.", System.StringComparison.Ordinal))
                {
                    obj["id"] = prefix + obj["id"]!.GetValue<string>();
                    obj.Remove("isRequired");
                    obj.Remove("errorMessage");
                }

                foreach (var child in obj.Select(p => p.Value).ToList())
                {
                    PrefixInputs(child, prefix);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    PrefixInputs(child, prefix);
                }

                break;
        }
    }

    private static JsonArray Array(IEnumerable<JsonObject?> items) => new([.. items.OfType<JsonObject>().Select(i => (JsonNode)i)]);

    public static JsonObject Heading(string text) => new()
    {
        ["type"] = "TextBlock",
        ["text"] = text,
        ["weight"] = "Bolder",
        ["size"] = "Medium",
        ["separator"] = true,
        ["spacing"] = "Large",
    };

    public static JsonObject Note(string text) => new()
    {
        ["type"] = "TextBlock",
        ["text"] = text,
        ["wrap"] = true,
        ["isSubtle"] = true,
        ["spacing"] = "Small",
    };

    public static JsonObject Text(string id, string label, string? value, string? placeholder = null, bool required = false, bool multiline = false)
    {
        var input = new JsonObject
        {
            ["type"] = "Input.Text",
            ["id"] = id,
            ["label"] = label,
            ["value"] = value ?? string.Empty,
        };

        if (placeholder is not null)
        {
            input["placeholder"] = placeholder;
        }

        if (required)
        {
            input["isRequired"] = true;
            input["errorMessage"] = $"{label} is required";
        }

        if (multiline)
        {
            input["isMultiline"] = true;
        }

        return input;
    }

    /// <summary>A number field; a null <paramref name="value"/> leaves it empty, meaning "not set".</summary>
    public static JsonObject Number(string id, string label, int? value, string? placeholder = null, int? min = null)
    {
        var input = new JsonObject { ["type"] = "Input.Number", ["id"] = id, ["label"] = label };
        if (value is int number)
        {
            input["value"] = number;
        }

        if (placeholder is not null)
        {
            input["placeholder"] = placeholder;
        }

        if (min is int minimum)
        {
            input["min"] = minimum;
        }

        return input;
    }

    public static JsonObject Toggle(string id, string title, bool value) => new()
    {
        ["type"] = "Input.Toggle",
        ["id"] = id,
        ["title"] = title,
        ["value"] = value ? "true" : "false",
        ["valueOn"] = "true",
        ["valueOff"] = "false",
    };

    public static JsonObject Choice(string id, string label, string value, IEnumerable<(string Title, string Value)> choices) => new()
    {
        ["type"] = "Input.ChoiceSet",
        ["id"] = id,
        ["label"] = label,
        ["style"] = "compact",
        ["value"] = value,
        ["choices"] = new JsonArray([.. choices.Select(c => (JsonNode)new JsonObject { ["title"] = c.Title, ["value"] = c.Value })]),
    };

    /// <summary>Lays inputs out side by side, e.g. X and Y.</summary>
    public static JsonObject Columns(params JsonObject[] items) => new()
    {
        ["type"] = "ColumnSet",
        ["columns"] = new JsonArray([.. items.Select(item => (JsonNode)new JsonObject
        {
            ["type"] = "Column",
            ["width"] = "stretch",
            ["items"] = new JsonArray(item),
        })]),
    };
}
