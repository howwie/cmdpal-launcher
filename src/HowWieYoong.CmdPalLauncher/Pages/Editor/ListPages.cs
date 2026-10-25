using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HowWieYoong.CmdPalLauncher.Commands;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Pages.Editor;

/// <summary>The editor's first page: every profile, plus "New profile" and the raw profiles.json.</summary>
internal sealed partial class ProfileEditorPage : ListPage, IRefreshable
{
    private readonly ProfileStore _store;
    private readonly IDesktop _desktop;
    private readonly Dictionary<string, ProfileFormPage> _pages = new(StringComparer.OrdinalIgnoreCase);

    public ProfileEditorPage(ProfileStore store, IDesktop desktop)
    {
        _store = store;
        _desktop = desktop;
        Title = "Edit Launch Profiles";
        Name = "Open";
        Icon = EditorIcons.Edit;
        Id = "HowWieYoong.CmdPalLauncher.Editor";
        PlaceholderText = "Search profiles";
        PageRefresh.Watch(store, this);
    }

    /// <summary>The form page for one profile, reused so it keeps its open sections while it's in use.</summary>
    public ProfileFormPage PageFor(string profileName)
    {
        if (!_pages.TryGetValue(profileName, out var page) || !string.Equals(page.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
        {
            page = new ProfileFormPage(_store, _desktop, profileName);
            _pages[profileName] = page;
        }

        return page;
    }

    public void Refresh() => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var openFile = new OpenProfilesFileCommand(_store.FilePath) { Name = "Open profiles.json" };
        if (_store.Error is { } error)
        {
            return [new ListItem(openFile) { Title = "profiles.json has an error, so it can't be edited here", Subtitle = error, Icon = EditorIcons.Warning }];
        }

        var profiles = _store.Profiles.Select(profile => (IListItem)new ListItem(PageFor(profile.Name))
        {
            Title = profile.Name,
            Subtitle = profile.Description ?? ProfilesPage.Summary(profile),
            Icon = ProfilesPage.IconFor(profile),
            MoreCommands = ListItemCommands.For(
                _store,
                $"\"{profile.Name}\"",
                "The profile and all its apps are removed from profiles.json. The previous version is kept in profiles.json.bak.",
                offset => file => ProfileEditing.MoveProfile(file, profile.Name, offset),
                file => ProfileEditing.DuplicateProfile(file, profile.Name, out _),
                file => ProfileEditing.DeleteProfile(file, profile.Name)),
        });

        return
        [
            .. profiles,
            new ListItem(new FormPage("New profile", EditorIcons.Add, new NewProfileFormContent(_store)))
            {
                Title = "New profile",
                Icon = EditorIcons.Add,
            },
            new ListItem(openFile)
            {
                Title = "Open profiles.json",
                Subtitle = $"{_store.FilePath}. Saving from the editor drops comments; the previous version goes to profiles.json.bak",
            },
        ];
    }
}

/// <summary>Lists open windows; picking one adds a captured app entry for it to the profile.</summary>
internal sealed partial class AddFromWindowPage : ListPage
{
    private const string PaletteProcess = "Microsoft.CmdPal.UI";

    private readonly ProfileStore _store;
    private readonly IDesktop _desktop;
    private readonly ProfileFormPage _profile;

    public AddFromWindowPage(ProfileStore store, IDesktop desktop, ProfileFormPage profile)
    {
        _store = store;
        _desktop = desktop;
        _profile = profile;
        Title = "Add from an open window";
        Name = "Open";
        Icon = EditorIcons.Window;
        PlaceholderText = "Search windows";
        ShowDetails = true;
    }

    public override IListItem[] GetItems()
    {
        var monitors = _desktop.GetMonitors();
        return _desktop.GetWindows()
            .Where(w => !w.IsMinimized && !string.Equals(w.ProcessName, PaletteProcess, StringComparison.OrdinalIgnoreCase))
            .Select(window =>
            {
                var captured = WindowCapture.Capture(window, _desktop.GetProcessDetails(window), monitors);
                return (IListItem)new ListItem(new DelegateCommand("Add to profile", EditorIcons.Add, () => Add(captured)))
                {
                    Title = window.Title,
                    Subtitle = window.ProcessName,
                    Details = new Details { Title = window.Title, Body = $"```json\n{captured.Entry}\n```\n" + string.Concat(captured.Notes.Select(n => $"\n- {n}")) },
                };
            })
            .ToArray();
    }

    private CommandResult Add(CapturedWindow captured)
    {
        AppEntry? app;
        try
        {
            app = JsonSerializer.Deserialize(captured.Entry, ProfilesJsonContext.Default.AppEntry);
        }
        catch (JsonException ex)
        {
            return EditorResults.Failed($"Couldn't read the captured window: {ex.Message}");
        }

        if (app is null)
        {
            return EditorResults.Failed("Couldn't read the captured window.");
        }

        var index = -1;
        var error = _store.Update(file =>
        {
            index = ProfileEditing.Find(file, _profile.ProfileName)?.Apps.Count ?? -1;
            return ProfileEditing.AddApp(file, _profile.ProfileName, app);
        });
        if (error is not null)
        {
            return EditorResults.Failed(error);
        }

        // Show the new app open when the form is back on screen.
        _profile.Expand(index);
        _profile.Redraw();
        return EditorResults.SavedAndBack($"Added {app.Name ?? "the app"}");
    }
}
