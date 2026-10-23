using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace HowWieYoong.CmdPalLauncher.Profiles;

/// <summary>Loads profiles.json and reloads it whenever the file changes.</summary>
internal sealed partial class ProfileStore : IDisposable
{
    public static readonly string DefaultFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CmdPalLauncher",
        "profiles.json");

    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounce;

    public ProfileStore(string? filePath = null, bool watch = true)
    {
        FilePath = filePath ?? DefaultFilePath;
        _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        EnsureFileExists(FilePath);
        Load();

        if (watch)
        {
            _watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(FilePath)!, System.IO.Path.GetFileName(FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            // Editors often save via a temp file + rename, so listen for all of these.
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public event EventHandler? Changed;

    public string FilePath { get; }

    public IReadOnlyList<LaunchProfile> Profiles { get; private set; } = [];

    /// <summary>Set when profiles.json couldn't be read or parsed.</summary>
    public string? Error { get; private set; }

    /// <summary>Creates the file with the sample profiles if it doesn't exist yet.</summary>
    public static void EnsureFileExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, SampleProfiles.Json);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) => _debounce.Change(250, Timeout.Infinite);

    private void Reload()
    {
        Load();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            var json = ReadWithRetry(FilePath);
            var file = JsonSerializer.Deserialize(json, ProfilesJsonContext.Default.ProfilesFile);
            Profiles = file?.Profiles ?? [];
            Error = null;
        }
        catch (JsonException ex)
        {
            Profiles = [];

            // The message ends with a zero-based " | LineNumber: ..." suffix, so swap it for a one-based prefix.
            var message = ex.Message.Split(" | LineNumber:")[0];
            Error = ex.LineNumber is long line ? $"Line {line + 1}: {message}" : message;
        }
        catch (IOException ex)
        {
            Profiles = [];
            Error = ex.Message;
        }
    }

    private static string ReadWithRetry(string filePath)
    {
        // The file may still be locked by the editor that just saved it.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(filePath);
            }
            catch (IOException) when (attempt < 5 && File.Exists(filePath))
            {
                Thread.Sleep(100);
            }
        }
    }
}
