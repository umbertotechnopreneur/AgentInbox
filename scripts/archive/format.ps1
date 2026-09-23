[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NoRestore,
    [string[]]$Include
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
