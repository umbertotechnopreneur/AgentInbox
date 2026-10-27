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
# File: Build one local MSIX from current source and previously scanned dependency inputs.
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
Build one local MSIX from current source and previously scanned dependency inputs.
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
    [Parameter(Mandatory)][ValidatePattern('^3\.\d+\.\d+$')][string]$PythonVersion,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$PythonSha256,
    [Parameter(Mandatory)][string]$DependencyManifest,
    [string]$CertificateThumbprint,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [switch]$Unsigned
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-windows-package.ps1') @PSBoundParameters
