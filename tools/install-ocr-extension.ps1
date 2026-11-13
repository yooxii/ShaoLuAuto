<#
.SYNOPSIS
    Install (or remove) the built OCR extension into an application folder for local runs.

.DESCRIPTION
    Copy this into the app folder so the developer build behaves like a deployed machine:
        <AppDir>\extensions\Ocr\
    The main program finds it there at start-up; without it the OCR UI is disabled.

    This is what makes "OCR 未安装 → 功能禁用 / 安装后可用" testable in Debug runs.
    Release packaging is done by tools\pack-ocr-extension.ps1 instead.

.EXAMPLE
    powershell -NoProfile -File tools/install-ocr-extension.ps1
    powershell -NoProfile -File tools/install-ocr-extension.ps1 -Uninstall
    powershell -NoProfile -File tools/install-ocr-extension.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    # Application folder that contains ShaoLu.exe (default: Debug x64 output).
    [string]$AppDir,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Remove <AppDir>\extensions\Ocr instead of installing it.
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $AppDir) {
    $AppDir = Join-Path $root "ShaoLu\bin\x64\$Configuration"
}
$AppDir = [System.IO.Path]::GetFullPath($AppDir)
$target = Join-Path (Join-Path $AppDir 'extensions') 'Ocr'

if ($Uninstall) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-Host "==> 已移除 OCR 扩展：$target"
    }
    else {
        Write-Host "==> 未安装（无需移除）：$target"
    }
    return
}

if (-not (Test-Path -LiteralPath (Join-Path $AppDir 'ShaoLu.exe'))) {
    throw "目标目录里没有 ShaoLu.exe：$AppDir（先构建，或用 -AppDir 指定程序目录）"
}

& (Join-Path $PSScriptRoot 'build-app.ps1') `
    -Configuration $Configuration `
    -Platform x64 `
    -Target 'OcrExtension\OcrExtension.csproj'
if ($LASTEXITCODE -ne 0) { throw "OcrExtension build FAILED (exit code $LASTEXITCODE)." }

$source = Join-Path $root "OcrExtension\bin\x64\$Configuration"
if (-not (Test-Path -LiteralPath (Join-Path $source 'ShaoLu.OcrExtension.dll'))) {
    throw "扩展构建输出缺失：$source\ShaoLu.OcrExtension.dll"
}

if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
New-Item -ItemType Directory -Path $target -Force | Out-Null

$count = 0
foreach ($item in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
    $relative = $item.FullName.Substring($source.Length).TrimStart('\')
    $topLevel = $relative.Split('\')[0]
    if ($item.Extension -in @('.pdb', '.xml')) { continue }
    if ($topLevel -in @('x86', 'runtimes')) { continue }

    $destination = Join-Path $target $relative
    $destinationDir = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationDir)) {
        $null = New-Item -ItemType Directory -Path $destinationDir -Force
    }
    Copy-Item -LiteralPath $item.FullName -Destination $destination -Force
    $count++
}

Write-Host "==> OCR 扩展已安装到：$target（$count 个文件）"
