using System.Linq;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class TerminalCommandLineTests
{
    [Fact]
    public void ParseTabs_ReadsBackEscapedSemicolons()
    {
        var tab = new TerminalTab { Title = "Build; Test", Directory = @"D:\a;b", Profile = "c;d", Command = "cd x; npm run dev" };
        var arguments = AppLauncher.BuildTerminalArguments(new AppEntry { Type = "terminal", Tabs = [tab] });

        var parsed = Assert.Single(TerminalCommandLine.ParseTabs(arguments, out _));

        Assert.Equal((tab.Title, tab.Directory, tab.Profile, tab.Command), (parsed.Title, parsed.Directory, parsed.Profile, parsed.Command));
    }

    [Fact]
    public void ParsesTabsWithAllOptions()
    {
        var tabs = TerminalCommandLine.ParseTabs(
            "-w new new-tab -p \"Command Prompt\" -d D:\\front --title front --suppressApplicationTitle --tabColor #f00 ; nt --profile Ubuntu --startingDirectory \"D:\\my api\"",
            out var skipped);

        Assert.Empty(skipped);
        Assert.Equal(2, tabs.Count);
        Assert.Equal(("front", @"D:\front", "Command Prompt", "#f00", null), Describe(tabs[0]));
        Assert.Equal(((string?)null, @"D:\my api", "Ubuntu", (string?)null, (string?)null), Describe(tabs[1]));
    }

    [Fact]
    public void CommandIsEverythingAfterTheOptions()
    {
        var tab = Assert.Single(TerminalCommandLine.ParseTabs("new-tab -d D:\\api claude --continue \"two words\"", out _));

        Assert.Equal("claude --continue \"two words\"", tab.Command);
    }

    [Fact]
    public void OptionsWithoutASubcommand_AreANewTab()
    {
        var tab = Assert.Single(TerminalCommandLine.ParseTabs("-M --pos 10,10 -p PowerShell", out _));

        Assert.Equal("PowerShell", tab.Profile);
    }

    [Fact]
    public void NoArguments_HasNoTabs()
    {
        Assert.Empty(TerminalCommandLine.ParseTabs(string.Empty, out _));
        Assert.Empty(TerminalCommandLine.ParseTabs("-w new", out _));
    }

    [Fact]
    public void SplitPanes_AreSkippedAndReported()
    {
        var tabs = TerminalCommandLine.ParseTabs("new-tab -p A ; split-pane -V -p B ; sp -H ; new-tab -p C", out var skipped);

        Assert.Equal(["A", "C"], tabs.Select(t => t.Profile));
        Assert.Equal(["split-pane", "sp"], skipped);
    }

    [Fact]
    public void EscapedSemicolon_StaysInTheCommand()
    {
        var tab = Assert.Single(TerminalCommandLine.ParseTabs(@"new-tab cmd /k cd x\; dir", out _));

        Assert.Equal("cmd /k cd x; dir", tab.Command);
    }

    [Fact]
    public void RoundTripsWithBuildTerminalArguments()
    {
        var original = new AppEntry
        {
            Type = "terminal",
            Tabs =
            [
                new TerminalTab { Title = "My API", Directory = @"C:\Program Files\", Profile = "Windows PowerShell", Color = "#0078D4", Command = "pwsh -NoExit -Command \"cd x; npm start\"" },
                new TerminalTab { Directory = @"D:\", Command = "claude --continue" },
            ],
        };

        var arguments = AppLauncher.BuildTerminalArguments(original);
        var parsed = TerminalCommandLine.ParseTabs(arguments, out _);

        Assert.Equal(original.Tabs.Select(Describe), parsed.Select(Describe));
        Assert.Equal(original.Tabs.Select(t => t.Command), parsed.Select(t => t.Command));
    }

    private static (string? Title, string? Directory, string? Profile, string? Color, string? Command) Describe(TerminalTab tab) =>
        (tab.Title, tab.Directory, tab.Profile, tab.Color, tab.Command);
}
