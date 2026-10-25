using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static HowWieYoong.CmdPalLauncher.Desktop.NativeMethods;

namespace HowWieYoong.CmdPalLauncher.Desktop;

internal sealed unsafe class Win32Desktop : IDesktop
{
    public static readonly Win32Desktop Instance = new();

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            EnumDisplayMonitors(0, 0, &CollectMonitor, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        var names = GetMonitorNames();
        var monitors = new List<MonitorInfo>();
        foreach (var handle in handles)
        {
            var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
            if (!GetMonitorInfo(handle, &info))
            {
                continue;
            }

            var deviceName = new string(info.szDevice);
            var name = names.TryGetValue(deviceName, out var friendly) && friendly.Length > 0
                ? friendly
                : deviceName.Replace(@"\\.\", string.Empty, StringComparison.Ordinal);

            monitors.Add(new MonitorInfo(
                0,
                name,
                deviceName,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                ToPixelRect(info.rcMonitor),
                ToPixelRect(info.rcWork)));
        }

        return MonitorLayout.Number(monitors);
    }

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        var handles = new List<nint>();
        var state = GCHandle.Alloc(handles);
        try
        {
            EnumWindows(&CollectWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        var processNames = new Dictionary<uint, string?>();
        var windows = new List<WindowInfo>();
        foreach (var handle in handles)
        {
            if (!IsAppWindow(handle) || GetTitle(handle) is not { Length: > 0 } title || !GetWindowRect(handle, out var rect))
            {
                continue;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (!processNames.TryGetValue(processId, out var processName))
            {
                processName = GetProcessName(processId);
                processNames[processId] = processName;
            }

            if (processName is not null)
            {
                windows.Add(new WindowInfo(handle, processName, title, ToPixelRect(rect), IsZoomed(handle), IsIconic(handle), processId));
            }
        }

        return windows;
    }

    public void PlaceWindow(nint handle, PixelRect bounds, WindowShow show)
    {
        // Placing again (after the app settles) shouldn't bring a window we already minimized back up.
        if (show == WindowShow.Minimized && IsIconic(handle))
        {
            return;
        }

        // A maximized or minimized window ignores SetWindowPos until it's restored.
        if (IsZoomed(handle) || IsIconic(handle))
        {
            ShowWindow(handle, SW_RESTORE);
        }

        // Moving to a monitor with a different scale makes the app resize itself, so set the bounds again once it's there.
        for (var pass = 0; pass < 2; pass++)
        {
            SetWindowPos(handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        // Both act on whichever monitor the window is now on, so a minimized window restores there.
        if (show == WindowShow.Maximized)
        {
            ShowWindow(handle, SW_MAXIMIZE);
        }
        else if (show == WindowShow.Minimized)
        {
            ShowWindow(handle, SW_SHOWMINNOACTIVE);
        }
    }

    public ProcessDetails GetProcessDetails(WindowInfo window)
    {
        var processId = window.ProcessId;

        // UWP apps draw inside a frame window owned by ApplicationFrameHost; the app itself owns a child window.
        if (string.Equals(window.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
            && HostedAppProcessId(window.Handle, processId) is uint hosted)
        {
            processId = hosted;
        }

        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return new ProcessDetails();
        }

        try
        {
            var path = GetExecutablePath(process);
            return new ProcessDetails(
                path,
                GetCommandLine(process),
                GetAppUserModelId(process),
                path is null ? null : GetFileDescription(path),
                string.Equals(window.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) ? ExplorerFolders.For(window) : null);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static uint? HostedAppProcessId(nint frame, uint frameProcessId)
    {
        var children = new List<nint>();
        var state = GCHandle.Alloc(children);
        try
        {
            EnumChildWindows(frame, &CollectWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        foreach (var child in children)
        {
            GetWindowThreadProcessId(child, out var childProcessId);
            if (childProcessId != frameProcessId)
            {
                return childProcessId;
            }
        }

        return null;
    }

    private static string? GetExecutablePath(nint process)
    {
        var buffer = new char[32768];
        var size = (uint)buffer.Length;
        fixed (char* name = buffer)
        {
            return QueryFullProcessImageName(process, 0, name, ref size) ? new string(name, 0, (int)size) : null;
        }
    }

    private static string? GetCommandLine(nint process)
    {
        // The first call reports how big the buffer needs to be.
        NtQueryInformationProcess(process, ProcessCommandLineInformation, null, 0, out var length);
        if (length == 0)
        {
            return null;
        }

        var buffer = new byte[length];
        fixed (byte* info = buffer)
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, info, length, out _) < 0)
            {
                return null;
            }

            // The result is a UNICODE_STRING whose text follows it in the same buffer.
            var text = (UNICODE_STRING*)info;
            return text->Buffer is null ? null : new string(text->Buffer, 0, text->Length / sizeof(char));
        }
    }

    private static string? GetAppUserModelId(nint process)
    {
        var buffer = new char[256];
        var length = (uint)buffer.Length;
        fixed (char* id = buffer)
        {
            // Length includes the terminating null.
            return GetApplicationUserModelId(process, ref length, id) == ERROR_SUCCESS && length > 1
                ? new string(id, 0, (int)length - 1)
                : null;
        }
    }

    private static string? GetFileDescription(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description ? description.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Packaged apps' folders aren't always readable.
            return null;
        }
    }

    /// <summary>Maps GDI device names ("\\.\DISPLAY1") to monitor model names ("DELL U2720Q").</summary>
    private static Dictionary<string, string> GetMonitorNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
        {
            return names;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        fixed (DISPLAYCONFIG_PATH_INFO* pathPtr = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* modePtr = modes)
        {
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, pathPtr, ref modeCount, modePtr, 0) != 0)
            {
                return names;
            }
        }

        for (var i = 0; i < pathCount; i++)
        {
            var path = paths[i];

            var source = default(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            source.header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME), path.sourceInfo.adapterId, path.sourceInfo.id);

            var target = default(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), path.targetInfo.adapterId, path.targetInfo.id);

            if (DisplayConfigGetDeviceInfo(&source.header) == 0 && DisplayConfigGetDeviceInfo(&target.header) == 0)
            {
                // With mirrored displays, several targets share a source; keep the first.
                names.TryAdd(new string(source.viewGdiDeviceName), new string(target.monitorFriendlyDeviceName));
            }
        }

        return names;
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(int type, uint size, LUID adapterId, uint id) =>
        new() { type = type, size = size, adapterId = adapterId, id = id };

    /// <summary>Skips windows that are hidden, owned (dialogs), tool windows, or cloaked (suspended UWP apps).</summary>
    private static bool IsAppWindow(nint handle)
    {
        if (!IsWindowVisible(handle) || GetWindow(handle, GW_OWNER) != 0)
        {
            return false;
        }

        if ((GetWindowLongPtr(handle, GWL_EXSTYLE) & (nint)WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        return DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static string GetTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            var copied = GetWindowText(handle, text, buffer.Length);
            return new string(text, 0, copied);
        }
    }

    private static string? GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // The process has already exited.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static PixelRect ToPixelRect(RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    [UnmanagedCallersOnly]
    private static int CollectWindow(nint handle, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(handle);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int CollectMonitor(nint monitor, nint hdc, RECT* rect, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(monitor);
        return 1;
    }
}
