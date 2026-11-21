param(
    [string]$Repo = "kinoyu-ai/quick-file-converter",
    [string]$InstallDir = "",
    [ValidateSet("Auto", "Native", "CustomContextMenu")]
    [string]$Mode = "Auto",
    [switch]$CheckOnly,
    [switch]$Force,
    [switch]$ResetPresets,
    [switch]$IgnoreSac,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function J([string]$escaped) {
    return [Regex]::Unescape($escaped)
}

function Pause-IfInteractive {
    if ($NoPause) { return }
    try {
        if (-not [Console]::IsInputRedirected) {
            Write-Host ""
            [void](Read-Host (J "Enter \u30AD\u30FC\u3092\u62BC\u3059\u3068\u30A6\u30A3\u30F3\u30C9\u30A6\u3092\u9589\u3058\u307E\u3059"))
        }
    } catch {
    }
}

function Test-SmartAppControlOn {
    try {
        $p = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy" -Name "VerifiedAndReputablePolicyState" -ErrorAction Stop
        return ($p.VerifiedAndReputablePolicyState -eq 1)
    } catch {
        return $false
    }
}

function Test-DeveloperModeOn {
    try {
        $p = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -Name "AllowDevelopmentWithoutDevLicense" -ErrorAction Stop
        return ($p.AllowDevelopmentWithoutDevLicense -eq 1)
    } catch {
        return $false
    }
}

function Show-DeveloperModeWarning {
    Write-Host ""
    Write-Host "========================================================================" -ForegroundColor Red
    Write-Host (J "[\u8B66\u544A] \u958B\u767A\u8005\u30E2\u30FC\u30C9 (Developer Mode) \u304C\u30AA\u30D5\u3067\u3059") -ForegroundColor Red
    Write-Host "========================================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host (J "\u30CD\u30A4\u30C6\u30A3\u30D6\u7248\u3092 Windows \u306B\u767B\u9332\u3059\u308B\u306B\u306F\u300C\u958B\u767A\u8005\u30E2\u30FC\u30C9\u300D\u3092\u30AA\u30F3\u306B\u3059\u308B\u5FC5\u8981\u304C\u3042\u308A\u307E\u3059\u3002") -ForegroundColor Yellow
    Write-Host (J "  \u8A2D\u5B9A > \u30B7\u30B9\u30C6\u30E0 > \u958B\u767A\u8005\u5411\u3051 > \u958B\u767A\u8005\u30E2\u30FC\u30C9 \u3092\u30AA\u30F3\u306B\u3057\u3066\u304B\u3089\u518D\u5B9F\u884C\u3057\u3066\u304F\u3060\u3055\u3044\u3002") -ForegroundColor Cyan
}

function Register-NativePackage([string]$TargetDir) {
    $manifestPath = Join-Path $TargetDir "AppxManifest.xml"
    if (-not (Test-Path $manifestPath)) {
        throw "AppxManifest.xml not found in $TargetDir"
    }

    if (-not (Test-DeveloperModeOn)) {
        Show-DeveloperModeWarning
        throw "Developer Mode is OFF. Enable Settings > System > For developers > Developer Mode and re-run."
    }

    $existingPkg = Get-AppxPackage -Name "QuickConvert.ShellExtension" -ErrorAction SilentlyContinue
    if ($existingPkg) {
        Write-Host "Removing previous QuickConvert.ShellExtension registration..." -ForegroundColor Yellow
        $existingPkg | Remove-AppxPackage
    }

    Write-Host "Registering Windows 11 App Extension..." -ForegroundColor Cyan
    Add-AppxPackage -Register $manifestPath

    $verified = Get-AppxPackage -Name "QuickConvert.ShellExtension" -ErrorAction SilentlyContinue
    if (-not $verified) {
        throw "Package registration did not complete (QuickConvert.ShellExtension not found after Add-AppxPackage)."
    }

    Write-Host ("Registered: {0} (Status: {1})" -f $verified.PackageFullName, $verified.Status) -ForegroundColor Green
}

function Show-SmartAppControlWarning {
    Write-Host ""
    Write-Host "========================================================================" -ForegroundColor Red
    Write-Host (J "[\u8B66\u544A] \u30B9\u30DE\u30FC\u30C8 \u30A2\u30D7\u30EA \u30B3\u30F3\u30C8\u30ED\u30FC\u30EB (Smart App Control) \u304C\u30AA\u30F3\u3067\u3059") -ForegroundColor Red
    Write-Host "========================================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host (J "\u3053\u306E PC \u3067\u306F Windows \u306E\u30B9\u30DE\u30FC\u30C8 \u30A2\u30D7\u30EA \u30B3\u30F3\u30C8\u30ED\u30FC\u30EB\u304C\u30AA\u30F3\u306B\u306A\u3063\u3066\u3044\u308B\u305F\u3081\u3001") -ForegroundColor Yellow
    Write-Host (J "\u672A\u7F72\u540D\u306E\u30CD\u30A4\u30C6\u30A3\u30D6\u7248 (QuickConvertShell.dll / QuickConvert.exe) \u306F Windows \u306B\u30D6\u30ED\u30C3\u30AF\u3055\u308C\u3066\u52D5\u4F5C\u3057\u307E\u305B\u3093\u3002") -ForegroundColor Yellow
    Write-Host ""
    Write-Host (J "\u5BFE\u51E6\u65B9\u6CD5:") -ForegroundColor Cyan
    Write-Host (J "1. \u30CD\u30A4\u30C6\u30A3\u30D6\u7248\u3092\u4F7F\u3046\u5834\u5408:")
    Write-Host (J "   \u8A2D\u5B9A > \u30D7\u30E9\u30A4\u30D0\u30B7\u30FC\u3068\u30BB\u30AD\u30E5\u30EA\u30C6\u30A3 > Windows \u30BB\u30AD\u30E5\u30EA\u30C6\u30A3")
    Write-Host (J "   > \u30A2\u30D7\u30EA\u3068\u30D6\u30E9\u30A6\u30B6\u30FC\u5236\u5FA1 > \u30B9\u30DE\u30FC\u30C8 \u30A2\u30D7\u30EA \u30B3\u30F3\u30C8\u30ED\u30FC\u30EB\u306E\u8A2D\u5B9A \u3092\u300C\u30AA\u30D5\u300D\u306B\u3059\u308B")
    Write-Host (J "   (\u203B\u4E00\u5EA6\u30AA\u30D5\u306B\u3059\u308B\u3068 OS \u521D\u671F\u5316\u307E\u3067\u30AA\u30F3\u306B\u623B\u305B\u306A\u3044\u5834\u5408\u304C\u3042\u308A\u307E\u3059)") -ForegroundColor Gray
    Write-Host (J "2. \u30B9\u30DE\u30FC\u30C8 \u30A2\u30D7\u30EA \u30B3\u30F3\u30C8\u30ED\u30FC\u30EB\u3092\u30AA\u30F3\u306E\u307E\u307E\u4F7F\u3046\u5834\u5408:")
    Write-Host (J "   Custom Context Menu \u9023\u643A\u7248 (install-menus.ps1) \u3092\u3054\u5229\u7528\u304F\u3060\u3055\u3044\u3002")
    Write-Host ""
    Write-Host (J "\u203B\u3053\u306E\u30C1\u30A7\u30C3\u30AF\u3092\u7121\u8996\u3057\u3066\u914D\u7F6E\u3060\u3051\u884C\u3046\u5834\u5408\u306F -IgnoreSac \u3092\u4ED8\u3051\u3066\u518D\u5B9F\u884C\u3057\u3066\u304F\u3060\u3055\u3044\u3002") -ForegroundColor DarkGray
}

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

if (($Mode -eq "Native") -and (-not $IgnoreSac) -and (-not $CheckOnly) -and (Test-SmartAppControlOn)) {
    Show-SmartAppControlWarning
    Pause-IfInteractive
    exit 2
}

if (($Mode -eq "Native") -and (-not $CheckOnly) -and (-not (Test-DeveloperModeOn))) {
    Show-DeveloperModeWarning
    Pause-IfInteractive
    exit 3
}

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
        Write-Host "Publish a release first via GitHub Actions, or download from: https://github.com/$Repo/actions" -ForegroundColor Yellow
        Pause-IfInteractive
        exit 0
    }
    Write-Host ("[Error] Failed to query GitHub Releases: {0}" -f $_.Exception.Message) -ForegroundColor Red
    Pause-IfInteractive
    exit 1
}

$LatestTag = [string]$release.tag_name
Write-Host ("Latest release    : {0}" -f $LatestTag) -ForegroundColor Green

if ((-not $Force) -and ($CurrentVersion -eq $LatestTag)) {
    if (($Mode -eq "Native") -and (-not $CheckOnly)) {
        $regPkg = Get-AppxPackage -Name "QuickConvert.ShellExtension" -ErrorAction SilentlyContinue
        $expectedLoc = $InstallDir.TrimEnd('\', '/')
        $actualLoc = if ($regPkg -and $regPkg.InstallLocation) { $regPkg.InstallLocation.TrimEnd('\', '/') } else { "" }
        if ((-not $regPkg) -or ($actualLoc -ine $expectedLoc)) {
            Write-Host "Files are up to date, but Windows App Extension is not registered yet. Registering now..." -ForegroundColor Yellow
            try {
                Register-NativePackage $InstallDir
                Write-Host ""
                Write-Host (J "\u767B\u9332\u304C\u5B8C\u4E86\u3057\u307E\u3057\u305F\uFF01") -ForegroundColor Green
                Write-Host (J "\u53F3\u30AF\u30EA\u30C3\u30AF\u30E1\u30CB\u30E5\u30FC\u306B\u8868\u793A\u3055\u308C\u306A\u3044\u5834\u5408\u306F\u3001\u30A8\u30AF\u30B9\u30D7\u30ED\u30FC\u30E9\u30FC\u3092\u518D\u8D77\u52D5\u3057\u3066\u304F\u3060\u3055\u3044\u3002") -ForegroundColor Gray
                Pause-IfInteractive
                exit 0
            } catch {
                Write-Host ("[Error] Registration failed: {0}" -f $_.Exception.Message) -ForegroundColor Red
                Pause-IfInteractive
                exit 1
            }
        }
    }
    Write-Host "QuickConvert is already up to date!" -ForegroundColor Green
    Pause-IfInteractive
    exit 0
}

if ($CheckOnly) {
    Write-Host ("Update available: {0} -> {1}" -f $CurrentVersion, $LatestTag) -ForegroundColor Yellow
    Write-Host "Run .\update.ps1 without -CheckOnly to install the update."
    Pause-IfInteractive
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
        Write-Host "Asset '$assetName' was not found in release $LatestTag." -ForegroundColor Red
        Pause-IfInteractive
        exit 1
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

    # Re-register updated extension or menus BEFORE stamping version.txt
    if ($Mode -eq "Native") {
        Register-NativePackage $InstallDir
    } else {
        $buildScript = Join-Path $InstallDir "build.ps1"
        $menuScript = Join-Path $InstallDir "install-menus.ps1"
        if (Test-Path $buildScript) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript
            if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE" }
        }
        if (Test-Path $menuScript) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $menuScript
            if ($LASTEXITCODE -ne 0) { throw "install-menus.ps1 failed with exit code $LASTEXITCODE" }
        }
    }

    # Record installed version only after registration succeeds
    $Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($VersionFile, $LatestTag, $Utf8NoBom)

    Write-Host ""
    Write-Host ("Successfully updated QuickConvert to {0}!" -f $LatestTag) -ForegroundColor Green
    if ($Mode -eq "Native") {
        Write-Host (J "\u53F3\u30AF\u30EA\u30C3\u30AF\u30E1\u30CB\u30E5\u30FC\u306B\u8868\u793A\u3055\u308C\u306A\u3044\u5834\u5408\u306F\u3001\u30A8\u30AF\u30B9\u30D7\u30ED\u30FC\u30E9\u30FC\u3092\u518D\u8D77\u52D5\u3057\u3066\u304F\u3060\u3055\u3044\u3002") -ForegroundColor Gray
    }
    Pause-IfInteractive
}
catch {
    Write-Host ""
    Write-Host ("[Error] Update failed: {0}" -f $_.Exception.Message) -ForegroundColor Red
    Pause-IfInteractive
    exit 1
}
finally {
    if (Test-Path $tmpRoot) {
        Remove-Item $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
