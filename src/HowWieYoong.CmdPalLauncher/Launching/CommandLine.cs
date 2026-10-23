using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HowWieYoong.CmdPalLauncher.Launching;

/// <summary>Splitting, quoting and cleaning up Windows command lines.</summary>
internal static class CommandLine
{
    /// <summary>
    /// Flags apps are given when Windows or the app itself starts them in the background (at sign-in or after an
    /// update), which you wouldn't want when starting them from a profile.
    /// </summary>
    private static readonly HashSet<string> StartupFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--background", "--autostart", "--auto-start", "--autorun", "--startup", "--from-startup", "--system-startup",
        "--minimized", "--start-minimized", "--hidden", "--silent", "--tray", "--start-in-tray",
        "/background", "/autostart", "/startup", "/minimized", "/hidden", "/silent", "/tray",
        "-background", "-autostart", "-startup", "-minimized", "-hidden", "-silent", "-tray",
    };

    /// <summary>Splits a command line into its program and the rest, keeping the rest exactly as written.</summary>
    public static (string Program, string Arguments) SplitProgram(string commandLine)
    {
        var text = commandLine.TrimStart();
        if (text.StartsWith('"'))
        {
            // The program is quoted up to the next quote; quotes can't be escaped inside it.
            var end = text.IndexOf('"', 1);
            return end < 0 ? (text[1..], string.Empty) : (text[1..end], text[(end + 1)..].Trim());
        }

        var space = text.IndexOfAny([' ', '\t']);
        return space < 0 ? (text, string.Empty) : (text[..space], text[space..].Trim());
    }

    /// <summary>Splits arguments the way CommandLineToArgvW does.</summary>
    public static List<string> Split(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            if (c == '\\')
            {
                var backslashes = 0;
                while (i < arguments.Length && arguments[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i < arguments.Length && arguments[i] == '"')
                {
                    // 2n backslashes + quote: n backslashes, then the quote toggles quoting.
                    // 2n+1 backslashes + quote: n backslashes and a literal quote.
                    current.Append('\\', backslashes / 2);
                    if (backslashes % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else
                {
                    current.Append('\\', backslashes);
                    i--;
                }

                hasToken = true;
            }
            else if (c == '"')
            {
                // A doubled quote inside quotes is a literal quote.
                if (inQuotes && i + 1 < arguments.Length && arguments[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>Quotes a value so CommandLineToArgvW reads it back as one argument.</summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"' || c == ';'))
        {
            return value;
        }

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes are only special right before a quote.
            builder.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            builder.Append(c);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Removes background-start flags such as --autostart or /minimized. Returns the arguments unchanged
    /// (not re-quoted) when there's nothing to remove.
    /// </summary>
    public static string RemoveStartupFlags(string arguments, out List<string> removed)
    {
        var tokens = Split(arguments);
        removed = tokens.Where(IsStartupFlag).ToList();
        return removed.Count == 0 ? arguments.Trim() : string.Join(' ', tokens.Where(t => !IsStartupFlag(t)).Select(Quote));
    }

    private static bool IsStartupFlag(string token)
    {
        // "--processStart=x" and "--minimized=true" count too.
        var name = token.Split('=', 2)[0];
        return StartupFlags.Contains(name);
    }
}
