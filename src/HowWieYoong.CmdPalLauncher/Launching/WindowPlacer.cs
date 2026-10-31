using System;
using System.Collections.Generic;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Launching;

/// <summary>
/// Waits for the windows of launched apps to appear and moves them into place.
/// </summary>
/// <remarks>
/// The process we start often isn't the one that shows the window (explorer.exe hands off to the running
/// shell, wt.exe to WindowsTerminal.exe), so windows are matched by being new and belonging to the
/// expected process name. When several apps wait on the same process, windows go to them in launch order.
/// </remarks>
internal sealed class WindowPlacer
{
    public const int PollMs = 100;
    public const int DefaultTimeoutMs = 15_000;

    /// <summary>Some apps (Electron, Docker Desktop) restore their own position after showing, so place them again after this.</summary>
    public const int SettleMs = 1_500;

    private readonly IDesktop _desktop;
    private readonly IProcessRunner _runner;
    private readonly HashSet<nint> _knownWindows = [];
    private readonly List<PendingWindow> _pending = [];
    private readonly List<string> _failures = [];
    private IReadOnlyList<MonitorInfo>? _monitors;

    public WindowPlacer(IDesktop desktop, IProcessRunner runner)
    {
        _desktop = desktop;
        _runner = runner;
        foreach (var window in desktop.GetWindows())
        {
            _knownWindows.Add(window.Handle);
        }
    }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors ??= _desktop.GetMonitors();

    /// <summary>Checks the app's placement settings before it starts, so problems are reported once.</summary>
    public bool Validate(AppEntry app, List<string> failures)
    {
        if (app.WindowState is { } state && !app.IsMaximized && !app.IsMinimized && !string.Equals(state, "normal", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{AppLauncher.DisplayName(app)}: unknown windowState \"{state}\" (use \"normal\", \"maximized\" or \"minimized\")");
            return false;
        }

        if (app.Monitor is { } selector && MonitorLayout.Resolve(selector, Monitors) is null)
        {
            failures.Add($"{AppLauncher.DisplayName(app)}: monitor {selector} not found ({Monitors.Count} connected), so its window wasn't moved");
            return false;
        }

        return true;
    }

    /// <summary>The monitor an app's window should go on, or null to leave it on the monitor it opens on.</summary>
    public MonitorInfo? TargetMonitor(AppEntry app) =>
        app.Monitor is { } selector ? MonitorLayout.Resolve(selector, Monitors)
        : app.Bounds is not null ? Monitors.FirstOrDefault(m => m.IsPrimary)
        : null;

    /// <summary>Starts watching for a new window from <paramref name="processName"/>. Call right after starting the app.</summary>
    public void Expect(AppEntry app, string processName) =>
        _pending.Add(new PendingWindow(app, processName, app.WindowTimeoutMs > 0 ? app.WindowTimeoutMs : DefaultTimeoutMs));

    /// <summary>Waits <paramref name="milliseconds"/>, placing windows as they appear.</summary>
    public void Wait(int milliseconds)
    {
        for (var waited = 0; waited < milliseconds; waited += PollMs)
        {
            var step = Math.Min(PollMs, milliseconds - waited);
            _runner.Delay(step);
            Poll(step);
        }
    }

    /// <summary>Waits until every expected window has been placed or timed out, and returns any failures.</summary>
    public List<string> Finish()
    {
        Poll(0);
        while (_pending.Count > 0)
        {
            _runner.Delay(PollMs);
            Poll(PollMs);
        }

        return _failures;
    }

    /// <param name="elapsedMs">Time waited since the previous poll.</param>
    private void Poll(int elapsedMs)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var windows = _desktop.GetWindows();
        foreach (var window in windows)
        {
            if (_knownWindows.Contains(window.Handle))
            {
                continue;
            }

            var pending = _pending.FirstOrDefault(p => p.Handle == 0 && Matches(p, window));
            if (pending is not null)
            {
                _knownWindows.Add(window.Handle);
                pending.Handle = window.Handle;
                Place(pending.App, window);
            }
            else if (!_pending.Any(p => p.Handle == 0 && IsFromProcess(p, window)))
            {
                // A window from a process we're waiting on stays unknown, because its title may still change to the
                // one we want. Any other window is ignored from now on, so an app started later can't claim it.
                _knownWindows.Add(window.Handle);
            }
        }

        foreach (var pending in _pending.ToList())
        {
            if (pending.Handle == 0)
            {
                pending.Remaining -= elapsedMs;
                if (pending.Remaining <= 0)
                {
                    _failures.Add($"{AppLauncher.DisplayName(pending.App)}: no new \"{pending.ProcessName}\" window appeared, so it wasn't moved");
                    _pending.Remove(pending);
                }

                continue;
            }

            pending.SettleRemaining -= elapsedMs;
            if (pending.SettleRemaining <= 0)
            {
                if (windows.FirstOrDefault(w => w.Handle == pending.Handle) is { } window)
                {
                    Place(pending.App, window, settling: true);
                }

                _pending.Remove(pending);
            }
        }
    }

    private static bool IsFromProcess(PendingWindow pending, WindowInfo window) =>
        string.Equals(window.ProcessName, pending.ProcessName, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(PendingWindow pending, WindowInfo window) =>
        IsFromProcess(pending, window)
        && (string.IsNullOrEmpty(pending.App.WindowTitle) || window.Title.Contains(pending.App.WindowTitle, StringComparison.OrdinalIgnoreCase));

    private void Place(AppEntry app, WindowInfo window, bool settling = false)
    {
        // Moving a window restores it, so a window that's left as it opened has to be put back in that state.
        var show = app.Show ?? (window.IsMaximized ? WindowShow.Maximized : window.IsMinimized ? WindowShow.Minimized : WindowShow.Normal);

        // Placing again shouldn't bring a window we already minimized back up.
        if (settling && show == WindowShow.Minimized && window.IsMinimized)
        {
            return;
        }

        var monitor = TargetMonitor(app) ?? MonitorLayout.MonitorFor(window.IsMinimized ? window.NormalBounds : window.Bounds, Monitors) ?? Monitors.FirstOrDefault(m => m.IsPrimary);
        if (monitor is null)
        {
            return;
        }

        _desktop.PlaceWindow(window.Handle, MonitorLayout.TargetBounds(monitor, app.Bounds, window.NormalBounds), show);
    }

    private sealed class PendingWindow(AppEntry app, string processName, int timeoutMs)
    {
        public AppEntry App { get; } = app;

        public string ProcessName { get; } = processName;

        /// <summary>Time left to wait for the window to appear.</summary>
        public int Remaining { get; set; } = timeoutMs;

        /// <summary>Time left before placing the window a second time.</summary>
        public int SettleRemaining { get; set; } = SettleMs;

        public nint Handle { get; set; }
    }
}
