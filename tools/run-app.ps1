<#
.SYNOPSIS
    Launch the built ShaoLu desktop app of a given configuration.

.DESCRIPTION
    Resolves the executable under ShaoLu\bin\x64\<Configuration>\ and starts it with that
    directory as the working directory (the app loads tessdata, Docs and Examples relative
    to itself). Start-Process returns immediately, so the DSH Action tab does not stay busy
    while the GUI runs; the app's own NLog output goes to <exe dir>\logs.

    Extra arguments are forwarded to the app.

.EXAMPLE
    powershell -NoProfile -File tools/run-app.ps1 -Configuration Debug
    powershell -NoProfile -File tools/run-app.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppArguments
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$exe = Join-Path $root "ShaoLu\bin\x64\$Configuration\ShaoLu.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Not found: $exe`nBuild first: powershell -NoProfile -File tools/build-app.ps1 -Configuration $Configuration"
}

# Do not stack up GUI instances of the same configuration.
$running = Get-Process -Name 'ShaoLu' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "==> ShaoLu is already running (PID $($running.Id -join ', ')); not starting another instance."
    return
}

$workDir = Split-Path -Parent $exe
Write-Host "==> Launching: $exe  ($Configuration)"

if ($AppArguments -and $AppArguments.Count -gt 0) {
    Start-Process -FilePath $exe -WorkingDirectory $workDir -ArgumentList $AppArguments
} else {
    Start-Process -FilePath $exe -WorkingDirectory $workDir
}
