using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;

namespace HowWieYoong.CmdPalLauncher.Editing;

/// <summary>Which button on the profile form was pressed, from the data that button submits.</summary>
internal sealed record ProfileCardAction(string Name, int App = -1, int Tab = -1, int Offset = 0)
{
    public const string Save = "save";
    public const string AddApp = "addApp";
    public const string AddTerminal = "addTerminal";
    public const string MoveApp = "moveApp";
    public const string DuplicateApp = "duplicateApp";
    public const string DeleteApp = "deleteApp";
    public const string AddTab = "addTab";
    public const string MoveTab = "moveTab";
    public const string DeleteTab = "deleteTab";

    public bool IsDelete => Name is DeleteApp or DeleteTab;

    /// <summary>Reads a button's data; anything unrecognised counts as Save.</summary>
    public static ProfileCardAction Parse(string? data)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(data) && JsonNode.Parse(data) is JsonObject obj && obj["action"]?.GetValue<string>() is { } name)
            {
                return new ProfileCardAction(name, Int(obj, "app"), Int(obj, "tab"), Int(obj, "offset", 0));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
        }

        return new ProfileCardAction(Save);
    }

    public JsonObject ToData()
    {
        var data = new JsonObject { ["action"] = Name };
        if (App >= 0)
        {
            data["app"] = App;
        }

        if (Tab >= 0)
        {
            data["tab"] = Tab;
        }

        if (Offset != 0)
        {
            data["offset"] = Offset;
        }

        return data;
    }

    private static int Int(JsonObject obj, string key, int fallback = -1) =>
        obj[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;
}

/// <summary>How many apps and tabs the form was drawn with, to notice if profiles.json changed underneath it.</summary>
internal sealed record ProfileCardLayout(int AppCount, IReadOnlyList<int> TabCounts)
{
    public static ProfileCardLayout Of(LaunchProfile profile) =>
        new(profile.Apps.Count, [.. profile.Apps.Select(a => a.IsTerminal ? a.Tabs?.Count ?? 0 : 0)]);

    public bool Matches(LaunchProfile profile) => Of(profile) is var now && now.AppCount == AppCount && now.TabCounts.SequenceEqual(TabCounts);
}

/// <summary>
/// One form for a whole profile: its settings, then a section per app (collapsed until "Edit" is pressed), with
/// Terminal tabs inside their app. Every button submits the whole form, so nothing typed is lost; the button's data
/// says what to do after the inputs are saved.
/// </summary>
internal static class ProfileCard
{
    /// <param name="iconPreview">Turns an icon into an image URL to show beside the Icon field, or null for none.</param>
    public static string Template(LaunchProfile profile, IReadOnlyList<MonitorInfo> monitors, IReadOnlySet<int> expanded, Func<string?, string?>? iconPreview = null)
    {
        var body = new List<JsonObject?> { Card.Heading("Profile") };
        body.AddRange(Card.Section("p_", WithIconPreview(ProfileForm.Fields(profile), iconPreview?.Invoke(profile.Icon))));
        body.Add(Card.Heading("Apps"));

        if (profile.Apps.Count == 0)
        {
            body.Add(Card.Note("No apps yet. Add one with the buttons below."));
        }

        for (var i = 0; i < profile.Apps.Count; i++)
        {
            body.Add(AppSection(profile.Apps[i], i, monitors, expanded.Contains(i)));
        }

        return Card.Form(
            body,
            Card.Submit("Save", new ProfileCardAction(ProfileCardAction.Save).ToData()),
            Card.Submit("Add app", new ProfileCardAction(ProfileCardAction.AddApp).ToData()),
            Card.Submit("Add Windows Terminal", new ProfileCardAction(ProfileCardAction.AddTerminal).ToData()));
    }

    /// <summary>
    /// Copies every input on the form into the profile called <paramref name="profileName"/> in <paramref name="file"/>.
    /// The app or tab that <paramref name="action"/> deletes isn't checked, so a half-filled one can still be deleted.
    /// </summary>
    public static string? ApplyInputs(ProfilesFile file, string profileName, string inputs, ProfileCardLayout layout, ProfileCardAction action, out string newName)
    {
        newName = profileName;
        if (ProfileEditing.Find(file, profileName) is not { } profile)
        {
            return $"There's no profile called \"{profileName}\" any more.";
        }

        if (!layout.Matches(profile))
        {
            return "profiles.json changed since this page was opened, so nothing was saved. Go back and open the profile again.";
        }

        var values = FormValues.Parse(inputs);
        var (name, description, icon) = ProfileForm.Read(values.Section("p_"));
        if (ProfileEditing.UpdateProfile(file, profileName, name, description, icon) is { } error)
        {
            return error;
        }

        newName = name.Trim();
        for (var i = 0; i < profile.Apps.Count; i++)
        {
            if (action is { Name: ProfileCardAction.DeleteApp } && action.App == i)
            {
                continue;
            }

            var app = profile.Apps[i];
            var appValues = values.Section($"a{i}_");
            if (AppForm.Apply(app, appValues) is { } appError)
            {
                return $"{Label(app, i)}: {appError}";
            }

            for (var j = 0; j < (app.IsTerminal ? app.Tabs?.Count ?? 0 : 0); j++)
            {
                if (action is { Name: ProfileCardAction.DeleteTab } && action.App == i && action.Tab == j)
                {
                    continue;
                }

                if (TabForm.Apply(app.Tabs![j], appValues.Section($"t{j}_")) is { } tabError)
                {
                    return $"{Label(app, i)}, tab {j + 1}: {tabError}";
                }
            }
        }

        return null;
    }

    /// <summary>Does what the button asked for, after <see cref="ApplyInputs"/>. <paramref name="expand"/> is the app to show open afterwards.</summary>
    public static string? Perform(LaunchProfile profile, ProfileCardAction action, out int? expand)
    {
        expand = null;
        var apps = profile.Apps;
        var app = action.App >= 0 && action.App < apps.Count ? apps[action.App] : null;
        var tabs = app is { IsTerminal: true } ? app.Tabs ??= [] : null;

        switch (action.Name)
        {
            case ProfileCardAction.Save:
                return null;

            case ProfileCardAction.AddApp:
                apps.Add(new AppEntry());
                expand = apps.Count - 1;
                return null;

            case ProfileCardAction.AddTerminal:
                apps.Add(new AppEntry { Type = "terminal", Tabs = [] });
                expand = apps.Count - 1;
                return null;
        }

        if (app is null)
        {
            return "That app no longer exists.";
        }

        switch (action.Name)
        {
            case ProfileCardAction.MoveApp:
                expand = Math.Clamp(action.App + action.Offset, 0, apps.Count - 1);
                return ProfileEditing.Move(apps, action.App, action.Offset);

            case ProfileCardAction.DuplicateApp:
                apps.Insert(action.App + 1, ProfileWriter.Clone(app));
                expand = action.App + 1;
                return null;

            case ProfileCardAction.DeleteApp:
                apps.RemoveAt(action.App);
                return null;
        }

        if (tabs is null)
        {
            return "Only Windows Terminal entries have tabs.";
        }

        expand = action.App;
        switch (action.Name)
        {
            case ProfileCardAction.AddTab:
                tabs.Add(new TerminalTab());
                return null;

            case ProfileCardAction.MoveTab:
                return ProfileEditing.Move(tabs, action.Tab, action.Offset);

            case ProfileCardAction.DeleteTab when action.Tab >= 0 && action.Tab < tabs.Count:
                tabs.RemoveAt(action.Tab);
                return null;

            case ProfileCardAction.DeleteTab:
                return "That tab no longer exists.";

            default:
                return $"Unknown button \"{action.Name}\".";
        }
    }

    /// <summary>The line under an app's name, e.g. "code ." or "2 tabs".</summary>
    public static string Describe(AppEntry app)
    {
        if (app.IsTerminal)
        {
            var count = app.Tabs?.Count ?? 0;
            return count == 1 ? "1 tab" : $"{count} tabs";
        }

        return string.IsNullOrWhiteSpace(app.Path) ? "No path yet"
            : string.IsNullOrWhiteSpace(app.Arguments) ? app.Path
            : $"{app.Path} {app.Arguments}";
    }

    public static string BodyId(int appIndex) => $"a{appIndex}_body";

    /// <summary>Puts the icon picture to the right of the Icon field. The picture only changes when the form is redrawn, e.g. after saving.</summary>
    private static List<JsonObject?> WithIconPreview(List<JsonObject?> fields, string? previewUrl)
    {
        if (previewUrl is null)
        {
            return fields;
        }

        // The field's own label would sit inside the row and push the text box below the picture's centre, so the
        // label goes above the row and the row holds only the text box and the picture.
        var index = fields.FindIndex(f => f?["id"]?.GetValue<string>() == "icon");
        var input = fields[index]!;
        var label = input["label"]?.GetValue<string>() ?? "Icon";
        input.Remove("label");
        fields[index] = Card.Row(null, ("stretch", [input]), ("auto", [Card.Image(previewUrl, 36, "Icon preview")]));
        fields.Insert(index, Card.TextBlock(label));
        return fields;
    }

    private static JsonObject AppSection(AppEntry app, int i, IReadOnlyList<MonitorInfo> monitors, bool expanded)
    {
        var body = new List<JsonObject?>
        {
            Card.Buttons(
                Card.Submit("Move up", new ProfileCardAction(ProfileCardAction.MoveApp, i, Offset: -1).ToData()),
                Card.Submit("Move down", new ProfileCardAction(ProfileCardAction.MoveApp, i, Offset: 1).ToData()),
                Card.Submit("Duplicate", new ProfileCardAction(ProfileCardAction.DuplicateApp, i).ToData()),
                Card.Submit("Delete", new ProfileCardAction(ProfileCardAction.DeleteApp, i).ToData(), destructive: true)),
        };
        body.AddRange(Card.Section($"a{i}_", AppForm.Fields(app, monitors)));

        if (app.IsTerminal)
        {
            body.Add(Card.TextBlock("Tabs", bold: true));
            var tabs = app.Tabs ?? [];
            for (var j = 0; j < tabs.Count; j++)
            {
                body.Add(Card.Container(
                    [
                        Card.TextBlock($"Tab {j + 1}{(string.IsNullOrWhiteSpace(tabs[j].Title) ? string.Empty : $": {tabs[j].Title}")}", bold: true),
                        .. Card.Section($"a{i}_t{j}_", TabForm.Fields(tabs[j])),
                        Card.Buttons(
                            Card.Submit("Move tab up", new ProfileCardAction(ProfileCardAction.MoveTab, i, j, -1).ToData()),
                            Card.Submit("Move tab down", new ProfileCardAction(ProfileCardAction.MoveTab, i, j, 1).ToData()),
                            Card.Submit("Delete tab", new ProfileCardAction(ProfileCardAction.DeleteTab, i, j).ToData(), destructive: true)),
                    ],
                    style: "emphasis"));
            }

            body.Add(Card.Buttons(Card.Submit("Add tab", new ProfileCardAction(ProfileCardAction.AddTab, i).ToData())));
        }

        // Clicking the header row shows or hides the settings, and swaps the ▸ arrow for ▾ (both are toggled, so
        // exactly one is ever visible; the arrows have no emoji form, so they stay plain text). Command Palette makes
        // card text selectable, so clicks on the text itself select it instead of toggling; a full-width button or
        // text drawn as an image would avoid that, but neither fits Command Palette's look, so this is accepted.
        var (closedArrow, openArrow) = ($"a{i}_closed", $"a{i}_open");
        var header = Card.Row(
            Card.ToggleVisibility("Show or hide settings", BodyId(i), closedArrow, openArrow),
            // A fixed width, because ▸ and ▾ differ in width and an "auto" column would nudge the name sideways.
            ("20px", [Card.TextBlock("▸", size: "Large", id: closedArrow, visible: !expanded), Card.TextBlock("▾", size: "Large", id: openArrow, visible: expanded)]),
            ("stretch", [Card.TextBlock(AppLauncher.DisplayName(app), bold: true, size: "Medium"), Card.TextBlock(Describe(app), subtle: true)]));

        return Card.Container([header, Card.Container(body, id: BodyId(i), visible: expanded)], style: "emphasis");
    }

    private static string Label(AppEntry app, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"App {index + 1} ({AppLauncher.DisplayName(app)})");
}
