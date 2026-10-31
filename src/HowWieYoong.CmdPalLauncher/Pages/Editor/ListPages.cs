using System;
using System.Collections.Generic;
using System.Linq;
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
