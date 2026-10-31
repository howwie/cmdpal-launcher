<#
.SYNOPSIS
    Writes the version of the next release: the base version in version.txt, then the next patch number.

.DESCRIPTION
    The patch number is one more than the highest release tag for that base, or 0 if it has none, so failed
    builds and pull requests never leave a gap. Changing version.txt to 1.0 makes the next release 1.0.0.
    Needs the release tags (actions/checkout with fetch-depth: 0).

.EXAMPLE
    .\build\Next-Version.ps1
.EXAMPLE
    .\build\Next-Version.ps1 -Base 1.0 -Tags v0.1.4
#>
[CmdletBinding()]
param(
    # Major and minor, e.g. 1.0. Defaults to version.txt.
    [string] $Base,

    # Defaults to the repository's v* tags.
    [string[]] $Tags
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
if (-not $Base) {
    $Base = (Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
}

if ($Base -notmatch '^\d+\.\d+$') {
    throw "The base version must be two numbers, such as 1.0, not '$Base'."
}

if (-not $PSBoundParameters.ContainsKey('Tags')) {
    $Tags = @(git -C $root tag --list 'v*')
}

$releases = @($Tags | ForEach-Object {
    if ($_ -match '^v(\d+\.\d+\.\d+)$') { [version] $Matches[1] }
})

# Windows won't update an installed package to a lower version.
$latest = $releases | Sort-Object -Descending | Select-Object -First 1
if ($latest -and [version] $Base -lt [version] "$($latest.Major).$($latest.Minor)") {
    throw "version.txt is $Base, which is lower than the latest release, $latest."
}

$sameBase = @($releases | Where-Object { "$($_.Major).$($_.Minor)" -eq ([version] $Base).ToString() })
$patch = if ($sameBase.Count -gt 0) { ($sameBase | Measure-Object -Property Build -Maximum).Maximum + 1 } else { 0 }

"$Base.$patch"
