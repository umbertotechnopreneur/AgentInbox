#Requires -Version 7.0
# SPDX-License-Identifier: MIT
[CmdletBinding()]
param(
    [ValidateSet('help', 'package-msix', 'install-debug-msix', 'clean-artifacts')][string]$Command = 'help',
    [ValidateSet('Debug', 'Store')][string]$Channel = 'Debug',
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$PackageVersion,
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
Store packages can be created only in GitHub Actions. This command never publishes.
Optional: -Architecture x64|arm64 -PackageVersion <four-part version> -MakeAppxPath <exe> -SignToolPath <exe>.
Cleanup: -Command clean-artifacts -TaskDirectory <exact artifacts/work/msix-ID directory>.
Existing packages and unrelated output are preserved.
'@ | Write-Output
    return
}
if ($Command -eq 'clean-artifacts') {
    & (Join-Path $PSScriptRoot 'commands/clean-artifacts.ps1') -TaskDirectory $TaskDirectory
    return
}
$parameters = @{
    Channel = $Channel; Architecture = $Architecture; Version = $PackageVersion
    PythonArchive = $PythonArchive; PythonDirectory = $PythonDirectory; PythonSource = $PythonSource
    PythonVersion = $PythonVersion; PythonSha256 = $PythonSha256; DependencyManifest = $DependencyManifest
    CertificateThumbprint = $CertificateThumbprint; MakeAppxPath = $MakeAppxPath; SignToolPath = $SignToolPath
    Unsigned = $Unsigned.IsPresent
}
& (Join-Path $PSScriptRoot "commands/$Command.ps1") @parameters
