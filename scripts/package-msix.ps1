#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',
    [string]$PackageVersion,
    [string]$Publisher = 'CN=Umberto Giacobbi',
    [string]$PublisherDisplayName = 'Umberto Giacobbi',
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$CertificateThumbprint,
    [uri]$TimestampServer
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'MSIX packaging requires Windows.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$runtime = "win-$Architecture"

function Resolve-WindowsSdkTool {
    param([string]$Name, [string]$ExplicitPath)

    if ($ExplicitPath) {
        $item = Get-Item -LiteralPath $ExplicitPath
        if ($item.PSIsContainer) { throw "Expected a tool executable: $ExplicitPath" }
        return $item.FullName
    }
    $command = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $sdkBin = Join-Path $programFilesX86 'Windows Kits/10/bin'
    if (Test-Path -LiteralPath $sdkBin) {
        $versions = Get-ChildItem -LiteralPath $sdkBin -Directory |
            Where-Object Name -Match '^10\.0\.\d+\.\d+$' |
            Sort-Object { [version]$_.Name } -Descending
        $hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
        foreach ($versionDirectory in $versions) {
            foreach ($toolArchitecture in @($hostArchitecture, 'x64') | Select-Object -Unique) {
                $candidate = Join-Path $versionDirectory.FullName "$toolArchitecture/$Name"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
            }
        }
    }
    throw "$Name was not found on PATH or in Windows Kits/10/bin. Supply its explicit path or install the Windows SDK tools."
}

function Write-PackageLogo {
    param([System.Drawing.Image]$SourceImage, [string]$Destination, [int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($SourceImage, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
        } finally {
            $graphics.Dispose()
        }
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }
}

function Copy-DependencyNotices {
    param([string[]]$AssetFiles, [string]$Destination)

    $packages = @{}
    foreach ($assetFile in $AssetFiles) {
        $graph = Get-Content -LiteralPath $assetFile -Raw | ConvertFrom-Json -AsHashtable
        foreach ($entry in $graph.libraries.GetEnumerator()) {
            if ($entry.Value.type -ne 'package' -or $packages.ContainsKey($entry.Key)) { continue }
            $packageFolder = $null
            foreach ($packageRoot in $graph.packageFolders.Keys) {
                $candidate = Join-Path $packageRoot $entry.Value.path
                if (Test-Path -LiteralPath $candidate -PathType Container) {
                    $packageFolder = $candidate
                    break
                }
            }
            if (-not $packageFolder) { throw "Cannot include dependency notices for $($entry.Key): restored package not found." }
            $packages[$entry.Key] = $packageFolder
        }
    }

    $rows = [System.Collections.Generic.List[string]]::new()
    $rows.Add('# MSIX dependency inventory')
    $rows.Add('')
    $rows.Add('Generated from the published desktop and CLI dependency graphs. Includes build dependencies; the self-contained .NET and Windows App SDK runtime payloads also carry their notices.')
    $rows.Add('')
    $rows.Add('| Package | Version | Declared license |')
    $rows.Add('| --- | --- | --- |')
    foreach ($key in $packages.Keys | Sort-Object) {
        $packageFolder = $packages[$key]
        $parts = $key.Split('/')
        $noticeDirectory = Join-Path $Destination "packages/$($parts[0]).$($parts[1])"
        New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
        $specification = Get-ChildItem -LiteralPath $packageFolder -Filter '*.nuspec' -File | Select-Object -First 1
        if (-not $specification) { throw "Missing dependency metadata for $key." }
        Copy-Item -LiteralPath $specification.FullName -Destination $noticeDirectory
        [xml]$spec = Get-Content -LiteralPath $specification.FullName -Raw
        $license = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="license"]')
        $licenseUrl = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="licenseUrl"]')
        $declaredLicense = if ($license) { $license.InnerText } elseif ($licenseUrl) { $licenseUrl.InnerText } else { 'See package metadata; license review required.' }
        $rows.Add("| $($parts[0]) | $($parts[1]) | $declaredLicense |")

        foreach ($noticeFile in Get-ChildItem -LiteralPath $packageFolder -File | Where-Object Name -Match '(?i)license|notice|copying') {
            Copy-Item -LiteralPath $noticeFile.FullName -Destination $noticeDirectory
        }
        if ($license -and $license.GetAttribute('type') -eq 'file') {
            $licensePath = [IO.Path]::GetFullPath((Join-Path $packageFolder $license.InnerText))
            $packagePrefix = [IO.Path]::GetFullPath($packageFolder).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $licensePath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid dependency license path for $key." }
            Copy-Item -LiteralPath $licensePath -Destination $noticeDirectory
        }
    }
    $rows | Set-Content -LiteralPath (Join-Path $Destination 'DEPENDENCIES.md') -Encoding utf8NoBOM
}

[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$productVersion = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $PackageVersion) { $PackageVersion = "$productVersion.0" }
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'PackageVersion must contain four numeric components, for example 0.1.1.0.' }
$parsedVersion = [version]$PackageVersion
foreach ($component in @($parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build, $parsedVersion.Revision)) {
    if ($component -gt 65535) { throw 'Each MSIX version component must be between 0 and 65535.' }
}
if ($parsedVersion.Major -eq 0 -and $parsedVersion.Minor -eq 0 -and $parsedVersion.Build -eq 0 -and $parsedVersion.Revision -eq 0) {
    throw 'MSIX version 0.0.0.0 is not permitted.'
}
if ([string]::IsNullOrWhiteSpace($Publisher)) { throw 'Publisher must be the signing identity distinguished name.' }
$makeAppx = Resolve-WindowsSdkTool -Name 'MakeAppx.exe' -ExplicitPath $MakeAppxPath
$signTool = $null
if ($CertificateThumbprint) {
    if (-not $TimestampServer -or $TimestampServer.Scheme -notin @('http', 'https')) {
        throw 'Signing requires an explicit HTTP(S) RFC 3161 TimestampServer.'
    }
    $certificate = Get-Item -LiteralPath "Cert:/CurrentUser/My/$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey) { throw 'The selected certificate does not have an accessible private key.' }
    if ($certificate.Subject -cne $Publisher) { throw 'Publisher must exactly match the selected signing certificate Subject.' }
    $signTool = Resolve-WindowsSdkTool -Name 'SignTool.exe' -ExplicitPath $SignToolPath
} elseif ($TimestampServer) {
    throw 'TimestampServer requires CertificateThumbprint.'
}

$packageName = "mailmeup-$PackageVersion-$runtime"
$artifactRoot = Join-Path $repoRoot "artifacts/msix/$packageName"
& (Join-Path $PSScriptRoot 'clean-artifacts.ps1')
$payload = Join-Path $artifactRoot 'payload'
$cliPayload = Join-Path $payload 'cli'
New-Item -ItemType Directory -Path $cliPayload -Force | Out-Null
$previousCliLanguage = $env:DOTNET_CLI_UI_LANGUAGE
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
Push-Location $repoRoot
try {
    # Packaging deliberately does not run the test, smoke, formatting or repository validation scripts.
    # MSIX restores use their own runtime graphs and preserve the normal cross-platform lock files.
    dotnet publish src/MailMeUp.Desktop/MailMeUp.Desktop.csproj -c Release -r $runtime --self-contained true `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false `
        -p:MailMeUpPortableBuild=true "-p:MailMeUpPortableRuntime=msix/$runtime" -p:RestoreLockedMode=false --output $payload
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    dotnet publish src/MailMeUp.Cli/MailMeUp.Cli.csproj -c Release -r $runtime --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
        -p:GenerateDocumentationFile=false -p:MailMeUpPortableBuild=true `
        "-p:MailMeUpPortableRuntime=msix/$runtime" -p:RestoreLockedMode=false --output $cliPayload
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }

    $assetsDirectory = Join-Path $payload 'Assets'
    New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
    Add-Type -AssemblyName System.Drawing
    # Package tiles are already rendered into exact Windows asset sizes, so use the tighter
    # original artwork. The more generously padded asset remains appropriate inside the app.
    $sourceLogo = [System.Drawing.Image]::FromFile((Join-Path $repoRoot 'resources/mailmeup-logo-original.png'))
    try {
        foreach ($asset in @(
            @{ Name = 'StoreLogo.png'; Size = 50 },
            @{ Name = 'Square44x44Logo.png'; Size = 44 },
            @{ Name = 'Square150x150Logo.png'; Size = 150 }
        )) {
            Write-PackageLogo -SourceImage $sourceLogo -Destination (Join-Path $assetsDirectory $asset.Name) -Size $asset.Size
        }
    } finally {
        $sourceLogo.Dispose()
    }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows/AppxManifest.xml') -Raw
    $manifest.Package.Identity.SetAttribute('Version', $PackageVersion)
    $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $Architecture)
    $manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
    $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    $manifest.Save((Join-Path $payload 'AppxManifest.xml'))
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/licenses') -Destination (Join-Path $payload 'licenses') -Recurse
    Copy-DependencyNotices -AssetFiles @(
        (Join-Path $repoRoot 'src/MailMeUp.Desktop/obj/project.assets.json'),
        (Join-Path $repoRoot 'src/MailMeUp.Cli/obj/project.assets.json')
    ) -Destination (Join-Path $payload 'licenses')

    @(
        "Version=$productVersion",
        "PackageVersion=$PackageVersion",
        "Runtime=$runtime",
        'Stage=desktop_onboarding_source_preview',
        'Tests=not-run-by-packaging',
        'InstallUpgradeAliasChecks=not-run-by-packaging'
    ) | Set-Content -LiteralPath (Join-Path $payload 'BUILD_INFO.txt') -Encoding utf8NoBOM

    $suffix = if ($CertificateThumbprint) { '.msix' } else { '.unsigned.msix' }
    $packagePath = Join-Path $artifactRoot "$packageName$suffix"
    & $makeAppx pack /d $payload /p $packagePath
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx packaging failed.' }
    if ($CertificateThumbprint) {
        & $signTool sign /sha1 $CertificateThumbprint /s My /fd SHA256 /tr $TimestampServer.AbsoluteUri /td SHA256 $packagePath
        if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed. The output must not be distributed as signed.' }
        $publicCertificatePath = Join-Path $artifactRoot "$packageName.cer"
        [IO.File]::WriteAllBytes($publicCertificatePath, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Write-Output "Created signed MSIX: $packagePath. Install and upgrade behavior has not been tested by this script."
        Write-Output "Exported public signing certificate: $publicCertificatePath. The certificate store and trust settings were not changed."
    } else {
        Write-Output "Created unsigned MSIX: $packagePath. It cannot be installed normally until signed with a certificate trusted by the target device."
    }
} finally {
    Pop-Location
    $env:DOTNET_CLI_UI_LANGUAGE = $previousCliLanguage
}
