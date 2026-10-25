using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Desktop;

/// <param name="Entry">A complete profiles.json app entry that would start the app and put its window back where it is.</param>
/// <param name="Placement">Only the placement settings, for adding to an existing entry.</param>
/// <param name="Notes">Anything that couldn't be captured or was changed, to show alongside.</param>
internal sealed record CapturedWindow(string Entry, string Placement, IReadOnlyList<string> Notes);

/// <summary>Turns an open window into profiles.json settings.</summary>
internal static class WindowCapture
{
    /// <summary>Folders replaced by environment variables in captured paths, so profiles work for other users and machines.</summary>
    public static IReadOnlyList<(string Variable, string Folder)> DefaultFolders { get; } =
    [
        ("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        ("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        ("%ProgramFiles(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
        ("%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
        ("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    ];

    /// <summary>
    /// For example:
    /// <code>
    /// {
    ///   "name": "Visual Studio Code",
    ///   "path": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe",
    ///   "arguments": "D:\\Development\\api",
    ///   "monitor": "DELL U2515H",
    ///   "windowState": "normal",
    ///   "bounds": { "x": 0, "y": 0, "width": 1280, "height": 1380 }
    /// }
    /// </code>
    /// </summary>
    public static CapturedWindow Capture(
        WindowInfo window,
        ProcessDetails details,
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<(string Variable, string Folder)>? folders = null)
    {
        folders ??= DefaultFolders;
        var notes = new List<string>();
        var entry = new List<Line>();
        var isTerminal = false;
        string? path = null;

        if (Is(window.ProcessName, "WindowsTerminal"))
        {
            // WindowsTerminal.exe lives in WindowsApps and can't be started directly; wt.exe is how to open it.
            isTerminal = true;
            entry.Add(new("name", Json("Windows Terminal")));
            entry.Add(new("type", Json("terminal")));

            // Terminal keeps the wt.exe command line it was started with, which lists the tabs it opened.
            var arguments = details.CommandLine is { } commandLine ? CommandLine.SplitProgram(commandLine).Arguments : string.Empty;
            var tabs = TerminalCommandLine.ParseTabs(arguments, out var skipped);
            entry.Add(new("tabs", TabsJson(tabs)));

            notes.Add(tabs.Count > 0
                ? "Tabs come from the command Windows Terminal was started with. Tabs or windows opened since then aren't included."
                : "Windows Terminal wasn't started with tabs on its command line. Add them under \"tabs\".");
            if (skipped.Count > 0)
            {
                notes.Add($"Left out what tabs can't represent: {string.Join(", ", skipped.Distinct())}");
            }
        }
        else if (Is(window.ProcessName, "explorer"))
        {
            // explorer.exe's command line doesn't say which folder a window shows.
            path = "explorer.exe";
            entry.Add(new("name", Json("File Explorer")));
            entry.Add(new("path", Json(path)));
            if (details.ExplorerFolder is { } folder)
            {
                entry.Add(new("arguments", Json(CommandLine.Quote(folder))));
            }
            else
            {
                notes.Add("Couldn't tell which folder is open. Places like Home or This PC have no folder path.");
            }
        }
        else if (details.AppUserModelId is { } appId && (details.ExecutablePath is null || IsInWindowsApps(details.ExecutablePath)))
        {
            // Packaged apps' exes can't be started directly; their app ID can.
            path = $@"shell:AppsFolder\{appId}";
            entry.Add(new("name", Json(details.Description ?? window.ProcessName)));
            entry.Add(new("path", Json(path)));
            notes.Add("Store app: started by its app ID, so its command-line arguments aren't included.");
        }
        else if (details.ExecutablePath is { } exe)
        {
            path = PortablePath(exe, folders);
            entry.Add(new("name", Json(details.Description ?? window.ProcessName)));
            entry.Add(new("path", Json(path)));

            List<string> removed = [];
            var arguments = details.CommandLine is { } commandLine
                ? CommandLine.RemoveStartupFlags(CommandLine.SplitProgram(commandLine).Arguments, out removed)
                : string.Empty;
            if (arguments.Length > 0)
            {
                entry.Add(new("arguments", Json(arguments)));
            }

            if (details.CommandLine is null)
            {
                notes.Add("Couldn't read the command line, so arguments aren't included.");
            }
            else if (removed.Count > 0)
            {
                notes.Add($"Left out startup flags: {string.Join(' ', removed)}");
            }
        }
        else
        {
            entry.Add(new("name", Json(window.ProcessName)));
            entry.Add(new("path", Json(string.Empty), "fill in: couldn't read this app's path"));
            notes.Add("Couldn't read this app's path. It may be running as administrator.");
        }

        // Only needed when the window's process isn't the one the launcher would look for anyway.
        var defaultProcess = AppLauncher.WindowProcessName(new AppEntry { Type = isTerminal ? "terminal" : null }, path ?? string.Empty);
        var placement = PlacementLines(window, monitors);
        if (path is not null && !Is(window.ProcessName, defaultProcess))
        {
            entry.Add(new("windowProcess", Json(window.ProcessName)));
        }

        entry.AddRange(placement);

        var placementOnly = new List<Line> { new("windowProcess", Json(window.ProcessName)) };
        placementOnly.AddRange(placement);

        return new CapturedWindow(
            "{\n" + Render(entry, indent: "  ") + "\n}",
            Render(placementOnly, indent: string.Empty),
            notes);
    }

    /// <summary>The "monitor" setting as JSON: the monitor's name when it's unique, which survives rearranging monitors; otherwise its number.</summary>
    public static string MonitorValue(MonitorInfo monitor, IReadOnlyList<MonitorInfo> monitors) =>
        IsNameUnique(monitor, monitors) ? Json(monitor.Name) : monitor.Number.ToString(CultureInfo.InvariantCulture);

    /// <summary>The same as <see cref="MonitorValue"/>, as plain text rather than JSON.</summary>
    public static string MonitorText(MonitorInfo monitor, IReadOnlyList<MonitorInfo> monitors) =>
        IsNameUnique(monitor, monitors) ? monitor.Name : monitor.Number.ToString(CultureInfo.InvariantCulture);

    private static bool IsNameUnique(MonitorInfo monitor, IReadOnlyList<MonitorInfo> monitors) =>
        monitors.Count(m => Is(m.Name, monitor.Name)) == 1;

    /// <summary>Replaces the longest matching known folder with its environment variable.</summary>
    public static string PortablePath(string path, IReadOnlyList<(string Variable, string Folder)> folders)
    {
        var match = folders
            .Where(f => f.Folder.Length > 0 && path.StartsWith(f.Folder.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Folder.Length)
            .FirstOrDefault();

        return match.Variable is null ? path : match.Variable + path[match.Folder.TrimEnd('\\').Length..];
    }

    private static List<Line> PlacementLines(WindowInfo window, IReadOnlyList<MonitorInfo> monitors)
    {
        var monitor = MonitorLayout.MonitorFor(window.Bounds, monitors) ?? monitors.First(m => m.IsPrimary);
        var lines = new List<Line> { new("monitor", MonitorValue(monitor, monitors)) };

        if (window.IsMaximized)
        {
            lines.Add(new("windowState", Json("maximized")));
        }
        else
        {
            var b = MonitorLayout.RelativeBounds(monitor, window.Bounds);
            lines.Add(new("windowState", Json("normal")));
            lines.Add(new("bounds", string.Create(CultureInfo.InvariantCulture, $"{{ \"x\": {b.X}, \"y\": {b.Y}, \"width\": {b.Width}, \"height\": {b.Height} }}")));
        }

        return lines;
    }

    /// <summary>One tab per line, e.g. <c>{ "title": "API", "directory": "D:\\api" }</c>.</summary>
    private static string TabsJson(List<TerminalTab> tabs)
    {
        if (tabs.Count == 0)
        {
            return "[]";
        }

        var lines = tabs.Select(tab =>
        {
            var fields = new List<string>();
            Add("title", tab.Title);
            Add("directory", tab.Directory);
            Add("profile", tab.Profile);
            Add("command", tab.Command);
            Add("color", tab.Color);
            return "    { " + string.Join(", ", fields) + " }";

            void Add(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    fields.Add($"{Json(key)}: {Json(value)}");
                }
            }
        });

        return "[\n" + string.Join(",\n", lines) + "\n  ]";
    }

    private static string Render(List<Line> lines, string indent)
    {
        var text = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            text.Append(indent).Append(Json(line.Key)).Append(": ").Append(line.Value);
            if (i < lines.Count - 1)
            {
                text.Append(',');
            }

            if (line.Comment is not null)
            {
                text.Append("  // ").Append(line.Comment);
            }

            if (i < lines.Count - 1)
            {
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    private static bool IsInWindowsApps(string path) => path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Json(string value) =>
        $"\"{JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"";

    private sealed record Line(string Key, string Value, string? Comment = null);
}
