using System.Linq;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class WindowCaptureTests
{
    private static readonly (string Variable, string Folder)[] Folders =
    [
        ("%LOCALAPPDATA%", @"C:\Users\me\AppData\Local"),
        ("%ProgramFiles%", @"C:\Program Files"),
        ("%USERPROFILE%", @"C:\Users\me"),
    ];

    private readonly FakeDesktop _desktop = new();

    [Fact]
    public void DesktopApp_CapturesPathArgumentsAndPlacement()
    {
        var window = _desktop.AddWindow("Code", bounds: new PixelRect(2560, 0, 1280, 1380));
        var details = new ProcessDetails(
            @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe",
            "\"C:\\Users\\me\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe\" \"D:\\Development\\api\"",
            Description: "Visual Studio Code");

        var captured = Capture(window, details);

        Assert.Equal(
            """
            {
              "name": "Visual Studio Code",
              "path": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe",
              "arguments": "\"D:\\Development\\api\"",
              "monitor": "DELL U2515H",
              "windowState": "normal",
              "bounds": { "x": 0, "y": 0, "width": 1280, "height": 1380 }
            }
            """.ReplaceLineEndings("\n"),
            captured.Entry);
        Assert.Empty(captured.Notes);
    }

    [Fact]
    public void DesktopApp_WithoutDescription_IsNamedAfterTheProcess()
    {
        var window = _desktop.AddWindow("tool", maximized: true);

        var captured = Capture(window, new ProcessDetails(@"D:\Tools\tool.exe", @"D:\Tools\tool.exe"));

        Assert.Contains("\"name\": \"tool\"", captured.Entry);
        Assert.Contains("\"path\": \"D:\\\\Tools\\\\tool.exe\"", captured.Entry);
        Assert.DoesNotContain("arguments", captured.Entry);
        Assert.DoesNotContain("windowProcess", captured.Entry);
    }

    [Fact]
    public void StartupFlags_AreLeftOutWithANote()
    {
        var window = _desktop.AddWindow("Slack", maximized: true);

        var captured = Capture(window, new ProcessDetails(@"C:\Slack\Slack.exe", "\"C:\\Slack\\Slack.exe\" --startup --profile work"));

        Assert.Contains("\"arguments\": \"--profile work\"", captured.Entry);
        Assert.Equal(["Left out startup flags: --startup"], captured.Notes);
    }

    [Fact]
    public void WindowFromAnotherProcess_IncludesWindowProcess()
    {
        var window = _desktop.AddWindow("Docker Desktop UI", maximized: true);

        var captured = Capture(window, new ProcessDetails(@"C:\Docker\Docker Desktop.exe", "\"C:\\Docker\\Docker Desktop.exe\""));

        Assert.Contains("\"windowProcess\": \"Docker Desktop UI\"", captured.Entry);
    }

    [Fact]
    public void Explorer_UsesTheOpenFolder()
    {
        var window = _desktop.AddWindow("explorer", maximized: true);

        var captured = Capture(window, new ProcessDetails(@"C:\WINDOWS\explorer.exe", @"C:\WINDOWS\explorer.exe /factory,{x} -Embedding", ExplorerFolder: @"D:\My Projects"));

        Assert.Equal(
            """
            {
              "name": "File Explorer",
              "path": "explorer.exe",
              "arguments": "\"D:\\My Projects\"",
              "monitor": "DELL U2724DE",
              "windowState": "maximized"
            }
            """.ReplaceLineEndings("\n"),
            captured.Entry);
    }

    [Fact]
    public void Explorer_VirtualFolder_HasNoArguments()
    {
        var window = _desktop.AddWindow("explorer", maximized: true);

        var captured = Capture(window, new ProcessDetails(@"C:\WINDOWS\explorer.exe"));

        Assert.DoesNotContain("arguments", captured.Entry);
        Assert.Single(captured.Notes);
    }

    [Fact]
    public void StoreApp_IsStartedByItsAppId()
    {
        var window = _desktop.AddWindow("WhatsApp.Root", maximized: true);
        var details = new ProcessDetails(
            @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.0_x64__cv1g1gvanyjgm\WhatsApp.Root.exe",
            "\"C:\\Program Files\\WindowsApps\\5319275A.WhatsAppDesktop_2.0_x64__cv1g1gvanyjgm\\WhatsApp.Root.exe\" --background",
            "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App",
            "WhatsApp");

        var captured = Capture(window, details);

        Assert.Equal(
            """
            {
              "name": "WhatsApp",
              "path": "shell:AppsFolder\\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App",
              "windowProcess": "WhatsApp.Root",
              "monitor": "DELL U2724DE",
              "windowState": "maximized"
            }
            """.ReplaceLineEndings("\n"),
            captured.Entry);
    }

    [Fact]
    public void PackagedAppOutsideWindowsApps_IsStartedByItsExe()
    {
        // Sparse-packaged apps have an app ID but a normal install folder.
        var window = _desktop.AddWindow("app", maximized: true);

        var captured = Capture(window, new ProcessDetails(@"C:\Program Files\App\app.exe", "\"C:\\Program Files\\App\\app.exe\" -x", "Vendor.App_abc!App"));

        Assert.Contains("\"path\": \"%ProgramFiles%\\\\App\\\\app.exe\"", captured.Entry);
        Assert.Contains("\"arguments\": \"-x\"", captured.Entry);
    }

    [Fact]
    public void Terminal_RebuildsTabsFromItsCommandLine()
    {
        var window = _desktop.AddWindow("WindowsTerminal", bounds: new PixelRect(2553, 318, 1643, 1069));
        var details = new ProcessDetails(
            @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe",
            "wt.exe -w new new-tab -p \"Command Prompt\" -d D:\\front --title front --suppressApplicationTitle ; new-tab -d D:\\back claude --continue",
            "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");

        var captured = Capture(window, details);

        Assert.Equal(
            """
            {
              "name": "Windows Terminal",
              "type": "terminal",
              "tabs": [
                { "title": "front", "directory": "D:\\front", "profile": "Command Prompt" },
                { "directory": "D:\\back", "command": "claude --continue" }
              ],
              "monitor": "DELL U2515H",
              "windowState": "normal",
              "bounds": { "x": -7, "y": 318, "width": 1643, "height": 1069 }
            }
            """.ReplaceLineEndings("\n"),
            captured.Entry);
        Assert.Single(captured.Notes);
    }

    [Fact]
    public void Terminal_HandedOverFromWt_UsesTheWindowTitle()
    {
        var window = _desktop.AddWindow("WindowsTerminal", "Build; Test");

        var captured = Capture(window, new ProcessDetails(CommandLine: "WindowsTerminal.exe -Embedding"));

        Assert.Contains("\"title\": \"Build; Test\"", captured.Entry);
        Assert.DoesNotContain("Embedding", captured.Entry);
        Assert.Contains(captured.Notes, n => n.Contains("only found by name", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Terminal_HandedOverFromWt_ListsEveryTabName()
    {
        var window = _desktop.AddWindow("WindowsTerminal", "Second");

        var captured = Capture(window, new ProcessDetails(CommandLine: "WindowsTerminal.exe -Embedding", TabNames: ["First", "Second"]));

        Assert.Equal(["First", "Second"], Tabs(captured).Select(t => t.Title));
    }

    [Fact]
    public void Terminal_TabAddedLater_KeepsTheStartedTabAndAddsTheName()
    {
        var window = _desktop.AddWindow("WindowsTerminal");

        var captured = Capture(window, new ProcessDetails(CommandLine: "wt.exe --title API -d D:\\api", TabNames: ["API", "New tab"]));

        var tabs = Tabs(captured);
        Assert.Equal(("API", @"D:\api"), (tabs[0].Title, tabs[0].Directory));
        Assert.Equal(("New tab", null), (tabs[1].Title, tabs[1].Directory));
        Assert.Contains(captured.Notes, n => n.Contains("only found by name", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Terminal_TabClosedLater_LeavesItOut()
    {
        var window = _desktop.AddWindow("WindowsTerminal");

        var captured = Capture(window, new ProcessDetails(CommandLine: "wt.exe --title A ; new-tab --title B", TabNames: ["B"]));

        Assert.Equal("B", Assert.Single(Tabs(captured)).Title);
    }

    [Fact]
    public void Terminal_SameNumberOfTabs_KeepsWhatItWasStartedWith()
    {
        var window = _desktop.AddWindow("WindowsTerminal");

        var captured = Capture(window, new ProcessDetails(CommandLine: "wt.exe -p Ubuntu", TabNames: ["shell prompt"]));

        var tab = Assert.Single(Tabs(captured));
        Assert.Null(tab.Title);
        Assert.Equal("Ubuntu", tab.Profile);
    }

    [Fact]
    public void Terminal_PlainWindow_DoesNotFixTheShellsNameAsATitle()
    {
        var window = _desktop.AddWindow("WindowsTerminal");

        var captured = Capture(window, new ProcessDetails(CommandLine: "WindowsTerminal.exe", TabNames: ["PowerShell"]));

        Assert.Contains("\"tabs\": []", captured.Entry);
    }

    [Fact]
    public void Terminal_StartedWithoutTabs_HasAnEmptyList()
    {
        var window = _desktop.AddWindow("WindowsTerminal", maximized: true);

        var captured = Capture(window, new ProcessDetails(CommandLine: "\"C:\\Program Files\\WindowsApps\\x\\WindowsTerminal.exe\""));

        Assert.Contains("\"tabs\": []", captured.Entry);
        Assert.Contains("Add them under", captured.Notes[0]);
    }

    [Fact]
    public void UnreadableProcess_LeavesAPathToFillIn()
    {
        var window = _desktop.AddWindow("elevated", maximized: true);

        var captured = Capture(window, new ProcessDetails());

        Assert.Contains("\"path\": \"\",  // fill in: couldn't read this app's path", captured.Entry);
        Assert.Contains("administrator", captured.Notes[0]);
    }

    [Fact]
    public void Placement_IsTheSettingsOnly()
    {
        var window = _desktop.AddWindow("Code", maximized: true);

        var captured = Capture(window, new ProcessDetails(@"C:\Code.exe"));

        Assert.Equal("\"windowProcess\": \"Code\",\n\"monitor\": \"DELL U2724DE\",\n\"windowState\": \"maximized\"", captured.Placement);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\x.exe", @"%LOCALAPPDATA%\Programs\x.exe")]
    [InlineData(@"C:\Users\me\tools\x.exe", @"%USERPROFILE%\tools\x.exe")]
    [InlineData(@"c:\program files\x\x.exe", @"%ProgramFiles%\x\x.exe")]
    [InlineData(@"C:\Program Files (x86)\x.exe", @"C:\Program Files (x86)\x.exe")]
    [InlineData(@"D:\x.exe", @"D:\x.exe")]
    public void PortablePath_UsesTheMostSpecificVariable(string path, string expected)
    {
        Assert.Equal(expected, WindowCapture.PortablePath(path, Folders));
    }

    [Theory]
    [InlineData("Downloads - File Explorer", @"C:\Users\me\Downloads")]
    [InlineData("Unknown - File Explorer", @"D:\Projects")]
    public void ExplorerFolder_PrefersTheTabMatchingTheTitle(string title, string expected)
    {
        var folder = ExplorerFolders.PickFolder(title, [("Projects", "file:///D:/Projects"), ("Downloads", "file:///C:/Users/me/Downloads")]);

        Assert.Equal(expected, folder);
    }

    [Fact]
    public void ExplorerFolder_VirtualLocationOrNoTabs_IsNull()
    {
        Assert.Null(ExplorerFolders.PickFolder("Home - File Explorer", [("Home", "")]));
        Assert.Null(ExplorerFolders.PickFolder("x", []));
    }

    [Fact]
    public void Entry_ParsesAsAProfileApp()
    {
        var window = _desktop.AddWindow("WindowsTerminal", maximized: true);
        var captured = Capture(window, new ProcessDetails(CommandLine: "wt.exe new-tab --title \"A \\\"B\\\"\" -d C:\\x ; nt -p P"));

        using var file = new TempProfilesFile($$"""{ "profiles": [ { "name": "P", "apps": [ {{captured.Entry}} ] } ] }""");
        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Null(store.Error);
        var app = store.Profiles[0].Apps[0];
        Assert.True(app.IsTerminal);
        Assert.Equal("A \"B\"", app.Tabs![0].Title);
        Assert.Equal("P", app.Tabs[1].Profile);
        Assert.True(app.IsMaximized);
    }

    private CapturedWindow Capture(WindowInfo window, ProcessDetails details) =>
        WindowCapture.Capture(window, details, _desktop.Monitors, Folders);

    private static System.Collections.Generic.List<TerminalTab> Tabs(CapturedWindow captured) =>
        System.Text.Json.JsonSerializer.Deserialize(captured.Entry, ProfilesJsonContext.Default.AppEntry)!.Tabs!;
}
