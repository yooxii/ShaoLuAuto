<#
.SYNOPSIS
    Build and run the NUnit test suite (NUnitTest project).

.DESCRIPTION
    Builds ShaoLu.slnx so both the app and NUnitTest are current, then runs the test
    assembly with vstest.console.exe from the Visual Studio installation (NUnit3TestAdapter
    is already a PackageReference of NUnitTest).
    Exit code is vstest's: 0 when every test passes.

.EXAMPLE
    powershell -NoProfile -File tools/test.ps1
    powershell -NoProfile -File tools/test.ps1 -Configuration Release
    powershell -NoProfile -File tools/test.ps1 -Filter "FullyQualifiedName~BurnIn"
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # vstest --TestCaseFilter expression, e.g. FullyQualifiedName~ConditionEvaluator
    [string]$Filter,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Resolve-VSTest {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vsRoot = $null
    if (Test-Path $vswhere) {
        $vsRoot = & $vswhere -latest -products * -property installationPath 2>$null
        if ($vsRoot) { $vsRoot = $vsRoot | Select-Object -First 1 }
    }
    if (-not $vsRoot) { $vsRoot = $env:ProgramFiles + '\Microsoft Visual Studio\18\Community' }

    $vstest = Join-Path $vsRoot 'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'
    if (-not (Test-Path $vstest)) {
        throw "vstest.console.exe not found under '$vsRoot'. Install the Visual Studio test platform component."
    }
    return $vstest
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-app.ps1') -Configuration $Configuration -Platform x64
}

$testDll = Join-Path $root "NUnitTest\bin\$Configuration\NUnitTest.dll"
if (-not (Test-Path -LiteralPath $testDll)) {
    throw "Test assembly not found: $testDll (run without -SkipBuild, or build $Configuration first)."
}

$vstest = Resolve-VSTest
$vstestArgs = @($testDll)
if ($Filter) { $vstestArgs += "/TestCaseFilter:$Filter" }

Write-Host "==> vstest: $vstest"
Write-Host "==> Assembly: $testDll"
if ($Filter) { Write-Host "==> Filter: $Filter" }

& $vstest @vstestArgs
$code = $LASTEXITCODE
if ($code -ne 0) { throw "Tests FAILED (vstest exit code $code)." }
Write-Host '==> Tests passed.'
