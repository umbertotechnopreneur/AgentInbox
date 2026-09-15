[CmdletBinding()]
param(
    [switch]$SkipUnitTests,
    [switch]$CheckFormatting
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'clean-artifacts.ps1')
    dotnet restore MailMeUp.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    if ($CheckFormatting) {
        & (Join-Path $PSScriptRoot 'format.ps1') -Check -NoRestore
    } else {
        & (Join-Path $PSScriptRoot 'format.ps1') -NoRestore
    }
    dotnet build MailMeUp.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipUnitTests) {
        dotnet test MailMeUp.slnx -c Release --no-build --logger 'trx;LogFileName=tests.trx' --results-directory TestResults
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
    python scripts/smoke-test.py src/MailMeUp.Cli/bin/Release/net10.0/mailmeup.dll
    if ($LASTEXITCODE -ne 0) { throw 'Protocol smoke test failed.' }
    python scripts/export-notices.py --check
    if ($LASTEXITCODE -ne 0) { throw 'Dependency inventory is out of date.' }
    python scripts/repo-check.py
    if ($LASTEXITCODE -ne 0) { throw 'Repository preflight failed.' }
} finally {
    Pop-Location
}
