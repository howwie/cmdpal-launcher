using System.Threading.Tasks;
using HowWieYoong.CmdPalLauncher.Launching;
using HowWieYoong.CmdPalLauncher.Profiles;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HowWieYoong.CmdPalLauncher.Commands;

internal sealed partial class LaunchProfileCommand : InvokableCommand
{
    private readonly LaunchProfile _profile;

    public LaunchProfileCommand(LaunchProfile profile)
    {
        _profile = profile;
        Name = "Launch";
        Id = $"HowWieYoong.CmdPalLauncher.Launch.{profile.Name}";
        Icon = new IconInfo("");
    }

    public override CommandResult Invoke()
    {
        // Apps can have delays between them, so don't hold the palette open while they start.
        _ = Task.Run(() =>
        {
            var failures = AppLauncher.LaunchAll(_profile);
            if (failures.Count > 0)
            {
                new ToastStatusMessage(new StatusMessage
                {
                    Message = $"{_profile.Name}: {string.Join("; ", failures)}",
                    State = MessageState.Error,
                }).Show();
            }
        });

        return CommandResult.ShowToast($"Launching {_profile.Name}");
    }
}
