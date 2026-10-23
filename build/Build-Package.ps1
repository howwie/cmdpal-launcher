<#
.SYNOPSIS
    Builds the extension into a folder that Windows can register as a package.

.DESCRIPTION
    Builds a plain exe (no Windows SDK needed), then writes AppxManifest.xml and the logo assets next to it.
    The folder can be registered with install.ps1, which is what deploy.ps1 and the release zips do.

.EXAMPLE
    .\build\Build-Package.ps1 -OutputDir artifacts\x64
.EXAMPLE
    .\build\Build-Package.ps1 -Configuration Release -Platform ARM64 -Version 0.1.42 -OutputDir artifacts\arm64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDir,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),

    # Three parts, e.g. 0.1.42. Windows packages need four, so ".0" is added for the manifest.
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\HowWieYoong.CmdPalLauncher'
$exeName = 'CmdPalLauncher'

New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

dotnet build (Join-Path $project 'HowWieYoong.CmdPalLauncher.csproj') -c $Configuration -p:Platform=$Platform -p:EnableMsixTooling=false -p:Version=$Version -p:OutDir="$OutputDir\" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# Fill in what MSIX tooling normally would. x-generate needs a resources.pri, which this build doesn't make.
[xml] $manifest = Get-Content (Join-Path $project 'Package.appxmanifest') -Raw
$manifest.Package.Identity.SetAttribute('Version', "$Version.0")
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $Platform.ToLowerInvariant())
$manifest.Package.Resources.Resource.SetAttribute('Language', 'en-us')
$application = $manifest.Package.Applications.Application
$application.SetAttribute('Executable', "$exeName.exe")
$application.SetAttribute('EntryPoint', 'Windows.FullTrustApplication')
$manifest.Save((Join-Path $OutputDir 'AppxManifest.xml'))

# Without resources.pri, Windows can't resolve "Logo.png" to "Logo.scale-200.png", so copy unqualified names too.
$assets = Join-Path $OutputDir 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
Get-ChildItem (Join-Path $project 'Assets') -File | ForEach-Object {
    Copy-Item $_.FullName $assets -Force
    Copy-Item $_.FullName (Join-Path $assets ($_.Name -replace '\.scale-\d+', '')) -Force
}

Write-Host "Built $Platform package $Version in $OutputDir"
