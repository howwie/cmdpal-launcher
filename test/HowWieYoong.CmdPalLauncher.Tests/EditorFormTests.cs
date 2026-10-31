using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HowWieYoong.CmdPalLauncher.Editing;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class EditorFormTests
{
    private readonly FakeDesktop _desktop = new();

    [Fact]
    public void AppForm_ShowsTheCurrentValues()
    {
        var app = new AppEntry { Name = "Code", Path = "code", Arguments = ".", DelayMs = 500, RunAsAdmin = true, Monitor = new MonitorSelector(null, "DELL U2515H"), WindowState = "Maximized" };

        var inputs = Inputs(AppForm.Template(app, _desktop.Monitors));

        Assert.Equal("Code", Value(inputs, "name"));
        Assert.Equal("code", Value(inputs, "path"));
        Assert.Equal("500", Value(inputs, "delayMs"));
        Assert.Equal("true", Value(inputs, "runAsAdmin"));
        Assert.Equal("false", Value(inputs, "skipIfRunning"));
        Assert.Equal("DELL U2515H", Value(inputs, "monitor"));
        Assert.Equal("maximized", Value(inputs, "windowState"));
        Assert.Null(Value(inputs, "boundsX"));
        Assert.True(inputs["path"]["isRequired"]!.GetValue<bool>());
    }

    [Fact]
    public void AppForm_TerminalHasWindowButNoPathWorkingDirectoryOrWindowProcess()
    {
        var inputs = Inputs(AppForm.Template(new AppEntry { Type = "terminal", Window = "last" }, _desktop.Monitors));

        Assert.Equal("last", Value(inputs, "window"));
        Assert.False(inputs.ContainsKey("path"));
        Assert.False(inputs.ContainsKey("workingDirectory"));
        Assert.False(inputs.ContainsKey("windowProcess"));
    }

    [Fact]
    public void AppForm_TerminalKeepsAHandWrittenPathAndWindowProcess()
    {
        var app = new AppEntry { Type = "terminal", Path = @"C:\dev\wtd.exe", WindowProcess = "WindowsTerminalDev" };

        Assert.Null(AppForm.Apply(app, Submit(AppForm.Template(app, _desktop.Monitors))));

        Assert.Equal((@"C:\dev\wtd.exe", "WindowsTerminalDev"), (app.Path, app.WindowProcess));
    }

    [Fact]
    public void AppForm_AppShowsPathAndWindowProcess()
    {
        var inputs = Inputs(AppForm.Template(new AppEntry { Path = "a.exe", WindowProcess = "a-ui" }, _desktop.Monitors));

        Assert.Equal(("a.exe", "a-ui"), (Value(inputs, "path"), Value(inputs, "windowProcess")));
    }

    public static TheoryData<string> Apps => new()
    {
        """{ "path": "a.exe" }""",
        """{ "name": "Code", "path": "%LOCALAPPDATA%\\Code.exe", "arguments": "\"D:\\x y\"", "workingDirectory": "D:\\", "runAsAdmin": true, "skipIfRunning": true, "delayMs": 1000 }""",
        """{ "path": "a.exe", "monitor": 2, "windowState": "normal", "bounds": { "x": -7, "y": 0, "width": 1280, "height": 1380 }, "windowProcess": "A UI", "windowTitle": "Main", "windowTimeoutMs": 3000 }""",
        """{ "path": "a.exe", "monitor": "primary", "bounds": { "x": 10, "y": 20 } }""",
        """{ "path": "a.exe", "monitor": "LG Ultrafine", "futureSetting": [1] }""",
        """{ "type": "terminal", "window": "last", "arguments": "--maximized", "tabs": [ { "title": "API", "directory": "D:\\api" } ], "monitor": "DELL U2724DE" }""",
    };

    /// <summary>Opening a form and saving it without changes must leave the entry exactly as it was.</summary>
    [Theory]
    [MemberData(nameof(Apps))]
    public void AppForm_SavingUnchanged_KeepsTheEntryAsItWas(string json)
    {
        var app = JsonSerializer.Deserialize(json, ProfilesJsonContext.Default.AppEntry)!;
        var before = Serialize(app);

        var error = AppForm.Apply(app, Submit(AppForm.Template(app, _desktop.Monitors)));

        Assert.Null(error);
        Assert.Equal(before, Serialize(app));
    }

    [Fact]
    public void AppForm_Apply_ReadsChanges()
    {
        var app = new AppEntry { Path = "old.exe", Bounds = new WindowBounds { X = 1, Y = 1, Width = 1, Height = 1 } };

        var error = AppForm.Apply(app, """
            { "path": " new.exe ", "arguments": "", "runAsAdmin": "true", "delayMs": "250.0", "monitor": "2",
              "windowState": "maximized", "boundsX": "", "boundsY": "", "boundsWidth": "", "boundsHeight": "" }
            """);

        Assert.Null(error);
        Assert.Equal(("new.exe", (string?)null, true, 250), (app.Path, app.Arguments, app.RunAsAdmin, app.DelayMs));
        Assert.Equal(new MonitorSelector(2, null), app.Monitor);
        Assert.True(app.IsMaximized);
        Assert.Null(app.Bounds);
    }

    [Fact]
    public void AppForm_Apply_PartialBoundsFillInZeros()
    {
        var app = new AppEntry { Path = "a.exe" };

        AppForm.Apply(app, """{ "path": "a.exe", "boundsWidth": "800" }""");

        Assert.Equal((0, 0, 800, 0), (app.Bounds!.X, app.Bounds.Y, app.Bounds.Width, app.Bounds.Height));
    }

    [Theory]
    [InlineData("""{ "path": "" }""", "Path is required.")]
    [InlineData("""{ "path": "a", "delayMs": "1.5" }""", "Wait before starting must be a whole number.")]
    [InlineData("""{ "path": "a", "delayMs": "-1" }""", "Wait before starting can't be less than 0.")]
    [InlineData("""{ "path": "a", "boundsWidth": "abc" }""", "Width must be a whole number.")]
    public void AppForm_Apply_RejectsBadInput_WithoutChangingTheEntry(string payload, string expected)
    {
        var app = new AppEntry { Path = "original.exe" };

        Assert.Equal(expected, AppForm.Apply(app, payload));
        Assert.Equal("original.exe", app.Path);
    }

    [Fact]
    public void MonitorChoices_ListConnectedMonitors()
    {
        var choices = AppForm.MonitorChoices(null, _desktop.Monitors);

        Assert.Equal(
            [
                ("Don't move the window", ""),
                ("Primary monitor", "primary"),
                ("Monitor 1: DELL U2724DE (primary)", "DELL U2724DE"),
                ("Monitor 2: DELL U2515H", "DELL U2515H"),
            ],
            choices);
    }

    [Fact]
    public void MonitorChoices_KeepTheSavedFormOfAConnectedMonitor()
    {
        var choices = AppForm.MonitorChoices(new MonitorSelector(2, null), _desktop.Monitors);

        Assert.Equal("2", choices[3].Value);
        Assert.Equal(4, choices.Count);
    }

    [Fact]
    public void MonitorChoices_KeepADisconnectedMonitor()
    {
        var choices = AppForm.MonitorChoices(new MonitorSelector(null, "LG Ultrafine"), _desktop.Monitors);

        Assert.Equal(("LG Ultrafine (not connected)", "LG Ultrafine"), choices[^1]);
    }

    [Fact]
    public void MonitorChoices_IdenticalMonitorsUseNumbers()
    {
        _desktop.Monitors[1] = _desktop.Monitors[1] with { Name = _desktop.Monitors[0].Name };

        var choices = AppForm.MonitorChoices(null, _desktop.Monitors);

        Assert.Equal(["1", "2"], choices.Skip(2).Select(c => c.Value));
    }

    [Fact]
    public void TabForm_RoundTripsAndNeedsSomethingFilledIn()
    {
        var tab = new TerminalTab { Title = "API", Directory = @"D:\api", Profile = "PowerShell", Command = "npm start", Color = "#0078D4" };
        var copy = new TerminalTab();

        Assert.Null(TabForm.Apply(copy, Submit(TabForm.Template(tab))));
        Assert.Equal((tab.Title, tab.Directory, tab.Profile, tab.Command, tab.Color), (copy.Title, copy.Directory, copy.Profile, copy.Command, copy.Color));

        Assert.Null(TabForm.Apply(new TerminalTab(), """{ "color": "#fff" }"""));
        Assert.Equal("Fill in at least a title, directory, profile, command or colour.", TabForm.Apply(new TerminalTab(), "{}"));
    }

    [Fact]
    public void ProfileForm_ReadsTrimmedValues()
    {
        var inputs = Inputs(ProfileForm.Template(new LaunchProfile { Name = "Dev", Description = "Daily" }));
        Assert.Equal("Dev", Value(inputs, "name"));

        Assert.Equal(("Gaming", (string?)null, "🎮"), ProfileForm.Read("""{ "name": " Gaming ", "description": "  ", "icon": "🎮" }"""));
        Assert.Equal((string.Empty, (string?)null, (string?)null), ProfileForm.Read("not json"));
    }

    [Theory]
    [InlineData("\uE943", @"\uE943")]
    [InlineData("🎮", "🎮")]
    [InlineData(@"C:\icons\dev.png", @"C:\icons\dev.png")]
    [InlineData(null, null)]
    public void IconText_ShowsGlyphsAsCodes(string? icon, string? editable)
    {
        Assert.Equal(editable, IconText.ToEditable(icon));
        Assert.Equal(icon, IconText.FromEditable(editable));
    }

    [Theory]
    [InlineData(@"\uE943")]
    [InlineData(@"\ue943")]
    [InlineData("U+E943")]
    [InlineData("0xE943")]
    [InlineData("E943")]
    [InlineData(" E943 ")]
    public void IconText_ReadsGlyphCodes(string text)
    {
        Assert.Equal("\uE943", IconText.FromEditable(text));
    }

    [Theory]
    [InlineData("1234")] // not a private-use character, so it isn't a glyph
    [InlineData("cafe")]
    [InlineData(@"\uE94")]
    public void IconText_LeavesOtherTextAlone(string text)
    {
        Assert.Equal(text, IconText.FromEditable(text));
    }

    [Fact]
    public void ProfileForm_GlyphIconRoundTrips()
    {
        var inputs = Inputs(ProfileForm.Template(new LaunchProfile { Name = "Dev", Icon = "\uE943" }));
        Assert.Equal(@"\uE943", Value(inputs, "icon"));

        Assert.Equal("\uE943", ProfileForm.Read(Submit(ProfileForm.Template(new LaunchProfile { Name = "Dev", Icon = "\uE943" }))).Icon);
    }

    [Fact]
    public void Card_IsAValidAdaptiveCard()
    {
        var card = JsonNode.Parse(AppForm.Template(new AppEntry(), _desktop.Monitors))!.AsObject();

        Assert.Equal("AdaptiveCard", card["type"]!.GetValue<string>());
        Assert.Equal("Action.Submit", card["actions"]![0]!["type"]!.GetValue<string>());
        Assert.All(Inputs(card.ToJsonString()).Keys, id => Assert.Matches("^[a-zA-Z]+$", id));
    }

    private static string Serialize(AppEntry app) =>
        ProfileWriter.Serialize(new ProfilesFile { Profiles = [new LaunchProfile { Name = "P", Apps = [app] }] });

    private static Dictionary<string, JsonObject> Inputs(string cardJson) => CardJson.Inputs(cardJson);

    private static string? Value(Dictionary<string, JsonObject> inputs, string id) => CardJson.Value(inputs, id);

    private static string Submit(string cardJson) => CardJson.Submit(cardJson);
}
