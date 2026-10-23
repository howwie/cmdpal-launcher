// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HowWieYoong.CmdPalLauncher;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);
            
            // We are instantiating an extension instance once above, and returning it every time the callback in RegisterExtension below is called.
            // This makes sure that only one instance of SampleExtension is alive, which is returned every time the host asks for the IExtension object.
            // If you want to instantiate a new instance each time the host asks, create the new instance inside the delegate.
            CmdPalLauncher extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<CmdPalLauncher, IExtension>(() => extensionInstance);
            server.Start();
            
            // This will make the main thread wait until the event is signalled by the extension class.
            // Since we have single instance of the extension object, we exit as soon as it is disposed.
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else if (args.Length is 2 or 3 && args[0] is "-launch" or "-print")
        {
            // Lets a profile be started from a shortcut or script, and -print shows what would run.
            Environment.ExitCode = RunFromCommandLine(args[1], launch: args[0] == "-launch", profilesFile: args.Length == 3 ? args[2] : null);
        }
        else if (args.Length == 1 && args[0] == "-windows")
        {
            PrintWindows();
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
            Console.WriteLine("Usage: CmdPalLauncher.exe -launch <profile> [profiles.json] | -print <profile> [profiles.json] | -windows");
        }
    }

    /// <summary>Lists monitors, and an app entry for each window that would reopen it where it is.</summary>
    private static void PrintWindows()
    {
        var monitors = Win32Desktop.Instance.GetMonitors();
        Console.WriteLine("Monitors (numbered left to right):");
        foreach (var monitor in monitors)
        {
            Console.WriteLine($"  {monitor.Number}: {monitor.Name}, {monitor.Bounds}, work area {monitor.WorkArea}{(monitor.IsPrimary ? ", primary" : string.Empty)} ({monitor.DeviceName})");
        }

        foreach (var window in Win32Desktop.Instance.GetWindows().Where(w => !w.IsMinimized))
        {
            var details = Win32Desktop.Instance.GetProcessDetails(window);
            var captured = WindowCapture.Capture(window, details, monitors);
            Console.WriteLine();
            Console.WriteLine($"{window.Title} [{window.ProcessName}]");
            Console.WriteLine(captured.Entry);
            captured.Notes.ToList().ForEach(note => Console.WriteLine($"  note: {note}"));
            if (details.CommandLine is { } commandLine)
            {
                Console.WriteLine($"  started with: {commandLine}");
            }
        }
    }

    private static int RunFromCommandLine(string profileName, bool launch, string? profilesFile)
    {
        using var store = new ProfileStore(profilesFile is null ? null : System.IO.Path.GetFullPath(profilesFile), watch: false);
        if (store.Error is { } error)
        {
            Console.Error.WriteLine($"{store.FilePath}: {error}");
            return 2;
        }

        var profile = store.Profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            Console.Error.WriteLine($"No profile named \"{profileName}\". Profiles: {string.Join(", ", store.Profiles.Select(p => p.Name))}");
            return 1;
        }

        if (!launch)
        {
            var placer = new WindowPlacer(Win32Desktop.Instance, ProcessRunner.Default);
            foreach (var app in profile.Apps)
            {
                try
                {
                    var place = app.HasPlacement && (!app.IsTerminal || AppLauncher.OpensNewTerminalWindow(app));
                    var terminalPlacement = place && app.IsTerminal ? AppLauncher.TerminalPlacementFor(app, placer) : (TerminalPlacement?)null;
                    var startInfo = AppLauncher.CreateStartInfo(app, terminalPlacement: terminalPlacement);
                    if (startInfo is null)
                    {
                        Console.WriteLine($"{AppLauncher.DisplayName(app)}: skipped (already running)");
                        continue;
                    }

                    Console.WriteLine($"{AppLauncher.DisplayName(app)}: {startInfo.FileName} {startInfo.Arguments}");
                    if (place)
                    {
                        var monitor = placer.TargetMonitor(app);
                        var where = monitor is null ? "the monitor it opens on" : $"monitor {monitor.Number} ({monitor.Name})";
                        var how = app.IsMaximized ? "maximized" : app.Bounds is { } b ? $"at {b.X},{b.Y} size {b.Width}x{b.Height} in the work area" : "centred";
                        Console.WriteLine($"  then moves the new \"{AppLauncher.WindowProcessName(app, startInfo.FileName)}\" window to {where}, {how}");
                    }
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"{AppLauncher.DisplayName(app)}: error, {ex.Message}");
                }
            }

            var problems = new List<string>();
            foreach (var app in profile.Apps.Where(a => a.HasPlacement))
            {
                placer.Validate(app, problems);
            }

            problems.ForEach(p => Console.WriteLine($"Problem: {p}"));
            return 0;
        }

        var failures = AppLauncher.LaunchAll(profile);
        failures.ForEach(Console.Error.WriteLine);
        return failures.Count == 0 ? 0 : 1;
    }
}
