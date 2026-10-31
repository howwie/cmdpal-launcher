using System.Linq;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Pages;
using HowWieYoong.CmdPalLauncher.Pages.Editor;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

/// <summary>Valid JSON with a null where the code expects a value, as a hand edit can leave it.</summary>
public class NullJsonTests
{
    private readonly FakeDesktop _desktop = new();

    private static readonly (string Json, string Error)[] InvalidFiles =
    [
        ("""{ "profiles": [ null ] }""", "$.profiles[0] can't be null."),
        ("""{ "profiles": [ { "name": null } ] }""", "$.profiles[0].name can't be null."),
        ("""{ "profiles": [ { "name": "Dev", "apps": null } ] }""", "$.profiles[0].apps can't be null."),
        ("""{ "profiles": [ { "name": "Dev", "apps": [ null ] } ] }""", "$.profiles[0].apps[0] can't be null."),
        ("""{ "profiles": [ { "name": "Dev", "apps": [ { "type": "terminal", "tabs": [ null ] } ] } ] }""", "$.profiles[0].apps[0].tabs[0] can't be null."),
    ];

    /// <summary>These only say there's nothing there, so they're read as empty.</summary>
    private static readonly string[] HarmlessFiles =
    [
        """{ "profiles": null }""",
        """{ "profiles": [ { "name": "Dev", "apps": [ { "type": "terminal", "tabs": null } ] } ] }""",
    ];

    public static TheoryData<string, string> InvalidWithError => new(InvalidFiles.Select(f => (f.Json, f.Error)));

    public static TheoryData<string> Invalid => new(InvalidFiles.Select(f => f.Json));

    public static TheoryData<string> Harmless => new(HarmlessFiles);

    public static TheoryData<string> Files => new(HarmlessFiles.Concat(InvalidFiles.Select(f => f.Json)));

    [Theory]
    [MemberData(nameof(InvalidWithError))]
    public void Load_ReportsWhereTheNullIs(string json, string error)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Equal(error, store.Error);
        Assert.Empty(store.Profiles);
    }

    [Theory]
    [MemberData(nameof(Harmless))]
    public void Load_HarmlessNull_IsNotAnError(string json)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Null(store.Error);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Update_RefusesToSave(string json)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.StartsWith("profiles.json has an error", store.Update(f => ProfileEditing.AddProfile(f, "New", null, null)));
        Assert.Equal(json, System.IO.File.ReadAllText(file.FilePath));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void LaunchPage_DoesNotThrow(string json)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        new ProfilesPage(store, new ProfileEditorPage(store, _desktop)).GetItems();
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EditorPage_DoesNotThrow(string json)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        new ProfileEditorPage(store, _desktop).GetItems();
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void ProfileForm_DoesNotThrow(string json)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);
        var editor = new ProfileEditorPage(store, _desktop);

        foreach (var profile in store.Profiles)
        {
            editor.PageFor(profile.Name).GetContent();
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Launch_DoesNotThrow(string json)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        foreach (var profile in store.Profiles)
        {
            AppLauncher.LaunchAll(profile, new FakeProcessRunner(), _desktop);
        }
    }

    [Theory]
    [MemberData(nameof(Harmless))]
    public void Update_HarmlessNull_Saves(string json)
    {
        using var file = new TempProfilesFile(json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        Assert.Null(store.Update(f => ProfileEditing.AddProfile(f, "New", null, null)));
    }
}
