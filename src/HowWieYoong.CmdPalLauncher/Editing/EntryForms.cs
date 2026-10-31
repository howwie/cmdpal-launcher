using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Editing;

/// <summary>The profile name, description and icon.</summary>
internal static class ProfileForm
{
    public static string Template(LaunchProfile? profile) => Card.Form(profile is null ? "Create profile" : "Save", [.. Fields(profile)]);

    public static List<JsonObject?> Fields(LaunchProfile? profile) =>
    [
        Card.Text("name", "Name", profile?.Name, "e.g. Development", required: true),
        Card.Text("description", "Description", profile?.Description, "Shown under the name; defaults to the list of apps"),
        Card.Text("icon", "Icon", IconText.ToEditable(profile?.Icon), @"Optional: an emoji, a Segoe Fluent icon code such as \uE943, or a path to an image or exe"),
    ];

    public static (string Name, string? Description, string? Icon) Read(string payload) => Read(FormValues.Parse(payload));

    public static (string Name, string? Description, string? Icon) Read(FormValues values) =>
        (values.Text("name") ?? string.Empty, values.Text("description"), IconText.FromEditable(values.Text("icon")));
}

/// <summary>Everything about one app entry except Terminal tabs.</summary>
internal static class AppForm
{
    /// <summary>A form for just this app.</summary>
    public static string Template(AppEntry app, IReadOnlyList<MonitorInfo> monitors) =>
        Card.Form("Save", [Card.Heading(app.IsTerminal ? "Windows Terminal" : "App"), .. Fields(app, monitors)]);

    /// <summary>The app's inputs, for <see cref="Template"/> or a section of a larger form.</summary>
    public static List<JsonObject?> Fields(AppEntry app, IReadOnlyList<MonitorInfo> monitors)
    {
        var bounds = app.Bounds;
        return
        [
            Card.Text("name", "Name", app.Name, app.IsTerminal ? "Windows Terminal" : "Shown in the details pane and in error messages"),

            // Terminal is always started with wt.exe, so there's no path to set. A path written in profiles.json
            // by hand (e.g. for a development build) isn't shown, and is kept when the form is saved.
            app.IsTerminal ? null : Card.Text("path", "Path", app.Path, @"e.g. C:\Program Files\App\app.exe, code, or a folder", required: true),
            Card.Text("arguments", "Arguments", app.Arguments, app.IsTerminal ? "Added after the tabs, e.g. --maximized" : "Command-line arguments"),
            app.IsTerminal ? null : Card.Text("workingDirectory", "Working directory", app.WorkingDirectory),
            app.IsTerminal ? Card.Text("window", "Window", app.Window, "new") : null,
            app.IsTerminal ? Card.Note("\"new\" opens a new window; \"last\" adds the tabs to the most recent one. Only new windows can be moved below.") : null,
            Card.Toggle("runAsAdmin", "Run as administrator", app.RunAsAdmin),
            Card.Toggle("skipIfRunning", "Don't start it if it's already running", app.SkipIfRunning),
            Card.Number("delayMs", "Wait before starting (milliseconds)", NonZero(app.DelayMs), "0", min: 0),
            Card.Heading("Window placement"),
            Card.Choice("monitor", "Monitor", MonitorValue(app.Monitor, monitors), MonitorChoices(app.Monitor, monitors)),
            Card.Choice(
                "windowState",
                "Window state",
                app.WindowState?.ToLowerInvariant() ?? string.Empty,
                [("Leave as it opens", string.Empty), ("Normal", "normal"), ("Maximized", "maximized"), ("Minimized", "minimized")]),
            Card.Note("Position and size are in pixels from the top-left of the monitor, not counting the taskbar. Leave them empty to centre the window at its own size. For a maximized or minimized window, they're where it goes when it's restored."),
            Card.Columns(
                Card.Number("boundsX", "X", bounds?.X),
                Card.Number("boundsY", "Y", bounds?.Y),
                Card.Number("boundsWidth", "Width", NonZero(bounds?.Width), min: 0),
                Card.Number("boundsHeight", "Height", NonZero(bounds?.Height), min: 0)),
            // Terminal's windows always belong to WindowsTerminal.exe (Preview and Canary builds too), which is the default.
            app.IsTerminal ? null : Card.Text("windowProcess", "Window process", app.WindowProcess, "Only if the window belongs to a different process than the path"),
            Card.Text("windowTitle", "Window title contains", app.WindowTitle, "Only move a window whose title contains this"),
            Card.Number("windowTimeoutMs", "Wait for the window (milliseconds)", NonZero(app.WindowTimeoutMs), "15000", min: 0),
        ];
    }

    /// <summary>Copies the submitted values into <paramref name="app"/>. Returns null, or what's wrong with the input.</summary>
    public static string? Apply(AppEntry app, string payload) => Apply(app, FormValues.Parse(payload));

    /// <inheritdoc cref="Apply(AppEntry, string)"/>
    public static string? Apply(AppEntry app, FormValues values)
    {
        string? error = null;

        // Fields a form doesn't show (Path and Window process for Terminal) keep their saved values.
        var path = values.Has("path") ? values.Text("path") : app.Path;
        if (!app.IsTerminal && path is null)
        {
            return "Path is required.";
        }

        var delay = values.Number("delayMs", "Wait before starting", ref error, min: 0);
        var timeout = values.Number("windowTimeoutMs", "Wait for the window", ref error, min: 0);
        var x = values.Number("boundsX", "X", ref error);
        var y = values.Number("boundsY", "Y", ref error);
        var width = values.Number("boundsWidth", "Width", ref error, min: 0);
        var height = values.Number("boundsHeight", "Height", ref error, min: 0);
        if (error is not null)
        {
            return error;
        }

        app.Name = values.Text("name");
        app.Path = path;
        app.Arguments = values.Text("arguments");
        app.WorkingDirectory = app.IsTerminal ? app.WorkingDirectory : values.Text("workingDirectory");
        app.Window = app.IsTerminal ? values.Text("window") : app.Window;
        app.RunAsAdmin = values.Toggle("runAsAdmin");
        app.SkipIfRunning = values.Toggle("skipIfRunning");
        app.DelayMs = delay ?? 0;
        app.Monitor = ParseMonitor(values.Text("monitor"));
        app.WindowState = values.Text("windowState");
        if (values.Has("boundsX") || values.Has("boundsY") || values.Has("boundsWidth") || values.Has("boundsHeight"))
        {
            app.Bounds = x is null && y is null && width is null && height is null
                ? null
                : new WindowBounds { X = x ?? 0, Y = y ?? 0, Width = width ?? 0, Height = height ?? 0, ExtensionData = app.Bounds?.ExtensionData };
        }
        app.WindowProcess = values.Has("windowProcess") ? values.Text("windowProcess") : app.WindowProcess;
        app.WindowTitle = values.Text("windowTitle");
        app.WindowTimeoutMs = timeout ?? 0;
        return null;
    }

    /// <summary>
    /// "Don't move", "Primary", then each connected monitor. A saved monitor that isn't connected stays selectable,
    /// so opening and saving the form never loses it.
    /// </summary>
    public static List<(string Title, string Value)> MonitorChoices(MonitorSelector? current, IReadOnlyList<MonitorInfo> monitors)
    {
        var choices = new List<(string Title, string Value)> { ("Don't move the window", string.Empty), ("Primary monitor", "primary") };
        var currentMonitor = current is { IsPrimary: false } ? MonitorLayout.Resolve(current, monitors) : null;

        foreach (var monitor in monitors)
        {
            var value = monitor == currentMonitor ? SelectorValue(current!) : WindowCapture.MonitorText(monitor, monitors);
            choices.Add(($"Monitor {monitor.Number}: {monitor.Name}{(monitor.IsPrimary ? " (primary)" : string.Empty)}", value));
        }

        if (current is { IsPrimary: false } && currentMonitor is null)
        {
            choices.Add(($"{SelectorValue(current)} (not connected)", SelectorValue(current)));
        }

        return choices;
    }

    public static MonitorSelector? ParseMonitor(string? value) =>
        value is null ? null
        : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? new MonitorSelector(number, null)
        : new MonitorSelector(null, value);

    private static string MonitorValue(MonitorSelector? current, IReadOnlyList<MonitorInfo> monitors) =>
        current is null ? string.Empty : current.IsPrimary ? "primary" : SelectorValue(current);

    private static string SelectorValue(MonitorSelector selector) =>
        selector.Number?.ToString(CultureInfo.InvariantCulture) ?? selector.Name ?? string.Empty;

    private static int? NonZero(int? value) => value is null or 0 ? null : value;
}

/// <summary>One Windows Terminal tab.</summary>
internal static class TabForm
{
    public static string Template(TerminalTab? tab) => Card.Form(tab is null ? "Add tab" : "Save", [.. Fields(tab)]);

    public static List<JsonObject?> Fields(TerminalTab? tab) =>
    [
        Card.Text("title", "Title", tab?.Title, "Stays fixed; the shell can't rename it"),
        Card.Text("directory", "Starting directory", tab?.Directory, @"e.g. D:\Development\api"),
        Card.Text("profile", "Terminal profile", tab?.Profile, "e.g. PowerShell, Command Prompt or Ubuntu; empty uses your default"),
        Card.Text("command", "Command", tab?.Command, "Runs instead of the profile's shell, e.g. pwsh -NoExit -Command npm run dev"),
        Card.Text("color", "Tab colour", tab?.Color, "e.g. #0078D4"),
    ];

    public static string? Apply(TerminalTab tab, string payload) => Apply(tab, FormValues.Parse(payload));

    public static string? Apply(TerminalTab tab, FormValues values)
    {
        tab.Title = values.Text("title");
        tab.Directory = values.Text("directory");
        tab.Profile = values.Text("profile");
        tab.Command = values.Text("command");
        tab.Color = values.Text("color");

        return new[] { tab.Title, tab.Directory, tab.Profile, tab.Command, tab.Color }.All(v => v is null)
            ? "Fill in at least a title, directory, profile, command or colour."
            : null;
    }
}
