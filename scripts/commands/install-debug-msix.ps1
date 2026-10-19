#Requires -Version 7.0
# SPDX-License-Identifier: MIT
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Store')][string]$Channel = 'Debug',
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$Version,
    [Parameter(Mandatory)][string]$PythonArchive,
    [Parameter(Mandatory)][string]$PythonDirectory,
    [Parameter(Mandatory)][string]$PythonSource,
    [Parameter(Mandatory)][string]$PythonVersion,
    [Parameter(Mandatory)][string]$PythonSha256,
    [Parameter(Mandatory)][string]$DependencyManifest,
    [string]$CertificateThumbprint,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [switch]$Unsigned
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $Channel -ne 'Debug' -or $Unsigned) { throw 'Installation requires a signed local Windows Debug MSIX.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Version) {
    [xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
    $Version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText + '.0'
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repository 'packaging/windows/AppxManifest.xml') -Raw
$identity = $manifest.Package.Identity.Name
$publisher = $manifest.Package.Identity.Publisher
if (-not $CertificateThumbprint) {
    $certificates = @(Get-ChildItem Cert:/CurrentUser/My | Where-Object {
        $_.Subject -ceq $publisher -and $_.HasPrivateKey -and $_.Verify() -and
        @($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }).Count -gt 0
    })
    if ($certificates.Count -ne 1) { throw 'Select an existing trusted current-user code-signing certificate with -CertificateThumbprint.' }
    $CertificateThumbprint = $certificates[0].Thumbprint
}
$parameters = @{
    Channel = 'Debug'; Architecture = $Architecture; Version = $Version; PythonArchive = $PythonArchive
    PythonDirectory = $PythonDirectory; PythonSource = $PythonSource; PythonVersion = $PythonVersion
    PythonSha256 = $PythonSha256; DependencyManifest = $DependencyManifest; CertificateThumbprint = $CertificateThumbprint
    MakeAppxPath = $MakeAppxPath; SignToolPath = $SignToolPath
}
$packagePath = & (Join-Path $PSScriptRoot 'package-msix.ps1') @parameters
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw 'The signed Debug package was not created.' }
Add-AppxPackage -Path $packagePath -ErrorAction Stop
$installed = @(Get-AppxPackage -Name $identity)
if ($installed.Count -ne 1 -or $installed[0].Version -ne [version]$Version -or
    $installed[0].Architecture.ToString() -ine $Architecture -or $installed[0].Status -ne 'Ok') {
    throw 'Installation did not report the requested version, architecture and Ok status.'
}
[ordered]@{ version = $Version; architecture = $Architecture; status = 'Ok'; ui_tests = 'not run' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $packagePath) 'installation.json') -Encoding utf8NoBOM
Write-Output "Installed AgentInbox Debug $Version ($Architecture). Retained installer: $packagePath"
