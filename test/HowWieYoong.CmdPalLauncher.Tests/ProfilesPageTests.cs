using System.Linq;
using HowWieYoong.CmdPalLauncher.Pages;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class ProfilesPageTests
{
    [Fact]
    public void ListsEachProfileThenTheEditItem()
    {
        using var file = new TempProfilesFile("""
            { "profiles": [
              { "name": "Dev", "description": "Daily setup", "apps": [ { "path": "a.exe" } ] },
              { "name": "Writing", "apps": [ { "name": "Notes", "path": "notes.exe" }, { "type": "terminal" } ] }
            ] }
            """);
        using var store = new ProfileStore(file.FilePath, watch: false);

        var items = new ProfilesPage(store).GetItems();

        Assert.Equal(["Dev", "Writing", "Edit launch profiles"], items.Select(i => i.Title));
        Assert.Equal("Daily setup", items[0].Subtitle);
        Assert.Equal("Notes, Windows Terminal", items[1].Subtitle);
        Assert.Equal(file.FilePath, items[2].Subtitle);
    }

    [Fact]
    public void Details_ListAppsAndTerminalTabs()
    {
        using var file = new TempProfilesFile("""
            { "profiles": [ { "name": "Dev", "apps": [
              { "name": "Explorer", "path": "explorer.exe", "arguments": "D:\\Development" },
              { "type": "terminal", "tabs": [ { "title": "API", "directory": "D:\\api" }, { "profile": "Ubuntu" } ] }
            ] } ] }
            """);
        using var store = new ProfileStore(file.FilePath, watch: false);

        var body = new ProfilesPage(store).GetItems()[0].Details!.Body;

        Assert.Equal(
            "- **Explorer** `D:\\Development`\n- **Windows Terminal**\n  - API — `D:\\api`\n  - Ubuntu\n",
            body);
    }

    [Fact]
    public void BrokenFile_ShowsOnlyTheError()
    {
        using var file = new TempProfilesFile("{ broken");
        using var store = new ProfileStore(file.FilePath, watch: false);

        var item = Assert.Single(new ProfilesPage(store).GetItems());

        Assert.Equal("profiles.json has an error", item.Title);
        Assert.Equal(store.Error, item.Subtitle);
    }

    [Fact]
    public void NoProfiles_ShowsJustTheEditItem()
    {
        using var file = new TempProfilesFile("""{ "profiles": [] }""");
        using var store = new ProfileStore(file.FilePath, watch: false);

        var item = Assert.Single(new ProfilesPage(store).GetItems());

        Assert.Equal("Edit launch profiles", item.Title);
    }
}
