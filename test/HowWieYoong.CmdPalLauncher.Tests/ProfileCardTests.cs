using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HowWieYoong.CmdPalLauncher.Editing;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class ProfileCardTests
{
    private const string Json = """
        { "profiles": [ { "name": "Dev", "icon": "\uE943", "apps": [
          { "name": "Code", "path": "code", "arguments": ".", "monitor": 2, "bounds": { "x": 0, "y": 0, "width": 1280 } },
          { "type": "terminal", "window": "last", "tabs": [ { "title": "API", "directory": "D:\\api" }, { "profile": "Ubuntu", "command": "htop" } ] },
          { "path": "explorer.exe", "arguments": "D:\\", "futureSetting": 1 }
        ] } ] }
        """;

    private readonly FakeDesktop _desktop = new();

    [Fact]
    public void Template_PrefixesEverySection()
    {
        var inputs = CardJson.Inputs(Template(Profile()));

        Assert.Equal("Dev", CardJson.Value(inputs, "p_name"));
        Assert.Equal(@"\uE943", CardJson.Value(inputs, "p_icon"));
        Assert.Equal("code", CardJson.Value(inputs, "a0_path"));
        Assert.Equal("last", CardJson.Value(inputs, "a1_window"));
        Assert.Equal("Ubuntu", CardJson.Value(inputs, "a1_t1_profile"));
        Assert.Equal(@"D:\", CardJson.Value(inputs, "a2_arguments"));
    }

    [Fact]
    public void Template_HasNoClientSideRequiredFields()
    {
        // Otherwise one app with an empty path would block every button, including its own Delete.
        Assert.All(CardJson.Inputs(Template(Profile())).Values, input => Assert.Null(input["isRequired"]));
    }

    [Fact]
    public void Template_ShowsOnlyExpandedApps()
    {
        var card = Template(Profile(), expanded: [1]);

        var bodies = CardJson.Elements(card).Where(e => e["id"]?.GetValue<string>() is { } id && id.EndsWith("_body", System.StringComparison.Ordinal)).ToList();
        Assert.Equal(["a0_body", "a1_body", "a2_body"], bodies.Select(b => b["id"]!.GetValue<string>()));
        Assert.Equal([false, true, false], bodies.Select(b => b["isVisible"]?.GetValue<bool>() ?? true));

        var toggles = CardJson.Elements(card).Where(e => CardJson.Type(e) == "Action.ToggleVisibility").ToList();
        Assert.Equal(["a0_body", "a1_body", "a2_body"], toggles.Select(t => t["targetElements"]![0]!.GetValue<string>()));
    }

    [Fact]
    public void Template_AppHeaderTogglesSettingsAndArrows()
    {
        var card = Template(Profile(), expanded: [1]);
        var elements = CardJson.Elements(card);

        var headers = elements.Where(e => CardJson.Type(e) == "ColumnSet" && e["selectAction"] is not null).ToList();
        Assert.Equal(3, headers.Count);
        Assert.Equal(
            ["a1_body", "a1_closed", "a1_open"],
            headers[1]["selectAction"]!["targetElements"]!.AsArray().Select(t => t!.GetValue<string>()));

        bool Visible(string id) => elements.Single(e => e["id"]?.GetValue<string>() == id)["isVisible"]?.GetValue<bool>() ?? true;
        Assert.Equal((true, false), (Visible("a0_closed"), Visible("a0_open")));
        Assert.Equal((false, true), (Visible("a1_closed"), Visible("a1_open")));

        Assert.DoesNotContain(elements, e => e["title"]?.GetValue<string>() == "Edit");
    }

    [Fact]
    public void Template_AppButtonsAreInsideTheSection()
    {
        var body = CardJson.Elements(Template(Profile())).Single(e => e["id"]?.GetValue<string>() == "a2_body");

        var buttons = CardJson.Elements(body.ToJsonString()).Where(e => CardJson.Type(e) == "Action.Submit").Select(e => e["title"]!.GetValue<string>());
        Assert.Equal(["Move up", "Move down", "Duplicate", "Delete"], buttons);
    }

    [Fact]
    public void Template_IconPreviewSitsBesideTheIconField()
    {
        var card = ProfileCard.Template(Profile(), _desktop.Monitors, new HashSet<int>(), icon => icon == "\uE943" ? "data:image/png;base64,AAAA" : null);

        var image = Assert.Single(CardJson.Elements(card), e => CardJson.Type(e) == "Image");
        Assert.Equal("data:image/png;base64,AAAA", image["url"]!.GetValue<string>());
        Assert.Equal(@"\uE943", CardJson.Value(CardJson.Inputs(card), "p_icon"));

        // Saving the form unchanged still works with the field inside a row.
        var file = File();
        Assert.Null(Apply(file, CardJson.Submit(card)));
        Assert.Equal("\uE943", file.Profiles[0].Icon);
    }

    [Fact]
    public void Template_NoPreview_NoImage()
    {
        var card = ProfileCard.Template(Profile(), _desktop.Monitors, new HashSet<int>(), _ => null);

        Assert.DoesNotContain(CardJson.Elements(card), e => CardJson.Type(e) == "Image");
    }

    [Fact]
    public void Template_ButtonsSayWhatToDo()
    {
        var buttons = CardJson.Elements(Template(Profile()))
            .Where(e => CardJson.Type(e) == "Action.Submit")
            .Select(e => ProfileCardAction.Parse(e["data"]!.ToJsonString()))
            .ToList();

        Assert.Contains(new ProfileCardAction(ProfileCardAction.MoveApp, 2, Offset: 1), buttons);
        Assert.Contains(new ProfileCardAction(ProfileCardAction.DeleteApp, 0), buttons);
        Assert.Contains(new ProfileCardAction(ProfileCardAction.DeleteTab, 1, 1), buttons);
        Assert.Contains(new ProfileCardAction(ProfileCardAction.AddTab, 1), buttons);
        Assert.Contains(new ProfileCardAction(ProfileCardAction.Save), buttons);
        Assert.DoesNotContain(buttons, b => b.Name == ProfileCardAction.AddTab && b.App != 1);
    }

    [Fact]
    public void Template_EmptyProfile_SaysHowToAddApps()
    {
        var card = Template(new LaunchProfile { Name = "Empty" });

        Assert.Contains("No apps yet", card);
        Assert.Equal(["p_name", "p_description", "p_icon"], CardJson.Inputs(card).Keys);
    }

    /// <summary>Saving the form without changes must leave profiles.json exactly as it was.</summary>
    [Fact]
    public void SavingUnchanged_KeepsTheProfileAsItWas()
    {
        var file = File();
        var before = ProfileWriter.Serialize(file);
        var profile = file.Profiles[0];

        var error = Apply(file, CardJson.Submit(Template(profile)), new ProfileCardAction(ProfileCardAction.Save), ProfileCardLayout.Of(profile));

        Assert.Null(error);
        Assert.Equal(before, ProfileWriter.Serialize(file));
    }

    [Fact]
    public void ApplyInputs_ReadsEverySection()
    {
        var file = File();
        var card = Template(file.Profiles[0]);

        var error = Apply(file, CardJson.Submit(card, new() { ["p_name"] = "Development", ["a0_arguments"] = "--reuse", ["a1_t0_title"] = "Back end", ["a2_monitor"] = "primary" }));

        Assert.Null(error);
        var profile = file.Profiles[0];
        Assert.Equal("Development", profile.Name);
        Assert.Equal("--reuse", profile.Apps[0].Arguments);
        Assert.Equal("Back end", profile.Apps[1].Tabs![0].Title);
        Assert.True(profile.Apps[2].Monitor!.IsPrimary);
        Assert.True(profile.Apps[2].ExtensionData!.ContainsKey("futureSetting"));
    }

    [Fact]
    public void ApplyInputs_NamesTheAppOrTabThatIsWrong()
    {
        var file = File();
        var card = Template(file.Profiles[0]);

        Assert.Equal("App 3 (explorer): Path is required.", Apply(file, CardJson.Submit(card, new() { ["a2_path"] = "" })));
        Assert.Equal(
            "App 2 (Windows Terminal), tab 2: Fill in at least a title, directory, profile, command or colour.",
            Apply(file, CardJson.Submit(card, new() { ["a1_t1_profile"] = "", ["a1_t1_command"] = "" })));
        Assert.Equal("Give the profile a name.", Apply(file, CardJson.Submit(card, new() { ["p_name"] = " " })));
    }

    [Fact]
    public void ApplyInputs_SkipsTheAppOrTabBeingDeleted()
    {
        var file = File();
        var card = Template(file.Profiles[0]);

        Assert.Null(Apply(file, CardJson.Submit(card, new() { ["a2_path"] = "" }), new ProfileCardAction(ProfileCardAction.DeleteApp, 2)));
        Assert.Null(Apply(file, CardJson.Submit(card, new() { ["a1_t1_profile"] = "", ["a1_t1_command"] = "" }), new ProfileCardAction(ProfileCardAction.DeleteTab, 1, 1)));
    }

    [Fact]
    public void ApplyInputs_RefusesWhenTheFileChangedUnderneath()
    {
        var file = File();
        var layout = ProfileCardLayout.Of(file.Profiles[0]);
        var card = Template(file.Profiles[0]);
        file.Profiles[0].Apps.RemoveAt(0);

        Assert.StartsWith("profiles.json changed since this page was opened", Apply(file, CardJson.Submit(card), layout: layout));
    }

    [Fact]
    public void Perform_AddsAppsAndOpensThem()
    {
        var profile = File().Profiles[0];

        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.AddApp), out var expand));
        Assert.Equal(3, expand);
        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.AddTerminal), out expand));
        Assert.Equal(4, expand);
        Assert.True(profile.Apps[4].IsTerminal);
        Assert.Empty(profile.Apps[4].Tabs!);
    }

    [Fact]
    public void Perform_MovesDuplicatesAndDeletesApps()
    {
        var profile = File().Profiles[0];

        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.MoveApp, 0, Offset: -1), out var expand));
        Assert.Equal(0, expand);
        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.MoveApp, 0, Offset: 1), out expand));
        Assert.Equal(1, expand);
        Assert.Equal("Code", profile.Apps[1].Name);

        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.DuplicateApp, 1), out expand));
        Assert.Equal((2, "Code"), (expand!.Value, profile.Apps[2].Name));

        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.DeleteApp, 0), out expand));
        Assert.Null(expand);
        Assert.Equal(["Code", "Code", null], profile.Apps.Select(a => a.Name));
    }

    [Fact]
    public void Perform_EditsTabsAndKeepsTheirAppOpen()
    {
        var profile = File().Profiles[0];

        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.AddTab, 1), out var expand));
        Assert.Equal(1, expand);
        Assert.Equal(3, profile.Apps[1].Tabs!.Count);

        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.MoveTab, 1, 0, 1), out _));
        Assert.Equal("Ubuntu", profile.Apps[1].Tabs![0].Profile);

        Assert.Null(ProfileCard.Perform(profile, new ProfileCardAction(ProfileCardAction.DeleteTab, 1, 0), out _));
        Assert.Equal(["API", null], profile.Apps[1].Tabs!.Select(t => t.Title));
    }

    [Theory]
    [InlineData(ProfileCardAction.MoveApp, 7, -1, "That app no longer exists.")]
    [InlineData(ProfileCardAction.AddTab, 0, -1, "Only Windows Terminal entries have tabs.")]
    [InlineData(ProfileCardAction.DeleteTab, 1, 9, "That tab no longer exists.")]
    [InlineData("explode", 1, -1, "Unknown button \"explode\".")]
    public void Perform_ReportsWhatCantBeDone(string action, int app, int tab, string expected)
    {
        Assert.Equal(expected, ProfileCard.Perform(File().Profiles[0], new ProfileCardAction(action, app, tab), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "app": 1 }""")]
    public void ActionParse_AnythingUnrecognisedIsSave(string? data)
    {
        Assert.Equal(new ProfileCardAction(ProfileCardAction.Save), ProfileCardAction.Parse(data));
    }

    [Fact]
    public void ActionParse_RoundTripsToData()
    {
        var action = new ProfileCardAction(ProfileCardAction.MoveTab, 2, 3, -1);

        Assert.Equal(action, ProfileCardAction.Parse(action.ToData().ToJsonString()));
    }

    [Fact]
    public async System.Threading.Tasks.Task Store_DoesNotReportItsOwnSaveTwice()
    {
        using var temp = new TempProfilesFile(Json);
        using var store = new ProfileStore(temp.FilePath, watch: true);
        var changes = 0;
        store.Changed += (_, _) => changes++;

        Assert.Null(store.Update(file => ProfileEditing.UpdateProfile(file, "Dev", "Dev", "desc", null)));

        // Give the file watcher time to see the write (it waits 250 ms after the last change).
        await System.Threading.Tasks.Task.Delay(1000, TestContext.Current.CancellationToken);
        Assert.Equal(1, changes);
    }

    private static ProfilesFile File() => JsonSerializer.Deserialize(Json, ProfilesJsonContext.Default.ProfilesFile)!;

    private static LaunchProfile Profile() => File().Profiles[0];

    private string Template(LaunchProfile profile, HashSet<int>? expanded = null) =>
        ProfileCard.Template(profile, _desktop.Monitors, expanded ?? []);

    private static string? Apply(ProfilesFile file, string inputs, ProfileCardAction? action = null, ProfileCardLayout? layout = null) =>
        ProfileCard.ApplyInputs(file, file.Profiles[0].Name, inputs, layout ?? ProfileCardLayout.Of(file.Profiles[0]), action ?? new ProfileCardAction(ProfileCardAction.Save), out _);
}
