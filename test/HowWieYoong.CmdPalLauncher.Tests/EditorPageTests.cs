using System.Linq;
using HowWieYoong.CmdPalLauncher.Pages;
using HowWieYoong.CmdPalLauncher.Pages.Editor;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class EditorPageTests
{
    private const string Json = """
        { "profiles": [
          { "name": "Dev", "apps": [ { "name": "Code", "path": "code", "arguments": "." }, { "type": "terminal", "tabs": [ { "title": "API" }, { "profile": "Ubuntu", "directory": "D:\\x" } ] } ] },
          { "name": "Writing", "description": "Notes" }
        ] }
        """;

    private readonly FakeDesktop _desktop = new();

    [Fact]
    public void EditorPage_ListsProfilesThenNewAndOpenFile()
    {
        using var file = new TempProfilesFile(Json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        var items = new ProfileEditorPage(store, _desktop).GetItems();

        Assert.Equal(["Dev", "Writing", "New profile", "Open profiles.json"], items.Select(i => i.Title));
        Assert.Equal("Notes", items[1].Subtitle);
        Assert.Equal(["Move up", "Move down", "Duplicate", "Delete"], items[0].MoreCommands.Select(c => ((Microsoft.CommandPalette.Extensions.ICommandContextItem)c).Command.Name));
    }

    [Fact]
    public void EditorPage_BrokenFile_OnlyOffersToOpenIt()
    {
        using var file = new TempProfilesFile("{ broken");
        using var store = new ProfileStore(file.FilePath, watch: false);

        var item = Assert.Single(new ProfileEditorPage(store, _desktop).GetItems());

        Assert.StartsWith("profiles.json has an error", item.Title);
    }

    [Fact]
    public void ProfilePage_ShowsOneFormWithEveryApp()
    {
        using var file = new TempProfilesFile(Json);
        using var store = new ProfileStore(file.FilePath, watch: false);

        var page = new ProfileEditorPage(store, _desktop).PageFor("dev");
        var form = Assert.IsType<ProfileCardContent>(Assert.Single(page.GetContent()));
        var inputs = CardJson.Inputs(form.TemplateJson);

        Assert.Equal("Dev", CardJson.Value(inputs, "p_name"));
        Assert.Equal("code", CardJson.Value(inputs, "a0_path"));
        Assert.Equal("API", CardJson.Value(inputs, "a1_t0_title"));
        Assert.Equal(["Launch this profile", "Add from an open window", "Show or hide all app settings", "Open profiles.json"], page.Commands.Select(c => ((Microsoft.CommandPalette.Extensions.ICommandContextItem)c).Title));
    }

    [Fact]
    public void ProfilePage_Save_RenamesAndKeepsTheFormOnScreen()
    {
        using var file = new TempProfilesFile(Json);
        using var store = new ProfileStore(file.FilePath, watch: false);
        var editor = new ProfileEditorPage(store, _desktop);
        var page = editor.PageFor("Dev");
        var form = (ProfileCardContent)page.GetContent()[0];

        form.SubmitForm(CardJson.Submit(form.TemplateJson, new() { ["p_name"] = "Development" }), CardJson.Buttons(form.TemplateJson)["Save"]);

        Assert.Equal(("Development", "Development"), (page.ProfileName, page.Title));
        Assert.Equal(["Development", "Writing"], store.Profiles.Select(p => p.Name));
        Assert.NotSame(page, editor.PageFor("Dev"));
    }

    [Fact]
    public void ProfilePage_AddApp_SavesAndOpensTheNewApp()
    {
        using var file = new TempProfilesFile(Json);
        using var store = new ProfileStore(file.FilePath, watch: false);
        var page = new ProfileEditorPage(store, _desktop).PageFor("Dev");
        var form = (ProfileCardContent)page.GetContent()[0];

        form.SubmitForm(CardJson.Submit(form.TemplateJson, new() { ["a0_arguments"] = "--new-window" }), CardJson.Buttons(form.TemplateJson)["Add app"]);

        var dev = store.Profiles[0];
        Assert.Equal(3, dev.Apps.Count);
        Assert.Equal("--new-window", dev.Apps[0].Arguments);

        var redrawn = ((ProfileCardContent)page.GetContent()[0]).TemplateJson;
        var bodies = CardJson.Elements(redrawn).Where(e => e["id"]?.GetValue<string>() is { } id && id.EndsWith("_body", System.StringComparison.Ordinal)).ToList();
        Assert.Equal([false, false, true], bodies.Select(b => b["isVisible"]?.GetValue<bool>() ?? true));
    }

    [Fact]
    public void ProfilePage_InvalidInput_SavesNothing()
    {
        using var file = new TempProfilesFile(Json);
        using var store = new ProfileStore(file.FilePath, watch: false);
        var page = new ProfileEditorPage(store, _desktop).PageFor("Dev");
        var form = (ProfileCardContent)page.GetContent()[0];

        form.SubmitForm(CardJson.Submit(form.TemplateJson, new() { ["p_description"] = "changed", ["a0_path"] = "" }), CardJson.Buttons(form.TemplateJson)["Save"]);

        Assert.Null(store.Profiles[0].Description);
        Assert.Equal(Json, System.IO.File.ReadAllText(file.FilePath));
    }

    [Fact]
    public void LaunchPage_WithEditor_LinksToIt()
    {
        using var file = new TempProfilesFile(Json);
        using var store = new ProfileStore(file.FilePath, watch: false);
        var editor = new ProfileEditorPage(store, _desktop);

        var items = new ProfilesPage(store, editor).GetItems();

        Assert.Same(editor, items[^1].Command);
        Assert.Equal("Edit profile", items[0].MoreCommands[0] is Microsoft.CommandPalette.Extensions.Toolkit.CommandContextItem c ? c.Title : null);
    }
}
