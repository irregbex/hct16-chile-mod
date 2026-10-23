<#
  HCT 1.6 - step 1 of 2: copies from YOUR Horizon Chase Turbo (Steam) the assemblies the mod is compiled against.
  The repository carries no game file: the plugin is compiled against the DLLs of your own copy.
  Copies HorizonChase_Data\Managed\*.dll to Source\game-managed\ and the game's .NET 3.5 base libraries to
  Source\game-bcl\ (both folders are in .gitignore: they are never committed).

  Usage:  Prepare-References.cmd                                looks for the game in the Steam libraries
          Prepare-References.cmd -GameDir "D:\...\Horizon Chase"   uses that folder
#>
param([string]$GameDir = "")
$ErrorActionPreference = 'Stop'
$AppId = 389140
$root = $PSScriptRoot

function Find-SteamGame {
  $roots = @()
  foreach ($k in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
    try {
      $p = Get-ItemProperty -LiteralPath $k -ErrorAction Stop
      if ($p.SteamPath) { $roots += ($p.SteamPath -replace '/', '\') }
      if ($p.InstallPath) { $roots += $p.InstallPath }
    } catch { }
  }
  $libs = New-Object System.Collections.Generic.List[string]
  foreach ($r in ($roots | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $r)) { continue }
    $libs.Add($r)
    $vdf = Join-Path $r 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf) {
      foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs.Add(($m.Groups[1].Value -replace '\\\\', '\')) }
    }
  }
  foreach ($lib in ($libs | Select-Object -Unique)) {
    $acf = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
    if (-not (Test-Path -LiteralPath $acf)) { continue }
    $m = [regex]::Match((Get-Content -LiteralPath $acf -Raw), '"installdir"\s+"([^"]+)"')
    if (-not $m.Success) { continue }
    $dir = Join-Path $lib ("steamapps\common\" + $m.Groups[1].Value)
    if (Test-Path -LiteralPath (Join-Path $dir 'HorizonChase.exe')) { return $dir }
  }
  return $null
}

if (-not $GameDir) { $GameDir = Find-SteamGame }
if (-not $GameDir -or -not (Test-Path -LiteralPath (Join-Path $GameDir 'HorizonChase.exe'))) {
  throw 'Horizon Chase Turbo was not found. Give it the game folder: Prepare-References.cmd -GameDir "<folder with HorizonChase.exe>"'
}
$managed = Join-Path $GameDir 'HorizonChase_Data\Managed'
if (Test-Path -LiteralPath (Join-Path $GameDir 'HorizonChase_Data\Managed-Original')) { $managed = Join-Path $GameDir 'HorizonChase_Data\Managed-Original' }
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw "There is no Assembly-CSharp.dll in $managed" }

$gm = Join-Path $root 'Source\game-managed'
$gb = Join-Path $root 'Source\game-bcl'
New-Item -ItemType Directory -Force $gm, $gb | Out-Null
Copy-Item (Join-Path $managed '*.dll') $gm -Force
foreach ($f in 'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Runtime.Serialization.dll', 'Mono.Security.dll') {
  $p = Join-Path $managed $f
  if (Test-Path -LiteralPath $p) { Copy-Item $p $gb -Force }
}
$n = (Get-ChildItem $gm -Filter *.dll).Count
Write-Host "Done: $n assemblies from $GameDir in Source\game-managed (and the .NET base in game-bcl)."
Write-Host 'Next step: Build-Installer.cmd'
