param(
    [string]$TargetDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $pkgRoot = Join-Path $env:LOCALAPPDATA "Packages"
    if (Test-Path $pkgRoot) {
        $candidates = Get-ChildItem -Path $pkgRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
            ($_.Name -match "CustomContextMenu|ContextMenuCustom|19051ikas") -and
            ($_.Name -notmatch "^Microsoft\.PowerToys")
        }
        foreach ($c in $candidates) {
            $dir = Join-Path $c.FullName "LocalState\custom_commands"
            if (Test-Path $dir) {
                $TargetDir = $dir
                break
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($TargetDir) -or -not (Test-Path $TargetDir)) {
    Write-Host "Custom Context Menu custom_commands folder not found." -ForegroundColor Yellow
    exit 0
}

$files = Get-ChildItem -Path $TargetDir -Filter "qc_*.json" -ErrorAction SilentlyContinue
if ($null -eq $files -or $files.Count -eq 0) {
    Write-Host "No QuickConvert menu entries found in: $TargetDir" -ForegroundColor Yellow
    exit 0
}

foreach ($f in $files) {
    Remove-Item -Path $f.FullName -Force
}

Write-Host ("Removed {0} QuickConvert menu entries from: {1}" -f $files.Count, $TargetDir) -ForegroundColor Green
