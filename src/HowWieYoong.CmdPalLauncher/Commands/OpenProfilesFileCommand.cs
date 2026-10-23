using System.ComponentModel;
using System.Diagnostics;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Commands;

internal sealed partial class OpenProfilesFileCommand : InvokableCommand
{
    private readonly string _filePath;

    public OpenProfilesFileCommand(string filePath)
    {
        _filePath = filePath;
        Name = "Edit profiles";
        Id = "HowWieYoong.CmdPalLauncher.EditProfiles";
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        ProfileStore.EnsureFileExists(_filePath);

        try
        {
            Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // No app is associated with .json files.
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_filePath}\""))?.Dispose();
        }

        return CommandResult.Dismiss();
    }
}
