$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$CsPath = Join-Path $Root "src\QuickConvert.cs"
$DefaultIconPath = Join-Path $Root "dist\QuickConvert.ico"
$IconsDir = Join-Path $Root "dist\icons"
$LocalFfmpeg = Join-Path $Root "dist\tools\ffmpeg.exe"

if (-not (Test-Path $CsPath)) {
    throw "QuickConvert.cs not found: $CsPath"
}

$targetKey = $null
for ($i = 0; $i -lt $args.Count; $i++) {
    if ([string]::Equals([string]$args[$i], "--to", [System.StringComparison]::OrdinalIgnoreCase) -and ($i + 1 -lt $args.Count)) {
        $targetKey = ([string]$args[$i + 1]).Trim().ToLowerInvariant()
        break
    }
}

$IconPath = $DefaultIconPath
if (-not [string]::IsNullOrEmpty($targetKey)) {
    $candidateIco = Join-Path $IconsDir ("{0}.ico" -f $targetKey)
    if (Test-Path $candidateIco) {
        $IconPath = $candidateIco
    }
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
