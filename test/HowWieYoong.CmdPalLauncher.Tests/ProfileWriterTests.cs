using System.IO;
using System.Text.Json;
using HowWieYoong.CmdPalLauncher.Profiles;
using HowWieYoong.CmdPalLauncher.Tests.Helpers;

namespace HowWieYoong.CmdPalLauncher.Tests;

public class ProfileWriterTests
{
    [Fact]
    public void Serialize_LeavesOutUnsetValues()
    {
        var file = new ProfilesFile { Profiles = [new LaunchProfile { Name = "Dev", Apps = [new AppEntry { Path = "code" }] }] };

        var json = ProfileWriter.Serialize(file);

        Assert.Equal(
            """
            {
              "profiles": [
                {
                  "name": "Dev",
                  "apps": [
                    {
                      "path": "code"
                    }
                  ]
                }
              ]
            }

            """.ReplaceLineEndings("\n"),
            json.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Serialize_KeepsPathsAndSymbolsReadable()
    {
        var file = new ProfilesFile { Profiles = [new LaunchProfile { Name = "C++ & 'tools'", Apps = [new AppEntry { Path = @"C:\Program Files\App\app.exe" }] }] };

        var json = ProfileWriter.Serialize(file);

        Assert.Contains("\"C++ & 'tools'\"", json);
        Assert.Contains(@"""C:\\Program Files\\App\\app.exe""", json);
    }

    [Fact]
    public void RoundTrip_KeepsEverySettingAndUnknownProperties()
    {
        const string Original = """
            {
              "profiles": [
                {
                  "name": "Dev",
                  "futureProfileSetting": { "a": 1 },
                  "apps": [
                    {
                      "type": "terminal",
                      "window": "last",
                      "monitor": 2,
                      "windowState": "maximized",
                      "bounds": { "x": -5, "y": 10, "width": 800, "height": 600, "z": 3 },
                      "tabs": [ { "title": "API", "directory": "D:\\api", "futureTabSetting": true } ],
                      "futureAppSetting": "kept"
                    },
                    { "path": "a.exe", "monitor": "DELL U2515H", "runAsAdmin": true, "skipIfRunning": true, "delayMs": 500, "windowTimeoutMs": 3000 }
                  ]
                }
              ],
              "futureRootSetting": [1, 2]
            }
            """;

        var file = JsonSerializer.Deserialize(Original, ProfilesJsonContext.Default.ProfilesFile)!;
        var saved = ProfileWriter.Serialize(file);

        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(Original).RootElement, JsonDocument.Parse(saved).RootElement), saved);
    }

    [Fact]
    public void Save_KeepsThePreviousVersionAsABackup()
    {
        using var temp = new TempProfilesFile("// my comment\n{ \"profiles\": [] }");

        ProfileWriter.Save(temp.FilePath, new ProfilesFile { Profiles = [new LaunchProfile { Name = "New" }] });

        Assert.Equal("// my comment\n{ \"profiles\": [] }", File.ReadAllText(ProfileWriter.BackupPath(temp.FilePath)));
        Assert.Contains("\"New\"", File.ReadAllText(temp.FilePath));
        Assert.False(File.Exists(temp.FilePath + ".tmp"));
    }

    [Fact]
    public void Clone_IsADeepCopy()
    {
        var app = new AppEntry { Type = "terminal", Tabs = [new TerminalTab { Title = "A" }], Bounds = new WindowBounds { X = 1 } };

        var copy = ProfileWriter.Clone(app);
        copy.Tabs![0].Title = "B";
        copy.Bounds!.X = 2;

        Assert.Equal("A", app.Tabs[0].Title);
        Assert.Equal(1, app.Bounds.X);
    }
}
