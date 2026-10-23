<#
  HCT 1.6 - step 2 of 2: builds the plugin (HCT15.dll) and puts together Installer\ with INSTALL.cmd, ready for your game.
  Needs step 1 (Prepare-References.cmd) and the .NET 10 SDK or newer (https://dotnet.microsoft.com/download).
  Downloads BepInEx 5.4.23.5 (x86) from its official GitHub release the first time.
#>
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'Source'
if (-not (Test-Path (Join-Path $src 'game-managed\Assembly-CSharp.dll'))) { throw 'Step 1 is missing: run Prepare-References.cmd' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK (10 or newer) is missing: https://dotnet.microsoft.com/download' }
& (Join-Path $src 'Install-Plugin.ps1') -BuildOnly
& (Join-Path $src 'Build-Package.ps1') -SkipBuild
Write-Host ''
Write-Host 'Done. Close the game and double-click Installer\INSTALL.cmd'
