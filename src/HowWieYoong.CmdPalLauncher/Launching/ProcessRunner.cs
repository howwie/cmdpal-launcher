using System.Diagnostics;
using System.Threading;

namespace HowWieYoong.CmdPalLauncher.Launching;

/// <summary>The process operations <see cref="AppLauncher"/> needs, so tests can replace them.</summary>
internal interface IProcessRunner
{
    bool IsRunning(string processName);

    void Start(ProcessStartInfo startInfo);

    void Delay(int milliseconds);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public static readonly ProcessRunner Default = new();

    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo)?.Dispose();

    public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
}
