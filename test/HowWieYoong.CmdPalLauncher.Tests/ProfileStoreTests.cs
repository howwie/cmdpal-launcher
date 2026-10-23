using System;
using System.IO;
using System.Threading.Tasks;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class ProfileStoreTests
{
    [Fact]
    public void MissingFile_IsCreatedWithTheSampleProfiles()
    {
        using var file = new TempProfilesFile();

        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.True(File.Exists(file.FilePath));
        Assert.Null(store.Error);
        var profile = Assert.Single(store.Profiles);
        Assert.Equal("Development", profile.Name);
        Assert.Equal(3, profile.Apps.Count);
        Assert.True(profile.Apps[1].IsTerminal);
    }

    [Fact]
    public void ExistingFile_IsNotOverwritten()
    {
        using var file = new TempProfilesFile("""{ "profiles": [] }""");

        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Empty(store.Profiles);
        Assert.Equal("""{ "profiles": [] }""", File.ReadAllText(file.FilePath));
    }

    [Fact]
    public void CommentsTrailingCommasAndAnyKeyCase_AreAccepted()
    {
        using var file = new TempProfilesFile("""
            // comment
            {
              "Profiles": [
                {
                  "NAME": "Dev", /* inline */
                  "apps": [ { "path": "a.exe", "SkipIfRunning": true, "delayms": 250, }, ],
                },
              ],
            }
            """);

        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Null(store.Error);
        var app = Assert.Single(Assert.Single(store.Profiles).Apps);
        Assert.Equal("a.exe", app.Path);
        Assert.True(app.SkipIfRunning);
        Assert.Equal(250, app.DelayMs);
    }

    [Fact]
    public void TerminalTabs_AreRead()
    {
        using var file = new TempProfilesFile("""
            { "profiles": [ { "name": "T", "apps": [ { "type": "terminal", "window": "last",
              "tabs": [ { "title": "API", "directory": "D:\\api", "profile": "PowerShell", "command": "npm start", "color": "#fff" } ] } ] } ] }
            """);

        using var store = new ProfileStore(file.FilePath, watch: false);

        var app = Assert.Single(Assert.Single(store.Profiles).Apps);
        Assert.Equal("last", app.Window);
        var tab = Assert.Single(app.Tabs!);
        Assert.Equal(("API", @"D:\api", "PowerShell", "npm start", "#fff"), (tab.Title, tab.Directory, tab.Profile, tab.Command, tab.Color));
    }

    [Fact]
    public void InvalidJson_ReportsAOneBasedLineNumber()
    {
        using var file = new TempProfilesFile("{\n  \"profiles\": [\n    { \"name\": \"x\" \n  ]\n}");

        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Empty(store.Profiles);
        Assert.NotNull(store.Error);
        Assert.StartsWith("Line 4: ", store.Error);
        Assert.DoesNotContain("LineNumber", store.Error);
    }

    [Fact]
    public void WrongValueType_ReportsTheJsonPath()
    {
        using var file = new TempProfilesFile("""{ "profiles": [ { "name": "x", "apps": [ { "path": 5 } ] } ] }""");

        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.StartsWith("Line 1: ", store.Error);
        Assert.Contains("$.profiles[0].apps[0].path", store.Error);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{ "profiles": [] }""")]
    public void NoProfiles_IsEmptyWithoutError(string json)
    {
        using var file = new TempProfilesFile(json);

        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Null(store.Error);
        Assert.Empty(store.Profiles);
    }

    [Fact]
    public async Task SavingTheFile_ReloadsAndRaisesChanged()
    {
        using var file = new TempProfilesFile("""{ "profiles": [ { "name": "Before" } ] }""");
        using var store = new ProfileStore(file.FilePath, watch: true);
        var changed = new TaskCompletionSource();
        store.Changed += (_, _) => changed.TrySetResult();

        file.Write("""{ "profiles": [ { "name": "After" } ] }""");

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("After", Assert.Single(store.Profiles).Name);
    }

    [Fact]
    public async Task FixingABrokenFile_ClearsTheError()
    {
        using var file = new TempProfilesFile("{ broken");
        using var store = new ProfileStore(file.FilePath, watch: true);
        Assert.NotNull(store.Error);
        var changed = new TaskCompletionSource();
        store.Changed += (_, _) =>
        {
            if (store.Error is null)
            {
                changed.TrySetResult();
            }
        };

        file.Write("""{ "profiles": [ { "name": "Fixed" } ] }""");

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Fixed", Assert.Single(store.Profiles).Name);
    }
}
