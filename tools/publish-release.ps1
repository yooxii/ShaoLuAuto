<#
.SYNOPSIS
    Publish a prepared release to GitHub (tag + Release + assets).

.DESCRIPTION
    Prerequisites:
      1. Run tools\pack-release.ps1 -Zip first, producing
         ShaoLu\bin\publish\AutoShaoLu_<version>.zip.
      2. The source archive AutoShaoLu_Source_<version>.zip in the same folder
         (see the repository's release preparation notes).
      3. Set GITHUB_TOKEN in the CURRENT PowerShell session only, e.g.
           $env:GITHUB_TOKEN = '<your token>'
         The token is read from the environment and is never written to disk,
         never printed, and never committed.

    What it does:
      - Creates the tag (if missing) and pushes it to origin.
      - Creates a GitHub Release for that tag (pre-release by default).
      - Uploads the binary zip and the source zip as release assets.

.EXAMPLE
    $env:GITHUB_TOKEN = '<your token>'
    powershell -NoProfile -File tools\publish-release.ps1

.EXAMPLE
    # already pushed the tag / release exists: only upload assets to it
    powershell -NoProfile -File tools\publish-release.ps1 -SkipCreateRelease
#>
[CmdletBinding()]
param(
    [string]$Version = '0.6.8.2611',

    # Default: v<Version>
    [string]$Tag,

    [string]$Owner = 'yooxii',

    [string]$Repo = 'ShaoLuAuto',

    # Default: tools\release-notes\<Version>.md
    [string]$NotesFile,

    # Default: ShaoLu\bin\publish
    [string]$PublishDir,

    [switch]$SkipTagPush,

    # Upload assets to an existing release instead of creating one.
    [switch]$SkipCreateRelease,

    # Mark the release as pre-release (default for preview builds).
    [bool]$PreRelease = $true
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$token = $env:GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Host 'GITHUB_TOKEN 未设置。请只在当前 PowerShell 会话里设置，例如：' -ForegroundColor Yellow
    Write-Host '  $env:GITHUB_TOKEN = ''<your token>''' -ForegroundColor Yellow
    Write-Host '脚本不会保存或打印 token。'
    exit 1
}

if (-not $Tag) { $Tag = "v$Version" }
if (-not $NotesFile) { $NotesFile = Join-Path $root "tools\release-notes\$Version.md" }
if (-not $PublishDir) { $PublishDir = Join-Path $root 'ShaoLu\bin\publish' }

$binZip = Join-Path $PublishDir "AutoShaoLu_$Version.zip"
$srcZip = Join-Path $PublishDir "AutoShaoLu_Source_$Version.zip"

if (-not (Test-Path -LiteralPath $binZip)) {
    throw "找不到程序包：$binZip（先运行 tools\pack-release.ps1 -Zip）"
}
if (-not (Test-Path -LiteralPath $srcZip)) {
    Write-Host "提示：未找到源码包 $srcZip，将只上传程序包。" -ForegroundColor Yellow
}

$headers = @{
    Authorization          = "Bearer $token"
    'User-Agent'           = 'AutoShaoLu-Release'
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

$repoApi = "https://api.github.com/repos/$Owner/$Repo"

# ---------- 1) tag ----------
Push-Location $root
try {
    $exists = git tag --list $Tag
    if (-not $exists) {
        Write-Host "==> 创建 tag：$Tag"
        git tag $Tag
        if ($LASTEXITCODE -ne 0) { throw "git tag 失败（$Tag）" }
    } else {
        Write-Host "==> tag 已存在：$Tag"
    }
    if (-not $SkipTagPush) {
        Write-Host "==> 推送 tag 到 origin"
        git push origin $Tag
        if ($LASTEXITCODE -ne 0) { throw "git push tag 失败（$Tag）" }
    }
} finally {
    Pop-Location
}

# ---------- 2) release ----------
$release = $null
if ($SkipCreateRelease) {
    Write-Host "==> 跳过创建 Release（-SkipCreateRelease），按 tag 查找已有 Release"
    $release = Invoke-RestMethod -Method Get -Uri "$repoApi/releases/tags/$Tag" -Headers $headers
} else {
    $bodyText = ''
    if (Test-Path -LiteralPath $NotesFile) {
        # 用 ReadAllText 取纯字符串：Get-Content -Raw 会附加 PSPath 等 NoteProperty，
        # ConvertTo-Json 会把它序列化成对象而不是字符串，导致 GitHub API 拒绝 body。
        $bodyText = [System.IO.File]::ReadAllText($NotesFile, [System.Text.Encoding]::UTF8)
    }
    $payload = @{
        tag_name   = $Tag
        name       = "AutoShaoLu $Version"
        draft      = $false
        prerelease = $PreRelease
        body       = $bodyText
    } | ConvertTo-Json -Depth 4

    Write-Host "==> 创建 GitHub Release：$Tag"
    # 显式按 UTF-8 发送，避免中文被按本地编码处理
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    try {
        $release = Invoke-RestMethod -Method Post -Uri "$repoApi/releases" -Headers $headers -Body $bodyBytes -ContentType 'application/json; charset=utf-8'
    } catch {
        # 已存在（该 tag 的 Release 之前建过）：取现有 Release，继续上传附件
        Write-Host "==> Release 可能已存在，按 tag 查询：$Tag" -ForegroundColor Yellow
        $release = Invoke-RestMethod -Method Get -Uri "$repoApi/releases/tags/$Tag" -Headers $headers
    }
}

# ---------- 3) assets ----------
# 上传地址：优先用 API 返回的 upload_url（去掉 {?name,label}），缺失时用 release id 构造
$uploadUrl = [string]$release.upload_url
if (-not $uploadUrl) { $uploadUrl = "https://uploads.github.com/repos/$Owner/$Repo/releases/$($release.id)/assets" }
if ($uploadUrl.Contains('{')) { $uploadUrl = $uploadUrl.Substring(0, $uploadUrl.IndexOf('{')) }
$uploadUrl = $uploadUrl.TrimEnd('/')
if ($uploadUrl -notmatch '^https?://') {
    throw "无法获取上传地址（release.upload_url='$($release.upload_url)'，id='$($release.id)'）"
}
Write-Host "==> 上传地址：$uploadUrl"

$assets = @($binZip)
if (Test-Path -LiteralPath $srcZip) { $assets += $srcZip }

foreach ($asset in $assets) {
    $name = [System.IO.Path]::GetFileName($asset)
    $sizeMb = [math]::Round((Get-Item -LiteralPath $asset).Length / 1MB, 1)
    Write-Host "==> 上传 $name（$sizeMb MB）"
    # 用 -f 拼接：字符串插值里 "$uploadUrl?name" 可能把 ? 当成变量名的一部分
    $uploadUri = '{0}?name={1}' -f $uploadUrl, [uri]::EscapeDataString($name)
    Write-Host "==> 上传 URI：$uploadUri"
    Invoke-RestMethod -Method Post -Uri $uploadUri -Headers $headers -InFile $asset -ContentType 'application/octet-stream' -TimeoutSec 900 | Out-Null
}

Write-Host ''
Write-Host "发布完成：$($release.html_url)" -ForegroundColor Green
