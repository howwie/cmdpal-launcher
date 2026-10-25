using System;
using System.Collections.Generic;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Commands;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Editing;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Pages.Editor;

/// <summary>One profile as a single form: its settings and all its apps (see <see cref="ProfileCard"/>).</summary>
internal sealed partial class ProfileFormPage : ContentPage, IRefreshable
{
    private readonly ProfileStore _store;
    private readonly IDesktop _desktop;
    private readonly HashSet<int> _expanded = [];
    private bool _expandAll;
    private bool _saving;

    public ProfileFormPage(ProfileStore store, IDesktop desktop, string profileName)
    {
        _store = store;
        _desktop = desktop;
        ProfileName = profileName;
        Title = profileName;
        Name = "Edit";
        Icon = EditorIcons.Edit;
        PageRefresh.Watch(store, this);

        Commands =
        [
            new CommandContextItem(new DelegateCommand("Launch this profile", ProfilesPage.ProfileIcon, Launch)),
            new CommandContextItem(new AddFromWindowPage(store, desktop, this)) { Title = "Add from an open window" },
            new CommandContextItem(new DelegateCommand("Show or hide all app settings", EditorIcons.Settings, ToggleExpandAll)),
            new CommandContextItem(new OpenProfilesFileCommand(store.FilePath)) { Title = "Open profiles.json" },
        ];
    }

    public string ProfileName { get; private set; }

    public LaunchProfile? Profile => _store.Profiles.FirstOrDefault(p => string.Equals(p.Name, ProfileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Redraws when profiles.json changes, except during this page's own saves, which redraw only when needed.</summary>
    public void Refresh()
    {
        if (!_saving)
        {
            RaiseItemsChanged();
        }
    }

    public override IContent[] GetContent()
    {
        if (Profile is not { } profile)
        {
            return [new MarkdownContent($"There's no profile called **{ProfileName}** any more. Go back to pick another one.")];
        }

        // The page header shows the profile's icon too, drawn by Command Palette itself.
        Icon = ProfilesPage.IconFor(profile);

        var expanded = _expandAll ? Enumerable.Range(0, profile.Apps.Count).ToHashSet() : _expanded;
        return [new ProfileCardContent(this, profile, _desktop.GetMonitors(), expanded, IconPreview.DataUri)];
    }

    /// <summary>Saves the form's inputs, then does what the pressed button asked for.</summary>
    internal CommandResult Run(string inputs, ProfileCardAction action, ProfileCardLayout layout)
    {
        var newName = ProfileName;
        var iconBefore = Profile?.Icon;
        int? expand = null;

        _saving = true;
        string? error;
        try
        {
            error = _store.Update(file =>
                ProfileCard.ApplyInputs(file, ProfileName, inputs, layout, action, out newName)
                ?? ProfileCard.Perform(ProfileEditing.Find(file, newName)!, action, out expand));
        }
        finally
        {
            _saving = false;
        }

        if (error is not null)
        {
            return EditorResults.Failed(error);
        }

        var renamed = !string.Equals(newName, ProfileName, StringComparison.Ordinal);
        ProfileName = newName;
        Title = newName;

        if (action.Name == ProfileCardAction.Save)
        {
            // Saving leaves the form as it is on screen, so sections the user opened stay open. A new name or
            // icon needs a redraw, for the title and the icon preview.
            if (renamed || !string.Equals(iconBefore, Profile?.Icon, StringComparison.Ordinal))
            {
                _expanded.Clear();
                RaiseItemsChanged();
            }

            return CommandResult.ShowToast(new ToastArgs { Message = "Saved", Result = CommandResult.KeepOpen() });
        }

        Expand(expand);
        RaiseItemsChanged();
        return CommandResult.KeepOpen();
    }

    /// <summary>Shows only <paramref name="appIndex"/>'s settings open next time the form is drawn.</summary>
    internal void Expand(int? appIndex)
    {
        _expanded.Clear();
        if (appIndex is int index)
        {
            _expanded.Add(index);
        }
    }

    internal void Redraw() => RaiseItemsChanged();

    private CommandResult Launch() =>
        Profile is { } profile ? new LaunchProfileCommand(profile).Invoke() : EditorResults.Failed($"There's no profile called \"{ProfileName}\" any more.");

    private CommandResult ToggleExpandAll()
    {
        _expandAll = !_expandAll;
        RaiseItemsChanged();
        return CommandResult.KeepOpen();
    }
}

/// <summary>The profile form itself. Deletes ask first; everything else saves straight away.</summary>
internal sealed partial class ProfileCardContent : FormContent
{
    private readonly ProfileFormPage _page;
    private readonly ProfileCardLayout _layout;

    public ProfileCardContent(ProfileFormPage page, LaunchProfile profile, IReadOnlyList<MonitorInfo> monitors, IReadOnlySet<int> expanded, Func<string?, string?>? iconPreview = null)
    {
        _page = page;
        _layout = ProfileCardLayout.Of(profile);
        TemplateJson = ProfileCard.Template(profile, monitors, expanded, iconPreview);
    }

    public override ICommandResult SubmitForm(string inputs, string data)
    {
        var action = ProfileCardAction.Parse(data);
        if (!action.IsDelete)
        {
            return _page.Run(inputs, action, _layout);
        }

        return CommandResult.Confirm(new ConfirmationArgs
        {
            Title = action.Name == ProfileCardAction.DeleteApp ? "Delete this app?" : "Delete this tab?",
            Description = "Your other changes on this page are saved too. The previous version of profiles.json is kept in profiles.json.bak.",
            PrimaryCommand = new DelegateCommand("Delete", EditorIcons.Delete, () => _page.Run(inputs, action, _layout)),
            IsPrimaryCommandCritical = true,
        });
    }
}
