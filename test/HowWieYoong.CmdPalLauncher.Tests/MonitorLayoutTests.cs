using System.Linq;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class MonitorLayoutTests
{
    private readonly FakeDesktop _desktop = new();

    [Fact]
    public void Number_CountsLeftToRightThenTopToBottom()
    {
        var unordered = new[]
        {
            FakeDesktop.Monitor(0, "Right", primary: false, x: 2560),
            FakeDesktop.Monitor(0, "Below left", primary: false, x: -1920, y: 1440),
            FakeDesktop.Monitor(0, "Middle", primary: true, x: 0),
            FakeDesktop.Monitor(0, "Left", primary: false, x: -1920),
        };

        var numbered = MonitorLayout.Number(unordered);

        Assert.Equal(["Left", "Below left", "Middle", "Right"], numbered.Select(m => m.Name));
        Assert.Equal([1, 2, 3, 4], numbered.Select(m => m.Number));
    }

    [Theory]
    [InlineData(2, "DELL U2515H")]
    [InlineData(1, "DELL U2724DE")]
    public void Resolve_ByNumber(int number, string expected)
    {
        Assert.Equal(expected, Resolve(new MonitorSelector(number, null))?.Name);
    }

    [Theory]
    [InlineData("primary", "DELL U2724DE")]
    [InlineData("PRIMARY", "DELL U2724DE")]
    [InlineData("dell u2515h", "DELL U2515H")]
    [InlineData(@"\\.\DISPLAY2", "DELL U2515H")]
    [InlineData("DISPLAY2", "DELL U2515H")]
    [InlineData("U2515H", "DELL U2515H")]
    public void Resolve_ByName(string name, string expected)
    {
        Assert.Equal(expected, Resolve(new MonitorSelector(null, name))?.Name);
    }

    [Theory]
    [InlineData(3, null)]
    [InlineData(0, null)]
    [InlineData(null, "LG")]
    [InlineData(null, "DELL")] // matches both, so it's ambiguous
    public void Resolve_UnknownOrAmbiguous_IsNull(int? number, string? name)
    {
        Assert.Null(Resolve(new MonitorSelector(number, name)));
    }

    [Fact]
    public void MonitorFor_PicksTheMonitorWithMostOfTheWindow()
    {
        Assert.Equal(1, MonitorLayout.MonitorFor(new PixelRect(2000, 0, 800, 600), _desktop.Monitors)?.Number);
        Assert.Equal(2, MonitorLayout.MonitorFor(new PixelRect(2400, 0, 800, 600), _desktop.Monitors)?.Number);
        Assert.Null(MonitorLayout.MonitorFor(new PixelRect(-5000, 0, 800, 600), _desktop.Monitors));
    }

    [Fact]
    public void MonitorFor_MaximizedWindowOverhangingTheEdges_StillMatches()
    {
        // Maximized windows extend a few pixels past the monitor on every side.
        Assert.Equal(2, MonitorLayout.MonitorFor(new PixelRect(2553, -7, 2574, 1394), _desktop.Monitors)?.Number);
    }

    [Fact]
    public void TargetBounds_IsRelativeToTheWorkArea()
    {
        var bounds = new WindowBounds { X = 10, Y = 20, Width = 1200, Height = 800 };

        var target = MonitorLayout.TargetBounds(_desktop.Monitors[1], bounds, new PixelRect(0, 0, 500, 500));

        Assert.Equal(new PixelRect(2570, 20, 1200, 800), target);
    }

    [Fact]
    public void TargetBounds_ZeroSize_KeepsTheWindowSize()
    {
        var target = MonitorLayout.TargetBounds(_desktop.Monitors[1], new WindowBounds { X = 0, Y = 0 }, new PixelRect(5, 5, 640, 480));

        Assert.Equal(new PixelRect(2560, 0, 640, 480), target);
    }

    [Fact]
    public void TargetBounds_WithoutBounds_CentresInTheWorkArea()
    {
        var target = MonitorLayout.TargetBounds(_desktop.Monitors[1], null, new PixelRect(5, 5, 1000, 600));

        Assert.Equal(new PixelRect(2560 + 780, 390, 1000, 600), target);
    }

    [Fact]
    public void TargetBounds_WithoutBounds_ShrinksWindowsBiggerThanTheWorkArea()
    {
        var target = MonitorLayout.TargetBounds(_desktop.Monitors[0], null, new PixelRect(0, 0, 4000, 2000));

        Assert.Equal(new PixelRect(0, 0, 2560, 1380), target);
    }

    [Fact]
    public void RelativeBounds_RoundTripsThroughTargetBounds()
    {
        var monitor = _desktop.Monitors[1];
        var window = new PixelRect(2600, 50, 1234, 567);

        var relative = MonitorLayout.RelativeBounds(monitor, window);

        Assert.Equal((40, 50, 1234, 567), (relative.X, relative.Y, relative.Width, relative.Height));
        Assert.Equal(window, MonitorLayout.TargetBounds(monitor, relative, new PixelRect(0, 0, 1, 1)));
    }

    private MonitorInfo? Resolve(MonitorSelector selector) => MonitorLayout.Resolve(selector, _desktop.Monitors);
}
