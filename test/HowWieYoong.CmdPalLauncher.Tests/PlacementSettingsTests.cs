using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

/// <summary>Reading placement settings from profiles.json, and writing them back out as a snippet.</summary>
public class PlacementSettingsTests
{
    [Theory]
    [InlineData("2", 2, null)]
    [InlineData("\"2\"", 2, null)]
    [InlineData("\"primary\"", null, "primary")]
    [InlineData("\"DELL U2515H\"", null, "DELL U2515H")]
    public void Monitor_AcceptsANumberOrAName(string json, int? number, string? name)
    {
        var app = ReadApp($$"""{ "path": "a.exe", "monitor": {{json}} }""", out var error);

        Assert.Null(error);
        Assert.Equal(new MonitorSelector(number, name), app.Monitor);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("\"\"")]
    [InlineData("{}")]
    public void Monitor_OtherValues_AreAnError(string json)
    {
        ReadApp($$"""{ "path": "a.exe", "monitor": {{json}} }""", out var error);

        Assert.Contains("\"monitor\" must be a number or a monitor name.", error);
    }

    [Fact]
    public void PlacementSettings_AreRead()
    {
        var app = ReadApp("""
            { "path": "a.exe", "windowState": "Maximized", "windowProcess": "A Ui", "windowTitle": "Main",
              "windowTimeoutMs": 3000, "bounds": { "x": -5, "y": 10, "width": 800, "height": 600 } }
            """, out _);

        Assert.True(app.HasPlacement);
        Assert.True(app.IsMaximized);
        Assert.Equal(("A Ui", "Main", 3000), (app.WindowProcess, app.WindowTitle, app.WindowTimeoutMs));
        Assert.Equal((-5, 10, 800, 600), (app.Bounds!.X, app.Bounds.Y, app.Bounds.Width, app.Bounds.Height));
    }

    [Fact]
    public void NoPlacementSettings_HasNoPlacement()
    {
        Assert.False(ReadApp("""{ "path": "a.exe", "windowProcess": "a" }""", out _).HasPlacement);
    }

    [Fact]
    public void Snippet_NormalWindow_HasBoundsRelativeToTheWorkArea()
    {
        var desktop = new FakeDesktop();
        var window = desktop.AddWindow("Code", bounds: new PixelRect(2600, 50, 1200, 900));

        Assert.Equal(
            """
            "windowProcess": "Code",
            "monitor": "DELL U2515H",
            "windowState": "normal",
            "bounds": { "x": 40, "y": 50, "width": 1200, "height": 900 }
            """.ReplaceLineEndings("\n"),
            WindowCapture.Capture(window, new ProcessDetails(), desktop.Monitors).Placement);
    }

    [Fact]
    public void Snippet_MaximizedWindow_HasNoBounds()
    {
        var desktop = new FakeDesktop();
        var window = desktop.AddWindow("explorer", bounds: new PixelRect(-7, -7, 2574, 1394), maximized: true);

        Assert.Equal(
            """
            "windowProcess": "explorer",
            "monitor": "DELL U2724DE",
            "windowState": "maximized"
            """.ReplaceLineEndings("\n"),
            WindowCapture.Capture(window, new ProcessDetails(), desktop.Monitors).Placement);
    }

    [Fact]
    public void Snippet_IdenticalMonitors_UsesTheNumber()
    {
        var desktop = new FakeDesktop();
        desktop.Monitors[1] = desktop.Monitors[1] with { Name = desktop.Monitors[0].Name };

        Assert.Equal("2", WindowCapture.MonitorValue(desktop.Monitors[1], desktop.Monitors));
        Assert.Equal("\"DELL U2724DE\"", WindowCapture.MonitorValue(desktop.Monitors[0], [desktop.Monitors[0]]));
    }

    [Fact]
    public void Snippet_RoundTripsThroughProfilesJson()
    {
        var desktop = new FakeDesktop();
        var window = desktop.AddWindow("My \"App\"", bounds: new PixelRect(2600, 50, 1200, 900));

        var app = ReadApp($$"""{ "path": "a.exe", {{WindowCapture.Capture(window, new ProcessDetails(), desktop.Monitors).Placement}} }""", out var error);

        Assert.Null(error);
        Assert.Equal("My \"App\"", app.WindowProcess);
        Assert.Equal(window.Bounds, MonitorLayout.TargetBounds(MonitorLayout.Resolve(app.Monitor!, desktop.Monitors)!, app.Bounds, default));
    }

    private static AppEntry ReadApp(string appJson, out string? error)
    {
        using var file = new TempProfilesFile($$"""{ "profiles": [ { "name": "P", "apps": [ {{appJson}} ] } ] }""");
        using var store = new ProfileStore(file.FilePath, watch: false);
        error = store.Error;
        return error is null ? store.Profiles[0].Apps[0] : new AppEntry();
    }
}
