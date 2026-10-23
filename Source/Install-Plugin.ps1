<#
.SYNOPSIS
  HCT 1.6 v2.2.1 - builds HCT15.dll and installs it (with BepInEx and the content) into a game folder.

.DESCRIPTION
  Paths are relative to the repository root (<root>\Source\ is $PSScriptRoot):
    <root>\TestGame\              default target: a copy of the game for tests (retail files, genuine assemblies)
    <root>\Content\               HCT15Content\ and HCT15Music\ (the content's source of truth)
    Source\game-managed\          the game's own assemblies (copied by Prepare-References.cmd), build-time references only
  Adds to the target only: winhttp.dll, doorstop_config.ini, .doorstop_version, BepInEx\core\,
  BepInEx\plugins\HCT15\HCT15.dll, HCT15Content\ (copied the first time or with -SyncContent),
  HCT15Music\ (hard links the first time). No game file is changed.

  -GameDir <folder>    another target (your Steam installation of the game)
  -SkipBuild           install the last build
  -SyncContent         re-copy Content\HCT15Content over the target's

.EXAMPLE
  powershell -File Source\Install-Plugin.ps1 -GameDir "<a copy of the game>" -SyncContent
#>
param(
  [string]$GameDir = "",
  [switch]$SkipBuild,
  [switch]$SyncContent,
  [switch]$BuildOnly      # only download BepInEx, publicize the references and build bin\HCT15.dll (Build-Installer.ps1)
)
$ErrorActionPreference = 'Stop'
$src     = $PSScriptRoot
$version = Split-Path $src -Parent
$bepin   = Join-Path $src 'tools\BepInEx_x86'
$managed = Join-Path $src 'game-managed'
$content = Join-Path $version 'Content'
if ([string]::IsNullOrEmpty($GameDir)) { $GameDir = Join-Path $version 'TestGame' }
$Genuine = 'B474D3E71FAA0F4A48E990A9FA25DD80'
$rc = @('/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')

# ---- 1. BepInEx (downloaded once into tools\)
if (-not (Test-Path (Join-Path $bepin 'BepInEx\core\BepInEx.dll'))) {
  $zip = Join-Path $src 'tools\BepInEx_win_x86_5.4.23.5.zip'
  if (-not (Test-Path $zip)) {
    Write-Host 'Downloading BepInEx 5.4.23.5 (x86)...'
    Invoke-WebRequest 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x86_5.4.23.5.zip' -OutFile $zip -UseBasicParsing
  }
  Expand-Archive -LiteralPath $zip -DestinationPath $bepin -Force
}

# ---- 2. publicized references (build-time only) and the plugin
if (-not $SkipBuild) {
  $refs = Join-Path $src 'refs'
  foreach ($a in 'Assembly-CSharp', 'aq-framework', 'aq-ui-framework') {
    $in = Join-Path $managed "$a.dll"; $out = Join-Path $refs "$a.dll"
    if (-not (Test-Path $out) -or (Get-Item $in).LastWriteTime -gt (Get-Item $out).LastWriteTime) {
      & dotnet run --project (Join-Path $src 'tools\publicize') -v q -- $in $out
      if ($LASTEXITCODE -ne 0) { throw "publicize failed for $a" }
    }
  }
  $log = & dotnet build (Join-Path $src 'HCT15.Plugin.csproj') -c Release -v q 2>&1 | Out-String
  $errs = ($log -split "`n" | Where-Object { $_ -match ' error ' })
  if ($errs.Count -gt 0) { $errs | Select-Object -First 15 | Write-Host; throw "plugin build failed ($($errs.Count) errors)" }
}
$dll = Join-Path $src 'bin\HCT15.dll'
if (-not (Test-Path $dll)) { throw 'bin\HCT15.dll not built' }
if ($BuildOnly) { Write-Host "HCT15.dll built: $dll"; return }

# ---- 3. the game folder
if (-not (Test-Path (Join-Path $GameDir 'HorizonChase.exe'))) { throw "no HorizonChase.exe in $GameDir (pass -GameDir <your Steam game folder>)" }
$have = (Get-FileHash -Algorithm MD5 (Join-Path $GameDir 'HorizonChase_Data\Managed\Assembly-CSharp.dll')).Hash
if ($have -ne $Genuine) { Write-Warning "Assembly-CSharp.dll in $GameDir is NOT the shipped one (md5 $have)" }

Copy-Item (Join-Path $bepin 'winhttp.dll'), (Join-Path $bepin 'doorstop_config.ini'), (Join-Path $bepin '.doorstop_version') $GameDir -Force
& robocopy (Join-Path $bepin 'BepInEx\core') (Join-Path $GameDir 'BepInEx\core') $rc | Out-Null
$plug = Join-Path $GameDir 'BepInEx\plugins\HCT15'
New-Item -ItemType Directory -Force $plug | Out-Null
Copy-Item $dll $plug -Force

$targetContent = Join-Path $GameDir 'HCT15Content'
if ($SyncContent -or -not (Test-Path $targetContent)) {
  Write-Host 'Copying HCT15Content...'
  & robocopy (Join-Path $content 'HCT15Content') $targetContent $rc | Out-Null
}
$targetMusic = Join-Path $GameDir 'HCT15Music'
if (-not (Test-Path $targetMusic)) {
  Write-Host 'Linking HCT15Music (hard links)...'
  $from = Join-Path $content 'HCT15Music'
  Get-ChildItem -LiteralPath $from -Recurse -File | ForEach-Object {
    $to = Join-Path $targetMusic $_.FullName.Substring($from.Length).TrimStart('\')
    New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
    try { New-Item -ItemType HardLink -Path $to -Target $_.FullName | Out-Null } catch { Copy-Item -LiteralPath $_.FullName $to }
  }
}

Write-Host ""
Write-Host "HCT 1.6 v2.2.1 plugin installed in $GameDir"
Write-Host ("  HCT15.dll md5 {0}   game Assembly-CSharp.dll md5 {1} ({2})" -f (Get-FileHash -Algorithm MD5 $dll).Hash.ToLowerInvariant(), $have.ToLowerInvariant(), $(if ($have -eq $Genuine) { 'genuine, unmodified' } else { 'NOT genuine' }))
$global:LASTEXITCODE = 0
