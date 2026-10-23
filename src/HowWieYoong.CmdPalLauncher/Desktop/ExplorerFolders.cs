using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HowWieYoong.CmdPalLauncher.Desktop;

/// <summary>Finds the folder open in a File Explorer window, which isn't part of explorer.exe's command line.</summary>
internal static class ExplorerFolders
{
    /// <summary>
    /// Asks the shell's list of open Explorer windows (Shell.Application's Windows()) for the folder shown in
    /// <paramref name="window"/>. With several tabs, picks the tab whose name matches the window title.
    /// Returns null for virtual locations like Home or This PC.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "IDispatch late binding on COM objects; no managed members are involved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "IDispatch late binding on COM objects; no managed members are involved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "IDispatch late binding on COM objects; no managed members are involved.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only used in JIT builds; returns null when COM late binding isn't available.")]
    public static string? For(WindowInfo window)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null || Activator.CreateInstance(shellType) is not { } shell)
            {
                return null;
            }

            var windows = Invoke(shell, "Windows", BindingFlags.InvokeMethod);
            if (windows is null)
            {
                return null;
            }

            var tabs = new List<(string Name, string Url)>();
            var count = Convert.ToInt32(Invoke(windows, "Count", BindingFlags.GetProperty), System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                var tab = Invoke(windows, "Item", BindingFlags.InvokeMethod, i);
                if (tab is null || Convert.ToInt64(Invoke(tab, "HWND", BindingFlags.GetProperty), System.Globalization.CultureInfo.InvariantCulture) != window.Handle)
                {
                    continue;
                }

                tabs.Add((Invoke(tab, "LocationName", BindingFlags.GetProperty) as string ?? string.Empty, Invoke(tab, "LocationURL", BindingFlags.GetProperty) as string ?? string.Empty));
            }

            return PickFolder(window.Title, tabs);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or NotSupportedException or InvalidCastException or MissingMethodException)
        {
            return null;
        }
    }

    /// <summary>The active tab's folder: the tab whose name starts the window title ("Downloads - File Explorer"), else the first.</summary>
    internal static string? PickFolder(string windowTitle, IReadOnlyList<(string Name, string Url)> tabs)
    {
        var tab = tabs.FirstOrDefault(t => t.Name.Length > 0 && windowTitle.StartsWith(t.Name, StringComparison.OrdinalIgnoreCase));
        if (tab == default && tabs.Count > 0)
        {
            tab = tabs[0];
        }

        return Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "IDispatch late binding on COM objects.")]
    private static object? Invoke(object target, string name, BindingFlags flags, params object[] args) =>
        target.GetType().InvokeMember(name, flags, null, target, args, System.Globalization.CultureInfo.InvariantCulture);
}
