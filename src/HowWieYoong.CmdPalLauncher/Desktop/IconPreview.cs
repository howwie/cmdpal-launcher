using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using static HowWieYoong.CmdPalLauncher.Desktop.NativeMethods;

namespace HowWieYoong.CmdPalLauncher.Desktop;

/// <summary>
/// Pictures of profile icons for the editor. Adaptive Cards can't draw text in the icon font, so a Segoe Fluent
/// glyph is drawn into a PNG here; an image file is used as it is. Both become data: URIs for an Image element.
/// </summary>
internal static unsafe class IconPreview
{
    private const int Size = 48;
    private const long MaxImageBytes = 256 * 1024;

    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    /// <summary>A data: URI picturing <paramref name="icon"/>, or null when there's nothing to draw (no icon, emoji, an exe).</summary>
    public static string? DataUri(string? icon) =>
        string.IsNullOrWhiteSpace(icon) ? null : Cache.GetOrAdd(icon, Create);

    private static string? Create(string icon)
    {
        if (icon.Length == 1 && icon[0] is >= '\uE000' and <= '\uF8FF')
        {
            return Glyph(icon) is { } png ? "data:image/png;base64," + Convert.ToBase64String(png) : null;
        }

        var path = Environment.ExpandEnvironmentVariables(icon);
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => null,
        };

        try
        {
            return mime is not null && new FileInfo(path) is { Exists: true, Length: <= MaxImageBytes }
                ? $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}"
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Draws a glyph dark on a light tile, so it reads in both light and dark themes.</summary>
    private static byte[]? Glyph(string glyph)
    {
        var dc = CreateCompatibleDC(0);
        if (dc == 0)
        {
            return null;
        }

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = Size,
            biHeight = -Size, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = CreateDIBSection(dc, &header, DIB_RGB_COLORS, out var bits, 0, 0);
        nint font = 0;
        try
        {
            if (bitmap == 0)
            {
                return null;
            }

            SelectObject(dc, bitmap);
            var pixels = new Span<uint>(bits, Size * Size);
            pixels.Fill(0xFFE8E8E8); // BGRA tile colour

            font = SelectIconFont(dc);
            if (font == 0)
            {
                return null;
            }

            _ = SetBkMode(dc, TRANSPARENT);
            _ = SetTextColor(dc, 0x001A1A1A);
            var rect = new RECT { Right = Size, Bottom = Size };
            DrawText(dc, glyph, glyph.Length, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
            GdiFlush();

            // GDI leaves the alpha byte alone (0 where it drew), so make every pixel opaque, and swap BGRA to RGBA for PNG.
            var rgba = new byte[Size * Size * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                rgba[(i * 4) + 0] = (byte)(p >> 16);
                rgba[(i * 4) + 1] = (byte)(p >> 8);
                rgba[(i * 4) + 2] = (byte)p;
                rgba[(i * 4) + 3] = 0xFF;
            }

            return Png.Encode(Size, Size, rgba);
        }
        finally
        {
            if (font != 0)
            {
                DeleteObject(font);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            DeleteDC(dc);
        }
    }

    /// <summary>Segoe Fluent Icons (Windows 11), else Segoe MDL2 Assets (Windows 10). Returns 0 if neither exists.</summary>
    private static nint SelectIconFont(nint dc)
    {
        var buffer = stackalloc char[64];
        foreach (var face in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var font = CreateFont(-32, 0, 0, 0, 400, 0, 0, 0, DEFAULT_CHARSET, 0, 0, ANTIALIASED_QUALITY, 0, face);
            if (font == 0)
            {
                continue;
            }

            SelectObject(dc, font);

            // GDI quietly substitutes another font for a missing one, so check which one it picked.
            if (GetTextFace(dc, 64, buffer) > 0 && string.Equals(new string(buffer), face, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            DeleteObject(font);
        }

        return 0;
    }
}

/// <summary>A minimal PNG encoder for 8-bit RGBA images.</summary>
internal static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; // bit depth
        header[9] = 6; // colour type: RGBA
        WriteChunk(output, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                var stride = width * 4;
                for (var y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // no filter
                    zlib.Write(rgba, y * stride, stride);
                }
            }

            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        output.Write(crcBytes);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
