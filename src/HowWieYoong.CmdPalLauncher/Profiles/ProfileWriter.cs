using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HowWieYoong.CmdPalLauncher.Profiles;

/// <summary>Writes profiles.json for the editor.</summary>
internal static class ProfileWriter
{
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
    /// Saves <paramref name="file"/> to <paramref name="path"/>. The previous version is kept as
    /// profiles.json.bak first, because rewriting the file drops any comments in it.
    /// </summary>
    public static void Save(string path, ProfilesFile file)
    {
        if (File.Exists(path))
        {
            File.Copy(path, BackupPath(path), overwrite: true);
        }

        // Write to a temp file and swap it in, so a failed write never leaves a half-written profiles.json.
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(file));
        File.Move(temp, path, overwrite: true);
    }

    public static string BackupPath(string path) => path + ".bak";
}
