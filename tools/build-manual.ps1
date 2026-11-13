# -----------------------------------------------------------------------------
# 使用说明书生成：Markdown -> 单文件 HTML -> PDF
# -----------------------------------------------------------------------------
# 目的：不再手工导出 XPS。使用说明书的唯一源文件是仓库根目录的「使用说明书.md」，
#       本脚本用 pandoc 渲染成自包含 HTML（图片内嵌），再用 Edge 无头模式打印成 PDF，
#       两个成品都放到 ShaoLu\Docs\ 随程序发布。
#
# 依赖：pandoc（默认 PATH 里可直接调用）、Microsoft Edge。
#
# 用法：
#   powershell -NoProfile -File tools/build-manual.ps1
#   powershell -NoProfile -File tools/build-manual.ps1 -OpenAfter
# -----------------------------------------------------------------------------
param(
    # 额外的 pandoc 参数，例如 --number-sections
    [string[]]$PandocArgs = @(),
    # 生成后打开 PDF 预览
    [switch]$OpenAfter
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$mdPath = Join-Path $repoRoot '使用说明书.md'
$cssPath = Join-Path $PSScriptRoot 'manual.css'
$docsDir = Join-Path $repoRoot 'ShaoLu\Docs'
$htmlName = '使用说明书.html'
$pdfName = '使用说明书.pdf'
$htmlPath = Join-Path $docsDir $htmlName
$pdfPath = Join-Path $docsDir $pdfName

if (-not (Test-Path $mdPath)) { throw "找不到源文件：$mdPath" }
if (-not (Test-Path $cssPath)) { throw "找不到样式文件：$cssPath" }
if (-not (Test-Path $docsDir)) { New-Item -ItemType Directory -Path $docsDir | Out-Null }

function Resolve-Exe([string]$name, [string[]]$fallbacks) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($f in $fallbacks) {
        if (Test-Path $f) { return $f }
    }
    throw "找不到可执行文件：$name"
}

$pandoc = Resolve-Exe 'pandoc' @(
    "$env:LOCALAPPDATA\Pandoc\pandoc.exe",
    "$env:ProgramFiles\Pandoc\pandoc.exe"
)
$edge = Resolve-Exe 'msedge' @(
    "$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
)

Write-Host "==> pandoc: $pandoc"
Write-Host "==> edge:   $edge"

# 1) Markdown -> 自包含 HTML（图片以 data URI 内嵌，单个文件即可离线打开）
$pandocCmd = @(
    $mdPath,
    '--from=markdown+pipe_tables+footnotes+task_lists',
    '--to=html5',
    '--standalone',
    '--embed-resources',
    '--toc',
    '--toc-depth=3',
    '--css', $cssPath,
    '--resource-path', $repoRoot,
    '--output', $htmlPath
) + $PandocArgs

$pandocOut = & $pandoc @pandocCmd 2>&1
$pandocCode = $LASTEXITCODE
$pandocOut | Where-Object { $_ -match 'WARNING|ERROR' } | ForEach-Object { Write-Host "   $_" }
if ($pandocCode -ne 0) { throw "pandoc 生成 HTML 失败（exit $pandocCode）" }
$htmlSize = [math]::Round((Get-Item $htmlPath).Length / 1KB)
Write-Host "==> HTML: $htmlPath ($htmlSize KB)"

# 2) HTML -> PDF（Edge 无头打印；先删旧 PDF，避免失败时留下旧文件）
if (Test-Path $pdfPath) { Remove-Item $pdfPath -Force }
$fileUri = ([System.Uri]$htmlPath).AbsoluteUri
$profileDir = Join-Path $env:TEMP 'AutoShaoLu-manual-pdf'
$edgeArgs = @(
    '--headless=new',
    '--disable-gpu',
    '--no-first-run',
    '--no-pdf-header-footer',
    "--user-data-dir=$profileDir",
    "--print-to-pdf=$pdfPath",
    $fileUri
)
# 用 Start-Process 启动，避免 Edge 往 stderr 输出的无关告警被当成 PowerShell 错误
Start-Process -FilePath $edge -ArgumentList $edgeArgs -Wait -WindowStyle Hidden

# Edge 打印是异步落盘的，等文件稳定
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    if ((Test-Path $pdfPath) -and ((Get-Item $pdfPath).Length -gt 10KB)) { break }
    Start-Sleep -Milliseconds 500
}
if (-not (Test-Path $pdfPath)) { throw "Edge 未生成 PDF：$pdfPath" }
$pdfSize = [math]::Round((Get-Item $pdfPath).Length / 1KB)
Write-Host "==> PDF:  $pdfPath ($pdfSize KB)"

# 3) 清理历史 XPS（已改为 HTML + PDF 方案）
Get-ChildItem $docsDir -Filter '*.xps' -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force
    Write-Host "==> 已移除旧 XPS：$($_.Name)"
}

if ($OpenAfter) { Start-Process $pdfPath }
Write-Host '==> 完成'
