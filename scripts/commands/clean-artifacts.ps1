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
# Modified with AI: OpenAI Codex; added this header on 2026-10-10.
# Human guidance: Umberto Giacobbi; requested VibeWare branding.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $artifactRoot.StartsWith($repoPrefix, $pathComparison)) {
    throw 'Artifact cleanup must stay inside the repository.'
}

if (Test-Path -LiteralPath $artifactRoot) {
    $artifactDirectory = Get-Item -LiteralPath $artifactRoot -Force
    if (-not $artifactDirectory.PSIsContainer -or
        ($artifactDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Artifact cleanup requires a regular directory, not a link or junction.'
    }

    # Refuse linked descendants before deleting anything; never follow paths outside artifacts.
    $linkedItem = Get-ChildItem -LiteralPath $artifactRoot -Force -Recurse |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
        Select-Object -First 1
    if ($linkedItem) { throw "Remove the link or junction before artifact cleanup: $($linkedItem.FullName)" }

    $artifactPrefix = $artifactRoot + [IO.Path]::DirectorySeparatorChar
    foreach ($item in Get-ChildItem -LiteralPath $artifactRoot -Force) {
        $targetPath = [IO.Path]::GetFullPath($item.FullName)
        if (-not $targetPath.StartsWith($artifactPrefix, $pathComparison)) {
            throw 'Artifact cleanup target escaped the artifact directory.'
        }
        Remove-Item -LiteralPath $targetPath -Recurse -Force
    }
} else {
    New-Item -ItemType Directory -Path $artifactRoot | Out-Null
}

Write-Output "Cleaned build artifacts: $artifactRoot"
