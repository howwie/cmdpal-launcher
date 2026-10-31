using System;
using System.Collections.Generic;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Desktop;

namespace HowWieYoong.CmdPalLauncher.Tests.Helpers;

/// <summary>Two side-by-side 2560x1440 monitors by default, with no windows.</summary>
internal sealed class FakeDesktop : IDesktop
{
    private readonly List<(int AfterCalls, WindowInfo Window)> _later = [];
    private readonly List<(int AfterCalls, nint Handle, string Title)> _renames = [];
    private nint _nextHandle = 100;
    private int _getWindowsCalls;

    public List<MonitorInfo> Monitors { get; } =
    [
        Monitor(1, "DELL U2724DE", primary: true, x: 0),
        Monitor(2, "DELL U2515H", primary: false, x: 2560),
    ];

    public List<WindowInfo> Windows { get; } = [];

    public List<(nint Handle, PixelRect Bounds, WindowShow Show)> Placed { get; } = [];

    /// <summary>Optional shared log, so tests can check placements against process starts.</summary>
    public List<string>? Log { get; set; }

    public static MonitorInfo Monitor(int number, string name, bool primary, int x, int y = 0) =>
        new(number, name, $@"\\.\DISPLAY{number}", primary, new PixelRect(x, y, 2560, 1440), new PixelRect(x, y, 2560, 1380));

    /// <summary>Adds a window now and returns it.</summary>
    public WindowInfo AddWindow(string processName, string title = "Window", PixelRect? bounds = null, bool maximized = false)
    {
        var window = new WindowInfo(_nextHandle++, processName, title, bounds ?? new PixelRect(100, 100, 800, 600), maximized, false);
        Windows.Add(window);
        return window;
    }

    /// <summary>Adds a window once <see cref="GetWindows"/> has been called <paramref name="afterCalls"/> more times.</summary>
    public void AddWindowLater(int afterCalls, string processName, string title = "Window") =>
        _later.Add((_getWindowsCalls + afterCalls, new WindowInfo(_nextHandle++, processName, title, new PixelRect(100, 100, 800, 600), false, false)));

    /// <summary>Changes a window's title once <see cref="GetWindows"/> has been called <paramref name="afterCalls"/> more times.</summary>
    public void RenameWindowLater(int afterCalls, nint handle, string title) =>
        _renames.Add((_getWindowsCalls + afterCalls, handle, title));

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        _getWindowsCalls++;
        foreach (var due in _later.Where(l => l.AfterCalls <= _getWindowsCalls).ToList())
        {
            Windows.Add(due.Window);
            _later.Remove(due);
        }

        foreach (var due in _renames.Where(r => r.AfterCalls <= _getWindowsCalls).ToList())
        {
            var index = Windows.FindIndex(w => w.Handle == due.Handle);
            Windows[index] = Windows[index] with { Title = due.Title };
            _renames.Remove(due);
        }

        return Windows.ToList();
    }

    public void PlaceWindow(nint handle, PixelRect bounds, WindowShow show)
    {
        Placed.Add((handle, bounds, show));
        Log?.Add($"place {Windows.First(w => w.Handle == handle).ProcessName}");
    }

    public Dictionary<nint, ProcessDetails> Details { get; } = [];

    public ProcessDetails GetProcessDetails(WindowInfo window) => Details.GetValueOrDefault(window.Handle) ?? new ProcessDetails();
}

/// <summary>Fails the test if anything touches the desktop.</summary>
internal sealed class UnusedDesktop : IDesktop
{
    public IReadOnlyList<MonitorInfo> GetMonitors() => throw new InvalidOperationException("desktop should not be used");

    public IReadOnlyList<WindowInfo> GetWindows() => throw new InvalidOperationException("desktop should not be used");

    public void PlaceWindow(nint handle, PixelRect bounds, WindowShow show) => throw new InvalidOperationException("desktop should not be used");

    public ProcessDetails GetProcessDetails(WindowInfo window) => throw new InvalidOperationException("desktop should not be used");
}
