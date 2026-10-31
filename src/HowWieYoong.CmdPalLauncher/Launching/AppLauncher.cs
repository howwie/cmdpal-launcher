using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Launching;

/// <summary>Starts the apps in a profile.</summary>
internal static class AppLauncher
{
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Starts each app in order, moves windows that have placement settings, and returns a message for every app that failed.
    /// </summary>
    public static List<string> LaunchAll(LaunchProfile profile, IProcessRunner? runner = null, IDesktop? desktop = null)
    {
        runner ??= ProcessRunner.Default;
        var failures = new List<string>();
        var placer = profile.Apps.Any(a => a.HasPlacement) ? new WindowPlacer(desktop ?? Win32Desktop.Instance, runner) : null;

        foreach (var app in profile.Apps)
        {
            if (app.DelayMs > 0)
            {
                // Keep placing earlier apps' windows while waiting.
                if (placer is null)
                {
                    runner.Delay(app.DelayMs);
                }
                else
                {
                    placer.Wait(app.DelayMs);
                }
            }

            try
            {
                // Only a new Terminal window can be placed; tabs added to an existing window stay where it is.
                var place = placer is not null && app.HasPlacement && (!app.IsTerminal || OpensNewTerminalWindow(app)) && placer.Validate(app, failures);
                var terminalPlacement = place && app.IsTerminal ? TerminalPlacementFor(app, placer!) : (TerminalPlacement?)null;

                var startInfo = CreateStartInfo(app, runner, terminalPlacement);
                if (startInfo is not null)
                {
                    runner.Start(startInfo);
                    if (place)
                    {
                        placer!.Expect(app, WindowProcessName(app, startInfo.FileName));
                    }
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                failures.Add($"{DisplayName(app)}: cancelled");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException or ArgumentException)
            {
                failures.Add($"{DisplayName(app)}: {ex.Message}");
            }
        }

        if (placer is not null)
        {
            failures.AddRange(placer.Finish());
        }

        return failures;
    }

    /// <summary>Builds the start info for an app, or returns null when it should be skipped.</summary>
    public static ProcessStartInfo? CreateStartInfo(AppEntry app, IProcessRunner? runner = null, TerminalPlacement? terminalPlacement = null)
    {
        runner ??= ProcessRunner.Default;
        string fileName;
        string arguments;

        if (app.IsTerminal)
        {
            fileName = string.IsNullOrWhiteSpace(app.Path) ? "wt.exe" : Expand(app.Path);
            arguments = BuildTerminalArguments(app, terminalPlacement);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(app.Path))
            {
                throw new ArgumentException("no \"path\" set");
            }

            fileName = Expand(app.Path);
            arguments = Expand(app.Arguments ?? string.Empty);
        }

        if (app.SkipIfRunning && runner.IsRunning(WindowProcessName(app, fileName)))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            // Shell execute resolves app execution aliases (wt, code), documents and folders the same way Run does.
            UseShellExecute = true,
        };

        if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Expand(app.WorkingDirectory);
        }

        if (app.RunAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        return startInfo;
    }

    public static string DisplayName(AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(app.Name))
        {
            return app.Name;
        }

        if (app.IsTerminal && string.IsNullOrWhiteSpace(app.Path))
        {
            return "Windows Terminal";
        }

        return string.IsNullOrWhiteSpace(app.Path) ? "(unnamed app)" : Path.GetFileNameWithoutExtension(Expand(app.Path));
    }

    /// <summary>The process whose new window belongs to this app.</summary>
    public static string WindowProcessName(AppEntry app, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(app.WindowProcess))
        {
            return app.WindowProcess;
        }

        // wt.exe is only a stub that hands off to WindowsTerminal.exe.
        return app.IsTerminal ? "WindowsTerminal" : Path.GetFileNameWithoutExtension(fileName);
    }

    public static bool OpensNewTerminalWindow(AppEntry app) =>
        string.IsNullOrWhiteSpace(app.Window) || string.Equals(app.Window, "new", StringComparison.OrdinalIgnoreCase);

    /// <summary>Terminal's own --pos/--maximized options, so the window opens in place instead of jumping there.</summary>
    public static TerminalPlacement TerminalPlacementFor(AppEntry app, WindowPlacer placer)
    {
        if (placer.TargetMonitor(app) is not { } monitor)
        {
            return new TerminalPlacement(null, null, app.IsMaximized);
        }

        var work = monitor.WorkArea;
        return new TerminalPlacement(work.X + (app.Bounds?.X ?? 0), work.Y + (app.Bounds?.Y ?? 0), app.IsMaximized);
    }

    /// <summary>Builds a wt.exe command line, e.g. <c>-w new new-tab --title "API" -d "D:\api" ; new-tab ...</c>.</summary>
    public static string BuildTerminalArguments(AppEntry app, TerminalPlacement? placement = null)
    {
        var builder = new StringBuilder();
        builder.Append("-w ").Append(Quote(string.IsNullOrWhiteSpace(app.Window) ? "new" : app.Window));

        if (placement is { X: int x, Y: int y })
        {
            builder.Append(CultureInfo.InvariantCulture, $" --pos {x},{y}");
        }

        if (placement is { Maximized: true })
        {
            builder.Append(" --maximized");
        }

        var tabs = app.Tabs ?? [];
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            builder.Append(i == 0 ? " new-tab" : " ; new-tab");

            if (!string.IsNullOrWhiteSpace(tab.Profile))
            {
                builder.Append(" -p ").Append(Quote(tab.Profile));
            }

            if (!string.IsNullOrWhiteSpace(tab.Directory))
            {
                builder.Append(" -d ").Append(Quote(Expand(tab.Directory)));
            }

            if (!string.IsNullOrWhiteSpace(tab.Title))
            {
                // Without --suppressApplicationTitle the shell renames the tab as soon as it starts.
                builder.Append(" --title ").Append(Quote(tab.Title)).Append(" --suppressApplicationTitle");
            }

            if (!string.IsNullOrWhiteSpace(tab.Color))
            {
                builder.Append(" --tabColor ").Append(Quote(tab.Color));
            }

            if (!string.IsNullOrWhiteSpace(tab.Command))
            {
                // wt treats a bare ";" as the start of the next subcommand.
                builder.Append(' ').Append(Expand(tab.Command).Replace(";", "\\;", StringComparison.Ordinal));
            }
        }

        if (!string.IsNullOrWhiteSpace(app.Arguments))
        {
            builder.Append(' ').Append(Expand(app.Arguments));
        }

        return builder.ToString();
    }

    private static string Expand(string value) => Environment.ExpandEnvironmentVariables(value);

    private static string Quote(string value) => CommandLine.Quote(value);
}

/// <summary>Where a new Windows Terminal window should open, in screen pixels.</summary>
internal readonly record struct TerminalPlacement(int? X, int? Y, bool Maximized);
