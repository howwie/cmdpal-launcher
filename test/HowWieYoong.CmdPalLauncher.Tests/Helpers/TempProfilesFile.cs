using System;
using System.IO;

namespace HowWieYoong.CmdPalLauncher.Tests.Helpers;

/// <summary>A profiles.json path in its own temp folder, deleted on dispose.</summary>
internal sealed class TempProfilesFile : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cmdpal-launcher-tests", Guid.NewGuid().ToString("N"));

    public TempProfilesFile(string? json = null)
    {
        Directory.CreateDirectory(_directory);
        if (json is not null)
        {
            Write(json);
        }
    }

    public string FilePath => Path.Combine(_directory, "profiles.json");

    public void Write(string json) => File.WriteAllText(FilePath, json);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still hold the folder briefly; it's only temp data.
        }
    }
}
