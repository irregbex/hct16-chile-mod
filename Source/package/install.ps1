<#
  HCT 1.6 v2.2.3 - installer / uninstaller of the mod for Horizon Chase Turbo (Steam).
  INSTALL.cmd and UNINSTALL.cmd call it; there is no need to open it by hand.

    install.ps1                     finds the game in Steam and installs
    install.ps1 -Uninstall          removes the mod (the mod's saves in HCT15Saves are kept unless you say so)
    install.ps1 -GameDir "<path>"   uses that game folder
    install.ps1 -Quiet              no questions, no windows (tests); with -Force it installs even on a non-genuine game
    install.ps1 -SteamRoot "<path>" looks for the Steam libraries there instead of in the registry (tests)

  It only ADDS files to the game folder - no game file is ever changed - and lists each one in HCT15-installed.txt,
  which is exactly what the uninstaller removes.
#>
param(
  [string]$GameDir = "",
  [string]$SteamRoot = "",
  [switch]$Uninstall,
  [switch]$Quiet,
  [switch]$Force,
  [switch]$RemoveSaves
)
$ErrorActionPreference = 'Stop'
$here    = $PSScriptRoot
$payload = Join-Path (Split-Path $here -Parent) 'files'
$AppId   = '389140'
$Genuine = 'B474D3E71FAA0F4A48E990A9FA25DD80'
$Record  = 'HCT15-installed.txt'
$OldRecord = 'HCT15-instalado.txt'   # the record's name in releases up to 2.2.0

function Say([string]$t, [string]$color = 'Gray') { Write-Host $t -ForegroundColor $color }
function Fail([string]$t) { Say ""; Say "ERROR: $t" 'Red'; exit 1 }
function AskYes([string]$q) {
  if ($Quiet) { return $false }
  $a = Read-Host "$q (y/n)"
  return $a -match '^[yYsS]'
}

# ---------------------------------------------------------------- where is the game
function Find-SteamGame {
  $roots = @()
  if ($SteamRoot) { $roots += $SteamRoot }
  else {
    foreach ($k in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
      try {
        $p = Get-ItemProperty -LiteralPath $k -ErrorAction Stop
        if ($p.SteamPath) { $roots += ($p.SteamPath -replace '/', '\') }
        if ($p.InstallPath) { $roots += $p.InstallPath }
      } catch { }
    }
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

function Pick-Folder {
  if ($Quiet) { return $null }
  try {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'Choose the Horizon Chase Turbo folder (the one that contains HorizonChase.exe)'
    if ($d.ShowDialog() -eq 'OK') { return $d.SelectedPath }
  } catch { }
  return $null
}

Say ""
Say "HCT 1.6 v2.2.3 - $(if ($Uninstall) {'uninstall'} else {'install'}) the Horizon Chase Turbo mod" 'Cyan'
Say ""
if (-not $GameDir) {
  $GameDir = Find-SteamGame
  if ($GameDir) { Say "Game found in Steam: $GameDir" }
  else {
    Say 'Horizon Chase Turbo was not found in your Steam libraries.' 'Yellow'
    $GameDir = Pick-Folder
    if (-not $GameDir) { Fail 'No folder chosen. Try again or use -GameDir "<path>".' }
  }
}
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'HorizonChase.exe'))) { Fail "HorizonChase.exe is not in '$GameDir'." }
if (Get-Process HorizonChase -ErrorAction SilentlyContinue) { Fail 'The game is running. Close it and run this again.' }
$recordPath = Join-Path $GameDir $Record
$oldRecordPath = Join-Path $GameDir $OldRecord

# ---------------------------------------------------------------- uninstall
if ($Uninstall) {
  if (-not (Test-Path -LiteralPath $recordPath) -and (Test-Path -LiteralPath $oldRecordPath)) { $recordPath = $oldRecordPath }
  if (-not (Test-Path -LiteralPath $recordPath)) { Fail "The mod does not seem to be installed here ($Record is missing)." }
  $items = Get-Content -LiteralPath $recordPath | Where-Object { $_ -and -not $_.StartsWith('#') }
  $n = 0
  # files first, deepest paths first, then the folders the mod created when they are empty
  foreach ($rel in ($items | Sort-Object { $_.Length } -Descending)) {
    $p = Join-Path $GameDir $rel
    if (Test-Path -LiteralPath $p -PathType Leaf) { Remove-Item -LiteralPath $p -Force; $n++ }
  }
  foreach ($d in 'BepInEx\plugins\HCT15', 'HCT15Content', 'HCT15Music') {
    $p = Join-Path $GameDir $d
    if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
  }
  $cfg = Join-Path $GameDir 'BepInEx\config\hct15.mod.cfg'
  if (Test-Path -LiteralPath $cfg) { Remove-Item -LiteralPath $cfg -Force }
  # what the mod itself wrote into Logs\ (screenshots, the controller log); the folder goes when it is left empty
  $logs = Join-Path $GameDir 'Logs'
  if (Test-Path -LiteralPath $logs) {
    Get-ChildItem -LiteralPath $logs -File | Where-Object { $_.Name -like 'hct15-*' -or $_.Name -eq 'controller-release.log' } | Remove-Item -Force
    if (@(Get-ChildItem -LiteralPath $logs -Force).Count -eq 0) { Remove-Item -LiteralPath $logs -Force }
  }
  # BepInEx itself goes too, unless another mod still uses it
  $plugins = Join-Path $GameDir 'BepInEx\plugins'
  $others = @(Get-ChildItem -LiteralPath $plugins -Recurse -File -ErrorAction SilentlyContinue)
  if ($others.Count -eq 0) {
    $bx = Join-Path $GameDir 'BepInEx'
    if (Test-Path -LiteralPath $bx) { Remove-Item -LiteralPath $bx -Recurse -Force }
    foreach ($f in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt') {
      $p = Join-Path $GameDir $f; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
    }
  } else { Say "BepInEx stays: other mods use it ($($others.Count) file(s) in BepInEx\plugins)." 'Yellow' }
  $saves = Join-Path $GameDir 'HCT15Saves'
  if (Test-Path -LiteralPath $saves) {
    if ($RemoveSaves -or (AskYes 'Also delete the mod''s saves (HCT15Saves)?')) { Remove-Item -LiteralPath $saves -Recurse -Force; Say 'The mod''s saves were deleted.' }
    else { Say "The mod's saves stay in $saves (your Steam save was never touched)." }
  }
  Remove-Item -LiteralPath $recordPath -Force
  Say ""
  Say "Done: the mod was removed ($n file(s)). The game is as Steam installed it." 'Green'
  exit 0
}

# ---------------------------------------------------------------- install
if (-not (Test-Path -LiteralPath (Join-Path $payload 'BepInEx\plugins\HCT15\HCT15.dll'))) { Fail "The package is incomplete (files\BepInEx\plugins\HCT15\HCT15.dll is missing)." }
$dll = Join-Path $GameDir 'HorizonChase_Data\Managed\Assembly-CSharp.dll'
$have = if (Test-Path -LiteralPath $dll) { (Get-FileHash -Algorithm MD5 -LiteralPath $dll).Hash } else { '' }
if ($have -ne $Genuine) {
  Say 'Warning: the game files are not those of the game version this mod was made for' 'Yellow'
  Say '(a Steam update, or a copy that was already modified). It may not work.' 'Yellow'
  if (-not ($Force -or (AskYes 'Install anyway?'))) { Fail 'Installation cancelled.' }
}
if ((Test-Path -LiteralPath $recordPath) -or (Test-Path -LiteralPath $oldRecordPath)) { Say 'The mod was already installed: updating it.' }

Say 'Copying the mod (the first time takes a while: tracks and music weigh about 2 GB)...'
$installed = New-Object System.Collections.Generic.List[string]
$src = (Resolve-Path -LiteralPath $payload).Path
# the content comes LIVE from <release>\Content when this Installer sits in its release folder, so whatever the track
# editor saved there reaches the game without rebuilding the package (the editor writes every file anew, which leaves
# the package's hard links pointing at the old copies). An Installer copied anywhere else installs its own files\.
$live = Join-Path (Split-Path (Split-Path $here -Parent) -Parent) 'Content'
$sources = @()
if (Test-Path -LiteralPath (Join-Path $live 'HCT15Content')) {
  $live = (Resolve-Path -LiteralPath $live).Path
  Say "Content: the latest from $live."
  $sources += , @($src, @('HCT15Content\', 'HCT15Music\'))
  $sources += , @((Join-Path $live 'HCT15Content'), @(), 'HCT15Content')
  if (Test-Path -LiteralPath (Join-Path $live 'HCT15Music')) { $sources += , @((Join-Path $live 'HCT15Music'), @(), 'HCT15Music') }
} else { $sources += , @($src, @()) }
$skip = @('_container_dump.txt', '_donors.json', '_bubbles.json')   # dumps a previous run wrote, never shipped
foreach ($s in $sources) {
  $root = $s[0]; $exclude = $s[1]; $prefix = if ($s.Count -gt 2) { $s[2] } else { '' }
  Get-ChildItem -LiteralPath $root -Recurse -Force -File | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length).TrimStart('\')
    if ($prefix) { $rel = Join-Path $prefix $rel }
    foreach ($x in $exclude) { if ($rel.StartsWith($x, [StringComparison]::OrdinalIgnoreCase)) { return } }
    if ($prefix -and ($skip -contains $_.Name) -and $rel -notlike '*\*\*') { return }
    if ($_.Name.EndsWith('.tmp') -or $_.Name.EndsWith('.bak')) { return }
    if ($prefix -and ($rel -match '\\history\\|\\_[^\\]*\\')) { return }   # FOLDERS: the editor's undo snapshots and scratch; _levels.json, _sheet.png stay
    $to = Join-Path $GameDir $rel
    $dir = Split-Path $to -Parent
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $copy = $true
    if ((Test-Path -LiteralPath $to) -and (Get-Item -LiteralPath $to).Length -eq $_.Length -and (Get-Item -LiteralPath $to).LastWriteTimeUtc -eq $_.LastWriteTimeUtc) { $copy = $false }
    if ($copy) { Copy-Item -LiteralPath $_.FullName -Destination $to -Force }
    $installed.Add($rel)
  }
}
# releases up to 2.1.2 carried a campaign the mod no longer ships; an install only adds files, so what is left of it
# is removed here - mod content only, never a game file
$gone = 0
$hc2 = Join-Path $GameDir 'HCT15Content\campaigns\hc2_world_tour'
if (Test-Path -LiteralPath $hc2) { Remove-Item -LiteralPath $hc2 -Recurse -Force; $gone++ }
foreach ($sub in 'HCT15Content\tracks', 'HCT15Content\assets\models') {
  $d = Join-Path $GameDir $sub
  if (Test-Path -LiteralPath $d) { Get-ChildItem -LiteralPath $d -Directory -Filter 'hc2_*' | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force; $gone++ } }
}
if ($gone -gt 0) { Say "Removed what an old release left behind ($gone folder(s))." }
if (Test-Path -LiteralPath $oldRecordPath) { Remove-Item -LiteralPath $oldRecordPath -Force }
$head = @("# HCT 1.6 v2.2.3 - files the installer added to this folder ($(Get-Date -Format 'yyyy-MM-dd HH:mm')).", "# UNINSTALL.cmd removes exactly these; no game file was changed.")
Set-Content -LiteralPath $recordPath -Value ($head + $installed) -Encoding UTF8
Say ""
Say "Done: HCT 1.6 v2.2.3 installed in $GameDir ($($installed.Count) file(s))." 'Green'
Say 'Start the game from Steam as usual. The title screen shows "HCT 1.6 v2.2.3 MOD ACTIVE".'
Say 'The mod''s campaigns are under Campaigns. To remove it: UNINSTALL.cmd.'
exit 0
