$ErrorActionPreference = "Stop"

$pkg = Get-AppxPackage -Name "QuickConvert.ShellExtension" -ErrorAction SilentlyContinue
if (-not $pkg) {
    Write-Host "QuickConvert.ShellExtension is not currently registered." -ForegroundColor Yellow
    exit 0
}

$pkg | Remove-AppxPackage
Write-Host "Uninstalled QuickConvert.ShellExtension successfully." -ForegroundColor Green
