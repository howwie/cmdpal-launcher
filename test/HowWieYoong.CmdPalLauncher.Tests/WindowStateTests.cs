using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Editing;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Pages.Editor;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

/// <summary>The "minimized" window state, and position and size being hidden for maximized and minimized windows.</summary>
public class WindowStateTests
{
    private readonly FakeDesktop _desktop = new();

    [Theory]
    [InlineData(null, "")]
    [InlineData("normal", "Normal")]
    [InlineData("Maximized", "Maximized")]
    [InlineData("MINIMIZED", "Minimized")]
    public void Show_FollowsWindowState(string? state, string expected)
    {
        Assert.Equal(expected, new AppEntry { WindowState = state }.Show.ToString());
    }

    [Fact]
    public void Minimized_IsMovedToItsMonitorThenMinimized()
    {
        var runner = new FakeProcessRunner();
        runner.OnStart = info => _desktop.AddWindow(Path.GetFileNameWithoutExtension(info.FileName));
        var profile = new LaunchProfile { Name = "P", Apps = [new AppEntry { Path = "chat.exe", Monitor = new MonitorSelector(2, null), WindowState = "minimized" }] };

        var failures = AppLauncher.LaunchAll(profile, runner, _desktop);

        Assert.Empty(failures);
        Assert.All(_desktop.Placed, p => Assert.Equal(WindowShow.Minimized, p.Show));
        Assert.True(_desktop.Placed[0].Bounds.X >= 2560);
    }

    [Fact]
    public void Minimized_AlreadyMinimized_IsMovedOnceAndNotBroughtBackUp()
    {
        var placed = Place(new AppEntry { Path = "chat.exe", Monitor = new MonitorSelector(2, null), WindowState = "minimized" }, minimized: true);

        var (_, bounds, show) = Assert.Single(placed);
        Assert.Equal(WindowShow.Minimized, show);
        Assert.Equal(new PixelRect(3440, 390, 800, 600), bounds);
    }

    [Fact]
    public void Minimized_WithoutMonitor_StaysOnTheMonitorItsWindowRestoresTo()
    {
        var placed = Place(new AppEntry { Path = "chat.exe", WindowState = "minimized" }, minimized: true, restored: new PixelRect(3000, 100, 800, 600));

        Assert.True(placed[0].Bounds.X >= 2560);
    }

    [Theory]
    [InlineData(true, false, "Maximized")]
    [InlineData(false, true, "Minimized")]
    public void LeftAsItOpens_IsPutBackInThatStateAtItsRestoredSize(bool maximized, bool minimized, string expected)
    {
        var placed = Place(new AppEntry { Path = "chat.exe", Monitor = new MonitorSelector(2, null) }, maximized, minimized);

        Assert.All(placed, p => Assert.Equal(expected, p.Show.ToString()));
        Assert.Equal(new PixelRect(3440, 390, 800, 600), placed[0].Bounds);
    }

    [Fact]
    public void Normal_RestoresAMaximizedWindow()
    {
        var placed = Place(new AppEntry { Path = "chat.exe", Monitor = new MonitorSelector(2, null), WindowState = "normal" }, maximized: true);

        Assert.All(placed, p => Assert.Equal(WindowShow.Normal, p.Show));
    }

    [Fact]
    public void AppForm_OffersMinimized()
    {
        var state = CardJson.Inputs(AppForm.Template(new AppEntry { Path = "a" }, _desktop.Monitors))["windowState"];

        Assert.Equal(["", "normal", "maximized", "minimized"], state["choices"]!.AsArray().Select(c => c!["value"]!.GetValue<string>()));
    }

    /// <summary>Forms can't show or hide fields as the dropdown changes, so position and size are always there.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("normal")]
    [InlineData("maximized")]
    [InlineData("minimized")]
    public void AppForm_AlwaysShowsPositionAndSize(string? state)
    {
        var app = new AppEntry { Path = "a", WindowState = state, Bounds = new WindowBounds { X = 10, Y = 20, Width = 800, Height = 600 } };
        var card = AppForm.Template(app, _desktop.Monitors);

        Assert.Equal("800", CardJson.Value(CardJson.Inputs(card), "boundsWidth"));
        Assert.Contains(CardJson.Elements(card), e => e["text"]?.GetValue<string>() is { } text && text.Contains("where it goes when it's restored", System.StringComparison.Ordinal));

        Assert.Null(AppForm.Apply(app, CardJson.Submit(card)));
        Assert.Equal((10, 20, 800, 600), (app.Bounds!.X, app.Bounds.Y, app.Bounds.Width, app.Bounds.Height));
    }

    [Fact]
    public void AppForm_ChangingToMaximized_KeepsTheShownPositionAndSize()
    {
        var app = new AppEntry { Path = "a", Bounds = new WindowBounds { X = 1, Y = 2, Width = 3, Height = 4 } };

        Assert.Null(AppForm.Apply(app, CardJson.Submit(AppForm.Template(app, _desktop.Monitors), new() { ["windowState"] = "maximized", ["boundsWidth"] = "900" })));

        Assert.True(app.IsMaximized);
        Assert.Equal(900, app.Bounds!.Width);
    }

    [Fact]
    public void ProfileCard_IconLabelSitsAboveTheFieldAndPreview()
    {
        var card = ProfileCard.Template(new LaunchProfile { Name = "Dev", Icon = "\uE943" }, _desktop.Monitors, new HashSet<int>(), _ => "data:image/png;base64,AAAA");
        var elements = CardJson.Elements(card);

        var icon = CardJson.Inputs(card)["p_icon"];
        Assert.Null(icon["label"]);

        var row = elements.Single(e => CardJson.Type(e) == "ColumnSet" && e.ToJsonString().Contains("p_icon", System.StringComparison.Ordinal));
        var label = elements[elements.IndexOf(row) - 1];
        Assert.Equal(("TextBlock", "Icon"), (CardJson.Type(label), label["text"]!.GetValue<string>()));
    }

    [Fact]
    public void ProfileCard_ArrowColumnHasAFixedWidth()
    {
        var card = ProfileCard.Template(new LaunchProfile { Name = "Dev", Apps = [new AppEntry { Path = "a" }] }, _desktop.Monitors, new HashSet<int>());

        var header = CardJson.Elements(card).First(e => CardJson.Type(e) == "ColumnSet" && e["selectAction"] is not null);
        Assert.Equal("20px", header["columns"]![0]!["width"]!.GetValue<string>());
    }

    /// <summary>Launches an app whose window opens maximized or minimized, at 800x600 when restored.</summary>
    private List<(nint Handle, PixelRect Bounds, WindowShow Show)> Place(AppEntry app, bool maximized = false, bool minimized = false, PixelRect? restored = null)
    {
        var bounds = minimized ? new PixelRect(-32000, -32000, 160, 28) : new PixelRect(-8, -8, 2576, 1396);
        var runner = new FakeProcessRunner();
        runner.OnStart = _ => _desktop.AddWindow("chat", bounds: bounds, maximized: maximized, minimized: minimized, restored: restored ?? new PixelRect(100, 100, 800, 600));

        Assert.Empty(AppLauncher.LaunchAll(new LaunchProfile { Name = "P", Apps = [app] }, runner, _desktop));
        return _desktop.Placed;
    }
}