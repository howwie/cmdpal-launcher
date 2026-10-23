using System;
using System.Collections.Generic;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Launching;

/// <summary>Reads tabs back out of a wt.exe command line, the reverse of <see cref="AppLauncher.BuildTerminalArguments"/>.</summary>
internal static class TerminalCommandLine
{
    private static readonly HashSet<string> NewTab = new(StringComparer.OrdinalIgnoreCase) { "new-tab", "nt" };

    private static readonly HashSet<string> OtherSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "split-pane", "sp", "focus-tab", "ft", "move-focus", "mf", "move-pane", "mp", "swap-pane", "focus-pane", "fp", "x-save",
    };

    /// <summary>Options that take a value, which aren't captured but must be skipped along with their value.</summary>
    private static readonly HashSet<string> IgnoredOptionsWithValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "-w", "--window", "--pos", "--size", "-s",
    };

    /// <summary>
    /// Returns the tabs that <paramref name="arguments"/> (wt's arguments, without "wt.exe") open.
    /// <paramref name="skipped"/> lists subcommands that tabs can't represent, such as split-pane.
    /// </summary>
    public static List<TerminalTab> ParseTabs(string arguments, out List<string> skipped)
    {
        var tabs = new List<TerminalTab>();
        skipped = [];

        // wt separates subcommands with a standalone ";" (an escaped "\;" stays inside a command).
        var segments = new List<List<string>> { new() };
        foreach (var token in CommandLine.Split(arguments))
        {
            if (token == ";")
            {
                segments.Add([]);
            }
            else
            {
                segments[^1].Add(token);
            }
        }

        foreach (var segment in segments)
        {
            var tokens = SkipGlobalOptions(segment);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (OtherSubcommands.Contains(tokens[0]))
            {
                skipped.Add(tokens[0]);
                continue;
            }

            // Options without a subcommand mean new-tab.
            tabs.Add(ParseTab(NewTab.Contains(tokens[0]) ? tokens.Skip(1).ToList() : tokens));
        }

        return tabs;
    }

    private static List<string> SkipGlobalOptions(List<string> tokens)
    {
        var i = 0;
        while (i < tokens.Count)
        {
            if (IgnoredOptionsWithValue.Contains(tokens[i]))
            {
                i += 2;
            }
            else if (tokens[i] is "-M" or "--maximized" or "-F" or "--fullscreen" or "-f" or "--focus")
            {
                i++;
            }
            else
            {
                break;
            }
        }

        return tokens.Skip(i).ToList();
    }

    private static TerminalTab ParseTab(List<string> tokens)
    {
        var tab = new TerminalTab();
        var i = 0;
        while (i < tokens.Count)
        {
            var option = tokens[i];
            var value = i + 1 < tokens.Count ? tokens[i + 1] : null;

            switch (option.ToLowerInvariant())
            {
                case "-p" or "--profile":
                    tab.Profile = value;
                    i += 2;
                    continue;
                case "-d" or "--startingdirectory":
                    tab.Directory = value;
                    i += 2;
                    continue;
                case "--title":
                    tab.Title = value;
                    i += 2;
                    continue;
                case "--tabcolor":
                    tab.Color = value;
                    i += 2;
                    continue;
                case "--colorscheme":
                    i += 2;
                    continue;
                case "--suppressapplicationtitle" or "--useapplicationtitle" or "--inheritenvironment":
                    i++;
                    continue;
            }

            // Everything from the first non-option token is the command line to run.
            tab.Command = string.Join(' ', tokens.Skip(i).Select(CommandToken));
            break;
        }

        return tab;
    }

    /// <summary>
    /// A token of a tab's command, as profiles.json holds it: "\;" becomes ";" again, because
    /// <see cref="AppLauncher.BuildTerminalArguments"/> escapes semicolons when launching.
    /// </summary>
    private static string CommandToken(string token)
    {
        token = token.Replace("\\;", ";", StringComparison.Ordinal);
        return token.Length > 0 && !token.Any(c => char.IsWhiteSpace(c) || c == '"') ? token : CommandLine.Quote(token);
    }
}
