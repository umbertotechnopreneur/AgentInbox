#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('menu', 'help', 'validate', 'package-portable', 'package-msix', 'clean-artifacts', 'format', 'format-staged', 'install-hooks', 'update-locks', 'repo-check', 'export-notices', 'smoke-test', 'desktop-smoke', 'provider-check', 'test-provider-check')]
    [string]$Command = 'menu',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [ValidateSet('Debug', 'Store')]
    [string]$Channel = 'Debug',
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',
    [string]$PackageVersion,
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$CertificateThumbprint,
    [uri]$TimestampServer,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [switch]$Unsigned,
    [string]$Executable,
    [string[]]$ToolArguments = @(),
    [switch]$SkipUnitTests,
    [switch]$CheckFormatting,
    [switch]$Check,
    [switch]$NoRestore,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot
$menuManifest = Join-Path $scriptRoot 'common/modules/MenuManager/MenuManager.psd1'
if (Test-Path -LiteralPath $menuManifest -PathType Leaf) {
    Import-Module -Name $menuManifest -Force -Prefix AgentInbox -Scope Local -ErrorAction Stop
}

function Invoke-AgentInboxScript {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$Arguments = @(),
        [hashtable]$Parameters = @{}
    )

    $target = Join-Path $scriptRoot (Join-Path 'archive' $Path)
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "AgentInbox command implementation was not found: $Path"
    }
    $isPython = $target.EndsWith('.py', [StringComparison]::OrdinalIgnoreCase)
    if ($isPython) {
        & python $target @Arguments
    } else {
        & $target @Parameters
    }
    if ($isPython -and $LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "Command failed: $Path (exit $LASTEXITCODE)" }
}

function Show-AgentInboxHelp {
    @'
AgentInbox command line

USAGE
  pwsh -NoProfile -File .\scripts\AgentInbox.ps1
  pwsh -NoProfile -File .\scripts\AgentInbox.ps1 -Command <command> [options]

COMMANDS
  validate          Restore, format, build, optionally test, and run repository checks
  package-portable  Build a Windows portable ZIP (win-x64 or win-arm64)
  package-msix      Build a manual Debug or Store MSIX under artifacts/debug or artifacts/store
  clean-artifacts   Remove generated files from artifacts/
  format            Format the solution; use -Check to verify without edits
  format-staged     Format staged C# files before commit
  install-hooks     Install the repository Git hooks; -Force replaces an existing hook path
  update-locks      Refresh portable dependency lock files for Windows runtimes
  repo-check        Check repository links and accidental local data
  export-notices    Generate docs/DEPENDENCIES.md; use -Check to verify it
  smoke-test       Run the CLI/MCP protocol smoke check against a published executable
  desktop-smoke     Launch a published desktop executable with synthetic local data
  provider-check    Run opt-in local read checks against an executable and real accounts
  test-provider-check  Run synthetic tests for the manual provider checker

OPTIONS
  -Runtime win-x64|win-arm64  Windows ZIP architecture (default: win-x64)
  -Channel Debug|Store        MSIX build destination (default: Debug)
  -Architecture x64|arm64     MSIX architecture (default: x64)
  -PackageVersion <version>   Optional four-part MSIX package version
  -CertificateThumbprint     Signing certificate for a signed MSIX
  -TimestampServer <URL>      RFC 3161 timestamp service for signed MSIX output
  -Unsigned                   Create an unsigned Debug MSIX
  -SkipUnitTests              Skip unit tests during validate
  -CheckFormatting            Verify formatting during validate
  -Check                      Verify formatting without changing files
  -NoRestore                  Skip restore for format
  -Force                      Replace a previously configured Git hooks path
  -Executable <path>          Published executable for smoke/provider commands
  -ToolArguments <args>        Arguments passed to the selected Python tool

EXAMPLES
  pwsh -NoProfile -File .\scripts\AgentInbox.ps1 -Command package-msix -Channel Debug -Architecture x64 -Unsigned
  pwsh -NoProfile -File .\scripts\AgentInbox.ps1 -Command package-msix -Channel Store -Architecture x64 -CertificateThumbprint <40-hex-digits> -TimestampServer https://timestamp.example.test

With no command, AgentInbox opens an interactive menu. Esc or 0 returns or closes it.
'@ | Write-Host
}

function Invoke-AgentInboxCommand {
    param([Parameter(Mandatory)][string]$Name)

    switch ($Name) {
        'help' { Show-AgentInboxHelp }
        'validate' {
            $scriptParameters = @{}
            if ($SkipUnitTests) { $scriptParameters.SkipUnitTests = $true }
            if ($CheckFormatting) { $scriptParameters.CheckFormatting = $true }
            Invoke-AgentInboxScript 'validate.ps1' -Parameters $scriptParameters
        }
        'package-portable' { Invoke-AgentInboxScript 'package-windows-portable.ps1' -Parameters @{ Runtime = $Runtime } }
        'package-msix' {
            $packageParameters = @{ Channel = $Channel; Architecture = $Architecture }
            if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) { $packageParameters.Version = $PackageVersion }
            if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) { $packageParameters.CertificateThumbprint = $CertificateThumbprint }
            if ($TimestampServer) { $packageParameters.TimestampServer = $TimestampServer }
            if (-not [string]::IsNullOrWhiteSpace($MakeAppxPath)) { $packageParameters.MakeAppxPath = $MakeAppxPath }
            if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) { $packageParameters.SignToolPath = $SignToolPath }
            if ($Unsigned) { $packageParameters.Unsigned = $true }
            Invoke-AgentInboxScript 'package-msix.ps1' -Parameters $packageParameters
        }
        'clean-artifacts' { Invoke-AgentInboxScript 'clean-artifacts.ps1' }
        'format' {
            $scriptParameters = @{}
            if ($Check) { $scriptParameters.Check = $true }
            if ($NoRestore) { $scriptParameters.NoRestore = $true }
            Invoke-AgentInboxScript 'format.ps1' -Parameters $scriptParameters
        }
        'format-staged' { Invoke-AgentInboxScript 'format-staged.ps1' }
        'install-hooks' {
            $scriptParameters = @{}
            if ($Force) { $scriptParameters.Force = $true }
            Invoke-AgentInboxScript 'install-git-hooks.ps1' -Parameters $scriptParameters
        }
        'update-locks' {
            Invoke-AgentInboxScript 'update-portable-locks.ps1' -Parameters @{ Runtime = @('win-x64', 'win-arm64') }
        }
        'repo-check' { Invoke-AgentInboxScript 'repo-check.py' }
        'export-notices' {
            $noticeArguments = @()
            if ($Check) { $noticeArguments += '--check' }
            $noticeArguments += $ToolArguments
            Invoke-AgentInboxScript 'export-notices.py' $noticeArguments
        }
        'smoke-test' {
            if ([string]::IsNullOrWhiteSpace($Executable)) { throw 'Supply the published executable path with -Executable.' }
            Invoke-AgentInboxScript 'smoke-test.py' (@($Executable) + $ToolArguments)
        }
        'desktop-smoke' {
            if ([string]::IsNullOrWhiteSpace($Executable)) { throw 'Supply the published desktop executable path with -Executable.' }
            Invoke-AgentInboxScript 'smoke-test-desktop.py' (@($Executable) + $ToolArguments)
        }
        'provider-check' {
            if ([string]::IsNullOrWhiteSpace($Executable)) { throw 'Supply the CLI executable path with -Executable.' }
            Invoke-AgentInboxScript 'real-provider-check.py' (@($Executable) + $ToolArguments)
        }
        'test-provider-check' { Invoke-AgentInboxScript 'test-real-provider-check.py' }
        default { throw "Unknown AgentInbox command: $Name" }
    }
}

function Show-AgentInboxMenu {
    $items = @(
        @{ Command = 'validate'; Label = 'Validate solution'; Detail = 'Build, tests, formatting and repository checks' },
        @{ Command = 'package-portable'; Label = 'Build portable ZIP'; Detail = "Windows $Runtime" },
        @{ Command = 'package-msix'; Label = 'Build Debug MSIX'; Detail = 'Output: artifacts/debug' },
        @{ Command = 'package-msix'; Label = 'Build Store MSIX'; Detail = 'Output: artifacts/store' },
        @{ Command = 'format'; Label = 'Format solution'; Detail = 'Apply C# formatting' },
        @{ Command = 'clean-artifacts'; Label = 'Clean artifacts'; Detail = 'Remove generated package and build outputs' },
        @{ Command = 'update-locks'; Label = 'Update Windows locks'; Detail = 'Restore x64 and ARM64 dependency graphs' },
        @{ Command = 'install-hooks'; Label = 'Install Git hooks'; Detail = 'Format staged C# files before commits' },
        @{ Command = 'help'; Label = 'Help'; Detail = 'Show CLI commands and options' }
    )

    while ($true) {
        Clear-Host
        Write-Host 'AgentInbox' -ForegroundColor Cyan
        Write-Host 'Portable packages, development and repository commands' -ForegroundColor DarkGray
        for ($index = 0; $index -lt $items.Count; $index++) {
            Write-Host ('  [{0}] {1,-22} {2}' -f ($index + 1), $items[$index].Label, $items[$index].Detail)
        }
        Write-Host '  [0] Exit'
        $selection = Read-AgentInboxIndexChoice -Min 1 -Max $items.Count
        if ($null -eq $selection) { return }
        if ($selection -eq '') { continue }
        try {
            if ($items[$selection - 1].Command -eq 'package-portable') {
                $runtimeChoice = Read-Host "Runtime (win-x64/win-arm64) [$Runtime]"
                if (-not [string]::IsNullOrWhiteSpace($runtimeChoice)) {
                    if ($runtimeChoice -notin @('win-x64', 'win-arm64')) { throw 'Choose win-x64 or win-arm64.' }
                    $script:Runtime = $runtimeChoice
                }
            }
            if ($items[$selection - 1].Label -eq 'Build Store MSIX') { $script:Channel = 'Store' }
            if ($items[$selection - 1].Label -eq 'Build Debug MSIX') { $script:Channel = 'Debug' }
            if ($items[$selection - 1].Command -eq 'package-msix') {
                $architectureChoice = Read-Host "Architecture (x64/arm64) [$Architecture]"
                if (-not [string]::IsNullOrWhiteSpace($architectureChoice)) {
                    if ($architectureChoice -notin @('x64', 'arm64')) { throw 'Choose x64 or arm64.' }
                    $script:Architecture = $architectureChoice
                }
                if (-not $Unsigned -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
                    $script:CertificateThumbprint = Read-Host 'Signing certificate thumbprint (40 hexadecimal characters)'
                    $script:TimestampServer = [uri](Read-Host 'RFC 3161 timestamp server URL')
                }
            }
            Invoke-AgentInboxCommand $items[$selection - 1].Command
        } catch {
            Write-Host $_ -ForegroundColor Red
        }
        if ($items[$selection - 1].Command -ne 'help') { [void](Read-Host 'Press Enter to return to the menu') }
    }
}

Push-Location $repoRoot
try {
    if ($Command -eq 'menu') {
        Show-AgentInboxMenu
    } else {
        Invoke-AgentInboxCommand $Command
    }
} finally {
    Pop-Location
}
