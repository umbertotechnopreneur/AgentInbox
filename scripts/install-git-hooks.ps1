[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
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
