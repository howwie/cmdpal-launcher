<#
.SYNOPSIS
    Writes the notes for a release: the commits since the previous release, then how to install.

.DESCRIPTION
    GitHub's generated notes only list merged pull requests, so changes pushed straight to main wouldn't
    appear. This lists every commit since the latest release tag instead (all commits for the first release).
    Run it before the new tag exists. Needs the full history and tags (actions/checkout with fetch-depth: 0).

.EXAMPLE
    .\build\Release-Notes.ps1 -Version 0.1.3 | Set-Content notes.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

# The newest release tag reachable from this commit, e.g. v0.1.2.
$previous = git describe --tags --abbrev=0 --match 'v*' 2>$null
$range = if ($LASTEXITCODE -eq 0 -and $previous) { "$previous..HEAD" } else { 'HEAD' }

# Subjects only; "[skip ci]" is a note to GitHub Actions, not part of the change.
$commits = @(git log $range --no-merges --format='%s (%h)') |
    ForEach-Object { '- ' + ($_ -replace '\s*\[skip ci\]', '') }

$changes = if ($commits.Count -gt 0) { $commits -join "`n" } else { '- No changes since the previous release.' }
$since = if ($previous) { " since $previous" } else { '' }

@"
## What's changed$since

$changes

## Install

1. Turn on **Developer Mode** (Settings > System > For developers).
2. Download the zip for your PC: **x64** for most PCs, **arm64** for Windows on Arm.
3. Extract it, open PowerShell in the extracted folder and run:

   ``````powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ``````

Command Palette restarts and shows the **Launch Profiles** commands. To update, install a newer release the same way; your profiles are kept. To remove the extension, run the same command with ``-Uninstall``.
"@
