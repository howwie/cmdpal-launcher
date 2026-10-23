using System;
using System.ComponentModel;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class AppLauncherTests
{
    private readonly FakeProcessRunner _runner = new();

    [Fact]
    public void CreateStartInfo_UsesShellExecuteWithPathAndArguments()
    {
        var startInfo = AppLauncher.CreateStartInfo(new AppEntry { Path = "explorer.exe", Arguments = @"D:\Development" }, _runner)!;

        Assert.Equal("explorer.exe", startInfo.FileName);
        Assert.Equal(@"D:\Development", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(string.Empty, startInfo.Verb);
        Assert.Equal(string.Empty, startInfo.WorkingDirectory);
    }

    [Fact]
    public void CreateStartInfo_ExpandsEnvironmentVariables()
    {
        var variable = $"CMDPAL_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, @"C:\Tools");
        try
        {
            var startInfo = AppLauncher.CreateStartInfo(
                new AppEntry { Path = $"%{variable}%\\app.exe", Arguments = $"--root %{variable}%", WorkingDirectory = $"%{variable}%\\work" },
                _runner)!;

            Assert.Equal(@"C:\Tools\app.exe", startInfo.FileName);
            Assert.Equal(@"--root C:\Tools", startInfo.Arguments);
            Assert.Equal(@"C:\Tools\work", startInfo.WorkingDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void CreateStartInfo_RunAsAdmin_UsesRunasVerb()
    {
        var startInfo = AppLauncher.CreateStartInfo(new AppEntry { Path = "cmd.exe", RunAsAdmin = true }, _runner)!;

        Assert.Equal("runas", startInfo.Verb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateStartInfo_AppWithoutPath_Throws(string? path)
    {
        var ex = Assert.Throws<ArgumentException>(() => AppLauncher.CreateStartInfo(new AppEntry { Path = path }, _runner));

        Assert.Equal("no \"path\" set", ex.Message);
    }

    [Fact]
    public void CreateStartInfo_Terminal_DefaultsToWindowsTerminal()
    {
        var startInfo = AppLauncher.CreateStartInfo(new AppEntry { Type = "terminal" }, _runner)!;

        Assert.Equal("wt.exe", startInfo.FileName);
        Assert.Equal("-w new", startInfo.Arguments);
    }

    [Fact]
    public void CreateStartInfo_Terminal_CanUseACustomPath()
    {
        var startInfo = AppLauncher.CreateStartInfo(new AppEntry { Type = "Terminal", Path = "wtd.exe" }, _runner)!;

        Assert.Equal("wtd.exe", startInfo.FileName);
    }

    [Fact]
    public void CreateStartInfo_SkipIfRunning_ChecksProcessNameWithoutFolderOrExtension()
    {
        _runner.RunningProcesses.Add("Docker Desktop");
        var app = new AppEntry { Path = @"C:\Program Files\Docker\Docker\Docker Desktop.exe", SkipIfRunning = true };

        Assert.Null(AppLauncher.CreateStartInfo(app, _runner));
        Assert.Equal(["isRunning Docker Desktop"], _runner.Calls);
    }

    [Fact]
    public void CreateStartInfo_SkipIfRunning_StartsWhenNotRunning()
    {
        var app = new AppEntry { Path = "notepad.exe", SkipIfRunning = true };

        Assert.NotNull(AppLauncher.CreateStartInfo(app, _runner));
    }

    [Fact]
    public void CreateStartInfo_WithoutSkipIfRunning_DoesNotCheckProcesses()
    {
        _runner.RunningProcesses.Add("notepad");

        Assert.NotNull(AppLauncher.CreateStartInfo(new AppEntry { Path = "notepad.exe" }, _runner));
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void LaunchAll_StartsAppsInOrderWithDelaysBeforeEach()
    {
        var profile = Profile(
            new AppEntry { Path = "first.exe" },
            new AppEntry { Path = "second.exe", DelayMs = 500 },
            new AppEntry { Path = "third.exe", DelayMs = 0 });

        var failures = AppLauncher.LaunchAll(profile, _runner);

        Assert.Empty(failures);
        Assert.Equal(["start first.exe", "delay 500", "start second.exe", "start third.exe"], _runner.Calls);
    }

    [Fact]
    public void LaunchAll_SkippedAppIsNotAFailure()
    {
        _runner.RunningProcesses.Add("docker");

        var failures = AppLauncher.LaunchAll(Profile(new AppEntry { Path = "docker.exe", SkipIfRunning = true }), _runner);

        Assert.Empty(failures);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void LaunchAll_KeepsGoingAfterAFailureAndReportsIt()
    {
        _runner.FailWith = info => info.FileName == "missing.exe" ? new Win32Exception(2, "The system cannot find the file specified.") : null;
        var profile = Profile(
            new AppEntry { Path = "missing.exe", Name = "Missing" },
            new AppEntry { Path = "ok.exe" });

        var failures = AppLauncher.LaunchAll(profile, _runner);

        Assert.Equal(["Missing: The system cannot find the file specified."], failures);
        Assert.Equal("ok.exe", Assert.Single(_runner.Started).FileName);
    }

    [Fact]
    public void LaunchAll_CancelledUacPromptIsReportedAsCancelled()
    {
        _runner.FailWith = _ => new Win32Exception(1223);

        var failures = AppLauncher.LaunchAll(Profile(new AppEntry { Path = "admin.exe", RunAsAdmin = true }), _runner);

        Assert.Equal(["admin: cancelled"], failures);
    }

    [Fact]
    public void LaunchAll_AppWithoutPathIsReported()
    {
        var failures = AppLauncher.LaunchAll(Profile(new AppEntry { Name = "Broken" }, new AppEntry { Path = "ok.exe" }), _runner);

        Assert.Equal(["Broken: no \"path\" set"], failures);
        Assert.Single(_runner.Started);
    }

    [Fact]
    public void DisplayName_PrefersNameThenTerminalThenFileName()
    {
        Assert.Equal("Docker", AppLauncher.DisplayName(new AppEntry { Name = "Docker", Path = "x.exe" }));
        Assert.Equal("Windows Terminal", AppLauncher.DisplayName(new AppEntry { Type = "terminal" }));
        Assert.Equal("wtd", AppLauncher.DisplayName(new AppEntry { Type = "terminal", Path = "wtd.exe" }));
        Assert.Equal("Docker Desktop", AppLauncher.DisplayName(new AppEntry { Path = @"C:\Docker\Docker Desktop.exe" }));
        Assert.Equal("(unnamed app)", AppLauncher.DisplayName(new AppEntry()));
    }

    private static LaunchProfile Profile(params AppEntry[] apps) => new() { Name = "Test", Apps = [.. apps] };
}
