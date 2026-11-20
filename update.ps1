param(
    [string]$Repo = "kinoyu-ai/quick-file-converter",
    [string]$InstallDir = "",
    [ValidateSet("Auto", "Native", "CustomContextMenu")]
    [string]$Mode = "Auto",
    [switch]$CheckOnly,
    [switch]$Force,
    [switch]$ResetPresets
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Support both local script execution and `irm ... | iex` one-liner execution
$ScriptRoot = if ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path } else { "" }

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    if (-not [string]::IsNullOrWhiteSpace($ScriptRoot) -and
        ((Test-Path (Join-Path $ScriptRoot "AppxManifest.xml")) -or (Test-Path (Join-Path $ScriptRoot "QuickConvert.ps1")))) {
        $InstallDir = $ScriptRoot
    } else {
        $InstallDir = Join-Path $env:LOCALAPPDATA "QuickConvert"
    }
}

if ($Mode -eq "Auto") {
    if (Test-Path (Join-Path $InstallDir "AppxManifest.xml")) {
        $Mode = "Native"
    } elseif (Test-Path (Join-Path $InstallDir "install-menus.ps1")) {
        $Mode = "CustomContextMenu"
    } else {
        $Mode = "Native"
    }
}

$VersionFile = Join-Path $InstallDir "version.txt"
$CurrentVersion = if (Test-Path $VersionFile) { (Get-Content $VersionFile -Raw).Trim() } else { "none" }

Write-Host "=== QuickConvert Updater (Zero-Resident) ===" -ForegroundColor Cyan
Write-Host ("Install directory : {0}" -f $InstallDir)
Write-Host ("Mode              : {0}" -f $Mode)
Write-Host ("Current version   : {0}" -f $CurrentVersion)

$headers = @{
    "User-Agent" = "QuickConvert-Updater"
    "Accept"     = "application/vnd.github+json"
}

Write-Host "Checking latest release on GitHub ($Repo)..." -ForegroundColor Cyan
$release = $null
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers -ErrorAction Stop
} catch {
    $status = $null
    if ($_.Exception.Response) {
        $status = [int]$_.Exception.Response.StatusCode
    }
    if ($status -eq 404) {
        Write-Host ""
        Write-Host "[Info] No GitHub Release has been published yet." -ForegroundColor Yellow
        Write-Host "Publish a release first via GitHub Actions (Run workflow -> release_tag: v1.0.0)," -ForegroundColor Yellow
        Write-Host "or download the latest build artifact from: https://github.com/$Repo/actions" -ForegroundColor Yellow
        exit 0
    }
    throw
}

$LatestTag = [string]$release.tag_name
Write-Host ("Latest release    : {0}" -f $LatestTag) -ForegroundColor Green

if ((-not $Force) -and ($CurrentVersion -eq $LatestTag)) {
    Write-Host "QuickConvert is already up to date!" -ForegroundColor Green
    exit 0
}

if ($CheckOnly) {
    Write-Host ("Update available: {0} -> {1}" -f $CurrentVersion, $LatestTag) -ForegroundColor Yellow
    Write-Host "Run .\update.ps1 without -CheckOnly to install the update."
    exit 0
}

# Pick asset URL
$assetName = if ($Mode -eq "Native") { "QuickConvert-Native-win-x64.zip" } else { "" }
$downloadUrl = $null
$shaAssetUrl = $null

foreach ($a in $release.assets) {
    if ($Mode -eq "Native" -and $a.name -eq $assetName) {
        $downloadUrl = $a.browser_download_url
    }
    if ($a.name -eq "SHA256SUMS.txt") {
        $shaAssetUrl = $a.browser_download_url
    }
}

if ($Mode -eq "CustomContextMenu" -or [string]::IsNullOrWhiteSpace($downloadUrl)) {
    if ($Mode -eq "Native") {
        throw "Asset '$assetName' was not found in release $LatestTag."
    }
    $downloadUrl = "https://github.com/$Repo/archive/refs/tags/$LatestTag.zip"
    $assetName = "QuickConvert-$LatestTag-source.zip"
}

$tmpRoot = Join-Path $env:TEMP ("qc_update_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$zipPath = Join-Path $tmpRoot $assetName
$extractDir = Join-Path $tmpRoot "extracted"
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

try {
    Write-Host ("Downloading {0}..." -f $assetName) -ForegroundColor Cyan
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -Headers $headers -UseBasicParsing

    # Verify SHA-256 when SHA256SUMS.txt is published in the release
    if ($shaAssetUrl -and $Mode -eq "Native") {
        Write-Host "Verifying SHA-256 checksum..." -ForegroundColor Cyan
        $sumsResp = Invoke-WebRequest -Uri $shaAssetUrl -Headers $headers -UseBasicParsing
        $sumsText = if ($sumsResp.Content -is [byte[]]) {
            [System.Text.Encoding]::UTF8.GetString($sumsResp.Content)
        } else {
            [string]$sumsResp.Content
        }
        $expectedHash = $null
        foreach ($line in ($sumsText -split "`r?`n")) {
            if ($line -match "^\s*([0-9a-fA-F]{64})\s+\*?$([Regex]::Escape($assetName))\s*$") {
                $expectedHash = $matches[1].ToUpperInvariant()
                break
            }
        }
        if ($expectedHash) {
            $actualHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
            if ($actualHash -ne $expectedHash) {
                throw "SHA-256 checksum mismatch! Expected $expectedHash, got $actualHash"
            }
            Write-Host "SHA-256 verified OK." -ForegroundColor Green
        }
    }

    Write-Host "Extracting package..." -ForegroundColor Cyan
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    # Find payload root (unwrap single top-level directory if present)
    $topDirs = @(Get-ChildItem -Path $extractDir -Directory)
    $topFiles = @(Get-ChildItem -Path $extractDir -File)
    $srcPayload = if ($topDirs.Count -eq 1 -and $topFiles.Count -eq 0) { $topDirs[0].FullName } else { $extractDir }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

    # Clean up leftover .old files from any previous update
    Get-ChildItem -Path $InstallDir -Filter "*.old" -ErrorAction SilentlyContinue | ForEach-Object {
        try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch {}
    }

    # Backup user's presets.json unless -ResetPresets is specified
    $userPresetsPath = Join-Path $InstallDir "presets.json"
    $savedPresets = $null
    if ((-not $ResetPresets) -and (Test-Path $userPresetsPath)) {
        $savedPresets = [System.IO.File]::ReadAllBytes($userPresetsPath)
        Write-Host "Preserving your existing presets.json..." -ForegroundColor Gray
    }

    # Unregister native AppX briefly before replacing DLL
    if ($Mode -eq "Native") {
        $existingPkg = Get-AppxPackage -Name "QuickConvert.ShellExtension" -ErrorAction SilentlyContinue
        if ($existingPkg) {
            Write-Host "Unregistering current App Extension before file update..." -ForegroundColor Yellow
            $existingPkg | Remove-AppxPackage
        }
    }

    Write-Host ("Installing files to {0}..." -f $InstallDir) -ForegroundColor Cyan

    # Copy files safely (rename locked DLLs to .old if Explorer/DllHost still holds a handle)
    $allItems = Get-ChildItem -Path $srcPayload -Recurse -File
    foreach ($item in $allItems) {
        $rel = $item.FullName.Substring($srcPayload.Length).TrimStart('\', '/')
        $dest = Join-Path $InstallDir $rel
        $destParent = Split-Path -Parent $dest
        if (-not (Test-Path $destParent)) {
            New-Item -ItemType Directory -Path $destParent -Force | Out-Null
        }

        try {
            Copy-Item -Path $item.FullName -Destination $dest -Force -ErrorAction Stop
        } catch {
            if (Test-Path $dest) {
                $oldPath = $dest + "." + [Guid]::NewGuid().ToString("N").Substring(0, 4) + ".old"
                Move-Item -Path $dest -Destination $oldPath -Force
                Copy-Item -Path $item.FullName -Destination $dest -Force
            } else {
                throw
            }
        }
    }

    if ($null -ne $savedPresets) {
        [System.IO.File]::WriteAllBytes($userPresetsPath, $savedPresets)
    }

    # Record installed version
    $Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($VersionFile, $LatestTag, $Utf8NoBom)

    # Re-register updated extension or menus
    if ($Mode -eq "Native") {
        $installScript = Join-Path $InstallDir "install-native.ps1"
        if (Test-Path $installScript) {
            & $installScript
        } else {
            Add-AppxPackage -Register (Join-Path $InstallDir "AppxManifest.xml")
        }
    } else {
        $buildScript = Join-Path $InstallDir "build.ps1"
        $menuScript = Join-Path $InstallDir "install-menus.ps1"
        if (Test-Path $buildScript) { & $buildScript }
        if (Test-Path $menuScript) { & $menuScript }
    }

    Write-Host ""
    Write-Host ("Successfully updated QuickConvert to {0}!" -f $LatestTag) -ForegroundColor Green
}
finally {
    if (Test-Path $tmpRoot) {
        Remove-Item $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
