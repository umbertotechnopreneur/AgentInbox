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
# File: Build, sign and install one local Debug package using an existing certificate.
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
Build, sign and install one local Debug package using an existing certificate.
.PARAMETER Channel
Debug or Store package channel; installation accepts signed Debug only.
.PARAMETER Architecture
Target Windows package architecture, x64 or arm64.
.PARAMETER Version
Requested package version; Store wrapper accepts three components and appends .0.
.PARAMETER PythonArchive
Previously scanned official Python embeddable archive, retained for hash and extraction checks.
.PARAMETER PythonDirectory
Extracted runtime directory whose files must match the supplied archive.
.PARAMETER PythonSource
Official HTTPS Python download URL recorded as import provenance.
.PARAMETER PythonVersion
Version of the supplied Python runtime, at least 3.11.0.
.PARAMETER PythonSha256
Expected SHA-256 of the supplied Python archive.
.PARAMETER DependencyManifest
JSON list of previously scanned NuGet archives, official sources and SHA-256 hashes.
.PARAMETER CertificateThumbprint
Existing trusted current-user signing certificate; no certificate trust is changed.
.PARAMETER MakeAppxPath
Optional existing Windows SDK MakeAppx executable path.
.PARAMETER SignToolPath
Optional existing Windows SDK SignTool executable path for a signed Debug package.
.PARAMETER Unsigned
Build without signing; required for Store packages and rejected by Debug installation.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Store')][string]$Channel = 'Debug',
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$Version,
    [Parameter(Mandatory)][string]$PythonArchive,
    [Parameter(Mandatory)][string]$PythonDirectory,
    [Parameter(Mandatory)][string]$PythonSource,
    [Parameter(Mandatory)][string]$PythonVersion,
    [Parameter(Mandatory)][string]$PythonSha256,
    [Parameter(Mandatory)][string]$DependencyManifest,
    [string]$CertificateThumbprint,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [switch]$Unsigned
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $Channel -ne 'Debug' -or $Unsigned) { throw 'Installation requires a signed local Windows Debug MSIX.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Version) {
    [xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
    $Version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText + '.0'
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repository 'packaging/windows/AppxManifest.xml') -Raw
$identity = $manifest.Package.Identity.Name
$publisher = $manifest.Package.Identity.Publisher
if (-not $CertificateThumbprint) {
    $certificates = @(Get-ChildItem Cert:/CurrentUser/My | Where-Object {
        $_.Subject -ceq $publisher -and $_.HasPrivateKey -and $_.Verify() -and
        @($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }).Count -gt 0
    })
    if ($certificates.Count -ne 1) { throw 'Select an existing trusted current-user code-signing certificate with -CertificateThumbprint.' }
    $CertificateThumbprint = $certificates[0].Thumbprint
}
$parameters = @{
    Channel = 'Debug'; Architecture = $Architecture; Version = $Version; PythonArchive = $PythonArchive
    PythonDirectory = $PythonDirectory; PythonSource = $PythonSource; PythonVersion = $PythonVersion
    PythonSha256 = $PythonSha256; DependencyManifest = $DependencyManifest; CertificateThumbprint = $CertificateThumbprint
    MakeAppxPath = $MakeAppxPath; SignToolPath = $SignToolPath
}
$packagePath = & (Join-Path $PSScriptRoot 'package-msix.ps1') @parameters
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw 'The signed Debug package was not created.' }
Add-AppxPackage -Path $packagePath -ForceApplicationShutdown -ErrorAction Stop
$installed = @(Get-AppxPackage -Name $identity)
if ($installed.Count -ne 1 -or $installed[0].Version -ne [version]$Version -or
    $installed[0].Architecture.ToString() -ine $Architecture -or $installed[0].Status -ne 'Ok') {
    throw 'Installation did not report the requested version, architecture and Ok status.'
}
[ordered]@{ version = $Version; architecture = $Architecture; status = 'Ok'; ui_tests = 'not run' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $packagePath) 'installation.json') -Encoding utf8NoBOM
Write-Output "Installed AgentInbox Debug $Version ($Architecture). Retained installer: $packagePath"
