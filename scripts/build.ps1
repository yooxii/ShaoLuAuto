# 统一构建脚本（串行 + 文件锁），供 Lead 与各成员共用
# 用法：pwsh -NoProfile -File scripts\build.ps1 [-Quiet]
param(
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $root '.build.lock'
$mb = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'

if (-not (Test-Path $mb)) {
    Write-Output "MSBUILD_MISSING $mb"
    exit 2
}

# 获取独占锁，避免多人同时构建污染 obj
$lockStream = $null
for ($i = 0; $i -lt 40; $i++) {
    try {
        $lockStream = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        break
    }
    catch {
        Start-Sleep -Seconds 3
    }
}
if ($null -eq $lockStream) {
    Write-Output 'BUILD_LOCK_TIMEOUT'
    exit 3
}

try {
    Push-Location $root
    $output = & $mb 'ShaoLu\ShaoLu.csproj' /t:Build /p:Configuration=Debug /p:Platform=x64 /v:m /nologo /nodeReuse:false 2>&1
    $code = $LASTEXITCODE
    Pop-Location

    $text = ($output | Out-String)
    $errors = $output | Where-Object { $_ -match ': error ' }
    if ($code -ne 0) {
        Write-Output "BUILD_FAILED exit=$code errors=$($errors.Count)"
        $errors | Select-Object -First 40 | ForEach-Object { Write-Output $_ }
    }
    else {
        Write-Output "BUILD_OK"
        if (-not $Quiet) {
            $output | Where-Object { $_ -match ': warning ' } | Select-Object -First 5 | ForEach-Object { Write-Output $_ }
        }
    }
    exit $code
}
finally {
    if ($lockStream) { $lockStream.Dispose() }
    Remove-Item $lockPath -ErrorAction SilentlyContinue
}
