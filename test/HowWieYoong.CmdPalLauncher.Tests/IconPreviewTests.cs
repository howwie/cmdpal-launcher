using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HowWieYoong.CmdPalLauncher.Desktop;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class IconPreviewTests
{
    [Fact]
    public void Glyph_IsDrawnIntoAValidPng()
    {
        var uri = IconPreview.DataUri("\uE943");

        // Machines without Segoe Fluent Icons or Segoe MDL2 Assets (some server images) get no preview.
        if (uri is null)
        {
            return;
        }

        Assert.StartsWith("data:image/png;base64,", uri);
        var png = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
        var pixels = DecodeRgba(png, out var width, out var height);
        Assert.Equal((48, 48), (width, height));

        // The tile is light; the glyph adds dark pixels.
        Assert.Contains(Enumerable.Range(0, width * height), i => pixels[i * 4] < 0x60);
        Assert.All(Enumerable.Range(0, width * height), i => Assert.Equal(0xFF, pixels[(i * 4) + 3]));

        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "cmdpal-launcher-icon-preview.png"), png);
    }

    [Fact]
    public void DifferentGlyphs_GiveDifferentPictures()
    {
        var a = IconPreview.DataUri("\uE943");
        var b = IconPreview.DataUri("\uE756");

        if (a is not null)
        {
            Assert.NotEqual(a, b);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("🎮")]
    [InlineData("A")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"C:\does\not\exist.png")]
    public void NothingToDraw_HasNoPreview(string? icon)
    {
        Assert.Null(IconPreview.DataUri(icon));
    }

    [Fact]
    public void ImageFile_IsUsedAsItIs()
    {
        using var temp = new TempProfilesFile();
        var path = Path.Combine(Path.GetDirectoryName(temp.FilePath)!, "icon.png");
        var png = Png.Encode(1, 1, [1, 2, 3, 255]);
        File.WriteAllBytes(path, png);

        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), IconPreview.DataUri(path));
    }

    [Fact]
    public void Png_RoundTrips()
    {
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 10, 20, 30, 40];

        var decoded = DecodeRgba(Png.Encode(2, 2, rgba), out var width, out var height);

        Assert.Equal((2, 2), (width, height));
        Assert.Equal(rgba, decoded);
    }

    /// <summary>Decodes the PNGs <see cref="Png"/> writes (8-bit RGBA, no filters), checking the signature and chunk CRCs.</summary>
    private static byte[] DecodeRgba(byte[] png, out int width, out int height)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        width = height = 0;
        using var idat = new MemoryStream();

        for (var offset = 8; offset < png.Length;)
        {
            var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png[(offset + 8)..(offset + 8 + length)];
            var crc = (uint)((png[offset + 8 + length] << 24) | (png[offset + 9 + length] << 16) | (png[offset + 10 + length] << 8) | png[offset + 11 + length]);
            Assert.Equal(Crc32(png[(offset + 4)..(offset + 8 + length)]), crc);

            if (type == "IHDR")
            {
                width = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
                height = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
                Assert.Equal((8, 6), (data[8], data[9]));
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }

            offset += 12 + length;
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var rows = raw.ToArray();

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, rows[y * ((width * 4) + 1)]);
            Array.Copy(rows, (y * ((width * 4) + 1)) + 1, pixels, y * width * 4, width * 4);
        }

        return pixels;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }
}
