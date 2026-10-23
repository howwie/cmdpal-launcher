using System;
using System.Linq;
using System.Text;
using HowWieYoong.CmdPalLauncher.Desktop;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Pages;

/// <summary>Lists monitors and open windows, and copies a profiles.json app entry that would reopen a window where it is.</summary>
internal sealed partial class WindowPositionsPage : ListPage
{
    internal static readonly IconInfo PageIcon = new("");

    /// <summary>Command Palette's own window, which is open while this page is.</summary>
    private const string PaletteProcess = "Microsoft.CmdPal.UI";

    private readonly IDesktop _desktop;

    public WindowPositionsPage(IDesktop desktop)
    {
        _desktop = desktop;
        Icon = PageIcon;
        Title = "Window Positions";
        Name = "Open";

        // A stable ID lets Command Palette recognise the command across reloads, and pin it or give it an alias.
        Id = "HowWieYoong.CmdPalLauncher.CaptureWindow";
        PlaceholderText = "Search windows and monitors";
        ShowDetails = true;
    }

    public override IListItem[] GetItems()
    {
        var monitors = _desktop.GetMonitors();

        var monitorItems = monitors.Select(monitor => (IListItem)new ListItem(
            new CopyTextCommand($"\"monitor\": {WindowCapture.MonitorValue(monitor, monitors)}") { Name = "Copy monitor setting" })
        {
            Title = $"Monitor {monitor.Number}: {monitor.Name}",
            Subtitle = $"{monitor.Bounds.Width}×{monitor.Bounds.Height}{(monitor.IsPrimary ? " · primary" : string.Empty)} · {monitor.DeviceName}",
            Icon = PageIcon,
        });

        // Minimized windows have no real position to capture.
        var windowItems = _desktop.GetWindows()
            .Where(w => !w.IsMinimized && !string.Equals(w.ProcessName, PaletteProcess, StringComparison.OrdinalIgnoreCase))
            .Select(window =>
            {
                var details = _desktop.GetProcessDetails(window);
                var captured = WindowCapture.Capture(window, details, monitors);
                var monitor = MonitorLayout.MonitorFor(window.Bounds, monitors);
                return (IListItem)new ListItem(new CopyTextCommand(captured.Entry) { Name = "Copy app entry" })
                {
                    Title = window.Title,
                    Subtitle = $"{window.ProcessName} · monitor {monitor?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} · "
                        + (window.IsMaximized ? "maximized" : window.Bounds.ToString()),
                    Details = new Details { Title = window.Title, Body = DetailsMarkdown(captured, details) },
                    MoreCommands =
                    [
                        new CommandContextItem(new CopyTextCommand(captured.Placement) { Name = "Copy placement settings only" }),
                    ],
                };
            });

        return [.. monitorItems, .. windowItems];
    }

    private static string DetailsMarkdown(CapturedWindow captured, ProcessDetails details)
    {
        var markdown = new StringBuilder();
        markdown.Append("```json\n").Append(captured.Entry).Append("\n```\n");

        foreach (var note in captured.Notes)
        {
            markdown.Append("\n- ").Append(note);
        }

        if (details.CommandLine is { Length: > 0 } commandLine)
        {
            markdown.Append("\n\n**Command line it was started with**\n\n```\n").Append(commandLine).Append("\n```");
        }

        return markdown.ToString();
    }
}
