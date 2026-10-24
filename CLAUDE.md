# CLAUDE.md

A PowerToys Command Palette extension ("Launch Profiles") that starts saved sets of apps from `%LOCALAPPDATA%\CmdPalLauncher\profiles.json`, places their windows on chosen monitors, and captures open windows as profile entries. README.md documents the user-facing behaviour and every profiles.json option; keep it in sync when behaviour changes.

## Commands

```powershell
dotnet test                                   # all unit tests, from the repo root
.\deploy.ps1                                  # build + register in deploy\ + restart Command Palette
powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -NoRestart
deploy\x64\CmdPalLauncher.exe -print <profile> [profiles.json]   # dry run: show what a profile would start
deploy\x64\CmdPalLauncher.exe -windows                           # monitors + captured entry for every window
```

- No Visual Studio or Windows SDK is needed. The project gets Windows metadata from the `Microsoft.Windows.SDK.Contracts` package (`CsWinRTWindowsMetadata`). MSIX tooling switches itself off when the SDK isn't installed. Scripts and CI pass `-p:EnableMsixTooling=false` explicitly, because the GitHub runner *does* have the SDK.
- `CmdPalLauncher.exe` is a WinExe. PowerShell doesn't wait for it or capture its output unless piped (`| Out-String`) or run with `Start-Process -Wait -RedirectStandardOutput`.
- To compile while the extension is loaded, build with `-p:OutDir=<somewhere else>\`, or just use `dotnet test`. Command Palette locks the exe in `deploy\`, never in `bin\`.

## Layout

- `src/HowWieYoong.CmdPalLauncher/`: the extension. Namespace and project are `HowWieYoong.CmdPalLauncher`, but `AssemblyName` is `CmdPalLauncher`, so the exe stays `CmdPalLauncher.exe`. Keep it that way; the manifest and scripts refer to it.
  - `Profiles/`: the profiles.json model (System.Text.Json source generation, comments and trailing commas allowed) and `ProfileStore`, which watches the file.
  - `Launching/`: `AppLauncher` (start info, `wt` arguments), `WindowPlacer` (waits for new windows and moves them), `CommandLine` (argv split/quote), `TerminalCommandLine` (parses `wt` args back into tabs).
  - `Desktop/`: `IDesktop` and `Win32Desktop` (hand-written `LibraryImport` P/Invoke in `NativeMethods`), `MonitorLayout` (pure maths), `WindowCapture` (window → JSON entry), `ExplorerFolders` (Shell.Application late-bound COM).
  - `Pages/`, `Commands/`, `CmdPalLauncherCommandsProvider.cs`: the Command Palette UI.
- `test/HowWieYoong.CmdPalLauncher.Tests/`: xUnit v3 on Microsoft.Testing.Platform (opted in via `global.json`).
- `build/Build-Package.ps1`: builds a registrable package folder. `build/install.ps1` registers one; it ships in the release zips and `deploy.ps1` uses it with `-InPlace`.

## Conventions

- **Testability:** process and window access go through `IProcessRunner` and `IDesktop`. Tests use `FakeProcessRunner`, `FakeDesktop` and `TempProfilesFile` and must never start real processes or touch real windows. Keep new Win32 or process code behind these interfaces, and keep the logic pure so it can be tested.
- Hand-written Win32 structs have size checks in `NativeMethodsTests`. Add one for every new struct.
- Command IDs are prefixed `HowWieYoong.CmdPalLauncher.`. Command Palette needs stable IDs for pinning and aliases.
- The COM class ID `FE7A55CB-1E76-408F-81DB-0058AEED0E09` must match in three places: `[Guid]` in `CmdPalLauncher.cs`, and `com:Class Id` and `CreateInstance ClassId` in `Package.appxmanifest`.
- `Package.appxmanifest` keeps `$targetnametoken$`/`$targetentrypoint$` and `x-generate`; `Build-Package.ps1` fills those in and sets the version and architecture. Don't hard-code them.
- Analyzers run in `Recommended` mode, and builds should stay warning-free. Test names use `Method_Scenario` underscores (CA1707 is off in the test project only).
- Files from Microsoft's extension template (`Program.cs`, `CmdPalLauncher.cs`) keep their Microsoft MIT header. Don't add headers to new files.
- Scripts must work in Windows PowerShell 5.1. In particular, `$PSScriptRoot` is empty in `param()` defaults, so resolve paths in the script body.

## Gotchas

- **Command Palette duplicates the extension on every re-registration**, and **Reload** doesn't clear it. That's why `deploy.ps1` and `install.ps1` restart `Microsoft.CmdPal.UI`. Its log is at `%LOCALAPPDATA%\Microsoft\PowerToys\CmdPal\Logs\<version>\Log_<date>.log`. "Started extension HowWieYoong.CmdPalLauncher…" should appear once per start.
- The manifest disables file and registry write virtualization (`unvirtualizedResources`), so apps started by the extension write to the real AppData.
- `ExplorerFolders` uses late-bound COM through reflection. It works in the JIT builds the scripts produce, and returns null if late binding isn't available (e.g. trimmed or AOT builds).

## Releases

**Every push to `main` publishes a public GitHub release** (`.github/workflows/build.yml`: tests, then x64 and ARM64 zips, tagged `v0.1.<run number>`). The exception is a push that only changes Markdown files or `LICENSE`, which doesn't run the workflow at all. Pull requests only run the tests. Don't push code changes to `main` unless a release is intended, and confirm first. To push a change that shouldn't release, such as workflow-only edits, put `[skip ci]` in the commit message.

`.github/copilot-instructions.md`, `.github/instructions/` and `.github/skills/` are the template's Copilot notes. They're gitignored but are a useful reference for the Command Palette API, which ships without docs.
