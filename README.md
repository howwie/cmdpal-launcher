# Launch Profiles for Command Palette

[![Build](https://github.com/howwie/cmdpal-launcher/actions/workflows/build.yml/badge.svg)](https://github.com/howwie/cmdpal-launcher/actions/workflows/build.yml)

A PowerToys Command Palette extension that starts a saved set of apps, each with its own arguments and on its own monitor, in one go. For example, a "Development" profile might start Docker Desktop, open Windows Terminal with titled tabs, and open Explorer at your projects folder.

## Install

Requirements:

- [PowerToys](https://github.com/microsoft/PowerToys) with Command Palette
- **Developer Mode** (Settings → System → For developers). The package isn't signed, and Windows only installs unsigned packages in Developer Mode.

Then:

1. Download the zip for your PC from the [latest release](https://github.com/howwie/cmdpal-launcher/releases/latest): **x64** for most PCs, **arm64** for Windows on Arm.
2. Extract it, open PowerShell in the extracted folder, and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

The extension is copied to `%LOCALAPPDATA%\Programs\CmdPalLauncher`, and Command Palette restarts with the **Launch Profiles** commands. To update, install a newer release the same way. To remove it, run `install.ps1 -Uninstall`. Your profiles are kept either way.

## Build from source

You need the .NET 10 SDK. You don't need Visual Studio or the Windows SDK.

```powershell
.\deploy.ps1
```

This builds the extension into `deploy\`, registers it there, and restarts Command Palette. Run it again after every code change. Other options:

- `.\deploy.ps1 -Configuration Release` builds in Release mode.
- `.\deploy.ps1 -Uninstall` removes the extension.
- `.\deploy.ps1 -NoRestart` skips the restart. Restart Command Palette yourself before using the extension.

**Why it restarts Command Palette:** Command Palette adds the extension again every time the package is re-registered, without removing the old entry, and **Reload** doesn't clear them. Each command would then show up once per deploy.

### If running scripts is disabled

If PowerShell reports that `deploy.ps1 cannot be loaded because running scripts is disabled on this system`, the execution policy is blocking scripts. The same applies to `install.ps1`. Pick one of these:

**Bypass it for a single run.** This changes nothing on the system:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy.ps1
powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -Configuration Release
powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -Uninstall
```

With PowerShell 7, use `pwsh` in place of `powershell`.

**Bypass it for the current terminal window.** The setting ends when you close the window:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\deploy.ps1
```

**Allow local scripts for your user account.** This setting is permanent and doesn't need admin rights:

```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
.\deploy.ps1
```

`RemoteSigned` still blocks scripts downloaded from the internet. If you downloaded this repo as a zip file, unblock the script once with `Unblock-File .\deploy.ps1`.

If your organisation sets the policy through Group Policy, it overrides all three options. `Get-ExecutionPolicy -List` shows this as a value in the `MachinePolicy` or `UserPolicy` row. In that case, ask your IT admin.

## Use

| Command | What it does |
|---|---|
| **Launch Profiles** | Lists your profiles; the details pane shows what each one starts |
| **Launch &lt;profile&gt;** | Each profile is also a top-level command, so typing "dev" is enough |
| **Edit launch profiles** | Opens `profiles.json` |
| **Capture window position** | Lists your monitors and open windows. Selecting a window copies a complete app entry that would start that app again and put its window back where it is now (see [Capturing an open window](#capturing-an-open-window)) |

From a shortcut or script. The exe is in `%LOCALAPPDATA%\Programs\CmdPalLauncher` for a release install, or `deploy\x64` when built from source:

```powershell
CmdPalLauncher.exe -launch Development                 # start a profile
CmdPalLauncher.exe -print Development                  # show the commands without running them
CmdPalLauncher.exe -launch Development D:\other.json   # use a different profiles file
CmdPalLauncher.exe -windows                            # list monitors, and a captured app entry for every open window
```

`CmdPalLauncher.exe` is a GUI app, so PowerShell doesn't wait for its output unless you pipe it, e.g. `.\CmdPalLauncher.exe -windows | Out-String`.

## Develop

```
src/HowWieYoong.CmdPalLauncher/          the extension (builds CmdPalLauncher.exe)
test/HowWieYoong.CmdPalLauncher.Tests/   xUnit v3 unit tests
build/Build-Package.ps1                  builds a folder that Windows can register as a package
build/install.ps1                        registers a package folder; shipped in the release zips
deploy.ps1                               Build-Package + install, for local development
.github/workflows/build.yml              CI: tests, then releases from main
```

Run the tests from the repo root:

```powershell
dotnet test
```

The tests cover:

- building Windows Terminal arguments and creating start info
- launch order, delays and failures
- loading and reloading `profiles.json`
- the items on the profiles page
- monitor numbering and lookup, window matching and timeouts, and the position maths
- capturing windows as app entries: command-line splitting and startup-flag removal, rebuilding Terminal tabs from `wt` arguments, and the Explorer, Terminal and Store app cases
- the layout of the hand-written Win32 structs

They don't start processes or touch real windows. `AppLauncher` makes its process calls through `IProcessRunner`, and `WindowPlacer` goes through `IDesktop`; the tests replace both with fakes. Some things still have to be checked by hand after deploying: whether Command Palette loads the extension, and whether real apps and Windows Terminal end up where the settings say.

`deploy.ps1` installs into `deploy\`, not `bin\`. Building and running the tests therefore never collide with the copy Command Palette has loaded.

### Releases

[`.github/workflows/build.yml`](.github/workflows/build.yml) runs the tests on every push and pull request. When `main` is updated and the tests pass, it also:

1. Builds x64 and ARM64 packages in Release mode with `build/Build-Package.ps1`.
2. Zips each one with `install.ps1` and the licence.
3. Publishes them as a GitHub release.

Releases are versioned `0.1.<workflow run number>`, e.g. tag `v0.1.42`. To move to `0.2` or `1.0`, change `VERSION` in the workflow.

Pushes that only change Markdown files or `LICENSE` don't run the workflow, so they don't create a release. To push any other change without releasing it, put `[skip ci]` in the commit message.

## profiles.json

The file is at `%LOCALAPPDATA%\CmdPalLauncher\profiles.json` and is created with a sample on first run. It's reloaded whenever you save it, and it may contain `//` comments and trailing commas. Environment variables such as `%USERPROFILE%` are expanded in paths, directories, arguments and commands.

```jsonc
{
  "profiles": [
    {
      "name": "Development",
      "description": "Docker, Terminal and Explorer",   // optional; defaults to the list of apps
      "icon": "\uE943",                                  // optional; Segoe Fluent glyph, emoji or image path
      "apps": [
        {
          "name": "Docker Desktop",
          "path": "%ProgramFiles%\\Docker\\Docker\\Docker Desktop.exe",
          "skipIfRunning": true
        },
        {
          "type": "terminal",
          "tabs": [
            { "title": "API", "directory": "D:\\Development\\api", "color": "#0078D4" },
            { "title": "Web", "directory": "D:\\Development\\web", "profile": "PowerShell",
              "command": "pwsh -NoExit -Command npm run dev" }
          ]
        },
        { "name": "Explorer", "path": "explorer.exe", "arguments": "D:\\Development" },
        { "path": "code", "arguments": ".", "workingDirectory": "D:\\Development\\api", "delayMs": 1000 }
      ]
    }
  ]
}
```

### App options

| Key | Description |
|---|---|
| `type` | `"app"` (default) or `"terminal"` |
| `name` | Display name for the details pane and error messages |
| `path` | Anything the Run dialog accepts: an exe, an app alias (`wt`, `code`), a document or a folder. Optional for `terminal` (defaults to `wt.exe`) |
| `arguments` | Command-line arguments. For `terminal`, these go after the generated tab arguments |
| `workingDirectory` | Starting directory |
| `runAsAdmin` | `true` shows a UAC prompt and runs the app elevated |
| `skipIfRunning` | Don't start the app if a process with the same name is already running |
| `delayMs` | Wait this many milliseconds before starting this app |

### Terminal options

| Key | Description |
|---|---|
| `window` | The Windows Terminal window to use. Default `"new"`; `"last"` adds tabs to the most recent window; any other name targets or creates a named window |
| `tabs[].title` | Tab title. It stays fixed; the shell can't rename it |
| `tabs[].directory` | Starting directory |
| `tabs[].profile` | Windows Terminal profile name, e.g. `"PowerShell"` or `"Ubuntu"` |
| `tabs[].command` | Command line to run instead of the profile's shell. To keep the shell open afterwards, wrap it, e.g. `pwsh -NoExit -Command ...` |
| `tabs[].color` | Tab colour, e.g. `"#0078D4"` |

### Window placement

Any app, including `terminal`, can be put on a specific monitor:

```jsonc
{
  "name": "Explorer",
  "path": "explorer.exe",
  "arguments": "D:\\Development",
  "monitor": 2,                                   // or "primary", or a name like "DELL U2515H"
  "windowState": "normal",                        // or "maximized"
  "bounds": { "x": 0, "y": 0, "width": 1280, "height": 1380 }
}
```

| Key | Description |
|---|---|
| `monitor` | `"primary"`; a number counted **left to right** (then top to bottom), starting at 1; or the monitor's model name, e.g. `"DELL U2515H"`. A unique part of a name such as `"U2515H"` also works, as does a device name like `"DISPLAY2"`. A name keeps working if you rearrange your monitors; use a number if two monitors are the same model |
| `windowState` | `"normal"` or `"maximized"` |
| `bounds` | Position and size in pixels, relative to the top-left of the monitor's work area (the screen minus the taskbar). Leave out `width`/`height`, or set them to 0, to keep the window's own size. Without `bounds`, the window is centred on the monitor at its own size |
| `windowProcess` | The process that shows the window, when it isn't the one in `path`. Examples: a launcher that starts a separate UI process, or a document or folder opened by another app. Defaults to the name of the exe in `path`, or `WindowsTerminal` for `terminal` |
| `windowTitle` | Only place a window whose title contains this text. Useful when an app shows a splash screen first |
| `windowTimeoutMs` | How long to wait for the window to appear. Default `15000` |

Without `monitor`, `bounds` are relative to the primary monitor, and `"maximized"` alone maximizes the window on whichever monitor it opened on.

**Getting the values.** Use [Capture window position](#capturing-an-open-window). The same page lists your monitors with their numbers and names.

**How it works.** After starting an app, the extension waits for a *new* window from the expected process and moves it. Windows that were already open are never moved. Some apps reposition themselves after they appear, so the window is placed a second time about 1.5 seconds later. If two apps in a profile use the same process (for example two Explorer windows), their windows are assigned in launch order. For Windows Terminal, the position is also passed as `--pos`/`--maximized`, so the window opens in place.

**Limitations:**

- Apps started with `runAsAdmin` can't be moved: Windows doesn't let a normal process move an elevated app's window.
- Apps skipped by `skipIfRunning` aren't moved, because no new window opens.
- A Terminal entry with `window` set to anything other than `"new"` isn't placed, because its tabs go into an existing window.
- If an app reuses an existing window instead of opening one, there's nothing to place. A toast then says the window didn't appear.

If any app fails to start, or its window can't be placed, a toast lists which ones and why. The other apps still start.

### Capturing an open window

Arrange a window the way you want it, run **Capture window position** in Command Palette, and select the window. A complete app entry is copied to the clipboard, ready to paste into a profile's `apps` list:

```jsonc
{
  "name": "Visual Studio Code",
  "path": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe",
  "arguments": "\"D:\\Development\\api\"",
  "monitor": "DELL U2515H",
  "windowState": "normal",
  "bounds": { "x": 0, "y": 0, "width": 1280, "height": 1380 }
}
```

To add placement to an entry you already have, use **Copy placement settings only** from the item's More menu. It copies just `windowProcess`, `monitor`, `windowState` and `bounds`. The details pane shows the copied entry, notes about anything that couldn't be captured, and the full command line the app was started with. `CmdPalLauncher.exe -windows` prints the same information.

How each part is found:

| Part | Where it comes from |
|---|---|
| `path` | The exe of the process that owns the window. Common folders are replaced with variables such as `%LOCALAPPDATA%`, `%ProgramFiles%` and `%USERPROFILE%`, so the entry works on other machines |
| `arguments` | The command line the app was started with. Flags that apps get when they start in the background, such as `--autostart`, `--startup`, `--minimized` and `--tray`, are left out and listed in the notes |
| `name` | The exe's description, e.g. "Visual Studio Code"; otherwise the process name |
| `windowProcess` | Only included when the window's process isn't the one `path` would start |

Some apps are handled specially:

- **File Explorer:** explorer.exe's command line doesn't say which folder is open, so the folder is read from Explorer itself and used as `arguments`. With several tabs, it uses the tab shown in the window title. Places without a folder path, such as Home or This PC, get no `arguments`.
- **Windows Terminal:** captured as `"type": "terminal"`. Terminal keeps the `wt` command line it was started with, so its tabs (title, folder, profile, command, colour) are rebuilt from that. Tabs or windows opened after Terminal started aren't included, and split panes are left out.
- **Store apps** (WhatsApp, Claude, Calculator…): their exe inside `WindowsApps` can't be started directly, so `path` becomes `shell:AppsFolder\<app ID>`, without arguments. For older UWP apps hosted by `ApplicationFrameHost`, the app inside the frame is used.

What can't be captured:

- **The working directory.** Windows only records the folder a process is in now, not the one it started in, and many apps change it. Add `workingDirectory` yourself if an app needs it.
- **Apps running as administrator.** Their command line can't be read from a normal process. You get an entry with an empty `path` to fill in.
- **Arguments that only made sense once,** such as a file the app was opened with. Check `arguments` before saving.

## Notes

- `deploy.ps1` builds a plain exe into `deploy\` and registers that folder as a package, which is what Visual Studio's Deploy does. If you later install Visual Studio, the solution also builds and deploys from there as usual.
- The package turns off file and registry write virtualization. Without that, apps started from the extension could have their AppData writes redirected into this package's private storage.

## Licence

[MIT](LICENSE) © HowWie Yoong. The project started from Microsoft's Command Palette extension template, which is also MIT-licensed; files from it keep Microsoft's copyright header.
