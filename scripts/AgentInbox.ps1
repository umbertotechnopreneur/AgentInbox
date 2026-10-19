#Requires -Version 7.0
# SPDX-License-Identifier: MIT
[CmdletBinding()]
param(
    [ValidateSet('help', 'package-msix', 'build-store-msix', 'install-debug-msix', 'clean-artifacts')][string]$Command = 'help',
    [ValidateSet('Debug', 'Store')][string]$Channel = 'Debug',
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$Version,
    [string]$PythonRuntimesManifest,
    [string]$PythonArchive,
    [string]$PythonDirectory,
    [string]$PythonSource,
    [string]$PythonVersion,
    [string]$PythonSha256,
    [string]$DependencyManifest,
    [string]$CertificateThumbprint,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [string]$TaskDirectory,
    [switch]$Unsigned
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Command -eq 'help') {
    @'
AgentInbox source commands

package-msix       Build Windows 11 x64 or ARM64 MSIX. Debug is the default.
build-store-msix   Build unsigned local Release MSIX packages for x64 and ARM64 together.
install-debug-msix Build, sign and immediately install a Debug MSIX using an existing current-user certificate.
clean-artifacts    Remove only an explicitly named task directory under artifacts/work.

Packaging requires an existing Python 3.11+ embeddable archive and matching extracted directory:
  -PythonArchive <zip> -PythonDirectory <directory>
  -PythonSource <official python.org download URL> -PythonVersion <x.y.z>
  -PythonSha256 <expected SHA256> -DependencyManifest <JSON manifest>

The dependency manifest contains all required NuGet archives, including transitive dependencies:
  {"packages":[{"archive":"ABSOLUTE_PATH.nupkg","source":"OFFICIAL_NUGET_URL","sha256":"EXPECTED_SHA256"}]}

All archives and extracted contents receive targeted Defender scans before use.
Restore is offline and fails when a package is missing. Nothing is downloaded or installed implicitly.
Supply -CertificateThumbprint for signing, or -Unsigned for an unsigned build.
Store recipe: -Command build-store-msix -Version X.Y.Z -PythonRuntimesManifest <JSON> -DependencyManifest <JSON>.
The runtime manifest supplies x64 and arm64 archive/directory/source/version/sha256 entries.
Both architectures are required. Store builds run locally, without signing, installation or publication.
Optional for single-architecture packaging: -Architecture x64|arm64 -Version <four-part version> -MakeAppxPath <exe> -SignToolPath <exe>.
Cleanup: -Command clean-artifacts -TaskDirectory <exact artifacts/work/msix-ID directory>.
Existing packages and unrelated output are preserved.
'@ | Write-Output
    return
}
if ($Command -eq 'clean-artifacts') {
    & (Join-Path $PSScriptRoot 'commands/clean-artifacts.ps1') -TaskDirectory $TaskDirectory
    return
}
if ($Command -eq 'build-store-msix') {
    foreach ($name in @('Channel', 'Architecture', 'PythonArchive', 'PythonDirectory', 'PythonSource', 'PythonVersion',
        'PythonSha256', 'CertificateThumbprint', 'SignToolPath', 'TaskDirectory', 'Unsigned')) {
        if ($PSBoundParameters.ContainsKey($name)) { throw "$name is not supported by build-store-msix. Supply the two-runtime manifest instead." }
    }
    & (Join-Path $PSScriptRoot 'commands/build-store-msix.ps1') -Version $Version -PythonRuntimesManifest $PythonRuntimesManifest `
        -DependencyManifest $DependencyManifest -MakeAppxPath $MakeAppxPath
    return
}
if ($PSBoundParameters.ContainsKey('PythonRuntimesManifest')) { throw 'PythonRuntimesManifest is only supported by build-store-msix.' }
$parameters = @{
    Channel = $Channel; Architecture = $Architecture; Version = $Version
    PythonArchive = $PythonArchive; PythonDirectory = $PythonDirectory; PythonSource = $PythonSource
    PythonVersion = $PythonVersion; PythonSha256 = $PythonSha256; DependencyManifest = $DependencyManifest
    CertificateThumbprint = $CertificateThumbprint; MakeAppxPath = $MakeAppxPath; SignToolPath = $SignToolPath
    Unsigned = $Unsigned.IsPresent
}
& (Join-Path $PSScriptRoot "commands/$Command.ps1") @parameters
