using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HowWieYoong.CmdPalLauncher.Tests.Helpers;

/// <summary>Reads Adaptive Card JSON the way Command Palette would use it.</summary>
internal static class CardJson
{
    /// <summary>Every input in a card, by id, however deeply it's nested.</summary>
    public static Dictionary<string, JsonObject> Inputs(string cardJson) =>
        Elements(cardJson).Where(e => Type(e).StartsWith("Input.", System.StringComparison.Ordinal)).ToDictionary(e => e["id"]!.GetValue<string>());

    /// <summary>Every JSON object in the card that has a "type", in document order.</summary>
    public static List<JsonObject> Elements(string cardJson)
    {
        var elements = new List<JsonObject>();
        Collect(JsonNode.Parse(cardJson)!);
        return elements;

        void Collect(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["type"] is not null)
                    {
                        elements.Add(obj);
                    }

                    foreach (var child in obj.Select(p => p.Value).ToList())
                    {
                        Collect(child);
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        Collect(child);
                    }

                    break;
            }
        }
    }

    public static string Type(JsonObject element) => element["type"]?.GetValue<string>() ?? string.Empty;

    /// <summary>An input's current value as the string Command Palette would submit, or null when it has none.</summary>
    public static string? Value(Dictionary<string, JsonObject> inputs, string id) =>
        inputs[id]["value"] is { } value ? (value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString()) : null;

    /// <summary>What Command Palette would submit if the form were sent without changes, with optional edits.</summary>
    public static string Submit(string cardJson, Dictionary<string, string>? changes = null)
    {
        var inputs = Inputs(cardJson);
        var payload = new JsonObject();
        foreach (var id in inputs.Keys)
        {
            payload[id] = changes is not null && changes.TryGetValue(id, out var changed) ? changed : Value(inputs, id) ?? string.Empty;
        }

        return payload.ToJsonString();
    }

    /// <summary>The data of each Action.Submit button, by title (the first one with each title).</summary>
    public static Dictionary<string, string> Buttons(string cardJson) =>
        Elements(cardJson)
            .Where(e => Type(e) == "Action.Submit" && e["data"] is not null)
            .GroupBy(e => e["title"]!.GetValue<string>())
            .ToDictionary(g => g.Key, g => g.First()["data"]!.ToJsonString());
}
