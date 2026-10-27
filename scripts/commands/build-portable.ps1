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
# File: Build local Release portable ZIPs for both Windows architectures from supplied source inputs.
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
Build local Release portable ZIPs for both Windows architectures from supplied source inputs.
.PARAMETER Version
Three-component Release version; the build appends .0 for Windows file versions.
.PARAMETER PythonRuntimesManifest
JSON manifest of official-source x64 and arm64 Python archives and extracted directories.
.PARAMETER DependencyManifest
JSON list of previously scanned NuGet archives, official sources and SHA-256 hashes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version,
    [Parameter(Mandatory)][string]$PythonRuntimesManifest,
    [Parameter(Mandatory)][string]$DependencyManifest
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-distribution.ps1') -Channel Portable @PSBoundParameters
