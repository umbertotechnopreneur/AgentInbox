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
# File: Remove only one marked packaging task directory after validating ownership and paths.
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
Remove only one marked packaging task directory after validating ownership and paths.
.PARAMETER TaskDirectory
Exact marked artifacts/work/msix-ID directory to remove; unrelated output is preserved.
#>
[CmdletBinding()]
param([string]$TaskDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($TaskDirectory)) {
    Write-Output 'No task directory supplied. Pending packages and unrelated artifacts were preserved.'
    return
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifacts = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
$work = [IO.Path]::GetFullPath((Join-Path $artifacts 'work'))
$target = [IO.Path]::GetFullPath($TaskDirectory)
if ([IO.Path]::GetDirectoryName($target) -ine $work -or [IO.Path]::GetFileName($target) -notmatch '^msix-[a-f0-9]{32}$') {
    throw 'Cleanup accepts only one exact artifacts/work/msix-<task ID> directory.'
}
foreach ($ancestor in @($artifacts, $work, $target)) {
    if (Test-Path -LiteralPath $ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Cleanup refuses a linked or non-directory path: $ancestor"
        }
    }
}
if (-not (Test-Path -LiteralPath $target)) { return }
$marker = Join-Path $target '.agentinbox-task'
if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
    (Get-Content -LiteralPath $marker -Raw).Trim() -cne [IO.Path]::GetFileName($target)) {
    throw 'Cleanup requires the matching AgentInbox task ownership marker.'
}
$queue = [Collections.Generic.Queue[string]]::new()
$queue.Enqueue($target)
while ($queue.Count -gt 0) {
    foreach ($child in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
        if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Cleanup refuses to follow a link: $($child.FullName)"
        }
        if ($child.PSIsContainer) { $queue.Enqueue($child.FullName) }
    }
}
Remove-Item -LiteralPath $target -Recurse -Force
if (Test-Path -LiteralPath $target) { throw "Task output remains after cleanup: $target" }
Write-Output "Removed task intermediates: $target"
