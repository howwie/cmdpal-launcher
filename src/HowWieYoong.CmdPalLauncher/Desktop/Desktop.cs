using System.Collections.Generic;

namespace HowWieYoong.CmdPalLauncher.Desktop;

/// <summary>A rectangle in physical screen pixels.</summary>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public override string ToString() => $"{Width}x{Height} at {X},{Y}";
}

/// <param name="Number">Position counted left to right (then top to bottom), starting at 1.</param>
/// <param name="Name">Model name, e.g. "DELL U2720Q". Falls back to the device name when Windows has none.</param>
/// <param name="DeviceName">GDI device name, e.g. "\\.\DISPLAY2".</param>
internal sealed record MonitorInfo(int Number, string Name, string DeviceName, bool IsPrimary, PixelRect Bounds, PixelRect WorkArea);

/// <summary>How a placed window is left: at its position, maximized on its monitor, or minimized (restoring there).</summary>
internal enum WindowShow
{
    Normal,
    Maximized,
    Minimized,
}

/// <param name="Bounds">Where the window is now. Windows parks a minimized window far off screen at title-bar size.</param>
/// <param name="RestoredBounds">Where a maximized or minimized window goes when it's restored, if known.</param>
internal sealed record WindowInfo(nint Handle, string ProcessName, string Title, PixelRect Bounds, bool IsMaximized, bool IsMinimized, uint ProcessId = 0, PixelRect? RestoredBounds = null)
{
    /// <summary>The window's position and size when it's neither maximized nor minimized.</summary>
    public PixelRect NormalBounds => RestoredBounds ?? Bounds;
}

/// <summary>How the app behind a window was started, as far as Windows can tell. Any part may be unavailable.</summary>
/// <param name="ExecutablePath">Full path of the app's exe.</param>
/// <param name="CommandLine">The command line the app was started with, including the exe.</param>
/// <param name="AppUserModelId">Set for packaged (Store) apps, e.g. "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App".</param>
/// <param name="Description">The exe's file description, e.g. "Visual Studio Code".</param>
/// <param name="ExplorerFolder">For File Explorer windows, the folder that's open.</param>
/// <param name="TabNames">For Windows Terminal windows, the name of each tab, left to right.</param>
internal sealed record ProcessDetails(
    string? ExecutablePath = null,
    string? CommandLine = null,
    string? AppUserModelId = null,
    string? Description = null,
    string? ExplorerFolder = null,
    IReadOnlyList<string>? TabNames = null);

/// <summary>The monitors and windows on screen, so tests can replace them.</summary>
internal interface IDesktop
{
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Visible top-level app windows, in z-order.</summary>
    IReadOnlyList<WindowInfo> GetWindows();

    /// <summary>Moves the window to <paramref name="bounds"/>, then maximizes or minimizes it there if asked.</summary>
    void PlaceWindow(nint handle, PixelRect bounds, WindowShow show);

    ProcessDetails GetProcessDetails(WindowInfo window);
}
