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
  - `Profiles/`: the profiles.json model (System.Text.Json source generation, comments and trailing commas allowed) and `ProfileStore`, which watches the file. `ProfileEditing` holds the editor's operations: pure functions on `ProfilesFile` that return null or an error message. `ProfileWriter` saves the file. Comments are lost, so it first keeps the last 10 versions (`profiles.json.bak`, `.bak1`...) and the last hand-written one (`profiles.json.handwritten.bak`). Every model class has `[JsonExtensionData]` so unknown settings survive a save.
  - `Launching/`: `AppLauncher` (start info, `wt` arguments), `WindowPlacer` (waits for new windows and moves them), `CommandLine` (argv split/quote), `TerminalCommandLine` (parses `wt` args back into tabs).
  - `Desktop/`: `IDesktop` and `Win32Desktop` (hand-written `LibraryImport` P/Invoke in `NativeMethods`), `MonitorLayout` (pure maths), `WindowCapture` (window → JSON entry), `ExplorerFolders` (Shell.Application late-bound COM), `TerminalTabNames` (UI Automation through raw COM vtables, because its interfaces can't be late-bound).
  - `Editing/`: the editor's forms, kept pure.
    - `Card` builds Adaptive Card JSON with the current values filled in (no `DataJson` binding). `Card.Section` prefixes input ids and drops `isRequired`, because a client-side required field would block every button on the form; the extension validates instead.
    - `FormValues` reads submissions; `.Section(prefix)` reads one section.
    - `AppForm`/`TabForm`/`ProfileForm` each have `Fields` (reusable inputs) and `Apply`/`Read`.
    - `ProfileCard` is the whole-profile form: `p_` holds the profile settings, `a{i}_` each app, and `a{i}_t{j}_` each tab. Every button is an `Action.Submit` whose `data` is a `ProfileCardAction`. A submission first applies all inputs (`ApplyInputs`, which skips the item being deleted), then runs the action (`Perform`).
    - `ProfileCardLayout` refuses to save if the file's app or tab counts changed since the form was drawn.
    - Opening a form and saving it unchanged must leave the file identical; `EditorFormTests` and `ProfileCardTests` check this.
  - `Pages/`, `Pages/Editor/`, `Commands/`, `CmdPalLauncherCommandsProvider.cs`: the Command Palette UI.
    - Editor changes go through `ProfileStore.Update`, which re-reads the file, applies one edit and saves. The store only raises `Changed` when the file's content differs from what it last loaded, so the watcher seeing the editor's own save doesn't redraw the form and close the sections the user opened.
    - `ProfileFormPage` (a `ContentPage`) hosts the profile form and tracks which app sections are open. Pages refresh through `PageRefresh.Watch`; `RaiseItemsChanged` is protected, hence `IRefreshable`.
    - Command Palette makes card text selectable, which swallows clicks on a `selectAction` row, so clicking on the text of a clickable row (the app headers) selects it instead of toggling. Card buttons can't stretch to full width and text drawn as images doesn't fit the look, so this is an accepted trade-off. Forms can't react to an input changing, so don't plan on fields that appear depending on another field.
    - Two SDK details: `FormContent.SubmitForm(inputs, data)` receives the pressed button's `data`, and `IconText` shows Segoe Fluent glyphs as `\uXXXX` codes, because text boxes can't draw them.
- `test/HowWieYoong.CmdPalLauncher.Tests/`: xUnit v3 on Microsoft.Testing.Platform (opted in via `global.json`).
- `build/Release-Notes.ps1`: writes a release's notes from the commits since the previous `v*` tag, so commit subjects become the changelog. Write them for users.
- `build/Next-Version.ps1`: works out the next release's version from `version.txt` and the release tags.
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
- Write Segoe Fluent glyphs as `\uXXXX` escapes (`"\uE943"`), never as the raw private-use character, which is invisible in editors and diffs. In a verbatim string (`@"..."`) `\u` isn't an escape, so `@"\uE943"` is the six characters `\uE943`; that's the form the editor shows to users.
- Adaptive Cards can't draw text in the icon font. The editor shows icons as images: `IconPreview` renders a glyph into a PNG with GDI, and that becomes a data: URI.

## Gotchas

- **Command Palette duplicates the extension on every re-registration**, and **Reload** doesn't clear it. That's why `deploy.ps1` and `install.ps1` restart `Microsoft.CmdPal.UI`. Its log is at `%LOCALAPPDATA%\Microsoft\PowerToys\CmdPal\Logs\<version>\Log_<date>.log`. "Started extension HowWieYoong.CmdPalLauncher…" should appear once per start.
- The manifest disables file and registry write virtualization (`unvirtualizedResources`), so apps started by the extension write to the real AppData.
- `ExplorerFolders` uses late-bound COM through reflection. It works in the JIT builds the scripts produce, and returns null if late binding isn't available (e.g. trimmed or AOT builds).

## Releases

**Every push to `main` publishes a public GitHub release** (`.github/workflows/build.yml`: tests, then x64 and ARM64 zips, tagged with the next version from `build/Next-Version.ps1`: `version.txt` holds major.minor, and the patch is the latest tag for it plus one, so changing it to `1.0` makes the next release `v1.0.0`). The exception is a push that only changes Markdown files or `LICENSE`, which doesn't run the workflow at all. Pull requests only run the tests. Don't push code changes to `main` unless a release is intended, and confirm first. To push a change that shouldn't release, such as workflow-only edits, put `[skip ci]` in the commit message.

`.github/copilot-instructions.md`, `.github/instructions/` and `.github/skills/` are the template's Copilot notes. They're gitignored but are a useful reference for the Command Palette API, which ships without docs.
