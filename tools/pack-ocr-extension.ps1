<#
.SYNOPSIS
    Build and package the OCR extension (Tesseract engine, native libraries and language data).

.DESCRIPTION
    The main program no longer ships the OCR runtime: TesseractOCR.dll, tesseract55.dll,
    leptonica and the ~90 MB tessdata language files are packaged here instead.

    Output layout (the inner "Ocr" folder is what the client machine gets):
        ShaoLu\bin\publish\<ProgramName>_<version>_OcrExtension\
            Ocr\                       <- copy this folder into <program dir>\extensions\
                ShaoLu.OcrExtension.dll
                TesseractOCR.dll
                Microsoft.Extensions.Logging.Abstractions.dll  (+ other managed deps)
                x64\tesseract55.dll, x64\leptonica-1.85.0.dll
                tessdata\*.traineddata
            安装说明.txt

    Without the extension the main program still runs; OCR-related UI is disabled.

.EXAMPLE
    powershell -NoProfile -File tools/pack-ocr-extension.ps1
    powershell -NoProfile -File tools/pack-ocr-extension.ps1 -Zip
    powershell -NoProfile -File tools/pack-ocr-extension.ps1 -Force
#>
[CmdletBinding()]
param(
    # Empty: read <ApplicationVersion> from ShaoLu.csproj (keeps main package and extension in step).
    [string]$Version,

    [string]$ProgramName = 'AutoShaoLu',

    # Overwrite an existing package folder (and its zip) of the same version.
    [switch]$Force,

    # Opt-in: also write <name>_<version>_OcrExtension.zip next to the folder.
    [switch]$Zip,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$csproj = Join-Path $root 'ShaoLu\ShaoLu.csproj'
if (-not (Test-Path $csproj)) { throw "Not found: $csproj" }

if (-not $Version) {
    [xml]$xml = Get-Content -LiteralPath $csproj -Encoding UTF8
    $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='ApplicationVersion']")
    if ($node) { $Version = $node.InnerText.Trim() }
    if (-not $Version) { throw 'Cannot read <ApplicationVersion> from ShaoLu.csproj; pass -Version instead.' }
}

$publishRoot = Join-Path $root 'ShaoLu\bin\publish'
$packageName = "${ProgramName}_${Version}_OcrExtension"
$packageDir = Join-Path $publishRoot $packageName
$ocrDir = Join-Path $packageDir 'Ocr'
$zipPath = Join-Path $publishRoot "$packageName.zip"

if (Test-Path -LiteralPath $packageDir) {
    if (-not $Force) { throw "$packageDir already exists. Re-run with -Force to replace it." }
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ocrDir -Force | Out-Null
if ($Zip -and $Force -and (Test-Path -LiteralPath $zipPath)) { Remove-Item -LiteralPath $zipPath -Force }

# ---------- build ----------
Write-Host "==> OCR 扩展版本：$Version（随主程序版本号标识）"
& (Join-Path $PSScriptRoot 'build-app.ps1') `
    -Configuration $Configuration `
    -Platform x64 `
    -Target 'OcrExtension\OcrExtension.csproj'
if ($LASTEXITCODE -ne 0) {
    throw "OcrExtension build FAILED (MSBuild exit code $LASTEXITCODE, Configuration=$Configuration, Platform=x64)."
}

$extensionOut = Join-Path $root "OcrExtension\bin\x64\$Configuration"
if (-not (Test-Path -LiteralPath (Join-Path $extensionOut 'ShaoLu.OcrExtension.dll'))) {
    throw "Build output missing: $extensionOut\ShaoLu.OcrExtension.dll"
}

# ---------- copy ----------
# 只带运行所需的托管程序集、x64 原生库与语言数据（不带 pdb / xml / x86）
$copied = 0
$items = Get-ChildItem -LiteralPath $extensionOut -Recurse -File -Force
foreach ($item in $items) {
    $relative = $item.FullName.Substring($extensionOut.Length).TrimStart('\')
    $topLevel = $relative.Split('\')[0]

    if ($item.Extension -in @('.pdb', '.xml')) { continue }
    if ($topLevel -eq 'x86') { continue }
    if ($topLevel -eq 'runtimes') { continue }

    $destination = Join-Path $ocrDir $relative
    $destinationDir = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $destinationDir)) {
        $null = New-Item -ItemType Directory -Path $destinationDir -Force
    }
    Copy-Item -LiteralPath $item.FullName -Destination $destination -Force
    $copied++
}

# ---------- 校验 ----------
$mustHave = @(
    'ShaoLu.OcrExtension.dll',
    'TesseractOCR.dll',
    'x64\tesseract55.dll',
    'x64\leptonica-1.85.0.dll'
)
foreach ($name in $mustHave) {
    $path = Join-Path $ocrDir $name
    if (-not (Test-Path -LiteralPath $path)) { throw "OCR 扩展缺少必需文件：$path" }
}
$tessData = Join-Path $ocrDir 'tessdata'
$languageFiles = @(Get-ChildItem -LiteralPath $tessData -Filter '*.traineddata' -ErrorAction SilentlyContinue)
if ($languageFiles.Count -eq 0) { throw "OCR 扩展缺少语言数据：$tessData\*.traineddata" }

# ---------- 安装说明 ----------
$readme = @'
ShaoLu / AutoShaoLu —— OCR 扩展安装说明

一、这个包是什么
    主程序不再内置 OCR（文字识别）引擎。需要「获取输入 → OCR 模式」、图像步骤里的
    「OCR 文本识别」等功能时，把本扩展装到程序目录下即可；不装也能正常使用除 OCR 以外
    的全部功能，界面上 OCR 相关按钮会自动禁用并提示未安装。

二、安装（两种方式任选一种）
    1. 解压本包，把里面的 Ocr 文件夹整个复制到程序目录的 extensions 下，最终形成：
           <程序目录>\extensions\Ocr\ShaoLu.OcrExtension.dll
           <程序目录>\extensions\Ocr\tessdata\*.traineddata
           <程序目录>\extensions\Ocr\x64\tesseract55.dll
       即：<程序目录>\extensions\Ocr\
    2. 如果程序目录下还没有 extensions 目录，自己新建一个再放进去。

三、验证
    启动程序后打开「帮助 → 日志 → 程序日志」，看到
        OCR extension loaded: Tesseract OCR ...
    即表示扩展已生效；也可以新建一个「获取输入」步骤，输入方式里能看到「OCR 识别」。

四、卸载
    关掉程序，删除 <程序目录>\extensions\Ocr 文件夹即可（OCR 功能随之禁用，其它功能不受影响）。

五、注意
    - 本扩展是 x64 版本，只能配 x64 的主程序使用。
    - 运行 Tesseract 原生库需要 Microsoft Visual C++ 2015-2022 运行库（x64）；多数机器已自带，
      若程序日志里出现原生库加载失败，安装 vc_redist.x64.exe 后重启程序即可。
'@
$readmePath = Join-Path $packageDir '安装说明.txt'
[System.IO.File]::WriteAllText($readmePath, $readme, (New-Object System.Text.UTF8Encoding($true)))

if ($Zip) {
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force
}

Write-Host ''
Write-Host "==> OCR 扩展目录：$ocrDir"
Write-Host "==> 安装位置：     <程序目录>\extensions\Ocr"
Write-Host "==> 文件数：       $copied（另加 安装说明.txt）"
if ($Zip) { Write-Host "==> 压缩包：       $zipPath" }
