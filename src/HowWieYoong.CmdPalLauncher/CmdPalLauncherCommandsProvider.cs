using System.Linq;
using HowWieYoong.CmdPalLauncher.Commands;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Pages;
using HowWieYoong.CmdPalLauncher.Pages.Editor;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher;

public partial class CmdPalLauncherCommandsProvider : CommandProvider
{
    private readonly ProfileStore _store = new();
    private readonly ProfilesPage _page;
    private readonly ProfileEditorPage _editor;
    private readonly WindowPositionsPage _windowsPage = new(Win32Desktop.Instance);

    public CmdPalLauncherCommandsProvider()
    {
        DisplayName = "Launch Profiles";
        Icon = ProfilesPage.ProfileIcon;
        _editor = new ProfileEditorPage(_store, Win32Desktop.Instance);
        _page = new ProfilesPage(_store, _editor);

        // Each profile is also a top-level command, so those need refreshing when the file changes.
        _store.Changed += (_, _) => RaiseItemsChanged();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        var profiles = _store.Profiles.Select(profile => (ICommandItem)new CommandItem(new LaunchProfileCommand(profile))
        {
            Title = $"Launch {profile.Name}",
            Subtitle = profile.Description ?? ProfilesPage.Summary(profile),
            Icon = ProfilesPage.IconFor(profile),
        });

        return [
            new CommandItem(_page) { Title = "Launch Profiles", Subtitle = "Start a saved set of apps" },
            .. profiles,
            new CommandItem(_editor)
            {
                Title = "Edit launch profiles",
                Subtitle = "Add, change and reorder profiles and their apps",
                MoreCommands = [new CommandContextItem(new OpenProfilesFileCommand(_store.FilePath)) { Title = "Open profiles.json" }],
            },
            new CommandItem(_windowsPage) { Title = "Capture window", Subtitle = "Copy an open window as a profile app entry" },
        ];
    }
}
