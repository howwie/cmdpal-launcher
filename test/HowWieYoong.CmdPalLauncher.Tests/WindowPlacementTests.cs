using System.IO;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class WindowPlacementTests
{
    private readonly FakeDesktop _desktop = new();
    private readonly FakeProcessRunner _runner = new();

    public WindowPlacementTests()
    {
        // By default, starting an app opens one window from a process named after the exe.
        _runner.OnStart = info => _desktop.AddWindow(Path.GetFileNameWithoutExtension(info.FileName));
    }

    [Fact]
    public void NewWindow_IsMovedToTheMonitorAndBounds()
    {
        var app = new AppEntry { Path = "code.exe", Monitor = new MonitorSelector(2, null), Bounds = new WindowBounds { X = 10, Y = 20, Width = 1200, Height = 800 } };

        var failures = Launch(app);

        Assert.Empty(failures);
        var placed = _desktop.Placed[0];
        Assert.Equal(new PixelRect(2570, 20, 1200, 800), placed.Bounds);
        Assert.Equal(WindowShow.Normal, placed.Show);
    }

    [Fact]
    public void Window_IsPlacedAgainAfterItSettles()
    {
        Launch(new AppEntry { Path = "code.exe", Monitor = new MonitorSelector(2, null) });

        Assert.Equal(2, _desktop.Placed.Count);
        Assert.Equal(_desktop.Placed[0], _desktop.Placed[1]);

        // The second placement waits for apps that restore their own position.
        var delays = _runner.Calls.Count(c => c.StartsWith("delay", System.StringComparison.Ordinal));
        Assert.InRange(delays * WindowPlacer.PollMs, WindowPlacer.SettleMs - WindowPlacer.PollMs, WindowPlacer.SettleMs + WindowPlacer.PollMs);
    }

    [Fact]
    public void Maximized_MaximizesOnTheChosenMonitor()
    {
        Launch(new AppEntry { Path = "code.exe", Monitor = new MonitorSelector(null, "U2515H"), WindowState = "maximized" });

        var placed = _desktop.Placed[0];
        Assert.Equal(WindowShow.Maximized, placed.Show);
        Assert.True(placed.Bounds.X >= 2560);
    }

    [Fact]
    public void MaximizedWithoutMonitor_StaysOnTheMonitorItOpenedOn()
    {
        _runner.OnStart = _ => _desktop.AddWindow("code", bounds: new PixelRect(3000, 100, 800, 600));

        Launch(new AppEntry { Path = "code.exe", WindowState = "maximized" });

        Assert.True(_desktop.Placed[0].Bounds.X >= 2560);
        Assert.Equal(WindowShow.Maximized, _desktop.Placed[0].Show);
    }

    [Fact]
    public void BoundsWithoutMonitor_UseThePrimaryMonitor()
    {
        _runner.OnStart = _ => _desktop.AddWindow("code", bounds: new PixelRect(3000, 100, 800, 600));

        Launch(new AppEntry { Path = "code.exe", Bounds = new WindowBounds { X = 0, Y = 0 } });

        Assert.Equal(new PixelRect(0, 0, 800, 600), _desktop.Placed[0].Bounds);
    }

    [Fact]
    public void ExistingWindowsOfTheSameProcess_AreLeftAlone()
    {
        var existing = _desktop.AddWindow("explorer", "Downloads");

        Launch(new AppEntry { Path = "explorer.exe", Arguments = @"D:\", Monitor = new MonitorSelector(2, null) });

        Assert.All(_desktop.Placed, p => Assert.NotEqual(existing.Handle, p.Handle));
        Assert.NotEmpty(_desktop.Placed);
    }

    [Fact]
    public void SameProcessTwice_WindowsGoToAppsInLaunchOrder()
    {
        var left = new AppEntry { Path = "explorer.exe", Monitor = new MonitorSelector(1, null) };
        var right = new AppEntry { Path = "explorer.exe", Monitor = new MonitorSelector(2, null) };

        Launch(left, right);

        var first = _desktop.Windows[0].Handle;
        var second = _desktop.Windows[1].Handle;
        Assert.True(_desktop.Placed.First(p => p.Handle == first).Bounds.X < 2560);
        Assert.True(_desktop.Placed.First(p => p.Handle == second).Bounds.X >= 2560);
    }

    [Fact]
    public void WindowProcess_MatchesALauncherChildProcess()
    {
        _runner.OnStart = _ => _desktop.AddWindowLater(3, "Docker Desktop UI");

        var failures = Launch(new AppEntry { Path = "docker-launcher.exe", WindowProcess = "Docker Desktop UI", Monitor = new MonitorSelector(2, null) });

        Assert.Empty(failures);
        Assert.NotEmpty(_desktop.Placed);
    }

    [Fact]
    public void WindowTitle_SkipsOtherWindowsFromTheProcess()
    {
        _runner.OnStart = _ =>
        {
            _desktop.AddWindow("app", "Splash");
            _desktop.AddWindowLater(5, "app", "App - Main");
        };

        Launch(new AppEntry { Path = "app.exe", WindowTitle = "main", Monitor = new MonitorSelector(2, null) });

        var mainWindow = _desktop.Windows.Single(w => w.Title == "App - Main").Handle;
        Assert.All(_desktop.Placed, p => Assert.Equal(mainWindow, p.Handle));
    }

    [Fact]
    public void WindowThatNeverAppears_TimesOutWithAMessage()
    {
        _runner.OnStart = _ => _desktop.AddWindow("something-else");

        var failures = Launch(new AppEntry { Path = "app.exe", Name = "App", WindowTimeoutMs = 500, Monitor = new MonitorSelector(2, null) });

        Assert.Equal(["App: no new \"app\" window appeared, so it wasn't moved"], failures);
        Assert.Empty(_desktop.Placed);
        Assert.Equal(5, _runner.Calls.Count(c => c.StartsWith("delay", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void UnknownMonitor_StillStartsTheAppButReportsIt()
    {
        var failures = Launch(new AppEntry { Path = "app.exe", Name = "App", Monitor = new MonitorSelector(3, null) });

        Assert.Equal(["App: monitor 3 not found (2 connected), so its window wasn't moved"], failures);
        Assert.Single(_runner.Started);
        Assert.Empty(_desktop.Placed);
    }

    [Fact]
    public void UnknownWindowState_IsReported()
    {
        var failures = Launch(new AppEntry { Path = "app.exe", Name = "App", WindowState = "fullscreen" });

        Assert.Equal(["App: unknown windowState \"fullscreen\" (use \"normal\", \"maximized\" or \"minimized\")"], failures);
        Assert.Single(_runner.Started);
    }

    [Fact]
    public void SkippedApp_IsNotWaitedFor()
    {
        _runner.RunningProcesses.Add("docker");

        var failures = Launch(new AppEntry { Path = "docker.exe", SkipIfRunning = true, Monitor = new MonitorSelector(2, null) });

        Assert.Empty(failures);
        Assert.DoesNotContain(_runner.Calls, c => c.StartsWith("delay", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileWithoutPlacement_NeverTouchesTheDesktop()
    {
        var failures = AppLauncher.LaunchAll(Profile(new AppEntry { Path = "a.exe" }), _runner, new UnusedDesktop());

        Assert.Empty(failures);
    }

    [Fact]
    public void WindowsArePlacedDuringALaterAppsDelay()
    {
        var log = _runner.Calls;
        _desktop.Log = log;

        Launch(
            new AppEntry { Path = "first.exe", Monitor = new MonitorSelector(2, null) },
            new AppEntry { Path = "second.exe", DelayMs = 300 });

        Assert.True(log.IndexOf("place first") < log.IndexOf("start second.exe"));
    }

    [Fact]
    public void Terminal_UsesItsOwnPositionOptions()
    {
        var app = new AppEntry
        {
            Type = "terminal",
            Monitor = new MonitorSelector(2, null),
            Bounds = new WindowBounds { X = 10, Y = 20 },
            WindowState = "maximized",
            Tabs = [new TerminalTab { Title = "API" }],
        };
        _runner.OnStart = _ => _desktop.AddWindow("WindowsTerminal");

        var failures = Launch(app);

        Assert.Empty(failures);
        Assert.Equal("-w new --pos 2570,20 --maximized new-tab --title API --suppressApplicationTitle", _runner.Started[0].Arguments);
        Assert.NotEmpty(_desktop.Placed);
    }

    [Fact]
    public void Terminal_ExistingWindow_IsNotPlaced()
    {
        var app = new AppEntry { Type = "terminal", Window = "last", Monitor = new MonitorSelector(2, null) };

        var failures = Launch(app);

        Assert.Empty(failures);
        Assert.Equal("-w last", _runner.Started[0].Arguments);
        Assert.Empty(_desktop.Placed);
    }

    [Fact]
    public void TerminalArguments_PositionOnlyOrMaximizedOnly()
    {
        var app = new AppEntry { Type = "terminal" };

        Assert.Equal("-w new --pos -1920,0", AppLauncher.BuildTerminalArguments(app, new TerminalPlacement(-1920, 0, false)));
        Assert.Equal("-w new --maximized", AppLauncher.BuildTerminalArguments(app, new TerminalPlacement(null, null, true)));
        Assert.Equal("-w new", AppLauncher.BuildTerminalArguments(app, new TerminalPlacement(null, null, false)));
    }

    [Theory]
    [InlineData("explorer.exe", null, null, "explorer")]
    [InlineData(@"C:\Program Files\Docker\Docker\Docker Desktop.exe", null, null, "Docker Desktop")]
    [InlineData("wt.exe", "terminal", null, "WindowsTerminal")]
    [InlineData("notes.txt", null, "notepad", "notepad")]
    public void WindowProcessName_DefaultsFromThePath(string fileName, string? type, string? windowProcess, string expected)
    {
        Assert.Equal(expected, AppLauncher.WindowProcessName(new AppEntry { Type = type, WindowProcess = windowProcess }, fileName));
    }

    private System.Collections.Generic.List<string> Launch(params AppEntry[] apps) => AppLauncher.LaunchAll(Profile(apps), _runner, _desktop);

    private static LaunchProfile Profile(params AppEntry[] apps) => new() { Name = "Test", Apps = [.. apps] };
}
