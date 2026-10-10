# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# Modified with AI: OpenAI Codex; added this header on 2026-10-10.
# Human guidance: Umberto Giacobbi; requested VibeWare branding.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    $currentHooksPath = git config --local --get core.hooksPath
    if ($LASTEXITCODE -notin @(0, 1)) { throw 'Could not read the local Git hook configuration.' }
    if ($currentHooksPath -and $currentHooksPath -ne '.githooks' -and -not $Force) {
        throw "This repository already uses '$currentHooksPath' for Git hooks. Re-run with -Force to replace it."
    }

    git config --local core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the repository Git hooks.' }
    Write-Host 'AgentInbox Git hooks installed. Staged C# files will be formatted before each commit.'
} finally {
    Pop-Location
}
