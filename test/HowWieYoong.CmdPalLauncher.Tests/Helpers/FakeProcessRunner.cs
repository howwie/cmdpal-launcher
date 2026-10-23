using System;
using System.Collections.Generic;
using System.Diagnostics;
using HowWieYoong.CmdPalLauncher.Launching;

namespace HowWieYoong.CmdPalLauncher.Tests.Helpers;

/// <summary>Records what <see cref="AppLauncher"/> asks for instead of starting processes.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public HashSet<string> RunningProcesses { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every call in order, e.g. "delay 500", "start notepad.exe".</summary>
    public List<string> Calls { get; } = [];

    public List<ProcessStartInfo> Started { get; } = [];

    /// <summary>Return an exception to make starting that app fail.</summary>
    public Func<ProcessStartInfo, Exception?> FailWith { get; set; } = _ => null;

    /// <summary>Runs after an app "starts", e.g. to make its window appear on a <see cref="FakeDesktop"/>.</summary>
    public Action<ProcessStartInfo> OnStart { get; set; } = _ => { };

    public bool IsRunning(string processName)
    {
        Calls.Add($"isRunning {processName}");
        return RunningProcesses.Contains(processName);
    }

    public void Start(ProcessStartInfo startInfo)
    {
        Calls.Add($"start {startInfo.FileName}");
        if (FailWith(startInfo) is { } exception)
        {
            throw exception;
        }

        Started.Add(startInfo);
        OnStart(startInfo);
    }

    public void Delay(int milliseconds) => Calls.Add($"delay {milliseconds}");
}
