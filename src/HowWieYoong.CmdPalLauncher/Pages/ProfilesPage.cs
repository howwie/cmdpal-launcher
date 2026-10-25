using System.Linq;
using System.Text;
using HowWieYoong.CmdPalLauncher.Commands;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Pages.Editor;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Pages;

internal sealed partial class ProfilesPage : ListPage
{
    internal static readonly IconInfo ProfileIcon = new("\uE7C4");

    private readonly ProfileStore _store;
    private readonly ProfileEditorPage? _editor;

    /// <param name="editor">The profile editor; without it, editing opens profiles.json instead.</param>
    public ProfilesPage(ProfileStore store, ProfileEditorPage? editor = null)
    {
        _store = store;
        _editor = editor;
        _store.Changed += (_, _) => RaiseItemsChanged();

        Icon = ProfileIcon;
        Title = "Launch Profiles";
        Name = "Open";
        Id = "HowWieYoong.CmdPalLauncher.Profiles";
        PlaceholderText = "Search launch profiles";
        ShowDetails = true;
    }

    public override IListItem[] GetItems()
    {
        var edit = new OpenProfilesFileCommand(_store.FilePath);

        if (_store.Error is { } error)
        {
            return [
                new ListItem(edit)
                {
                    Title = "profiles.json has an error",
                    Subtitle = error,
                    Icon = new IconInfo("\uE7BA"),
                },
            ];
        }

        var items = _store.Profiles.Select(profile => (IListItem)new ListItem(new LaunchProfileCommand(profile))
        {
            Title = profile.Name,
            Subtitle = profile.Description ?? Summary(profile),
            Icon = IconFor(profile),
            Details = new Details { Title = profile.Name, Body = DetailsMarkdown(profile) },
            MoreCommands = _editor is null
                ? [new CommandContextItem(edit)]
                : [new CommandContextItem(_editor.PageFor(profile.Name)) { Title = "Edit profile" }, new CommandContextItem(edit) { Title = "Open profiles.json" }],
        });

        return [
            .. items,
            _editor is null
                ? new ListItem(edit) { Title = "Edit launch profiles", Subtitle = _store.FilePath }
                : new ListItem(_editor) { Title = "Edit launch profiles", Subtitle = "Add, change and reorder profiles and their apps" },
        ];
    }

    internal static IconInfo IconFor(LaunchProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Icon) ? ProfileIcon : new IconInfo(profile.Icon);

    internal static string Summary(LaunchProfile profile) =>
        string.Join(", ", profile.Apps.Select(AppLauncher.DisplayName));

    private static string DetailsMarkdown(LaunchProfile profile)
    {
        var markdown = new StringBuilder();
        foreach (var app in profile.Apps)
        {
            markdown.Append("- **").Append(AppLauncher.DisplayName(app)).Append("**");

            if (app.IsTerminal)
            {
                foreach (var tab in app.Tabs ?? [])
                {
                    markdown.Append("\n  - ").Append(tab.Title ?? tab.Profile ?? "Tab");
                    if (!string.IsNullOrWhiteSpace(tab.Directory))
                    {
                        markdown.Append(" — `").Append(tab.Directory).Append('`');
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(app.Arguments))
            {
                markdown.Append(" `").Append(app.Arguments).Append('`');
            }

            markdown.Append('\n');
        }

        return markdown.ToString();
    }
}
