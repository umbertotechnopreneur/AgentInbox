#Requires -Version 7.0
# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Build unsigned local Store packages for both Windows architectures from supplied source inputs.
# Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
# Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
# Evidence: docs/VALIDATION.md (source review; execution checks pending).
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

<#
.SYNOPSIS
Build unsigned local Store packages for both Windows architectures from supplied source inputs.
.PARAMETER Version
Requested package version; Store wrapper accepts three components and appends .0.
.PARAMETER PythonRuntimesManifest
JSON manifest of official-source x64 and arm64 Python archives and extracted directories.
.PARAMETER DependencyManifest
JSON list of previously scanned NuGet archives, official sources and SHA-256 hashes.
.PARAMETER MakeAppxPath
Optional existing Windows SDK MakeAppx executable path.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version,
    [Parameter(Mandatory)][string]$PythonRuntimesManifest,
    [Parameter(Mandatory)][string]$DependencyManifest,
    [string]$MakeAppxPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Local Store packaging requires Windows.' }
if ($env:GITHUB_ACTIONS -ceq 'true') { throw 'Build Store packages locally; GitHub Actions is not the Store build path.' }

<#
.SYNOPSIS
Reject incomplete or unexpected manifest fields.
.PARAMETER Value
Manifest object being checked before any build starts.
.PARAMETER Keys
Complete allowed key set; missing and additional keys are rejected.
.PARAMETER Description
Safe manifest-section label used in validation errors.
#>
function Assert-ExactKeys($Value, [string[]]$Keys, [string]$Description) {
    if ($Value -isnot [Collections.IDictionary] -or $Value.Count -ne $Keys.Count) {
        throw "$Description must contain exactly: $($Keys -join ', ')."
    }
    foreach ($key in $Keys) {
        if (-not $Value.Contains($key)) { throw "$Description is missing $key." }
    }
}

<#
.SYNOPSIS
Require an existing input of the selected kind and reject redirected ancestors.
.PARAMETER Path
Exact input or output path whose regular-file and ancestor constraints must be enforced.
.PARAMETER Kind
Required filesystem item kind, Leaf or Container.
#>
function Resolve-InputPath([string]$Path, [ValidateSet('Leaf', 'Container')][string]$Kind) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType $Kind)) {
        throw "A required $Kind input does not exist: $Path"
    }
    $resolved = (Resolve-Path -LiteralPath $Path).ProviderPath
    Assert-NoRedirectedAncestor $resolved
    return $resolved
}

<#
.SYNOPSIS
Reject links or reparse points on the selected path and its ancestors.
.PARAMETER Path
Exact input or output path whose regular-file and ancestor constraints must be enforced.
#>
function Assert-NoRedirectedAncestor([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Packaging paths must not pass through links or reparse points: $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current.TrimEnd([IO.Path]::DirectorySeparatorChar))
        if (-not $parent -or $parent -ceq $current) { break }
        $current = $parent
    }
}

$parts = $Version.Split('.')
foreach ($part in $parts) {
    [uint32]$number = 0
    if (-not [uint32]::TryParse($part, [ref]$number) -or $number -gt 65535) {
        throw 'Store version components must be integers from 0 to 65535.'
    }
}
if ([uint32]$parts[0] -eq 0) { throw 'The first Store version component must be greater than zero.' }
$packageVersion = "$Version.0"
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$storeRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts/store'))
$manifestPath = Resolve-InputPath $PythonRuntimesManifest 'Leaf'
$dependencyPath = Resolve-InputPath $DependencyManifest 'Leaf'
if ($MakeAppxPath) { $MakeAppxPath = Resolve-InputPath $MakeAppxPath 'Leaf' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
Assert-ExactKeys $manifest @('runtimes') 'The runtime manifest'
Assert-ExactKeys $manifest.runtimes @('x64', 'arm64') 'The runtime collection'
$inputs = @{}
$destinations = @{}

# Check both architectures before either engine invocation can create a package.
foreach ($architecture in @('x64', 'arm64')) {
    $runtime = $manifest.runtimes[$architecture]
    Assert-ExactKeys $runtime @('archive', 'directory', 'source', 'version', 'sha256') "$architecture runtime"
    foreach ($key in @('archive', 'directory', 'source', 'version', 'sha256')) {
        if ($runtime[$key] -isnot [string] -or [string]::IsNullOrWhiteSpace($runtime[$key])) {
            throw "$architecture runtime $key must be a nonempty string."
        }
    }
    [version]$pythonVersionValue = $null
    if ($runtime.version -cnotmatch '^3\.[0-9]+\.[0-9]+$' -or
        -not [version]::TryParse($runtime.version, [ref]$pythonVersionValue)) {
        throw "$architecture requires a three-part Python 3 version."
    }
    if ($pythonVersionValue -lt [version]'3.11.0') { throw "$architecture requires Python 3.11 or later." }
    if ($runtime.sha256 -cnotmatch '^[a-fA-F0-9]{64}$') { throw "$architecture requires a 64-character SHA-256." }
    [uri]$source = $null
    $suffix = if ($architecture -eq 'x64') { 'amd64' } else { 'arm64' }
    $expectedPath = "/ftp/python/$($runtime.version)/python-$($runtime.version)-embed-$suffix.zip"
    if (-not [uri]::TryCreate($runtime.source, [UriKind]::Absolute, [ref]$source) -or
        $source.Scheme -cne 'https' -or $source.Port -ne 443 -or
        $source.Host -notin @('python.org', 'www.python.org') -or $runtime.source.Contains('@') -or
        $source.UserInfo -or $source.Query -or $source.Fragment -or $source.AbsolutePath -cne $expectedPath) {
        throw "$architecture requires its matching official HTTPS Python embeddable archive URL."
    }
    $archive = Resolve-InputPath $runtime.archive 'Leaf'
    $directory = Resolve-InputPath $runtime.directory 'Container'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $runtime.sha256) {
        throw "$architecture Python archive does not match its expected SHA-256."
    }
    $destination = [IO.Path]::GetFullPath((Join-Path $storeRoot "$packageVersion/$architecture"))
    if (-not $destination.StartsWith($storeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The Store output path must remain inside artifacts/store.'
    }
    Assert-NoRedirectedAncestor $destination
    if (Test-Path -LiteralPath $destination) { throw "Store output already exists and will not be overwritten: $destination" }
    $inputs[$architecture] = @{
        PythonArchive = $archive; PythonDirectory = $directory; PythonSource = $runtime.source
        PythonVersion = $runtime.version; PythonSha256 = $runtime.sha256
    }
    $destinations[$architecture] = $destination
}

$packages = [Collections.Generic.List[string]]::new()
try {
    foreach ($architecture in @('x64', 'arm64')) {
        $parameters = $inputs[$architecture].Clone()
        $parameters.Channel = 'Store'
        $parameters.Architecture = $architecture
        $parameters.Version = $packageVersion
        $parameters.DependencyManifest = $dependencyPath
        $parameters.Unsigned = $true
        if ($MakeAppxPath) { $parameters.MakeAppxPath = $MakeAppxPath }
        $result = @(& (Join-Path $PSScriptRoot 'package-msix.ps1') @parameters)
        if ($result.Count -ne 1 -or $result[0] -isnot [string] -or
            -not (Test-Path -LiteralPath $result[0] -PathType Leaf)) {
            throw "The $architecture packaging engine did not return one completed package path."
        }
        $packagePath = [IO.Path]::GetFullPath($result[0])
        if (-not $packagePath.StartsWith($destinations[$architecture] + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The $architecture package is outside its expected Store output directory."
        }
        $packages.Add($packagePath)
    }
}
catch {
    if ($packages.Count -gt 0) { Write-Warning "Completed Store packages were preserved: $($packages -join ', ')" }
    throw
}

# Emit only the two completed package paths after both architecture builds succeed.
$packages | Write-Output
