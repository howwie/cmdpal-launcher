using System;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class TerminalArgumentsTests
{
    [Fact]
    public void NoTabs_OpensNewWindow()
    {
        Assert.Equal("-w new", Build(new AppEntry { Type = "terminal" }));
    }

    [Fact]
    public void Window_IsQuotedWhenItHasSpaces()
    {
        Assert.Equal("-w last", Build(new AppEntry { Type = "terminal", Window = "last" }));
        Assert.Equal("-w \"dev box\"", Build(new AppEntry { Type = "terminal", Window = "dev box" }));
    }

    [Fact]
    public void Title_IsSuppressedFromShellRenaming()
    {
        var args = Build(Terminal(new TerminalTab { Title = "API" }));

        Assert.Equal("-w new new-tab --title API --suppressApplicationTitle", args);
    }

    [Fact]
    public void AllTabOptions_AreInAStableOrder()
    {
        var args = Build(Terminal(new TerminalTab
        {
            Title = "My API",
            Directory = @"D:\api",
            Profile = "PowerShell",
            Color = "#0078D4",
            Command = "pwsh -NoExit",
        }));

        Assert.Equal(
            "-w new new-tab -p PowerShell -d D:\\api --title \"My API\" --suppressApplicationTitle --tabColor #0078D4 pwsh -NoExit",
            args);
    }

    [Fact]
    public void MultipleTabs_AreSeparatedBySemicolons()
    {
        var args = Build(Terminal(new TerminalTab { Title = "One" }, new TerminalTab { Title = "Two" }));

        Assert.Equal(
            "-w new new-tab --title One --suppressApplicationTitle ; new-tab --title Two --suppressApplicationTitle",
            args);
    }

    [Fact]
    public void BlankValues_AreIgnored()
    {
        var args = Build(Terminal(new TerminalTab { Title = " ", Directory = "", Profile = null, Command = "  " }));

        Assert.Equal("-w new new-tab", args);
    }

    [Fact]
    public void QuotedDirectoryWithTrailingBackslash_DoublesTheBackslash()
    {
        // A lone backslash before the closing quote would escape it.
        var args = Build(Terminal(new TerminalTab { Directory = @"C:\Program Files\" }));

        Assert.Equal("-w new new-tab -d \"C:\\Program Files\\\\\"", args);
    }

    [Fact]
    public void UnquotedDriveRoot_IsLeftAlone()
    {
        Assert.Equal(@"-w new new-tab -d D:\", Build(Terminal(new TerminalTab { Directory = @"D:\" })));
    }

    [Fact]
    public void QuotesInsideValues_AreEscaped()
    {
        var args = Build(Terminal(new TerminalTab { Title = "Say \"hi\"" }));

        Assert.Equal("-w new new-tab --title \"Say \\\"hi\\\"\" --suppressApplicationTitle", args);
    }

    [Fact]
    public void BackslashesBeforeAnInnerQuote_AreDoubled()
    {
        var args = Build(Terminal(new TerminalTab { Title = "a\\\"b" }));

        Assert.Equal("-w new new-tab --title \"a\\\\\\\"b\" --suppressApplicationTitle", args);
    }

    [Fact]
    public void SemicolonInTitle_IsEscapedSoTerminalDoesNotSplitIt()
    {
        var args = Build(Terminal(new TerminalTab { Title = "Build; Test" }));

        Assert.Equal("-w new new-tab --title \"Build\\; Test\" --suppressApplicationTitle", args);
    }

    [Fact]
    public void SemicolonInProfileDirectoryOrColor_IsEscaped()
    {
        var args = Build(Terminal(new TerminalTab { Profile = "a;b", Directory = @"D:\c;d", Color = "e;f" }));

        Assert.Equal("-w new new-tab -p \"a\\;b\" -d \"D:\\c\\;d\" --tabColor \"e\\;f\"", args);
    }

    [Fact]
    public void SemicolonInCommand_IsEscapedSoTerminalDoesNotSplitIt()
    {
        var args = Build(Terminal(new TerminalTab { Command = "cd x; npm run dev" }));

        Assert.Equal("-w new new-tab cd x\\; npm run dev", args);
    }

    [Fact]
    public void EnvironmentVariables_AreExpandedInDirectoryAndCommand()
    {
        var variable = $"CMDPAL_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, @"D:\expanded");
        try
        {
            var args = Build(Terminal(new TerminalTab { Directory = $"%{variable}%", Command = $"code %{variable}%" }));

            Assert.Equal(@"-w new new-tab -d D:\expanded code D:\expanded", args);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void ExtraArguments_GoAfterTheTabs()
    {
        var app = Terminal(new TerminalTab { Title = "A" });
        app.Arguments = "--maximized";

        Assert.Equal("-w new new-tab --title A --suppressApplicationTitle --maximized", Build(app));
    }

    private static string Build(AppEntry app) => AppLauncher.BuildTerminalArguments(app);

    private static AppEntry Terminal(params TerminalTab[] tabs) => new() { Type = "terminal", Tabs = [.. tabs] };
}
