param(
    [ValidateSet("Register", "Msix")]
    [string]$Mode = "Register"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Resolve package folder (works both from repo root and extracted release folder)
$PkgDir = $Root
if (-not (Test-Path (Join-Path $PkgDir "AppxManifest.xml"))) {
    $candidate = Join-Path $Root "dist\native"
    if (Test-Path (Join-Path $candidate "AppxManifest.xml")) {
        $PkgDir = $candidate
    } else {
        throw "AppxManifest.xml not found. Download the Native release ZIP from GitHub Releases/Actions or run build-native.ps1 first."
    }
}

$ManifestPath = Join-Path $PkgDir "AppxManifest.xml"
$DllPath = Join-Path $PkgDir "QuickConvertShell.dll"
$ExePath = Join-Path $PkgDir "QuickConvert.exe"

if (-not (Test-Path $DllPath) -or -not (Test-Path $ExePath)) {
    throw "QuickConvertShell.dll or QuickConvert.exe is missing in $PkgDir"
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
        Write-Host "Also note: if Smart App Control is ON, unsigned native DLLs are blocked by Windows." -ForegroundColor Yellow
        throw
    }

    Write-Host ""
    Write-Host "Registered successfully!" -ForegroundColor Green
    Write-Host "Open: Settings > Personalization > Context menu (App extensions) to pin QuickConvert inline." -ForegroundColor Cyan
    Write-Host "Tip: Restart Windows Explorer if the right-click menu does not appear immediately." -ForegroundColor Gray
    exit 0
}

# Msix mode: self-sign & install QuickConvert.msix (requires Admin + signtool.exe)
$MsixPath = Join-Path $Root "QuickConvert.msix"
if (-not (Test-Path $MsixPath)) {
    $MsixPath = Join-Path $Root "dist\QuickConvert.msix"
}
if (-not (Test-Path $MsixPath)) {
    throw "QuickConvert.msix not found. Use -Mode Register (default) or build the MSIX first."
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
    throw "signtool.exe not found (Windows SDK required for -Mode Msix). Use -Mode Register instead."
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
if ($LASTEXITCODE -ne 0) { throw "signtool failed." }

Write-Host "Installing signed MSIX..." -ForegroundColor Cyan
Add-AppxPackage -Path $MsixPath
Write-Host "Installed QuickConvert.msix successfully!" -ForegroundColor Green
