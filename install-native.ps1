param(
    [ValidateSet("Register", "Msix")]
    [string]$Mode = "Register",
    [switch]$IgnoreSac,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

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

if ((-not $IgnoreSac) -and (Test-SmartAppControlOn)) {
    Show-SmartAppControlWarning
    Pause-IfInteractive
    exit 2
}

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Resolve package folder (works both from repo root and extracted release folder)
$PkgDir = $Root
if (-not (Test-Path (Join-Path $PkgDir "AppxManifest.xml"))) {
    $candidate = Join-Path $Root "dist\native"
    if (Test-Path (Join-Path $candidate "AppxManifest.xml")) {
        $PkgDir = $candidate
    } else {
        Write-Host "AppxManifest.xml not found. Download the Native release ZIP from GitHub Releases/Actions or run build-native.ps1 first." -ForegroundColor Red
        Pause-IfInteractive
        exit 1
    }
}

$ManifestPath = Join-Path $PkgDir "AppxManifest.xml"
$DllPath = Join-Path $PkgDir "QuickConvertShell.dll"
$ExePath = Join-Path $PkgDir "QuickConvert.exe"

if (-not (Test-Path $DllPath) -or -not (Test-Path $ExePath)) {
    Write-Host "QuickConvertShell.dll or QuickConvert.exe is missing in $PkgDir" -ForegroundColor Red
    Pause-IfInteractive
    exit 1
}

if (($Mode -eq "Register") -and (-not (Test-DeveloperModeOn))) {
    Show-DeveloperModeWarning
    Pause-IfInteractive
    exit 3
}

# Remove existing version if already registered
$existing = Get-AppxPackage -Name "QuickConvert.ShellExtension" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing previous QuickConvert.ShellExtension registration..." -ForegroundColor Yellow
    $existing | Remove-AppxPackage
}

if ($Mode -eq "Register") {
    Write-Host "Registering Windows 11 App Extension (Developer Mode / loose registration)..." -ForegroundColor Cyan
    try {
        Add-AppxPackage -Register $ManifestPath
    } catch {
        Write-Host ""
        Write-Host "[Error] Registration failed. Make sure 'Developer Mode' is enabled in:" -ForegroundColor Red
        Write-Host "  Settings > System > For developers > Developer Mode" -ForegroundColor Yellow
        Write-Host $_.Exception.Message -ForegroundColor Red
        Pause-IfInteractive
        exit 1
    }

    Write-Host ""
    Write-Host "Registered successfully!" -ForegroundColor Green
    Write-Host "Open: Settings > Personalization > Context menu (App extensions) to pin QuickConvert inline." -ForegroundColor Cyan
    Write-Host "Tip: Restart Windows Explorer if the right-click menu does not appear immediately." -ForegroundColor Gray
    Pause-IfInteractive
    exit 0
}

# Msix mode: self-sign & install QuickConvert.msix (requires Admin + signtool.exe)
$MsixPath = Join-Path $Root "QuickConvert.msix"
if (-not (Test-Path $MsixPath)) {
    $MsixPath = Join-Path $Root "dist\QuickConvert.msix"
}
if (-not (Test-Path $MsixPath)) {
    Write-Host "QuickConvert.msix not found. Use -Mode Register (default) or build the MSIX first." -ForegroundColor Red
    Pause-IfInteractive
    exit 1
}

$sdkRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
$signtool = $null
if (Test-Path $sdkRoot) {
    $signtool = Get-ChildItem $sdkRoot -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signtool) {
    Write-Host "signtool.exe not found (Windows SDK required for -Mode Msix). Use -Mode Register instead." -ForegroundColor Red
    Pause-IfInteractive
    exit 1
}

$subject = "CN=QuickConvert"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating local self-signed certificate ($subject)..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
        -KeyUsage DigitalSignature -FriendlyName "QuickConvert Local Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}

$cerPath = Join-Path $env:TEMP "QuickConvert_Local.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

Write-Host "Importing certificate into LocalMachine\TrustedPeople (requires Administrator)..." -ForegroundColor Cyan
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null

Write-Host "Signing MSIX..." -ForegroundColor Cyan
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $MsixPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "signtool failed." -ForegroundColor Red
    Pause-IfInteractive
    exit 1
}

Write-Host "Installing signed MSIX..." -ForegroundColor Cyan
Add-AppxPackage -Path $MsixPath
Write-Host "Installed QuickConvert.msix successfully!" -ForegroundColor Green
Pause-IfInteractive
