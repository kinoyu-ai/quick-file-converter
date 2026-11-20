param(
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$DistDir = Join-Path $Root "dist"
$SrcDir = Join-Path $Root "src"

if ($Clean -and (Test-Path $DistDir)) {
    Remove-Item $DistDir -Recurse -Force
}

New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

$Csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $Csc)) {
    throw "csc.exe not found at $Csc"
}

Write-Host "[1/2] Generating QuickConvert.ico and format-specific monotone icons..." -ForegroundColor Cyan
$IconPath = Join-Path $DistDir "QuickConvert.ico"
$IconsDir = Join-Path $DistDir "icons"
$IconGenSrc = Join-Path $SrcDir "IconGen.cs"
if (-not ([System.Management.Automation.PSTypeName]'QuickConvertTools.IconGen').Type) {
    Add-Type -Path $IconGenSrc -ReferencedAssemblies System.Drawing
}
$rc = [QuickConvertTools.IconGen]::Main([string[]]@($IconPath, "--target-icons", $IconsDir))
if ($rc -ne 0) { throw "Icon generation failed." }

Write-Host "[2/2] Building QuickConvert.exe (zero-resident WinExe)..." -ForegroundColor Cyan
$ExePath = Join-Path $DistDir "QuickConvert.exe"
$MainSrc = Join-Path $SrcDir "QuickConvert.cs"
& $Csc /nologo /optimize+ /codepage:65001 /target:winexe /win32icon:$IconPath /out:$ExePath `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    $MainSrc
if ($LASTEXITCODE -ne 0) { throw "QuickConvert compilation failed." }

$FileInfo = Get-Item $ExePath
Write-Host ("Build succeeded: {0} ({1:N1} KB)" -f $ExePath, ($FileInfo.Length / 1KB)) -ForegroundColor Green
