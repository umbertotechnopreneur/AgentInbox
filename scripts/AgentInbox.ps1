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
# File: Dispatch explicit source packaging, Debug installation or task cleanup commands.
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
Dispatch explicit source packaging, Debug installation or task cleanup commands.
.PARAMETER Command
Explicit command to dispatch; help describes usage without building or installing.
.PARAMETER Channel
Debug or Store package channel; installation accepts signed Debug only.
.PARAMETER Architecture
Target Windows package architecture, x64 or arm64.
.PARAMETER Version
Requested package version; Store wrapper accepts three components and appends .0.
.PARAMETER PythonRuntimesManifest
JSON manifest of official-source x64 and arm64 Python archives and extracted directories.
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
.PARAMETER TaskDirectory
Exact marked artifacts/work/msix-ID directory to remove; unrelated output is preserved.
.PARAMETER Unsigned
Build without signing; required for Store packages and rejected by Debug installation.
#>
[CmdletBinding()]
param(
    [ValidateSet('help', 'package-msix', 'build-store-msix', 'install-debug-msix', 'clean-artifacts')][string]$Command = 'help',
    [ValidateSet('Debug', 'Store')][string]$Channel = 'Debug',
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$Version,
    [string]$PythonRuntimesManifest,
    [string]$PythonArchive,
    [string]$PythonDirectory,
    [string]$PythonSource,
    [string]$PythonVersion,
    [string]$PythonSha256,
    [string]$DependencyManifest,
    [string]$CertificateThumbprint,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [string]$TaskDirectory,
    [switch]$Unsigned
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Command -eq 'help') {
    @'
AgentInbox source commands

Connect as many compatible mailboxes as you need to your AI agent through MCP.
Gmail, Microsoft, iCloud, GoDaddy and other IMAP/POP3 providers; selected IMAP folders across accounts.

package-msix       Build Windows 11 x64 or ARM64 MSIX. Debug is the default.
build-store-msix   Build unsigned local Release MSIX packages for x64 and ARM64 together.
install-debug-msix Build, sign and immediately install a Debug MSIX using an existing current-user certificate.
clean-artifacts    Remove only an explicitly named task directory under artifacts/work.

Packaging requires an existing Python 3.11+ embeddable archive and matching extracted directory:
  -PythonArchive <zip> -PythonDirectory <directory>
  -PythonSource <official python.org download URL> -PythonVersion <x.y.z>
  -PythonSha256 <expected SHA256> -DependencyManifest <JSON manifest>

The dependency manifest contains all required NuGet archives, including transitive dependencies:
  {"packages":[{"archive":"ABSOLUTE_PATH.nupkg","source":"OFFICIAL_NUGET_URL","sha256":"EXPECTED_SHA256"}]}

Scan new external imports once before staging; builds never launch Defender scans.
Restore is offline and fails when a package is missing. Nothing is downloaded or installed implicitly.
Supply -CertificateThumbprint for signing, or -Unsigned for an unsigned build.
Store recipe: -Command build-store-msix -Version X.Y.Z -PythonRuntimesManifest <JSON> -DependencyManifest <JSON>.
The runtime manifest supplies x64 and arm64 archive/directory/source/version/sha256 entries.
Both architectures are required. Store builds run locally, without signing, installation or publication.
Optional for single-architecture packaging: -Architecture x64|arm64 -Version <four-part version> -MakeAppxPath <exe> -SignToolPath <exe>.
Cleanup: -Command clean-artifacts -TaskDirectory <exact artifacts/work/msix-ID directory>.
Existing packages and unrelated output are preserved.
'@ | Write-Output
    return
}
if ($Command -eq 'clean-artifacts') {
    & (Join-Path $PSScriptRoot 'commands/clean-artifacts.ps1') -TaskDirectory $TaskDirectory
    return
}
if ($Command -eq 'build-store-msix') {
    foreach ($name in @('Channel', 'Architecture', 'PythonArchive', 'PythonDirectory', 'PythonSource', 'PythonVersion',
        'PythonSha256', 'CertificateThumbprint', 'SignToolPath', 'TaskDirectory', 'Unsigned')) {
        if ($PSBoundParameters.ContainsKey($name)) { throw "$name is not supported by build-store-msix. Supply the two-runtime manifest instead." }
    }
    & (Join-Path $PSScriptRoot 'commands/build-store-msix.ps1') -Version $Version -PythonRuntimesManifest $PythonRuntimesManifest `
        -DependencyManifest $DependencyManifest -MakeAppxPath $MakeAppxPath
    return
}
if ($PSBoundParameters.ContainsKey('PythonRuntimesManifest')) { throw 'PythonRuntimesManifest is only supported by build-store-msix.' }
$parameters = @{
    Channel = $Channel; Architecture = $Architecture; Version = $Version
    PythonArchive = $PythonArchive; PythonDirectory = $PythonDirectory; PythonSource = $PythonSource
    PythonVersion = $PythonVersion; PythonSha256 = $PythonSha256; DependencyManifest = $DependencyManifest
    CertificateThumbprint = $CertificateThumbprint; MakeAppxPath = $MakeAppxPath; SignToolPath = $SignToolPath
    Unsigned = $Unsigned.IsPresent
}
& (Join-Path $PSScriptRoot "commands/$Command.ps1") @parameters
