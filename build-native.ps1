param(
    [switch]$Clean,
    [switch]$SkipMsix,
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$DistDir = Join-Path $Root "dist"
$NativeDir = Join-Path $DistDir "native"
$AssetsDir = Join-Path $NativeDir "Assets"
$SrcDir = Join-Path $Root "src"
$ShellDir = Join-Path $Root "shell-ext"
$PkgDir = Join-Path $Root "packaging"

# Normalize version strings (tag e.g. "v1.2.3" -> MSIX 4-part "1.2.3.0")
$cleanVer = $Version.Trim().TrimStart('v', 'V')
if ($cleanVer -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    $cleanVer = "1.0.0"
}
$msixVersion = if ($cleanVer -match '^\d+\.\d+\.\d+$') { "$cleanVer.0" } else { $cleanVer }
$displayTag = "v" + ($cleanVer -replace '\.0$', '')
if ($Version.Trim() -match '^v\d+\.\d+\.\d+') {
    $displayTag = $Version.Trim()
}

if ($Clean -and (Test-Path $NativeDir)) {
    Remove-Item $NativeDir -Recurse -Force
}

New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null

$Csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $Csc)) {
    throw "csc.exe not found at $Csc"
}

Write-Host ("Building QuickConvert {0} (MSIX {1})..." -f $displayTag, $msixVersion) -ForegroundColor Cyan

Write-Host "[1/4] Generating icons (root + format-specific monotone) and MSIX visual assets..." -ForegroundColor Cyan
$IconPath = Join-Path $NativeDir "QuickConvert.ico"
$IconsDir = Join-Path $NativeDir "icons"
$IconGenSrc = Join-Path $SrcDir "IconGen.cs"
if (-not ([System.Management.Automation.PSTypeName]'QuickConvertTools.IconGen').Type) {
    Add-Type -Path $IconGenSrc -ReferencedAssemblies System.Drawing
}
$rc = [QuickConvertTools.IconGen]::Main([string[]]@($IconPath, "--assets", $AssetsDir, "--target-icons", $IconsDir))
if ($rc -ne 0) { throw "Icon/Asset generation failed." }

Write-Host "[2/4] Building QuickConvert.exe..." -ForegroundColor Cyan
$ExePath = Join-Path $NativeDir "QuickConvert.exe"
$MainSrc = Join-Path $SrcDir "QuickConvert.cs"
& $Csc /nologo /optimize+ /codepage:65001 /target:winexe /win32icon:$IconPath /out:$ExePath `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    $MainSrc
if ($LASTEXITCODE -ne 0) { throw "QuickConvert.exe compilation failed." }

Write-Host "[3/4] Building QuickConvertShell.dll (native x64 C++ COM IExplorerCommand)..." -ForegroundColor Cyan

function Find-VcVars64 {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($installPath) {
            $vc = Join-Path $installPath "VC\Auxiliary\Build\vcvars64.bat"
            if (Test-Path $vc) { return $vc }
        }
    }
    $fallback = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
    )
    foreach ($f in $fallback) {
        if (Test-Path $f) { return $f }
    }
    return $null
}

$vcVars = Find-VcVars64
if (-not $vcVars) {
    throw "Visual Studio C++ x64 build tools (vcvars64.bat) not found. Install Visual Studio Build Tools or build via GitHub Actions."
}

$CppSrc = Join-Path $ShellDir "QuickConvertShell.cpp"
$DefFile = Join-Path $ShellDir "QuickConvertShell.def"
$DllPath = Join-Path $NativeDir "QuickConvertShell.dll"
$ObjTmpDir = Join-Path $DistDir "native_obj"
New-Item -ItemType Directory -Path $ObjTmpDir -Force | Out-Null

$tmpBat = Join-Path $ObjTmpDir "compile_shell.cmd"
$batContent = @"
@echo off
call "$vcVars"
if errorlevel 1 exit /b %errorlevel%
cl /nologo /O2 /W4 /MT /std:c++17 /EHsc /utf-8 /LD /DUNICODE /D_UNICODE /Fo"$ObjTmpDir\QuickConvertShell.obj" "$CppSrc" /Fe:"$DllPath" /link /DEF:"$DefFile" /IMPLIB:"$ObjTmpDir\QuickConvertShell.lib" shlwapi.lib shell32.lib ole32.lib uuid.lib user32.lib
exit /b %errorlevel%
"@
[System.IO.File]::WriteAllText($tmpBat, $batContent, [System.Text.Encoding]::ASCII)
& cmd.exe /c $tmpBat
if ($LASTEXITCODE -ne 0) { throw "QuickConvertShell.dll compilation failed." }

# Stage manifest (with stamped version), presets, version.txt, and helper scripts
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$manifestRaw = [System.IO.File]::ReadAllText((Join-Path $PkgDir "AppxManifest.xml"), [System.Text.Encoding]::UTF8)
$manifestStamped = [Regex]::Replace($manifestRaw, '(<Identity\b[^>]*?\bVersion=")[0-9\.]+(")', ('${1}' + $msixVersion + '${2}'))
[System.IO.File]::WriteAllText((Join-Path $NativeDir "AppxManifest.xml"), $manifestStamped, $Utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $NativeDir "version.txt"), $displayTag, $Utf8NoBom)

Copy-Item (Join-Path $Root "presets.json") (Join-Path $NativeDir "presets.json") -Force
foreach ($helper in @("install-native.ps1", "uninstall-native.ps1", "update.ps1", "update.bat")) {
    $srcHelper = Join-Path $Root $helper
    if (Test-Path $srcHelper) {
        Copy-Item $srcHelper (Join-Path $NativeDir $helper) -Force
    }
}

Remove-Item $ObjTmpDir -Recurse -Force -ErrorAction SilentlyContinue

if (-not $SkipMsix) {
    Write-Host "[4/4] Packing QuickConvert.msix..." -ForegroundColor Cyan
    $sdkRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $makeAppx = $null
    if (Test-Path $sdkRoot) {
        $makeAppx = Get-ChildItem $sdkRoot -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\x64\\makeappx\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $makeAppx) {
        throw "makeappx.exe not found in Windows 10/11 SDK."
    }

    # Pack only MSIX payload (exclude installer/updater scripts from inside MSIX container)
    $msixStage = Join-Path $DistDir "msix_stage"
    if (Test-Path $msixStage) { Remove-Item $msixStage -Recurse -Force }
    Copy-Item $NativeDir $msixStage -Recurse -Force
    Get-ChildItem $msixStage -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq ".ps1" -or $_.Extension -eq ".bat" } |
        Remove-Item -Force

    $MsixPath = Join-Path $DistDir "QuickConvert.msix"
    if (Test-Path $MsixPath) { Remove-Item $MsixPath -Force }
    & $makeAppx pack /d $msixStage /p $MsixPath /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed." }

    Remove-Item $msixStage -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("MSIX created: {0} ({1:N1} KB)" -f $MsixPath, ((Get-Item $MsixPath).Length / 1KB)) -ForegroundColor Green
}

Write-Host ("Native build succeeded in: {0}" -f $NativeDir) -ForegroundColor Green
