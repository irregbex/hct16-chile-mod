<#
.SYNOPSIS
  HCT 1.6 v2.2.2 - builds the double-click install package into <root>\Installer\ .

    INSTALL.cmd / UNINSTALL.cmd / README.txt       (from package\)
    tools\install.ps1                              the installer logic (package\install.ps1)
    files\                                         what the installer adds to the game folder:
      winhttp.dll, doorstop_config.ini, .doorstop_version, BepInEx\core\   BepInEx 5.4.23.5
      BepInEx\plugins\HCT15\HCT15.dll                                      the plugin
      HCT15Content\, HCT15Music\                                           hard links to <root>\Content\
  The package carries the mod's own content only, plus BepInEx (LGPL-2.1) and HarmonyX (MIT).
#>
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$version = Split-Path $src -Parent
if (-not $SkipBuild) { & (Join-Path $src 'Install-Plugin.ps1') | Out-Null }
$dll = Join-Path $src 'bin\HCT15.dll'
if (-not (Test-Path $dll)) { throw 'bin\HCT15.dll missing - build first' }
$bepin = Join-Path $src 'tools\BepInEx_x86'
$out = Join-Path $version 'Installer'
$files = Join-Path $out 'files'
if (Test-Path $files) { [System.IO.Directory]::Delete($files, $true) }
New-Item -ItemType Directory -Force (Join-Path $out 'tools'), (Join-Path $files 'BepInEx\plugins\HCT15') | Out-Null

Copy-Item (Join-Path $src 'package\INSTALL.cmd'), (Join-Path $src 'package\UNINSTALL.cmd'), (Join-Path $src 'package\README.txt') $out -Force
Copy-Item (Join-Path $src 'package\install.ps1') (Join-Path $out 'tools') -Force
Copy-Item (Join-Path $bepin 'winhttp.dll'), (Join-Path $bepin 'doorstop_config.ini'), (Join-Path $bepin '.doorstop_version') $files
& robocopy (Join-Path $bepin 'BepInEx\core') (Join-Path $files 'BepInEx\core') @('/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP') | Out-Null
Copy-Item $dll (Join-Path $files 'BepInEx\plugins\HCT15')

function Link-Tree([string]$from, [string]$to, [string[]]$skip) {
  Get-ChildItem -LiteralPath $from -Recurse -File | ForEach-Object {
    if ($skip -contains $_.Name) { return }
    $dst = Join-Path $to $_.FullName.Substring($from.Length).TrimStart('\')
    New-Item -ItemType Directory -Force (Split-Path $dst -Parent) | Out-Null
    try { New-Item -ItemType HardLink -Path $dst -Target $_.FullName | Out-Null } catch { Copy-Item -LiteralPath $_.FullName $dst }
  }
}
# the machine-written dumps of a previous run stay out; hct15.settings.json travels (the player's own tuning)
Link-Tree (Join-Path $version 'Content\HCT15Content') (Join-Path $files 'HCT15Content') @('_container_dump.txt', '_donors.json', '_bubbles.json')
Link-Tree (Join-Path $version 'Content\HCT15Music') (Join-Path $files 'HCT15Music') @()

$n = (Get-ChildItem -LiteralPath $files -Recurse -File).Count
$size = (Get-ChildItem -LiteralPath $files -Recurse -File | Measure-Object Length -Sum).Sum / 1GB
Write-Host ("Installer: {0}  ({1} files, {2:0.00} GB, HCT15.dll md5 {3})" -f $out, $n, $size, (Get-FileHash -Algorithm MD5 $dll).Hash.ToLowerInvariant())
$global:LASTEXITCODE = 0
