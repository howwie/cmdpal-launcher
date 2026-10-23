namespace HowWieYoong.CmdPalLauncher.Profiles;

/// <summary>Written to profiles.json the first time the extension runs.</summary>
internal static class SampleProfiles
{
    public const string Json = """
        // Launch profiles for the Command Palette "Launch Profiles" extension.
        // Changes are picked up automatically when you save this file.
        // See README.md in the cmdpal-launcher repo for every option.
        {
          "profiles": [
            {
              "name": "Development",
              "description": "Docker, Terminal and Explorer",
              "apps": [
                {
                  "name": "Docker Desktop",
                  "path": "%ProgramFiles%\\Docker\\Docker\\Docker Desktop.exe",
                  "skipIfRunning": true
                },
                {
                  "type": "terminal",
                  "tabs": [
                    { "title": "Shell", "directory": "%USERPROFILE%" },
                    { "title": "Projects", "directory": "D:\\Development" }
                  ]
                },
                {
                  "name": "Explorer",
                  "path": "explorer.exe",
                  "arguments": "D:\\Development"
                }
              ]
            }
          ]
        }

        """;
}
