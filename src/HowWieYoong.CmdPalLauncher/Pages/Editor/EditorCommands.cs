using System;
using System.Collections.Generic;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Pages.Editor;

internal static class EditorIcons
{
    public static readonly IconInfo Edit = new("\uE70F");
    public static readonly IconInfo Add = new("\uE710");
    public static readonly IconInfo Delete = new("\uE74D");
    public static readonly IconInfo Up = new("\uE74A");
    public static readonly IconInfo Down = new("\uE74B");
    public static readonly IconInfo Copy = new("\uE8C8");
    public static readonly IconInfo Settings = new("\uE713");
    public static readonly IconInfo App = new("\uE737");
    public static readonly IconInfo Terminal = new("\uE756");
    public static readonly IconInfo Tab = new("\uE7C3");
    public static readonly IconInfo Window = new("\uE7F4");
    public static readonly IconInfo Warning = new("\uE7BA");
}

/// <summary>Results shared by the editor's commands and forms.</summary>
internal static class EditorResults
{
    /// <summary>Shows <paramref name="error"/> and stays on the page, so a form keeps what was typed.</summary>
    public static CommandResult Failed(string error) =>
        CommandResult.ShowToast(new ToastArgs { Message = error, Result = CommandResult.KeepOpen() });

    public static CommandResult SavedAndBack(string message) =>
        CommandResult.ShowToast(new ToastArgs { Message = message, Result = CommandResult.GoBack() });
}

/// <summary>Makes one change to profiles.json through <see cref="ProfileStore.Update"/>, e.g. move or delete.</summary>
internal sealed partial class EditCommand : InvokableCommand
{
    private readonly ProfileStore _store;
    private readonly Func<ProfilesFile, string?> _edit;
    private readonly bool _goBack;

    public EditCommand(string name, IconInfo icon, ProfileStore store, Func<ProfilesFile, string?> edit, bool goBack = false)
    {
        Name = name;
        Icon = icon;
        _store = store;
        _edit = edit;
        _goBack = goBack;
    }

    public override CommandResult Invoke() =>
        _store.Update(_edit) is { } error ? EditorResults.Failed(error)
        : _goBack ? CommandResult.GoBack()
        : CommandResult.KeepOpen();
}

/// <summary>A command that runs a function and returns its result.</summary>
internal sealed partial class DelegateCommand : InvokableCommand
{
    private readonly Func<CommandResult> _invoke;

    public DelegateCommand(string name, IconInfo icon, Func<CommandResult> invoke)
    {
        Name = name;
        Icon = icon;
        _invoke = invoke;
    }

    public override CommandResult Invoke() => _invoke();
}

/// <summary>Asks before running <paramref name="command"/>, for deletes.</summary>
internal sealed partial class ConfirmCommand : InvokableCommand
{
    private readonly string _title;
    private readonly string _description;
    private readonly InvokableCommand _command;

    public ConfirmCommand(string title, string description, InvokableCommand command)
    {
        Name = command.Name;
        Icon = command.Icon;
        _title = title;
        _description = description;
        _command = command;
    }

    public override CommandResult Invoke() => CommandResult.Confirm(new ConfirmationArgs
    {
        Title = _title,
        Description = _description,
        PrimaryCommand = _command,
        IsPrimaryCommandCritical = true,
    });
}

/// <summary>The "Move up", "Move down", "Duplicate" and "Delete" commands shown on profiles, apps and tabs.</summary>
internal static class ListItemCommands
{
    public static IContextItem[] For(
        ProfileStore store,
        string what,
        string description,
        Func<int, Func<ProfilesFile, string?>> move,
        Func<ProfilesFile, string?> duplicate,
        Func<ProfilesFile, string?> delete)
    {
        var items = new List<IContextItem>
        {
            new CommandContextItem(new EditCommand("Move up", EditorIcons.Up, store, move(-1))),
            new CommandContextItem(new EditCommand("Move down", EditorIcons.Down, store, move(1))),
            new CommandContextItem(new EditCommand("Duplicate", EditorIcons.Copy, store, duplicate)),
            new CommandContextItem(new ConfirmCommand(
                $"Delete {what}?",
                description,
                new EditCommand("Delete", EditorIcons.Delete, store, delete)))
            {
                IsCritical = true,
            },
        };

        return [.. items];
    }
}

/// <summary>A list page that can be told to reload its items (RaiseItemsChanged is protected).</summary>
internal interface IRefreshable
{
    void Refresh();
}

/// <summary>
/// Refreshes a list page whenever profiles.json changes. Holds the page weakly, so pages that Command Palette
/// has finished with can be collected.
/// </summary>
internal static class PageRefresh
{
    public static void Watch(ProfileStore store, IRefreshable page)
    {
        var target = new WeakReference<IRefreshable>(page);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (target.TryGetTarget(out var alive))
            {
                alive.Refresh();
            }
            else
            {
                store.Changed -= handler;
            }
        };
        store.Changed += handler;
    }
}
