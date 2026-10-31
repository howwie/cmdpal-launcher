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
    private readonly Lock _saveLock = new();
    private string? _loadedJson;

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

    /// <summary>
    /// Applies <paramref name="edit"/> to what's in profiles.json right now (not what was last loaded, in case the
    /// file was edited by hand since) and saves it. Returns null on success, or why it couldn't be done.
    /// </summary>
    public string? Update(Func<ProfilesFile, string?> edit)
    {
        lock (_saveLock)
        {
            ProfilesFile file;
            try
            {
                file = JsonSerializer.Deserialize(ReadWithRetry(FilePath), ProfilesJsonContext.Default.ProfilesFile) ?? new ProfilesFile();
            }
            catch (JsonException)
            {
                return "profiles.json has an error, so the editor can't save. Fix it in a text editor first.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }

            if (edit(file) is { } error)
            {
                return error;
            }

            try
            {
                ProfileWriter.Save(FilePath, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Couldn't save profiles.json: {ex.Message}";
            }

            // Refresh now rather than waiting for the file watcher, so pages show the change straight away.
            Reload();
            return null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) => _debounce.Change(250, Timeout.Infinite);

    private void Reload()
    {
        if (Load())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reads the file. Returns false when its content is the same as last time: the watcher also reports the
    /// editor's own saves, which were already loaded, and redrawing for them would close sections the user opened.
    /// </summary>
    private bool Load()
    {
        string? json = null;
        try
        {
            json = ReadWithRetry(FilePath);
            if (json == _loadedJson)
            {
                return false;
            }

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Profiles = [];
            Error = ex.Message;
        }

        _loadedJson = json;
        return true;
    }

    private static string ReadWithRetry(string filePath)
    {
        // The file may still be locked by the editor that just saved it, or be part-way through being replaced.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5 && File.Exists(filePath))
            {
                Thread.Sleep(100);
            }
        }
    }
}
