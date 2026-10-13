<#
.SYNOPSIS
    Build the ShaoLu solution (Debug / Release, x64).

.DESCRIPTION
    Thin wrapper around MSBuild, used by DSH Actions and by hand.
    - Locates MSBuild through vswhere, then falls back to known VS 18 paths.
    - Builds ShaoLu.slnx (app ShaoLu + test project NUnitTest) by default.
    - -Target can narrow the build to a single project.
    - -ExtraProperties appends MSBuild properties (pack-release.ps1 uses it for staging output).

.EXAMPLE
    powershell -NoProfile -File tools/build-app.ps1 -Configuration Debug
    powershell -NoProfile -File tools/build-app.ps1 -Configuration Release -Target ShaoLu\ShaoLu.csproj
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Platform = 'x64',

    # Default: the whole solution. Pass a project path to build only that project.
    [string]$Target = 'ShaoLu.slnx',

    # Extra MSBuild properties, e.g. @{ OutputPath = 'C:\temp\stage\' }
    [hashtable]$ExtraProperties = @{},

    # Program version. Empty: read <ApplicationVersion> from ShaoLu\ShaoLu.csproj,
    # which is the single source of truth for ApplicationVersion, AssemblyVersion
    # and FileVersion.
    [string]$Version,

    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Resolve-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
        if ($found) { return ($found | Select-Object -First 1) }
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw 'MSBuild.exe not found. Install Visual Studio with the MSBuild component, or add MSBuild to PATH.'
}

# Read <ApplicationVersion> from ShaoLu\ShaoLu.csproj — the single version source.
function Resolve-CsprojVersion {
    param([string]$RepoRoot)

    $csproj = Join-Path $RepoRoot 'ShaoLu\ShaoLu.csproj'
    if (-not (Test-Path -LiteralPath $csproj)) { return '' }

    [xml]$xml = Get-Content -LiteralPath $csproj -Encoding UTF8
    # The csproj declares the MSBuild XML namespace, so a namespace-less XPath finds
    # nothing; match on local-name() and fall back to property navigation.
    $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='ApplicationVersion']")
    $raw = ''
    if ($node) { $raw = $node.InnerText.Trim() }
    if (-not $raw) { $raw = [string]$xml.Project.PropertyGroup.ApplicationVersion }
    $raw = $raw.Trim()
    # MSBuild may store '*' URL-encoded as %2a; drop a trailing automatic field.
    $raw = ($raw -replace '%2[aA]', '*') -replace '\.\*$', ''
    return $raw
}

$msbuild = Resolve-MSBuild
$target = Join-Path $root $Target
if (-not (Test-Path $target)) { throw "Build target not found: $target" }

$msbuildArgs = @(
    $target,
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform",
    '/m',
    '/v:minimal',
    '/nologo'
)
if ($Rebuild) { $msbuildArgs += '/t:Rebuild' }
foreach ($key in $ExtraProperties.Keys) {
    $msbuildArgs += "/p:$key=$($ExtraProperties[$key])"
}

# Version unification. This is a legacy (non-SDK) project: MSBuild does NOT generate
# assembly attributes, so the version lives in three places that must stay identical —
# <ApplicationVersion> in the csproj and AssemblyVersion/AssemblyFileVersion in
# Properties\AssemblyInfo.cs. Resolve the value, then fail the build on drift instead of
# silently shipping mismatched versions.
if (-not $Version) { $Version = Resolve-CsprojVersion -RepoRoot $root }
if ($Version) {
    $assemblyInfo = Join-Path $root 'ShaoLu\Properties\AssemblyInfo.cs'
    if (Test-Path -LiteralPath $assemblyInfo) {
        $text = [System.IO.File]::ReadAllText($assemblyInfo)

        function Get-DeclaredVersion {
            param([string]$Text, [string]$Pattern)

            $match = [regex]::Match($Text, $Pattern)
            if ($match.Success) { return $match.Groups[1].Value }
            return '(missing)'
        }

        $assemblyVersion = Get-DeclaredVersion -Text $text -Pattern 'AssemblyVersion\("([^"]+)"\)'
        $fileVersion = Get-DeclaredVersion -Text $text -Pattern 'AssemblyFileVersion\("([^"]+)"\)'
        if ($assemblyVersion -ne $Version -or $fileVersion -ne $Version) {
            throw @"
Version drift detected. The three places must match:
  ShaoLu\ShaoLu.csproj  <ApplicationVersion>      = $Version
  ShaoLu\Properties\AssemblyInfo.cs  AssemblyVersion     = $assemblyVersion
  ShaoLu\Properties\AssemblyInfo.cs  AssemblyFileVersion = $fileVersion
Update Properties\AssemblyInfo.cs to $Version, or build with -Version $assemblyVersion to pin the old number.
"@
        }
    }

    if (-not $ExtraProperties.ContainsKey('ApplicationVersion')) {
        $msbuildArgs += "/p:ApplicationVersion=$Version"
    }
    Write-Host "==> Version: $Version (ApplicationVersion / AssemblyVersion / AssemblyFileVersion all match)"
}

Write-Host "==> MSBuild: $msbuild"
Write-Host "==> Target: $target | Configuration=$Configuration | Platform=$Platform"

& $msbuild @msbuildArgs
$code = $LASTEXITCODE
if ($code -ne 0) {
    throw "Build FAILED (MSBuild exit code $code, Configuration=$Configuration, Platform=$Platform)."
}

Write-Host "==> Build OK: $Configuration|$Platform"
