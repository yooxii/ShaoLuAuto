<#
.SYNOPSIS
    Release packaging: build Release, then copy the runtime-required files to bin\publish\<name>_<version>\.

.DESCRIPTION
    1. Resolve the version: -Version, otherwise <ApplicationVersion> in ShaoLu\ShaoLu.csproj
       (the single version source; AssemblyVersion/FileVersion are passed the same value).
    2. Build Release|x64 with -p:OutputPath=<staging> -p:ApplicationVersion=<version>, so the
       staging tree is fresh and ShaoLu\bin\Release is left untouched.
    3. Build AcbelBridge (the x86 bridge process) and copy AcbelBridge.exe plus its native
       Acbel_Chroma_Test.dll into the package root, right next to ShaoLu.exe.
    4. Copy everything runtime-required into ShaoLu\bin\publish\<name>_<version>\,
       excluding pdb, ClickOnce manifests, the WebView2 user cache, logs and user data.
    5. No archive by default; pass -Zip for an additional .zip next to the package folder.

.EXAMPLE
    powershell -NoProfile -File tools/pack-release.ps1
    powershell -NoProfile -File tools/pack-release.ps1 -Version 0.6.2.5 -Force
    powershell -NoProfile -File tools/pack-release.ps1 -Zip
#>
[CmdletBinding()]
param(
    # Empty: read <ApplicationVersion> from ShaoLu.csproj.
    [string]$Version,

    [string]$ProgramName = 'AutoShaoLu',

    # Overwrite an existing package folder (and its zip) of the same version.
    [switch]$Force,

    # Opt-in: also write <name>_<version>.zip next to the folder (the tree is ~150 MB).
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$csproj = Join-Path $root 'ShaoLu\ShaoLu.csproj'
if (-not (Test-Path $csproj)) { throw "Not found: $csproj" }

# ---------- version ----------
function Resolve-Version {
    param([string]$CsprojPath)

    [xml]$xml = Get-Content -LiteralPath $CsprojPath -Encoding UTF8
    # The csproj declares the MSBuild XML namespace, so a namespace-less XPath finds nothing;
    # match on local-name() instead and fall back to property navigation.
    $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='ApplicationVersion']")
    $raw = ''
    if ($node) { $raw = $node.InnerText.Trim() }
    if (-not $raw) { $raw = [string]$xml.Project.PropertyGroup.ApplicationVersion }
    $raw = $raw.Trim()
    if (-not $raw) { throw 'Cannot read <ApplicationVersion> from ShaoLu.csproj; pass -Version instead.' }
    # MSBuild may store '*' URL-encoded as %2a; drop a trailing automatic field.
    $raw = ($raw -replace '%2[aA]', '*') -replace '\.\*$', ''
    return $raw
}

if (-not $Version) { $Version = Resolve-Version -CsprojPath $csproj }
if ($Version -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Invalid -Version '$Version' (expected e.g. 0.6.2 or 0.6.2.0)."
}

# ---------- paths ----------
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "ShaoLu_release_$stamp"
$publishRoot = Join-Path $root 'ShaoLu\bin\publish'
$packageName = "${ProgramName}_$Version"
$packageDir = Join-Path $publishRoot $packageName
$zipPath = Join-Path $publishRoot "$packageName.zip"

if (Test-Path -LiteralPath $packageDir) {
    if (-not $Force) { throw "$packageDir already exists. Re-run with -Force to replace it." }
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
if ($Zip -and $Force -and (Test-Path -LiteralPath $zipPath)) { Remove-Item -LiteralPath $zipPath -Force }

# ---------- build ----------
Write-Host "==> Version: $Version"
& (Join-Path $PSScriptRoot 'build-app.ps1') `
    -Configuration Release `
    -Platform x64 `
    -Target 'ShaoLu\ShaoLu.csproj' `
    -Version $Version `
    -ExtraProperties @{
        OutputPath = "$staging\"
    }

# ---------- AcbelBridge（x86 桥接进程） ----------
# 主程序通过 AcbelBridgeClient 从自身目录旁启动 AcbelBridge.exe（Acbel_Chroma_Test.dll 是
# 32 位原生 DLL，必须与这个 x86 的 exe 同目录）；客户机上没有源码树，所以这两个文件必须进包，
# 否则 ResolveBridgeExe 只能落到「向上找 AcbelBridge\bin\Debug|Release」那一步并最终抛异常。
# 桥接工程只声明了 Debug|AnyCPU 与 Release|AnyCPU，默认的 -Platform x64 会落到没有 OutputPath
# 的 Debug|x64 组合，所以这里显式指定 AnyCPU（真正的位数仍由 csproj 里的 PlatformTarget=x86 决定）。
# 它的产物走自己的 bin\<Configuration>，不套主程序那份 OutputPath 覆盖。
$bridgeConfiguration = 'Release'
$bridgeDir = Join-Path $root "AcbelBridge\bin\$bridgeConfiguration"
Write-Host "==> AcbelBridge: 构建（$bridgeConfiguration|AnyCPU）"
& (Join-Path $PSScriptRoot 'build-app.ps1') `
    -Configuration $bridgeConfiguration `
    -Platform AnyCPU `
    -Target 'AcbelBridge\AcbelBridge.csproj'
if ($LASTEXITCODE -ne 0) {
    throw "AcbelBridge build FAILED (MSBuild exit code $LASTEXITCODE, Configuration=$bridgeConfiguration, Platform=AnyCPU)."
}

# ---------- copy runtime files ----------
# Not needed at runtime: debug symbols, ClickOnce manifests, the WebView2 user cache,
# logs, and per-machine user data (regenerated on first launch).
$excludeDirs = @('app.publish', 'ShaoLu.exe.WebView2', 'logs', 'Logs')
$excludeFiles = @(
    '*.pdb', '*.xml', '*.application', '*.manifest', '*.tmp',
    'settings.json', 'users.json', 'current_language.txt'
)

$copied = 0
$stagingItems = Get-ChildItem -LiteralPath $staging -Recurse -Force
foreach ($item in $stagingItems) {
    $relative = $item.FullName.Substring($staging.Length).TrimStart('\')
    $topLevel = $relative.Split('\')[0]
    if ($excludeDirs -contains $topLevel) { continue }

    if ($item.PSIsContainer) {
        $null = New-Item -ItemType Directory -Path (Join-Path $packageDir $relative) -Force
        continue
    }

    $skip = $false
    foreach ($pattern in $excludeFiles) {
        if ($item.Name -like $pattern) { $skip = $true; break }
    }
    if ($skip) { continue }

    $destination = Join-Path $packageDir $relative
    $destinationDir = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationDir)) {
        $null = New-Item -ItemType Directory -Path $destinationDir -Force
    }
    Copy-Item -LiteralPath $item.FullName -Destination $destination -Force
    $copied++
}

if ($copied -eq 0) { throw "Staging tree is empty: $staging" }

# ---------- 复制 AcbelBridge ----------
# 只复制 exe 与原生 DLL（不带 pdb），放到包目录根：ResolveBridgeExe 第 3 条就是找 ShaoLu.exe 旁边。
$bridgeOutputs = @('AcbelBridge.exe', 'Acbel_Chroma_Test.dll')
foreach ($name in $bridgeOutputs) {
    $bridgeFile = Join-Path $bridgeDir $name
    if (-not (Test-Path -LiteralPath $bridgeFile)) {
        throw "AcbelBridge output missing: $bridgeFile"
    }
    Copy-Item -LiteralPath $bridgeFile -Destination (Join-Path $packageDir $name) -Force
}
Write-Host "==> AcbelBridge: 复制到 $packageDir"

if ($Zip) {
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force
}

Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "==> Package folder: $packageDir"
if ($Zip) { Write-Host "==> Package zip:    $zipPath" }
Write-Host "==> File count:     $copied"
