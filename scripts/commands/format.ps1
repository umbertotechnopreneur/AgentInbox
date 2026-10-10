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
    [switch]$Check,
    [switch]$NoRestore,
    [string[]]$Include = @()
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    if (-not $NoRestore) {
        dotnet restore MailMeUp.slnx --locked-mode
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    }

    $formatArguments = @('format', 'MailMeUp.slnx', '--no-restore')
    if ($Check) {
        $formatArguments += '--verify-no-changes'
    }
    if ($Include.Count -gt 0) {
        $formatArguments += '--include'
        $formatArguments += $Include
    }

    dotnet @formatArguments
    if ($LASTEXITCODE -ne 0) {
        if ($Check) {
            throw 'Formatting check failed. Run pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command format to apply the fixes.'
        }
        throw 'Formatting failed.'
    }
} finally {
    Pop-Location
}
