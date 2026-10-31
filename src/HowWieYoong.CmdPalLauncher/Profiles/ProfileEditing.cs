using System;
using System.Collections.Generic;
using System.Linq;

namespace HowWieYoong.CmdPalLauncher.Profiles;

/// <summary>
/// The edits the editor makes to profiles.json. Each returns null on success or a message saying why it couldn't be
/// done. Profiles are found by name and apps by position, as the editor showed them.
/// </summary>
internal static class ProfileEditing
{
    public static string? AddProfile(ProfilesFile file, string name, string? description, string? icon)
    {
        if (ValidateName(file, name, ignore: null) is { } error)
        {
            return error;
        }

        file.Profiles.Add(new LaunchProfile { Name = name.Trim(), Description = Blank(description), Icon = Blank(icon) });
        return null;
    }

    public static string? UpdateProfile(ProfilesFile file, string profileName, string name, string? description, string? icon)
    {
        if (Find(file, profileName) is not { } profile)
        {
            return NotFound(profileName);
        }

        if (ValidateName(file, name, ignore: profile) is { } error)
        {
            return error;
        }

        profile.Name = name.Trim();
        profile.Description = Blank(description);
        profile.Icon = Blank(icon);
        return null;
    }

    public static string? DeleteProfile(ProfilesFile file, string profileName)
    {
        if (Find(file, profileName) is not { } profile)
        {
            return NotFound(profileName);
        }

        file.Profiles.Remove(profile);
        return null;
    }

    public static string? MoveProfile(ProfilesFile file, string profileName, int offset) =>
        Find(file, profileName) is { } profile ? Move(file.Profiles, file.Profiles.IndexOf(profile), offset) : NotFound(profileName);

    /// <summary>Copies a profile as "Name (copy)", "Name (copy 2)", … right after the original.</summary>
    public static string? DuplicateProfile(ProfilesFile file, string profileName, out string copyName)
    {
        copyName = string.Empty;
        if (Find(file, profileName) is not { } profile)
        {
            return NotFound(profileName);
        }

        copyName = UniqueName(file, $"{profile.Name} (copy)");
        var copy = ProfileWriter.Clone(profile);
        copy.Name = copyName;
        file.Profiles.Insert(file.Profiles.IndexOf(profile) + 1, copy);
        return null;
    }

    public static string? UpdateApp(ProfilesFile file, string profileName, int index, Func<AppEntry, string?> update) =>
        WithApp(file, profileName, index, (_, app) => update(app));

    public static string? DeleteApp(ProfilesFile file, string profileName, int index) =>
        WithApp(file, profileName, index, (profile, _) =>
        {
            profile.Apps.RemoveAt(index);
            return null;
        });

    public static string? MoveApp(ProfilesFile file, string profileName, int index, int offset) =>
        WithApp(file, profileName, index, (profile, _) => Move(profile.Apps, index, offset));

    public static string? DuplicateApp(ProfilesFile file, string profileName, int index) =>
        WithApp(file, profileName, index, (profile, app) =>
        {
            profile.Apps.Insert(index + 1, ProfileWriter.Clone(app));
            return null;
        });

    /// <summary>Edits the tab list of a Terminal entry, creating it if needed.</summary>
    public static string? UpdateTabs(ProfilesFile file, string profileName, int appIndex, Func<List<TerminalTab>, string?> update) =>
        WithApp(file, profileName, appIndex, (_, app) =>
        {
            if (!app.IsTerminal)
            {
                return "Only Windows Terminal entries have tabs.";
            }

            app.Tabs ??= [];
            return update(app.Tabs);
        });

    /// <summary>Moves the item at <paramref name="index"/> by <paramref name="offset"/>, e.g. -1 for up.</summary>
    public static string? Move<T>(List<T> list, int index, int offset)
    {
        if (index < 0 || index >= list.Count)
        {
            return "That item no longer exists.";
        }

        var target = Math.Clamp(index + offset, 0, list.Count - 1);
        if (target != index)
        {
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(target, item);
        }

        return null;
    }

    public static LaunchProfile? Find(ProfilesFile file, string profileName) =>
        file.Profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

    private static string? WithApp(ProfilesFile file, string profileName, int index, Func<LaunchProfile, AppEntry, string?> edit)
    {
        if (Find(file, profileName) is not { } profile)
        {
            return NotFound(profileName);
        }

        return index >= 0 && index < profile.Apps.Count
            ? edit(profile, profile.Apps[index])
            : $"That app is no longer in \"{profileName}\". profiles.json may have changed; go back and try again.";
    }

    /// <summary>Names must be non-empty and unique, because profiles are found, and launched from the command line, by name.</summary>
    private static string? ValidateName(ProfilesFile file, string name, LaunchProfile? ignore)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Give the profile a name.";
        }

        return file.Profiles.Any(p => p != ignore && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? $"There's already a profile called \"{name.Trim()}\"."
            : null;
    }

    private static string UniqueName(ProfilesFile file, string name)
    {
        var candidate = name;
        for (var n = 2; Find(file, candidate) is not null; n++)
        {
            candidate = $"{name[..^1]} {n})";
        }

        return candidate;
    }

    private static string NotFound(string profileName) =>
        $"There's no profile called \"{profileName}\" any more. profiles.json may have changed; go back and try again.";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
