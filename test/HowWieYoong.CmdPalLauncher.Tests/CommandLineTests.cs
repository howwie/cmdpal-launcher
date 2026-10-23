using System.Linq;
using HowWieYoong.CmdPalLauncher.Launching;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --flag x", "C:\\Program Files\\App\\app.exe", "--flag x")]
    [InlineData("\"C:\\App\\app.exe\"", "C:\\App\\app.exe", "")]
    [InlineData("\"C:\\App\\app.exe\"   ", "C:\\App\\app.exe", "")]
    [InlineData("C:\\WINDOWS\\explorer.exe /factory,{75dff2b7} -Embedding", "C:\\WINDOWS\\explorer.exe", "/factory,{75dff2b7} -Embedding")]
    [InlineData("notepad.exe", "notepad.exe", "")]
    [InlineData("  app.exe \"a b\"", "app.exe", "\"a b\"")]
    public void SplitProgram_KeepsTheArgumentsAsWritten(string commandLine, string program, string arguments)
    {
        Assert.Equal((program, arguments), CommandLine.SplitProgram(commandLine));
    }

    [Theory]
    [InlineData("a b  c", new[] { "a", "b", "c" })]
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData("a\"b c\"d", new[] { "ab cd" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData(@"C:\dir\ x", new[] { @"C:\dir\", "x" })]
    [InlineData(@"""C:\dir\\"" x", new[] { @"C:\dir\", "x" })]
    [InlineData(@"a\""b", new[] { "a\"b" })]
    [InlineData(@"a\\\""b", new[] { "a\\\"b" })]
    [InlineData("\"say \"\"hi\"\"\"", new[] { "say \"hi\"" })]
    [InlineData("", new string[0])]
    public void Split_FollowsCommandLineToArgvRules(string arguments, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Split(arguments));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with space")]
    [InlineData(@"C:\Program Files\")]
    [InlineData("quote \" inside")]
    [InlineData(@"back\\""slash")]
    [InlineData("")]
    public void Quote_RoundTripsThroughSplit(string value)
    {
        Assert.Equal([value], CommandLine.Split(CommandLine.Quote(value)));
    }

    [Fact]
    public void RemoveStartupFlags_LeavesOtherArgumentsUntouched()
    {
        var result = CommandLine.RemoveStartupFlags("  \"D:\\My Projects\"  --new-window ", out var removed);

        Assert.Equal("\"D:\\My Projects\"  --new-window", result);
        Assert.Empty(removed);
    }

    [Fact]
    public void RemoveStartupFlags_DropsBackgroundStartFlags()
    {
        var result = CommandLine.RemoveStartupFlags("--autostart \"D:\\My Projects\" /MINIMIZED --hidden=true --keep", out var removed);

        Assert.Equal("\"D:\\My Projects\" --keep", result);
        Assert.Equal(["--autostart", "/MINIMIZED", "--hidden=true"], removed);
    }

    [Fact]
    public void RemoveStartupFlags_KeepsLookalikes()
    {
        CommandLine.RemoveStartupFlags("--backgrounds --startup-dir x --minimize", out var removed);

        Assert.Empty(removed);
    }

    [Fact]
    public void Split_HandlesTheQuotedOutputOfRemoveStartupFlags()
    {
        var result = CommandLine.RemoveStartupFlags("--tray \"a \\\"b\\\" c\" d", out _);

        Assert.Equal(["a \"b\" c", "d"], CommandLine.Split(result).ToArray());
    }
}
