using System.IO;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class ProfileEditingTests
{
    private readonly ProfilesFile _file = new()
    {
        Profiles =
        [
            new LaunchProfile { Name = "Dev", Apps = [new AppEntry { Path = "a.exe" }, new AppEntry { Path = "b.exe" }, new AppEntry { Type = "terminal" }] },
            new LaunchProfile { Name = "Writing" },
        ],
    };

    [Fact]
    public void AddProfile_TrimsAndAppends()
    {
        Assert.Null(ProfileEditing.AddProfile(_file, "  Gaming ", " ", "🎮"));

        var added = _file.Profiles[^1];
        Assert.Equal(("Gaming", (string?)null, "🎮"), (added.Name, added.Description, added.Icon));
    }

    [Theory]
    [InlineData("", "Give the profile a name.")]
    [InlineData("  ", "Give the profile a name.")]
    [InlineData("dev", "There's already a profile called \"dev\".")]
    public void AddProfile_RejectsEmptyAndDuplicateNames(string name, string error)
    {
        Assert.Equal(error, ProfileEditing.AddProfile(_file, name, null, null));
        Assert.Equal(2, _file.Profiles.Count);
    }

    [Fact]
    public void UpdateProfile_CanChangeTheCaseOfItsOwnName()
    {
        Assert.Null(ProfileEditing.UpdateProfile(_file, "Dev", "DEV", "desc", null));
        Assert.Equal("DEV", _file.Profiles[0].Name);
        Assert.Equal("There's already a profile called \"Writing\".", ProfileEditing.UpdateProfile(_file, "DEV", "Writing", null, null));
    }

    [Fact]
    public void MissingProfile_IsReported()
    {
        Assert.StartsWith("There's no profile called \"Gone\"", ProfileEditing.DeleteProfile(_file, "Gone"));
    }

    [Fact]
    public void MoveProfile_ClampsAtTheEnds()
    {
        Assert.Null(ProfileEditing.MoveProfile(_file, "Dev", -1));
        Assert.Equal(["Dev", "Writing"], _file.Profiles.Select(p => p.Name));

        Assert.Null(ProfileEditing.MoveProfile(_file, "Dev", 1));
        Assert.Equal(["Writing", "Dev"], _file.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void DuplicateProfile_PicksAFreeNameAndCopiesApps()
    {
        Assert.Null(ProfileEditing.DuplicateProfile(_file, "Dev", out var first));
        Assert.Null(ProfileEditing.DuplicateProfile(_file, "Dev", out var second));

        Assert.Equal(("Dev (copy)", "Dev (copy 2)"), (first, second));
        Assert.Equal(["Dev", "Dev (copy 2)", "Dev (copy)", "Writing"], _file.Profiles.Select(p => p.Name));
        Assert.Equal(3, _file.Profiles[1].Apps.Count);
        Assert.NotSame(_file.Profiles[0].Apps[0], _file.Profiles[1].Apps[0]);
    }

    [Fact]
    public void Apps_CanBeMovedDuplicatedAndDeleted()
    {
        Assert.Null(ProfileEditing.MoveApp(_file, "Dev", 0, 1));
        Assert.Equal(["b.exe", "a.exe", null], Paths());

        Assert.Null(ProfileEditing.DuplicateApp(_file, "Dev", 1));
        Assert.Equal(["b.exe", "a.exe", "a.exe", null], Paths());

        Assert.Null(ProfileEditing.DeleteApp(_file, "Dev", 0));
        Assert.Equal(["a.exe", "a.exe", null], Paths());
    }

    [Fact]
    public void UpdateApp_PassesOnTheEditsError()
    {
        Assert.Equal("bad", ProfileEditing.UpdateApp(_file, "Dev", 0, _ => "bad"));
        Assert.Null(ProfileEditing.UpdateApp(_file, "Dev", 0, app =>
        {
            app.Arguments = "-x";
            return null;
        }));
        Assert.Equal("-x", _file.Profiles[0].Apps[0].Arguments);
    }

    [Fact]
    public void AppOutOfRange_IsReported()
    {
        Assert.StartsWith("That app is no longer in \"Dev\"", ProfileEditing.DeleteApp(_file, "Dev", 3));
        Assert.StartsWith("That app is no longer in \"Dev\"", ProfileEditing.MoveApp(_file, "Dev", -1, 1));
    }

    [Fact]
    public void UpdateTabs_OnlyForTerminalEntries_AndCreatesTheList()
    {
        Assert.Equal("Only Windows Terminal entries have tabs.", ProfileEditing.UpdateTabs(_file, "Dev", 0, _ => null));

        Assert.Null(ProfileEditing.UpdateTabs(_file, "Dev", 2, tabs =>
        {
            tabs.Add(new TerminalTab { Title = "API" });
            return null;
        }));
        Assert.Equal("API", _file.Profiles[0].Apps[2].Tabs![0].Title);
    }

    [Fact]
    public void StoreUpdate_EditsWhatIsOnDiskNow_AndRefreshes()
    {
        using var temp = new TempProfilesFile("""{ "profiles": [ { "name": "Dev" } ] }""");
        using var store = new ProfileStore(temp.FilePath, watch: false);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        // Edited by hand after the store loaded it.
        temp.Write("""{ "profiles": [ { "name": "Dev" }, { "name": "Hand-made" } ] }""");

        Assert.Null(store.Update(file => ProfileEditing.AddProfile(file, "Editor", null, null)));

        Assert.Equal(["Dev", "Hand-made", "Editor"], store.Profiles.Select(p => p.Name));
        Assert.Equal(1, changed);
        Assert.True(File.Exists(ProfileWriter.BackupPath(temp.FilePath)));
    }

    [Fact]
    public void StoreUpdate_FailedEdit_LeavesTheFileAlone()
    {
        const string Json = """{ "profiles": [ { "name": "Dev" } ] }""";
        using var temp = new TempProfilesFile(Json);
        using var store = new ProfileStore(temp.FilePath, watch: false);

        Assert.Equal("Give the profile a name.", store.Update(file => ProfileEditing.AddProfile(file, "", null, null)));

        Assert.Equal(Json, File.ReadAllText(temp.FilePath));
        Assert.False(File.Exists(ProfileWriter.BackupPath(temp.FilePath)));
    }

    [Fact]
    public void StoreUpdate_BrokenFile_IsNotOverwritten()
    {
        using var temp = new TempProfilesFile("{ broken");
        using var store = new ProfileStore(temp.FilePath, watch: false);

        Assert.StartsWith("profiles.json has an error", store.Update(file => ProfileEditing.AddProfile(file, "X", null, null)));
        Assert.Equal("{ broken", File.ReadAllText(temp.FilePath));
    }

    private string?[] Paths() => [.. _file.Profiles[0].Apps.Select(a => a.Path)];
}
