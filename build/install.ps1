<#
.SYNOPSIS
    Installs (or removes) the Launch Profiles extension for PowerToys Command Palette.

.DESCRIPTION
    Copies the package in the "app" folder next to this script to
    %LOCALAPPDATA%\Programs\CmdPalLauncher, registers it with Windows, and restarts Command Palette
    so it loads the extension. Requires Developer Mode (Settings > System > For developers),
    because the package isn't signed.

    Your profiles (%LOCALAPPDATA%\CmdPalLauncher\profiles.json) are never touched.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    # The package folder to install from. Defaults to the "app" folder next to this script.
    [string] $PackageDir,

    # Register the package where it is instead of copying it first (used by deploy.ps1).
    [switch] $InPlace,

    [switch] $Uninstall,

    # Don't restart Command Palette afterwards.
    [switch] $NoRestart
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 hasn't set $PSScriptRoot yet when parameter defaults are evaluated.
if (-not $PackageDir) {
    $PackageDir = Join-Path $PSScriptRoot 'app'
}

$packageName = 'HowWieYoong.CmdPalLauncher'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\CmdPalLauncher'

# Earlier builds used this package name; remove them so Command Palette doesn't show both.
$oldPackageNames = @('CmdPalLauncher')

function Restart-CommandPalette {
    # Command Palette adds the extension again each time the package is re-registered, without dropping
    # the old entry, so its commands pile up and "Reload" doesn't clear them. Restarting it does.
    $palette = Get-Process 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue
    if ($NoRestart) {
        Write-Host 'Restart Command Palette to load the change (Reload alone can leave duplicate commands).'
    }
    elseif ($palette) {
        $palette | Stop-Process -Force
        $palette | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        Start-Process 'shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App'
        Write-Host 'Restarted Command Palette.'
    }
}

function Remove-Packages([string[]] $names) {
    # The extension process keeps the package files locked.
    Get-Process 'CmdPalLauncher' -ErrorAction SilentlyContinue | Stop-Process -Force
    foreach ($name in $names) {
        Get-AppxPackage $name | ForEach-Object {
            Write-Host "Removing $($_.PackageFullName)"
            $_ | Remove-AppxPackage
        }
    }
}

if ($Uninstall) {
    Remove-Packages (@($packageName) + $oldPackageNames)
    if (Test-Path $installDir) {
        Remove-Item -Recurse -Force $installDir
    }

    Write-Host 'Removed the Launch Profiles extension. Your profiles were left in place.'
    Restart-CommandPalette
    return
}

$devMode = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
if ($devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw 'Developer Mode is off. Turn it on in Settings > System > For developers, then run this again.'
}

if (-not (Test-Path (Join-Path $PackageDir 'AppxManifest.xml'))) {
    throw "No package found in $PackageDir."
}

$existing = Get-AppxPackage $packageName
Remove-Packages $oldPackageNames

if ($InPlace) {
    $source = (Resolve-Path $PackageDir).Path
}
else {
    # Windows runs a registered package from its folder, so install somewhere that won't be moved or deleted.
    Get-Process 'CmdPalLauncher' -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path $installDir) {
        Remove-Item -Recurse -Force $installDir
    }

    New-Item -ItemType Directory -Force $installDir | Out-Null
    Copy-Item (Join-Path $PackageDir '*') $installDir -Recurse -Force

    # Files from a downloaded zip carry the "from the internet" mark.
    Get-ChildItem $installDir -Recurse -File | Unblock-File
    $source = $installDir
}

# A package registered from another folder has to be removed before it can move.
if ($existing -and $existing.InstallLocation -ne $source) {
    Remove-Packages @($packageName)
}

Get-Process 'CmdPalLauncher' -ErrorAction SilentlyContinue | Stop-Process -Force
Add-AppxPackage -Register (Join-Path $source 'AppxManifest.xml') -ForceUpdateFromAnyVersion -ForceApplicationShutdown
$installed = Get-AppxPackage $packageName
Write-Host "Installed $($installed.Name) $($installed.Version) from $source"

Restart-CommandPalette
