<#
.SYNOPSIS
    Builds the extension from source and installs it so Command Palette loads it.

.DESCRIPTION
    Does what Visual Studio's "Deploy" does, without needing Visual Studio or the Windows SDK:
    builds a package into the deploy\ folder (build\Build-Package.ps1) and registers it there
    (build\install.ps1 -InPlace). Requires Developer Mode.

    The installed copy lives in deploy\, separate from bin\, so building and running tests
    don't collide with the copy Command Palette has loaded.

    Command Palette is then restarted so it loads the new build exactly once; use -NoRestart to skip that.

.EXAMPLE
    .\deploy.ps1
.EXAMPLE
    .\deploy.ps1 -Configuration Release
.EXAMPLE
    .\deploy.ps1 -NoRestart
.EXAMPLE
    .\deploy.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $Uninstall,

    # Don't restart Command Palette after deploying.
    [switch] $NoRestart
)

$ErrorActionPreference = 'Stop'
$install = Join-Path $PSScriptRoot 'build\install.ps1'

if ($Uninstall) {
    & $install -Uninstall -NoRestart:$NoRestart
    return
}

$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$output = Join-Path $PSScriptRoot "deploy\$platform"

# Command Palette keeps the extension running, which locks the exe we're about to overwrite.
Get-Process 'CmdPalLauncher' -ErrorAction SilentlyContinue | Stop-Process -Force

& (Join-Path $PSScriptRoot 'build\Build-Package.ps1') -Configuration $Configuration -Platform $platform -OutputDir $output
& $install -PackageDir $output -InPlace -NoRestart:$NoRestart
