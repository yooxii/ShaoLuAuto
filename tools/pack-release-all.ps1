<#
.SYNOPSIS
    一键打 Release：主体 + 扩展，分别压缩，输出到桌面。

.DESCRIPTION
    C# 这条打包线独立于 Python（EMIRper / EMI_deal），互不影响。

    流程：
      1. 版本号：-Version，否则读 ShaoLu\ShaoLu.csproj 的 <ApplicationVersion>
         （唯一版本来源；与 Properties\AssemblyInfo.cs 三处需一致，build-app.ps1 会校验）。
      2. 主体：tools\pack-release.ps1 -Zip
         → ShaoLu\bin\publish\AutoShaoLu_<版本>.zip
         （主程序 + AcbelBridge.exe / Acbel_Chroma_Test.dll；不含 OCR 运行时）
      3. 扩展：tools\pack-ocr-extension.ps1 -Zip
         → ShaoLu\bin\publish\AutoShaoLu_<版本>_OcrExtension.zip
         （解压得到 Ocr\ 与 安装说明.txt；装到 <程序目录>\extensions\Ocr）
      4. 两个 zip 复制到 <桌面>\ShaoLu_Release\<版本>\，并生成 说明.txt
         （大小 / SHA256 / 安装位置）。

    两个子脚本各在独立 powershell 进程里跑，互不污染；任一步失败即中止并明确报错。
    主体包是重建的：同名旧 zip 会被覆盖（交给子脚本的 -Force）。

.PARAMETER Version
    要打的版本号，如 0.7.2.2615。省略则取 csproj 的 ApplicationVersion。

.PARAMETER OutRoot
    输出根目录，默认 <桌面>\ShaoLu_Release；最终产物在 <OutRoot>\<版本>\。

.PARAMETER ProgramName
    包名前缀，默认 AutoShaoLu（与现有发布命名一致）。

.PARAMETER SkipMain
    只打扩展包，跳过主体。

.PARAMETER SkipExtension
    只打主体包，跳过扩展。

.PARAMETER RevealInExplorer
    完成后打开输出目录。

.EXAMPLE
    powershell -NoProfile -File tools\pack-release-all.ps1

.EXAMPLE
    powershell -NoProfile -File tools\pack-release-all.ps1 -Version 0.7.2.2615 -RevealInExplorer

.EXAMPLE
    # 只重打扩展
    powershell -NoProfile -File tools\pack-release-all.ps1 -SkipMain
#>
[CmdletBinding()]
param(
    [string]$Version,

    [string]$OutRoot,

    [string]$ProgramName = 'AutoShaoLu',

    [switch]$SkipMain,

    [switch]$SkipExtension,

    [switch]$RevealInExplorer
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$packMainScript = Join-Path $PSScriptRoot 'pack-release.ps1'
$packOcrScript = Join-Path $PSScriptRoot 'pack-ocr-extension.ps1'

$psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $psExe)) { $psExe = 'powershell.exe' }

function Resolve-Version {
    param([string]$CsprojPath)

    if (-not (Test-Path -LiteralPath $CsprojPath)) { throw "找不到工程文件：$CsprojPath" }
    [xml]$xml = Get-Content -LiteralPath $CsprojPath -Encoding UTF8
    # csproj 带 MSBuild 命名空间，用 local-name() 取 <ApplicationVersion>
    $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='ApplicationVersion']")
    $raw = ''
    if ($node) { $raw = $node.InnerText.Trim() }
    if (-not $raw) { $raw = [string]$xml.Project.PropertyGroup.ApplicationVersion }
    $raw = $raw.Trim()
    if (-not $raw) { throw '读不到 <ApplicationVersion>，请用 -Version 指定。' }
    # MSBuild 可能把通配符存成 %2a，末尾自动位去掉
    $raw = ($raw -replace '%2[aA]', '*') -replace '\.\*$', ''
    return $raw
}

function Get-DesktopPath {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if ([string]::IsNullOrWhiteSpace($desktop)) {
        $desktop = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Desktop'
    }
    if (-not (Test-Path -LiteralPath $desktop)) { throw "找不到桌面目录：$desktop" }
    return $desktop
}

function Invoke-PackStep {
    param([string]$Label, [string]$ScriptPath)

    if (-not (Test-Path -LiteralPath $ScriptPath)) { throw "找不到子脚本：$ScriptPath" }

    Write-Host ''
    Write-Host ("========== $Label ==========") -ForegroundColor Cyan
    Write-Host ("          版本 {0}" -f $Version) -ForegroundColor DarkGray
    Write-Host ''

    # 独立进程跑子脚本：变量互不干扰，失败靠退出码判定
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -Version $Version -Zip -Force
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw ("{0} 打包失败（子脚本退出码 {1}）。" -f $Label, $code)
    }
}

# ---------- 版本 ----------
if (-not $Version) { $Version = Resolve-Version -CsprojPath (Join-Path $root 'ShaoLu\ShaoLu.csproj') }
if ($Version -notmatch '^\d+(\.\d+){1,3}$') {
    throw "版本号不合法：'$Version'（示例 0.6.2 / 0.7.2.2615）"
}

# ---------- 输出目录 ----------
if (-not $OutRoot) { $OutRoot = Join-Path (Get-DesktopPath) 'ShaoLu_Release' }
$outDir = Join-Path $OutRoot $Version
$null = New-Item -ItemType Directory -Path $outDir -Force

$publishRoot = Join-Path $root 'ShaoLu\bin\publish'
$mainZip = Join-Path $publishRoot "${ProgramName}_$Version.zip"
$extZip = Join-Path $publishRoot "${ProgramName}_${Version}_OcrExtension.zip"

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
Write-Host ''
Write-Host ("==> ShaoLu Release {0}" -f $Version) -ForegroundColor Green
Write-Host ("==> 输出目录：{0}" -f $outDir) -ForegroundColor DarkGray

$delivered = New-Object System.Collections.ArrayList

# ---------- 主体 ----------
if (-not $SkipMain) {
    Invoke-PackStep -Label '主体包（主程序，不含 OCR 运行时）' -ScriptPath $packMainScript
    if (-not (Test-Path -LiteralPath $mainZip)) { throw "主体 zip 没有生成：$mainZip" }
    if ((Get-Item -LiteralPath $mainZip).Length -lt 1MB) { throw "主体 zip 尺寸异常：$mainZip" }
    $target = Join-Path $outDir (Split-Path -Leaf $mainZip)
    Copy-Item -LiteralPath $mainZip -Destination $target -Force
    Write-Host ("==> 主体包已复制：{0}" -f $target) -ForegroundColor Green
    $null = $delivered.Add([pscustomobject]@{ Kind = '主体'; Path = $target; Note = '解压即用：ShaoLu.exe + AcbelBridge.exe + Acbel_Chroma_Test.dll' })
}
else {
    Write-Host '==> 跳过主体包（-SkipMain）' -ForegroundColor Yellow
}

# ---------- 扩展 ----------
if (-not $SkipExtension) {
    Invoke-PackStep -Label '扩展包（OCR：引擎 + 原生库 + 语言数据）' -ScriptPath $packOcrScript
    if (-not (Test-Path -LiteralPath $extZip)) { throw "扩展 zip 没有生成：$extZip" }
    if ((Get-Item -LiteralPath $extZip).Length -lt 1MB) { throw "扩展 zip 尺寸异常：$extZip" }
    $target = Join-Path $outDir (Split-Path -Leaf $extZip)
    Copy-Item -LiteralPath $extZip -Destination $target -Force
    Write-Host ("==> 扩展包已复制：{0}" -f $target) -ForegroundColor Green
    $null = $delivered.Add([pscustomobject]@{ Kind = '扩展'; Path = $target; Note = '解压出 Ocr\ 与 安装说明.txt，装到 <程序目录>\extensions\Ocr' })
}
else {
    Write-Host '==> 跳过扩展包（-SkipExtension）' -ForegroundColor Yellow
}

if ($delivered.Count -eq 0) { throw '两个包都被跳过了，什么都没打。' }

# ---------- 说明.txt ----------
$lines = New-Object System.Collections.ArrayList
$null = $lines.Add("ShaoLu / AutoShaoLu Release $Version")
$null = $lines.Add("打包时间：$stamp")
$null = $lines.Add("打包脚本：tools\pack-release-all.ps1")
$null = $lines.Add("目标平台：x64 / .NET Framework 4.8")
$null = $lines.Add('')
$null = $lines.Add('一、两个包的关系')
$null = $lines.Add('    主体包：程序本体，解压就能跑，不含 OCR 引擎。')
$null = $lines.Add('    扩展包：OCR 文字识别（Tesseract + 语言数据），可选；不装则 OCR 相关功能禁用，其余照常。')
$null = $lines.Add('    两者版本号必须一致，请成对分发。')
$null = $lines.Add('')
$null = $lines.Add('二、安装')
$null = $lines.Add('    1) 主体包解压到任意目录，双击 ShaoLu.exe 运行。')
$null = $lines.Add('       AcbelBridge.exe 与 Acbel_Chroma_Test.dll 必须和 ShaoLu.exe 放在同一目录（MES 上传用）。')
$null = $lines.Add('    2) 需要 OCR 时，把扩展包解压出的 Ocr 文件夹整个放到：')
$null = $lines.Add('           <程序目录>\extensions\Ocr\')
$null = $lines.Add('       启动后在「帮助 → 日志」里看到 "OCR extension loaded" 即生效。')
$null = $lines.Add('')
$null = $lines.Add('三、文件清单')
foreach ($item in $delivered) {
    $file = Get-Item -LiteralPath $item.Path
    $hash = (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash
    $sizeMb = [math]::Round($file.Length / 1MB, 1)
    $null = $lines.Add(("    [{0}] {1}" -f $item.Kind, $file.Name))
    $null = $lines.Add(("          大小   : {0} MB ({1:N0} 字节)" -f $sizeMb, $file.Length))
    $null = $lines.Add(("          SHA256 : {0}" -f $hash))
    $null = $lines.Add(("          说明   : {0}" -f $item.Note))
}
$null = $lines.Add('')
$null = $lines.Add('四、注意')
$null = $lines.Add('    本目录由打包脚本生成，重打同版本会覆盖同名 zip。')
$null = $lines.Add('    主体包内不该出现 tessdata / TesseractOCR.dll 等 OCR 运行时；出现说明构建配置被改坏了。')

$readmePath = Join-Path $outDir '说明.txt'
[System.IO.File]::WriteAllText($readmePath, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($true)))

# ---------- 汇总 ----------
Write-Host ''
Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray
foreach ($item in $delivered) {
    $file = Get-Item -LiteralPath $item.Path
    Write-Host ("  {0}包  {1}  ({2} MB)" -f $item.Kind, $file.Name, [math]::Round($file.Length / 1MB, 1)) -ForegroundColor Green
}
Write-Host ("  说明    {0}" -f $readmePath) -ForegroundColor DarkGray
Write-Host ("  目录    {0}" -f $outDir) -ForegroundColor Gray
Write-Host '------------------------------------------------------------' -ForegroundColor DarkGray

if ($RevealInExplorer) { Start-Process -FilePath $outDir }
