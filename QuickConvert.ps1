$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$CsPath = Join-Path $Root "src\QuickConvert.cs"
$IconPath = Join-Path $Root "dist\QuickConvert.ico"
$LocalFfmpeg = Join-Path $Root "dist\tools\ffmpeg.exe"

if (-not (Test-Path $CsPath)) {
    throw "QuickConvert.cs not found: $CsPath"
}

$forwardArgs = New-Object System.Collections.Generic.List[string]
if (Test-Path $IconPath) {
    $forwardArgs.Add("--icon")
    $forwardArgs.Add($IconPath)
}
if (Test-Path $LocalFfmpeg) {
    $forwardArgs.Add("--ffmpeg")
    $forwardArgs.Add($LocalFfmpeg)
}
foreach ($a in $args) {
    $forwardArgs.Add([string]$a)
}

if (-not ([System.Management.Automation.PSTypeName]'QuickConvert.Program').Type) {
    $src = [System.IO.File]::ReadAllText($CsPath, [System.Text.Encoding]::UTF8)
    Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing, System.Windows.Forms
}

$exitCode = [QuickConvert.Program]::Main($forwardArgs.ToArray())
exit $exitCode
