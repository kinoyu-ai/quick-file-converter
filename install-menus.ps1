param(
    [string]$TargetDir = "",
    [switch]$GenerateOnly,
    [switch]$UseExe
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$DistDir = Join-Path $Root "dist"
$ExePath = Join-Path $DistDir "QuickConvert.exe"
$IconPath = Join-Path $DistDir "QuickConvert.ico"
$IconsDir = Join-Path $DistDir "icons"
$LauncherPath = Join-Path $Root "QuickConvert.ps1"
$PresetsPath = Join-Path $Root "presets.json"
$OutCmdDir = Join-Path $DistDir "custom_commands"

if ((-not (Test-Path $IconPath)) -or (-not (Test-Path $IconsDir)) -or ($UseExe -and -not (Test-Path $ExePath))) {
    Write-Host "Build artifacts missing. Running build.ps1 first..." -ForegroundColor Yellow
    & (Join-Path $Root "build.ps1")
}

if (-not (Test-Path $PresetsPath)) {
    throw "presets.json not found at $PresetsPath"
}

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$PresetsJson = [System.IO.File]::ReadAllText($PresetsPath, [System.Text.Encoding]::UTF8)
$Presets = $PresetsJson | ConvertFrom-Json

New-Item -ItemType Directory -Path $OutCmdDir -Force | Out-Null
Get-ChildItem -Path $OutCmdDir -Filter "qc_*.json" -ErrorAction SilentlyContinue | Remove-Item -Force

$GeneratedFiles = @()
foreach ($p in $Presets) {
    $targetIco = Join-Path $IconsDir ("{0}.ico" -f ([string]$p.target).ToLowerInvariant())
    $resolvedIcon = if (Test-Path $targetIco) { $targetIco } else { $IconPath }

    if ($UseExe) {
        $exeField = $ExePath
        $paramSingle = "--to $($p.target) `"{path}`""
        $paramMulti = "--to $($p.target) {path}"
        $iconField = if (Test-Path $targetIco) { $targetIco } else { "$ExePath,0" }
        $showFlag = 0
    } else {
        $exeField = "powershell.exe"
        $paramSingle = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$LauncherPath`" --to $($p.target) `"{path}`""
        $paramMulti = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$LauncherPath`" --to $($p.target) {path}"
        $iconField = $resolvedIcon
        $showFlag = -1
    }

    $cmdObj = [ordered]@{
        title                   = [string]$p.title
        index                   = [int]$p.index
        exe                     = $exeField
        param                   = $paramSingle
        icon                    = $iconField
        acceptDirectoryFlag     = 0
        acceptFileFlag          = 3
        acceptExts              = [string]$p.acceptExts
        acceptMultipleFilesFlag = 2
        pathDelimiter           = " "
        paramForMultipleFiles   = $paramMulti
        showWindowFlag          = $showFlag
    }

    $jsonText = ($cmdObj | ConvertTo-Json -Depth 4)
    $filePath = Join-Path $OutCmdDir ("{0}.json" -f $p.id)
    [System.IO.File]::WriteAllText($filePath, $jsonText, $Utf8NoBom)
    $GeneratedFiles += $filePath
}

$modeLabel = if ($UseExe) { "Direct EXE mode" } else { "Signed PowerShell launcher mode (Smart App Control safe)" }
Write-Host ("Generated {0} menu JSON files ({1}) in: {2}" -f $GeneratedFiles.Count, $modeLabel, $OutCmdDir) -ForegroundColor Green

if ($GenerateOnly) {
    exit 0
}

function Find-CustomContextMenuDir {
    $pkgRoot = Join-Path $env:LOCALAPPDATA "Packages"
    if (-not (Test-Path $pkgRoot)) { return $null }

    $candidates = Get-ChildItem -Path $pkgRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
        ($_.Name -match "CustomContextMenu|ContextMenuCustom|19051ikas") -and
        ($_.Name -notmatch "^Microsoft\.PowerToys")
    }

    foreach ($c in $candidates) {
        $localState = Join-Path $c.FullName "LocalState"
        if (Test-Path $localState) {
            return (Join-Path $localState "custom_commands")
        }
    }
    return $null
}

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Find-CustomContextMenuDir
}

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    Write-Host ""
    Write-Host "[Notice] Custom Context Menu app data folder was not found yet." -ForegroundColor Yellow
    Write-Host "1. Install 'Custom Context Menu' (ikas-mc/ContextMenuForWindows11) from Microsoft Store or GitHub Releases."
    Write-Host "2. Launch Custom Context Menu once (so LocalState is initialized), then close it (zero residency)."
    Write-Host "3. Re-run this script: .\install-menus.ps1"
    Write-Host "   Or import the JSON files manually from: $OutCmdDir"
    exit 0
}

New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
Get-ChildItem -Path $TargetDir -Filter "qc_*.json" -ErrorAction SilentlyContinue | Remove-Item -Force

foreach ($f in $GeneratedFiles) {
    Copy-Item -Path $f -Destination $TargetDir -Force
}

Write-Host ("Installed {0} menu entries to: {1}" -f $GeneratedFiles.Count, $TargetDir) -ForegroundColor Green
Write-Host "Tip: Open Custom Context Menu once to set parent menu title (e.g. 'File Convert'), then close the app." -ForegroundColor Cyan
