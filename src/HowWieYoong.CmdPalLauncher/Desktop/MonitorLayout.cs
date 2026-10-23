using System;
using System.Collections.Generic;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Desktop;

/// <summary>Monitor numbering, lookup and window-position maths.</summary>
internal static class MonitorLayout
{
    /// <summary>Numbers monitors left to right, then top to bottom, starting at 1.</summary>
    public static List<MonitorInfo> Number(IEnumerable<MonitorInfo> monitors) =>
        monitors
            .OrderBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .Select((m, index) => m with { Number = index + 1 })
            .ToList();

    public static MonitorInfo? Resolve(MonitorSelector selector, IReadOnlyList<MonitorInfo> monitors)
    {
        if (selector.Number is int number)
        {
            return monitors.FirstOrDefault(m => m.Number == number);
        }

        if (selector.IsPrimary)
        {
            return monitors.FirstOrDefault(m => m.IsPrimary);
        }

        var name = selector.Name ?? string.Empty;
        var exact = monitors.FirstOrDefault(m =>
            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.DeviceName, @"\\.\" + name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Allow a shorter name like "U2720Q", as long as it isn't ambiguous.
        var partial = monitors.Where(m => m.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    /// <summary>The monitor a window is mostly on, like MonitorFromWindow.</summary>
    public static MonitorInfo? MonitorFor(PixelRect window, IReadOnlyList<MonitorInfo> monitors) =>
        monitors
            .Select(m => (Monitor: m, Area: IntersectionArea(window, m.Bounds)))
            .Where(x => x.Area > 0)
            .OrderByDescending(x => x.Area)
            .Select(x => x.Monitor)
            .FirstOrDefault();

    /// <summary>
    /// Where to put a window on a monitor: at <paramref name="bounds"/> relative to the work area,
    /// or centred in the work area at its current size when no bounds are given.
    /// </summary>
    public static PixelRect TargetBounds(MonitorInfo monitor, WindowBounds? bounds, PixelRect current)
    {
        var work = monitor.WorkArea;
        var width = bounds is { Width: > 0 } ? bounds.Width : Math.Min(current.Width, work.Width);
        var height = bounds is { Height: > 0 } ? bounds.Height : Math.Min(current.Height, work.Height);

        return bounds is null
            ? new PixelRect(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height)
            : new PixelRect(work.X + bounds.X, work.Y + bounds.Y, width, height);
    }

    /// <summary>The inverse of <see cref="TargetBounds"/>: a window's position relative to the monitor's work area.</summary>
    public static WindowBounds RelativeBounds(MonitorInfo monitor, PixelRect window) => new()
    {
        X = window.X - monitor.WorkArea.X,
        Y = window.Y - monitor.WorkArea.Y,
        Width = window.Width,
        Height = window.Height,
    };

    private static long IntersectionArea(PixelRect a, PixelRect b)
    {
        long width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        long height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}
