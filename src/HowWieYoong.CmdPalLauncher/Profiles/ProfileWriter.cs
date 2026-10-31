using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HowWieYoong.CmdPalLauncher.Profiles;

/// <summary>Writes profiles.json for the editor.</summary>
internal static class ProfileWriter
{
    /// <summary>How many previous versions are kept. The editor saves on every button, so one wouldn't last long.</summary>
    public const int BackupCount = 10;

    private static readonly JsonSerializerOptions Options = new(ProfilesJsonContext.Default.Options)
    {
        WriteIndented = true,

        // Leaves out false, 0 and null, so saved entries only show what was set.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,

        // Keeps characters like + and ' readable instead of +.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = ProfilesJsonContext.Default,
    };

    public static string Serialize(ProfilesFile file) =>
        JsonSerializer.Serialize(file, Options.GetTypeInfo(typeof(ProfilesFile))) + "\n";

    /// <summary>A deep copy, e.g. for duplicating a profile or app.</summary>
    public static T Clone<T>(T value)
        where T : class =>
        (T)JsonSerializer.Deserialize(JsonSerializer.Serialize(value, Options.GetTypeInfo(typeof(T))), Options.GetTypeInfo(typeof(T)))!;

    /// <summary>
    /// Saves <paramref name="file"/> to <paramref name="path"/>. Previous versions are kept first (see
    /// <see cref="KeepBackups"/>), because rewriting the file drops any comments in it.
    /// </summary>
    public static void Save(string path, ProfilesFile file)
    {
        if (File.Exists(path))
        {
            KeepBackups(path);
        }

        // Write to a temp file and swap it in, so a failed write never leaves a half-written profiles.json.
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(file));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The backup <paramref name="age"/> saves before the latest one: profiles.json.bak, then .bak1, .bak2 and so on.</summary>
    public static string BackupPath(string path, int age = 0) =>
        age == 0 ? path + ".bak" : string.Create(CultureInfo.InvariantCulture, $"{path}.bak{age}");

    /// <summary>The last version that wasn't written by the editor, which is the one with the user's comments.</summary>
    public static string HandWrittenBackupPath(string path) => path + ".handwritten.bak";

    private static void KeepBackups(string path)
    {
        if (IsHandWritten(File.ReadAllText(path)))
        {
            File.Copy(path, HandWrittenBackupPath(path), overwrite: true);
        }

        for (var age = BackupCount - 1; age > 0; age--)
        {
            if (File.Exists(BackupPath(path, age - 1)))
            {
                File.Move(BackupPath(path, age - 1), BackupPath(path, age), overwrite: true);
            }
        }

        File.Copy(path, BackupPath(path), overwrite: true);
    }

    /// <summary>Whether saving <paramref name="json"/> would change more than its content, e.g. drop comments.</summary>
    private static bool IsHandWritten(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ProfilesJsonContext.Default.ProfilesFile) is not { } file || Serialize(file) != json;
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
