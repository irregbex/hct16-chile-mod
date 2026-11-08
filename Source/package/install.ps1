<#
  HCT 1.6 v2.4.5 - installer / uninstaller of the mod for Horizon Chase Turbo (Steam).
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

# 2.4.5: safe-delete helpers
$PreBepMark = '# preexisting-bepinex'   # record line: BepInEx was in the game folder before the mod - never remove it
$ModPathRx  = '^(HCT15Content|HCT15Music|BepInEx)\\|^(winhttp\.dll|doorstop_config\.ini|\.doorstop_version|changelog\.txt)$'
$LoaderRx   = '^(winhttp\.dll|doorstop_config\.ini|\.doorstop_version|changelog\.txt)$|^BepInEx\\core\\'
$UserOwned  = @('HCT15Content\hct15.settings.json')   # the game rewrites it (Options, F12): an update keeps the player's copy
function Full-Path([string]$rel) {
  # the full path of a record line, or $null when it would leave the game folder ("..\..", a drive, a UNC path)
  if (-not $rel -or $rel.Contains(':') -or $rel.StartsWith('\')) { return $null }
  $base = [IO.Path]::GetFullPath($GameDir).TrimEnd('\') + '\'
  $full = [IO.Path]::GetFullPath((Join-Path $GameDir $rel))
  if ($full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { return $full }
  return $null
}
function Is-Edited([string]$rel, [string]$p) {
  # a mod file that no longer matches the package's copy was changed by the player (track editor)
  $pl = Join-Path $payload $rel
  if (-not (Test-Path -LiteralPath $pl -PathType Leaf)) { return $false }
  $a = Get-Item -LiteralPath $p; $b = Get-Item -LiteralPath $pl
  return ($a.Length -ne $b.Length) -or ($a.LastWriteTimeUtc -ne $b.LastWriteTimeUtc)
}
function Remove-Tree([string]$p) {
  # [IO.Directory]::Delete removes a symlink / junction itself, never what it points to (Remove-Item -Recurse in
  # Windows PowerShell 5.1 follows directory links and empties their target)
  if (-not (Test-Path -LiteralPath $p)) { return }
  $i = Get-Item -LiteralPath $p -Force
  if ($i.Attributes -band [IO.FileAttributes]::ReparsePoint) { [IO.Directory]::Delete($p, $false) } else { [IO.Directory]::Delete($p, $true) }
}
function Remove-EmptyFolders([string]$p) {
  if (-not (Test-Path -LiteralPath $p)) { return }
  if ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return }
  foreach ($d in (Get-ChildItem -LiteralPath $p -Directory -Force | Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) })) { Remove-EmptyFolders $d.FullName }
  if (@(Get-ChildItem -LiteralPath $p -Force).Count -eq 0) { [IO.Directory]::Delete($p, $false) }
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
      foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw -Encoding UTF8), '"path"\s+"([^"]+)"')) { $libs.Add(($m.Groups[1].Value -replace '\\\\', '\')) }
    }
  }
  foreach ($lib in ($libs | Select-Object -Unique)) {
    $acf = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
    if (-not (Test-Path -LiteralPath $acf)) { continue }
    $m = [regex]::Match((Get-Content -LiteralPath $acf -Raw -Encoding UTF8), '"installdir"\s+"([^"]+)"')
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
Say "HCT 1.6 v2.4.5 - $(if ($Uninstall) {'uninstall'} else {'install'}) the Horizon Chase Turbo mod" 'Cyan'
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
  $lines = @(Get-Content -LiteralPath $recordPath -Encoding UTF8)
  $preBep = [bool]($lines | Where-Object { $_ -eq $PreBepMark })
  $items = $lines | Where-Object { $_ -and -not $_.StartsWith('#') }
  $n = 0
  $loaderFiles = New-Object System.Collections.Generic.List[string]
  # 2.4.5: only what the record lists, and only inside the game folder under the mod's own places; the loader
  # (winhttp.dll, doorstop, BepInEx\core) is set aside and only removed below when no other mod needs it
  foreach ($rel in ($items | Sort-Object { $_.Length } -Descending)) {
    if ($rel -notmatch $ModPathRx) { continue }
    $p = Full-Path $rel
    if (-not $p) { continue }
    if ($rel -match $LoaderRx) { $loaderFiles.Add($p); continue }
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { continue }
    if (Is-Edited $rel $p) { continue }   # changed since it was installed (track editor): the player's work stays
    Remove-Item -LiteralPath $p -Force; $n++
  }
  # what the mod itself generated at run time (never something a player made)
  foreach ($gen in '_container_dump.txt', '_donors.json', '_bubbles.json', 'hct15.settings.json', 'donor_lanes.json') {   # the mod rewrites these itself
    $c = Join-Path $GameDir 'HCT15Content'
    if (Test-Path -LiteralPath $c) { Get-ChildItem -LiteralPath $c -Recurse -File -Filter $gen -ErrorAction SilentlyContinue | Remove-Item -Force }
  }
  Remove-Tree (Join-Path $GameDir 'HCT15Content\assets\game_props')   # a cache older releases wrote
  $cfg = Join-Path $GameDir 'BepInEx\config\hct15.mod.cfg'
  if (Test-Path -LiteralPath $cfg) { Remove-Item -LiteralPath $cfg -Force }
  # the mod's folders go only when nothing is left in them: a player's own music, tracks edited with the
  # track editor and added photos are KEPT
  $kept = 0
  foreach ($d in 'BepInEx\plugins\HCT15', 'HCT15Content', 'HCT15Music') {
    $p = Join-Path $GameDir $d
    Remove-EmptyFolders $p
    if (Test-Path -LiteralPath $p) { $kept += @(Get-ChildItem -LiteralPath $p -Recurse -File -Force -ErrorAction SilentlyContinue).Count }
  }
  # what the mod itself wrote into Logs\ (screenshots, the controller log); the folder goes when it is left empty
  $logs = Join-Path $GameDir 'Logs'
  if (Test-Path -LiteralPath $logs) {
    Get-ChildItem -LiteralPath $logs -File | Where-Object { $_.Name -like 'hct15-*' -or $_.Name -eq 'controller-release.log' } | Remove-Item -Force
    if (@(Get-ChildItem -LiteralPath $logs -Force).Count -eq 0) { Remove-Item -LiteralPath $logs -Force }
  }
  # BepInEx itself goes too, unless another mod still uses it or it was there before the mod
  $plugins = Join-Path $GameDir 'BepInEx\plugins'
  $others = @(Get-ChildItem -LiteralPath $plugins -Recurse -File -ErrorAction SilentlyContinue)
  if ($preBep) { Say 'BepInEx stays: it was already installed before this mod.' 'Yellow' }
  elseif ($others.Count -gt 0) { Say "BepInEx stays: other mods use it ($($others.Count) file(s) in BepInEx\plugins)." 'Yellow' }
  else {
    foreach ($p in $loaderFiles) { if (Test-Path -LiteralPath $p -PathType Leaf) { Remove-Item -LiteralPath $p -Force; $n++ } }
    foreach ($f in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
      $p = Join-Path $GameDir $f; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
    }
    Remove-Tree (Join-Path $GameDir 'BepInEx')
  }
  $saves = Join-Path $GameDir 'HCT15Saves'
  if (Test-Path -LiteralPath $saves) {
    if ($RemoveSaves -or (AskYes 'Also delete the mod''s saves (HCT15Saves)?')) { Remove-Tree $saves; Say 'The mod''s saves were deleted.' }
    else { Say "The mod's saves stay in $saves (your Steam save was never touched)." }
  }
  Remove-Item -LiteralPath $recordPath -Force
  Say ""
  if ($kept -gt 0) { Say "Kept $kept file(s) you added or edited (your music, tracks saved in the editor) in HCT15Content / HCT15Music." 'Yellow' }
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

# 2.4.5: can we write here at all (a library under Program Files, a read-only folder)?
$probe = Join-Path $GameDir '.hct15-write-test'
try { [IO.File]::WriteAllText($probe, ''); [IO.File]::Delete($probe) }
catch { Fail "Cannot write to '$GameDir'. Run INSTALL.cmd as administrator, or move the Steam library." }
# 2.4.5: a BepInEx that was there before the mod is never removed by UNINSTALL (a fresh install looks; an update keeps
# what the first install found)
$preBep = $false
if (Test-Path -LiteralPath $recordPath) { $preBep = [bool](Get-Content -LiteralPath $recordPath -Encoding UTF8 | Where-Object { $_ -eq $PreBepMark }) }
elseif (-not (Test-Path -LiteralPath $oldRecordPath) -and (Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx\core\BepInEx.dll'))) {
  $preBep = $true; Say 'BepInEx is already installed here: UNINSTALL will leave it in place.'
}

Say 'Copying the mod (the first time takes a while: tracks and music weigh about 2 GB)...'
$installed = New-Object System.Collections.Generic.List[string]
$src = [IO.Path]::GetFullPath($payload).TrimEnd('\')
function Write-Record([string]$note) {
  $head = @("# HCT 1.6 v2.4.5 - files the installer added to this folder ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))$note.", "# UNINSTALL.cmd removes exactly these; no game file was changed.")
  if ($preBep) { $head += $PreBepMark }
  Set-Content -LiteralPath $recordPath -Value ($head + $installed) -Encoding UTF8
}
# the content comes LIVE from <release>\Content when this Installer sits in its release folder, so whatever the track
# editor saved there reaches the game without rebuilding the package (the editor writes every file anew, which leaves
# the package's hard links pointing at the old copies). An Installer copied anywhere else installs its own files\.
$live = Join-Path (Split-Path (Split-Path $here -Parent) -Parent) 'Content'
$sources = @()
if (Test-Path -LiteralPath (Join-Path $live 'HCT15Content')) {
  $live = [IO.Path]::GetFullPath($live).TrimEnd('\')
  Say "Content: the latest from $live."
  $liveMusic = Test-Path -LiteralPath (Join-Path $live 'HCT15Music')
  $sources += , @($src, $(if ($liveMusic) { @('HCT15Content\', 'HCT15Music\') } else { @('HCT15Content\') }))
  $sources += , @((Join-Path $live 'HCT15Content'), @(), 'HCT15Content')
  if (Test-Path -LiteralPath (Join-Path $live 'HCT15Music')) { $sources += , @((Join-Path $live 'HCT15Music'), @(), 'HCT15Music') }
} else { $sources += , @($src, @()) }
$skip = @('_container_dump.txt', '_donors.json', '_bubbles.json')   # dumps a previous run wrote, never shipped
try {
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
    if (($UserOwned -contains $rel) -and (Test-Path -LiteralPath $to)) { $copy = $false }   # 2.4.5: the player's settings stay
    elseif ((Test-Path -LiteralPath $to) -and (Get-Item -LiteralPath $to).Length -eq $_.Length -and (Get-Item -LiteralPath $to).LastWriteTimeUtc -eq $_.LastWriteTimeUtc) { $copy = $false }
    if ($copy) { Copy-Item -LiteralPath $_.FullName -Destination $to -Force }
    $installed.Add($rel)
  }
}
} catch {
  # 2.4.5: a copy that stops halfway still leaves a record, so UNINSTALL can clean up
  Write-Record ' - INCOMPLETE'
  Fail "Copying stopped: $($_.Exception.Message)`nRun UNINSTALL.cmd to clean up, then INSTALL.cmd again (close the game first)."
}
# releases up to 2.1.2 carried a campaign the mod no longer ships; an install only adds files, so what is left of it
# is removed here - mod content only, never a game file
$gone = 0
$hc2 = Join-Path $GameDir 'HCT15Content\campaigns\hc2_world_tour'
if (Test-Path -LiteralPath $hc2) { Remove-Tree $hc2; $gone++ }
foreach ($sub in 'HCT15Content\tracks', 'HCT15Content\assets\models') {
  $d = Join-Path $GameDir $sub
  if (Test-Path -LiteralPath $d) { Get-ChildItem -LiteralPath $d -Directory -Filter 'hc2_*' | ForEach-Object { Remove-Tree $_.FullName; $gone++ } }
}
if ($gone -gt 0) { Say "Removed what an old release left behind ($gone folder(s))." }
# 2.3.0: mod files a newer release retires (an install only adds): the race-long fireworks of Estacion Central (they
# belong to the podium now) and the podium's single soundtrack mix (now one file per sound in podium\sonidos)
foreach ($rel in @('HCT15Content\tracks\cl_stgo_estacion_central\nightfx.json', 'HCT15Content\podium\podio.wav', 'HCT15Content\tracks\cl_ruta47_losvilos_illapel\tolls.json',   # 2.3.8: Los Vilos - Illapel without its tolls
                     # 2.4.2: three beaches became harbour laps and two became special rounds - no more beach-wave strips there
                     'HCT15Content\tracks\cl_playa_caleta_abarca\stripwaves.json', 'HCT15Content\tracks\cl_playa_el_canelillo\stripwaves.json',
                     'HCT15Content\tracks\cl_playa_los_enamorados\stripwaves.json', 'HCT15Content\tracks\cl_playa_amarilla\stripwaves.json',
                     'HCT15Content\tracks\cl_playa_las_docas\stripwaves.json')) {
  $p = Join-Path $GameDir $rel
  if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force; Say "Retired: $rel" }
}
# 2.3.2: the frames of Playa Luna's old flat intro (it is a live 3D scene now) and any downloaded track photos an early
# release left in tracks\<track>\photos (recognised by their credits.json; photos a player adds are kept)
$old = 0
$tr = Join-Path $GameDir 'HCT15Content\tracks'
if (Test-Path -LiteralPath $tr) { Get-ChildItem -LiteralPath $tr -Directory | ForEach-Object { $ph = Join-Path $_.FullName 'photos'; if (Test-Path -LiteralPath (Join-Path $ph 'credits.json')) { Remove-Tree $ph; $old++ } } }
$cut = Join-Path $GameDir 'HCT15Content\tracks\cl_playa_luna\cutscene'
if (Test-Path -LiteralPath $cut) { Remove-Tree $cut; $old++ }
if ($old -gt 0) { Say "Retired the old flat intro and old photos ($old folder(s))." }
if (Test-Path -LiteralPath $oldRecordPath) { Remove-Item -LiteralPath $oldRecordPath -Force }
# 2.4.5: the mines' pickup was renamed G-151: what an older release installed under the old names goes
# (mod content only: names under HCT15Content containing "f150")
$f150 = 0
$c150 = Join-Path $GameDir 'HCT15Content'
if (Test-Path -LiteralPath $c150) {
  Get-ChildItem -LiteralPath $c150 -Recurse -Force | Where-Object { $_.Name -match '(?i)f150' } | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    if (-not (Test-Path -LiteralPath $_.FullName)) { return }
    if ($_.PSIsContainer) { Remove-Tree $_.FullName } else { Remove-Item -LiteralPath $_.FullName -Force }
    $f150++
  }
}
if ($f150 -gt 0) { Say "Retired the old files of the mines' pickup ($f150)." }
Write-Record ''
Say ""
Say "Done: HCT 1.6 v2.4.5 installed in $GameDir ($($installed.Count) file(s))." 'Green'
Say 'Start the game from Steam as usual. The title screen shows "HCT 1.6 v2.4.5 MOD ACTIVE".'
Say 'The mod''s campaigns are under Campaigns. To remove it: UNINSTALL.cmd.'
exit 0
