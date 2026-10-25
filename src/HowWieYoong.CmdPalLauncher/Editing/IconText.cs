using System.Globalization;
using System.Text.RegularExpressions;

namespace HowWieYoong.CmdPalLauncher.Editing;

/// <summary>
/// Segoe Fluent icons are private-use characters, which a text box shows as an empty box because it doesn't use
/// the icon font. The forms show them as codes such as \uE943 instead, and read codes back as the character.
/// </summary>
internal static partial class IconText
{
    /// <summary>The icon as it should appear in a text box: a glyph becomes "\uE943"; anything else is unchanged.</summary>
    public static string? ToEditable(string? icon) =>
        icon is { Length: 1 } && IsPrivateUse(icon[0]) ? $"\\u{(int)icon[0]:X4}" : icon;

    /// <summary>Reads "\uE943", "U+E943", "0xE943" or a bare "E943" as the glyph; anything else is returned as typed.</summary>
    public static string? FromEditable(string? text)
    {
        if (text is null || GlyphCode().Match(text.Trim()) is not { Success: true } match)
        {
            return text;
        }

        var code = int.Parse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return IsPrivateUse((char)code) ? ((char)code).ToString() : text;
    }

    private static bool IsPrivateUse(char c) => c is >= '\uE000' and <= '\uF8FF';

    [GeneratedRegex(@"^(?:\\u|U\+|0x)?(?<hex>[0-9A-Fa-f]{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex GlyphCode();
}
