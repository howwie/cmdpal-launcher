using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HowWieYoong.CmdPalLauncher.Profiles;

/// <summary>Root of profiles.json.</summary>
internal sealed class ProfilesFile
{
    public List<LaunchProfile> Profiles { get; set; } = [];

    /// <summary>Properties this version doesn't know about, kept so saving from the editor doesn't drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A named set of apps that are started together.</summary>
internal sealed class LaunchProfile
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Segoe Fluent glyph, emoji, or path to an image/exe.</summary>
    public string? Icon { get; set; }

    public List<AppEntry> Apps { get; set; } = [];

    /// <summary>Properties this version doesn't know about, kept so saving from the editor doesn't drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One app to start as part of a profile.</summary>
internal sealed class AppEntry
{
    /// <summary>"app" (default) or "terminal".</summary>
    public string? Type { get; set; }

    /// <summary>Display name, used in the details pane and error messages.</summary>
    public string? Name { get; set; }

    /// <summary>Executable, document, or anything the Run dialog accepts. Environment variables are expanded.</summary>
    public string? Path { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    public bool RunAsAdmin { get; set; }

    /// <summary>Don't start the app if a process with the same name is already running.</summary>
    public bool SkipIfRunning { get; set; }

    /// <summary>Milliseconds to wait before starting this app.</summary>
    public int DelayMs { get; set; }

    /// <summary>Terminal only: the window to open tabs in. Defaults to "new"; use "last" to add to the most recent window.</summary>
    public string? Window { get; set; }

    /// <summary>Terminal only: tabs to open.</summary>
    public List<TerminalTab>? Tabs { get; set; }

    /// <summary>Monitor to put the window on: "primary", a number counted left to right, or the monitor's name.</summary>
    public MonitorSelector? Monitor { get; set; }

    /// <summary>"normal", "maximized" or "minimized".</summary>
    public string? WindowState { get; set; }

    /// <summary>Window position and size, relative to the monitor's work area.</summary>
    public WindowBounds? Bounds { get; set; }

    /// <summary>Process that owns the window, when it isn't the one in "path" (e.g. a launcher or a document).</summary>
    public string? WindowProcess { get; set; }

    /// <summary>Only place a window whose title contains this text.</summary>
    public string? WindowTitle { get; set; }

    /// <summary>How long to wait for the window to appear. Defaults to 15 seconds.</summary>
    public int WindowTimeoutMs { get; set; }

    /// <summary>Properties this version doesn't know about, kept so saving from the editor doesn't drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool IsTerminal => string.Equals(Type, "terminal", System.StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool HasPlacement => Monitor is not null || Bounds is not null || WindowState is not null;

    [JsonIgnore]
    public bool IsMaximized => string.Equals(WindowState, "maximized", System.StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsMinimized => string.Equals(WindowState, "minimized", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The state to leave the window in, or null to keep the one it opened in.</summary>
    [JsonIgnore]
    public Desktop.WindowShow? Show =>
        WindowState is null ? null
        : IsMaximized ? Desktop.WindowShow.Maximized
        : IsMinimized ? Desktop.WindowShow.Minimized
        : Desktop.WindowShow.Normal;
}

/// <summary>A window position in pixels. Width and height of 0 keep the window's own size.</summary>
internal sealed class WindowBounds
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Properties this version doesn't know about, kept so saving from the editor doesn't drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Either a monitor number (1 = leftmost) or a name such as "primary", "DELL U2720Q" or "DISPLAY2".</summary>
[JsonConverter(typeof(MonitorSelectorConverter))]
internal sealed record MonitorSelector(int? Number, string? Name)
{
    public bool IsPrimary => string.Equals(Name, "primary", System.StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? $"\"{Name}\"";
}

internal sealed class MonitorSelectorConverter : JsonConverter<MonitorSelector>
{
    public override MonitorSelector Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return new MonitorSelector(number, null);
        }

        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 } text)
        {
            return int.TryParse(text, out number) ? new MonitorSelector(number, null) : new MonitorSelector(null, text);
        }

        throw new JsonException("\"monitor\" must be a number or a monitor name.");
    }

    public override void Write(Utf8JsonWriter writer, MonitorSelector value, JsonSerializerOptions options)
    {
        if (value.Number is int number)
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteStringValue(value.Name);
        }
    }
}

/// <summary>A Windows Terminal tab.</summary>
internal sealed class TerminalTab
{
    public string? Title { get; set; }

    /// <summary>Starting directory. Environment variables are expanded.</summary>
    public string? Directory { get; set; }

    /// <summary>Windows Terminal profile name, e.g. "PowerShell" or "Ubuntu".</summary>
    public string? Profile { get; set; }

    /// <summary>Command line to run instead of the profile's default shell.</summary>
    public string? Command { get; set; }

    /// <summary>Tab colour, e.g. "#0078D4".</summary>
    public string? Color { get; set; }

    /// <summary>Properties this version doesn't know about, kept so saving from the editor doesn't drop them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ProfilesFile))]
[JsonSerializable(typeof(LaunchProfile))]
[JsonSerializable(typeof(AppEntry))]
[JsonSerializable(typeof(TerminalTab))]
internal sealed partial class ProfilesJsonContext : JsonSerializerContext;
