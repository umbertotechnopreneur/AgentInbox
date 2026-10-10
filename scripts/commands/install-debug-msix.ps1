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
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [string]$MakeAppxPath,
    [string]$SignToolPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Debug MSIX installation requires Windows.' }

$parsedVersion = [version]$Version
$versionComponents = @($parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build, $parsedVersion.Revision)
if (@($versionComponents | Where-Object { $_ -gt 65535 }).Count -gt 0 -or $parsedVersion -eq [version]'0.0.0.0') {
    throw 'Choose a nonzero four-part MSIX version with each component at most 65535.'
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot 'packaging\windows\AppxManifest.xml') -Raw
$packageName = $manifest.Package.Identity.GetAttribute('Name')
$publisher = $manifest.Package.Identity.GetAttribute('Publisher')
$installed = @(Get-AppxPackage -Name $packageName)
if ($installed.Count -gt 1) { throw 'More than one current-user AgentInbox package was found.' }
if ($installed.Count -eq 1 -and [version]$installed[0].Version -ge $parsedVersion) {
    throw "The installed version is $($installed[0].Version). Choose a higher Debug MSIX version."
}

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $certificates = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object {
        $_.Subject -ceq $publisher -and $_.HasPrivateKey -and $_.Verify() -and
        @($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }).Count -gt 0
    })
    if ($certificates.Count -ne 1) {
        throw 'Expected one trusted current-user code-signing certificate for AgentInbox; supply -CertificateThumbprint if there are several.'
    }
    $certificate = $certificates[0]
}
else {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
}
if ($certificate.Subject -cne $publisher -or -not $certificate.HasPrivateKey -or -not $certificate.Verify() -or
    @($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }).Count -eq 0) {
    throw 'The signing certificate must be trusted, usable for code signing, and match the AgentInbox publisher.'
}

$runtime = "win-$Architecture"
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $artifactsRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to delete an artifacts path outside the repository: $artifactsRoot"
}
if (Test-Path -LiteralPath $artifactsRoot) {
    if (((Get-Item -LiteralPath $artifactsRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to delete a linked artifacts directory: $artifactsRoot"
    }
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}
$packageDirectory = Join-Path $repositoryRoot "artifacts\debug\$Version\$Architecture"
if (Test-Path -LiteralPath $packageDirectory) {
    throw "Package output already exists; choose a new version: $packageDirectory"
}
$logDirectory = Join-Path $repositoryRoot 'artifacts\logs\debug-msix'
$packageLog = Join-Path $logDirectory "$Version-$Architecture.log"
[IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$packageStarted = $false

try {
    $packageArguments = @{
        Channel = 'Debug'
        Architecture = $Architecture
        Version = $Version
        CertificateThumbprint = $certificate.Thumbprint
        SkipArtifactCleanup = $true
    }
    if ($MakeAppxPath) { $packageArguments.MakeAppxPath = $MakeAppxPath }
    if ($SignToolPath) { $packageArguments.SignToolPath = $SignToolPath }
    $packageStarted = $true
    & (Join-Path $PSScriptRoot 'package-msix.ps1') @packageArguments *> $packageLog

    $packagePath = Join-Path $packageDirectory "agentinbox-$Version-$runtime.msix"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Debug MSIX packaging failed. See $packageLog"
    }
    Add-AppxPackage -Path $packagePath -ForceUpdateFromAnyVersion -ForceApplicationShutdown -ErrorAction Stop

    $registered = Get-AppxPackage -Name $packageName
    if ($null -eq $registered -or [version]$registered.Version -ne $parsedVersion -or
        $registered.Status -ne 'Ok' -or $registered.Architecture.ToString() -ine $Architecture) {
        throw "The installed AgentInbox package did not report version $Version, $Architecture, and Ok status."
    }
    Write-Output "Installed AgentInbox Debug MSIX $Version ($Architecture): $packagePath"
}
finally {
    if ($packageStarted) {
        foreach ($project in @('src\MailMeUp.Desktop\MailMeUp.Desktop.csproj', 'src\MailMeUp.Cli\MailMeUp.Cli.csproj')) {
            try {
                & dotnet clean (Join-Path $repositoryRoot $project) --configuration Debug --runtime $runtime --verbosity quiet
                if ($LASTEXITCODE -ne 0) { Write-Warning "Debug build cleanup failed for $project." }
            }
            catch { Write-Warning "Debug build cleanup failed for $project`: $($_.Exception.Message)" }
        }
    }
}
