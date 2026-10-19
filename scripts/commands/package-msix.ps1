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
    [Parameter(Mandatory)][ValidatePattern('^3\.\d+\.\d+$')][string]$PythonVersion,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$PythonSha256,
    [Parameter(Mandatory)][string]$DependencyManifest,
    [string]$CertificateThumbprint,
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [switch]$Unsigned
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'MSIX packaging requires Windows and Microsoft Defender.' }
if ($Channel -eq 'Store' -and $env:GITHUB_ACTIONS -cne 'true') { throw 'Store MSIX packages may be created only in GitHub Actions.' }
if ([version]$PythonVersion -lt [version]'3.11.0') { throw 'Python 3.11 or later is required.' }
if ($Unsigned -and $CertificateThumbprint) { throw 'Choose either an unsigned package or a signing certificate.' }
if (-not $Unsigned -and -not $CertificateThumbprint) { throw 'Provide an existing signing certificate or explicitly request -Unsigned.' }

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifacts = Join-Path $repository 'artifacts'
$taskName = 'msix-' + [guid]::NewGuid().ToString('N')
$taskRoot = Join-Path $artifacts "work/$taskName"
$runtime = "win-$Architecture"
$configuration = if ($Channel -eq 'Store') { 'Release' } else { 'Debug' }
$project = Join-Path $repository 'src/MailMeUp.Desktop/MailMeUp.Desktop.csproj'
$records = [Collections.Generic.List[object]]::new()
$imports = [Collections.Generic.List[object]]::new()
$publishStarted = $false
$succeeded = $false
$cleanupFailure = $null
$originalEnvironment = @{}

function Assert-RegularTree([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $current = Get-Item -LiteralPath $resolved -Force
    while ($null -ne $current) {
        if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked import/output paths are not accepted: $resolved" }
        $current = if ($current -is [IO.FileInfo]) { $current.Directory } else { $current.Parent }
    }
    $rootItem = Get-Item -LiteralPath $resolved -Force
    if ($rootItem.PSIsContainer) {
        $queue = [Collections.Generic.Queue[string]]::new()
        $queue.Enqueue($resolved)
        while ($queue.Count -gt 0) {
            foreach ($child in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
                if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked content is not accepted: $($child.FullName)" }
                if ($child.PSIsContainer) { $queue.Enqueue($child.FullName) }
            }
        }
    }
    return $resolved
}

function Assert-OutputAncestors([string]$Path) {
    $candidate = [IO.Path]::GetFullPath($Path)
    while (-not (Test-Path -LiteralPath $candidate)) {
        $candidate = [IO.Path]::GetDirectoryName($candidate)
        if (-not $candidate) { throw 'The output directory has no accessible parent.' }
    }
    $current = Get-Item -LiteralPath $candidate -Force
    while ($null -ne $current) {
        if (-not $current.PSIsContainer -or ($current.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Output directories must have regular, non-linked parent directories.'
        }
        $current = $current.Parent
    }
}

function Resolve-SdkTool([string]$Name, [string]$ExplicitPath) {
    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) { throw "Missing Windows SDK tool: $Name" }
        return Assert-RegularTree $ExplicitPath
    }
    $sdkRoot = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin'
    $hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    foreach ($directory in Get-ChildItem -LiteralPath $sdkRoot -Directory | Sort-Object Name -Descending) {
        $candidate = Join-Path $directory.FullName "$hostArchitecture/$Name"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return Assert-RegularTree $candidate }
    }
    throw "Install the Windows SDK or supply the path of $Name. This script does not install tools."
}

function Invoke-TargetedScan([string]$Target) {
    $targetPath = Assert-RegularTree $Target
    $status = Get-MpComputerStatus -ErrorAction Stop
    if (-not $status.AMServiceEnabled -or -not $status.AntivirusEnabled) { throw 'Microsoft Defender is unavailable or disabled. Import stopped.' }
    $log = Join-Path $taskRoot ('scan-' + $records.Count + '.log')
    $started = [DateTime]::UtcNow
    $arguments = @('-Scan', '-ScanType', '3', '-File', $targetPath, '-DisableRemediation')
    & $script:defender @arguments *> $log
    $code = $LASTEXITCODE
    $record = [ordered]@{
        executable = $script:defender; arguments = $arguments
        started_utc = $started.ToString('o'); completed_utc = [DateTime]::UtcNow.ToString('o')
        exit_code = $code; result = $(if ($code -eq 0) { 'completed_without_detections' } else { 'failed_or_detected' })
        signature_version = $status.AntivirusSignatureVersion
        output_tail = @(Get-Content -LiteralPath $log -Tail 12)
    }
    $records.Add($record)
    # DisableRemediation ignores exclusions, scans archives, and cannot turn a detection into a remediated success.
    # Official behavior: https://learn.microsoft.com/defender-endpoint/command-line-arguments-microsoft-defender-antivirus
    if ($code -ne 0) { throw "Targeted Defender scan failed or detected a threat. Import stopped; scan log: $log" }
}

function Confirm-Archive([string]$Path, [string]$ExpectedHash, [string]$Source, [string[]]$Hosts) {
    $uri = [uri]$Source
    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -cne 'https' -or $uri.Host -notin $Hosts -or $uri.UserInfo) {
        throw 'Every import must name an official HTTPS download source.'
    }
    $archive = Assert-RegularTree $Path
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw 'An import archive must be a file.' }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ExpectedHash -notmatch '^[a-fA-F0-9]{64}$' -or $hash -ine $ExpectedHash) { throw "Import SHA-256 did not match: $archive" }
    Invoke-TargetedScan $archive
    $imports.Add([ordered]@{ archive = [IO.Path]::GetFileName($archive); source = $Source; sha256 = $hash })
    return $archive
}

function Assert-ArchiveMatchesDirectory([string]$Archive, [string]$Directory) {
    $folder = Assert-RegularTree $Directory
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw 'The extracted Python runtime must be a directory.' }
    $prefix = $folder.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $zip.Entries) {
            if (-not $entry.Name) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $folder $entry.FullName))
            if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not $names.Add($target)) {
                throw 'The runtime archive contains an invalid or duplicate file path.'
            }
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'The extracted runtime does not match its archive.' }
            $stream = $entry.Open()
            try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $expected) { throw 'An extracted Python file differs from its archive.' }
        }
        foreach ($file in Get-ChildItem -LiteralPath $folder -File -Force -Recurse) {
            if (-not $names.Contains($file.FullName)) { throw 'The Python directory contains files absent from its official archive.' }
        }
    } finally { $zip.Dispose() }
}

function Write-Logo([string]$Source, [string]$Destination, [int]$Size) {
    $image = [Drawing.Image]::FromFile($Source)
    $bitmap = [Drawing.Bitmap]::new($Size, $Size)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($image, [Drawing.Rectangle]::new(0, 0, $Size, $Size))
        } finally { $graphics.Dispose() }
        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose(); $image.Dispose() }
}

[xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
if (-not $Version) { $Version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText + '.0' }
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or [version]$Version -eq [version]'0.0.0.0') { throw 'Choose a nonzero four-part MSIX version.' }
foreach ($part in $Version.Split('.')) { if ([int]$part -gt 65535) { throw 'MSIX version components must not exceed 65535.' } }
$destination = Join-Path $artifacts "msix/$($Channel.ToLowerInvariant())/$Architecture/$Version"
if (Test-Path -LiteralPath $destination) { throw 'This package destination already exists. Preserve it and choose a new version.' }
foreach ($parentPath in @($artifacts, (Join-Path $artifacts 'work'), (Split-Path -Parent $destination))) {
    Assert-OutputAncestors $parentPath
    New-Item -ItemType Directory -Path $parentPath -Force | Out-Null
}
& (Join-Path $PSScriptRoot 'clean-artifacts.ps1') -TaskDirectory $taskRoot
New-Item -ItemType Directory -Path $taskRoot | Out-Null
Set-Content -LiteralPath (Join-Path $taskRoot '.agentinbox-task') -Value $taskName -Encoding utf8NoBOM
New-Item -ItemType Directory -Path $destination | Out-Null
$payload = Join-Path $taskRoot 'payload'
$packages = Join-Path $taskRoot 'packages'
$feed = Join-Path $taskRoot 'feed'
$extracted = Join-Path $taskRoot 'extracted'
$obj = (Join-Path $taskRoot 'obj') + [IO.Path]::DirectorySeparatorChar
$bin = (Join-Path $taskRoot 'bin') + [IO.Path]::DirectorySeparatorChar
$packagePath = Join-Path $destination "agentinbox-$Version-$runtime$(if ($Unsigned) { '.unsigned' }).msix"
$recordPath = Join-Path $destination 'package-record.json'
$buildLog = Join-Path $taskRoot 'build.log'
Push-Location $repository
try {
    $platform = Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'
    $script:defender = Get-ChildItem -LiteralPath $platform -Filter MpCmdRun.exe -File -Recurse |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $script:defender) { throw 'A current Microsoft Defender command-line scanner is required.' }
    foreach ($directory in @($payload, $packages, $feed, $extracted, (Join-Path $taskRoot 'temp'))) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
    $expectedName = "python-$PythonVersion-embed-$(if ($Architecture -eq 'x64') { 'amd64' } else { 'arm64' }).zip"
    if ([uri]::UnescapeDataString(([uri]$PythonSource).AbsolutePath) -cne "/ftp/python/$PythonVersion/$expectedName") {
        throw 'The Python source must name the matching official embeddable archive for this version and architecture.'
    }
    $archive = Confirm-Archive $PythonArchive $PythonSha256 $PythonSource @('www.python.org', 'python.org')
    Invoke-TargetedScan $PythonDirectory
    Assert-ArchiveMatchesDirectory $archive $PythonDirectory
    $pythonExecutable = Join-Path $PythonDirectory 'python.exe'
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($pythonExecutable))
    try {
        $reader.BaseStream.Position = 0x3c
        $reader.BaseStream.Position = $reader.ReadInt32() + 4
        $machine = $reader.ReadUInt16()
    } finally { $reader.Dispose() }
    if ($machine -ne $(if ($Architecture -eq 'x64') { 0x8664 } else { 0xaa64 })) { throw 'Python runtime architecture does not match the requested MSIX.' }

    $dependencyList = Get-Content -LiteralPath $DependencyManifest -Raw | ConvertFrom-Json -Depth 10
    if ($null -eq $dependencyList.packages -or @($dependencyList.packages).Count -eq 0) { throw 'Supply a complete offline NuGet dependency manifest.' }
    foreach ($dependency in $dependencyList.packages) {
        $source = Confirm-Archive $dependency.archive $dependency.sha256 $dependency.source @('api.nuget.org', 'www.nuget.org', 'globalcdn.nuget.org')
        if ([IO.Path]::GetExtension($source) -ine '.nupkg') { throw 'NuGet dependency archives must use the .nupkg extension.' }
        $packageName = [IO.Path]::GetFileName($source)
        $folder = Join-Path $extracted ([IO.Path]::GetFileNameWithoutExtension($source))
        if (Test-Path -LiteralPath $folder) { throw "Duplicate dependency archive name: $packageName" }
        [IO.Compression.ZipFile]::ExtractToDirectory($source, $folder)
        Invoke-TargetedScan $folder
        Copy-Item -LiteralPath $source -Destination (Join-Path $feed $packageName)
    }
    $makeAppx = Resolve-SdkTool 'MakeAppx.exe' $MakeAppxPath
    Invoke-TargetedScan (Split-Path -Parent $makeAppx)
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    Invoke-TargetedScan (Split-Path -Parent $dotnet)
    $signTool = $null
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $repository 'packaging/windows/AppxManifest.xml') -Raw
    if (-not $Unsigned) {
        $certificate = Get-Item -LiteralPath "Cert:/CurrentUser/My/$CertificateThumbprint"
        if (-not $certificate.HasPrivateKey -or $certificate.Subject -cne $manifest.Package.Identity.Publisher -or
            [DateTime]::Now -lt $certificate.NotBefore -or [DateTime]::Now -gt $certificate.NotAfter) {
            throw 'Use a valid current-user signing certificate matching the MSIX publisher.'
        }
        $signTool = Resolve-SdkTool 'SignTool.exe' $SignToolPath
        if ((Split-Path -Parent $signTool) -ine (Split-Path -Parent $makeAppx)) { Invoke-TargetedScan (Split-Path -Parent $signTool) }
    }
    foreach ($key in @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH', 'DOTNET_CLI_HOME', 'TEMP', 'TMP', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE')) {
        $originalEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    }
    $env:NUGET_PACKAGES = $packages
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $taskRoot 'nuget-http'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $taskRoot 'nuget-plugins'
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot 'dotnet-home'
    $env:TEMP = Join-Path $taskRoot 'temp'
    $env:TMP = $env:TEMP
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    $nugetConfig = Join-Path $taskRoot 'NuGet.Config'
    $feedXml = [Security.SecurityElement]::Escape($feed)
    "<configuration><packageSources><clear/><add key=`"scanned-offline-feed`" value=`"$feedXml`"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>" |
        Set-Content -LiteralPath $nugetConfig -Encoding utf8NoBOM
    $platformName = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
    $buildProperties = @("-p:BaseIntermediateOutputPath=$obj", "-p:MSBuildProjectExtensionsPath=$obj", "-p:BaseOutputPath=$bin", "-p:Platform=$platformName",
        '-p:RestorePackagesWithLockFile=false', '-p:RestoreLockedMode=false', '-p:NuGetAudit=false', '-p:RestoreFallbackFolders=',
        '-p:RestoreAdditionalProjectSources=', '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false')
    & $dotnet restore $project --runtime $runtime --configfile $nugetConfig --packages $packages @buildProperties *> $buildLog
    if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed. Supply the missing pinned dependencies; no network fallback is allowed.' }
    Invoke-TargetedScan $packages
    $publishStarted = $true
    & $dotnet publish $project --configuration $configuration --runtime $runtime --self-contained true --no-restore --output $payload @buildProperties *>> $buildLog
    if ($LASTEXITCODE -ne 0) { throw 'WinUI publish failed.' }
    $runtimeDirectory = Join-Path $payload 'runtime'
    Copy-Item -LiteralPath $PythonDirectory -Destination $runtimeDirectory -Recurse
    $pth = Join-Path $runtimeDirectory ("python" + ([version]$PythonVersion).Major + ([version]$PythonVersion).Minor + '._pth')
    if (-not (Test-Path -LiteralPath $pth -PathType Leaf)) { throw 'The official embedded runtime path configuration is missing.' }
    $runtimePaths = (Get-Content -LiteralPath $pth -Raw).TrimEnd() + [Environment]::NewLine + '../Python' + [Environment]::NewLine
    Set-Content -LiteralPath $pth -Value $runtimePaths -Encoding utf8NoBOM -NoNewline
    $assets = Join-Path $payload 'Assets'
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    Add-Type -AssemblyName System.Drawing
    foreach ($logo in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
        Write-Logo (Join-Path $repository 'resources/agentinbox-logo-original.png') (Join-Path $assets $logo[0]) $logo[1]
    }
    $manifest.Package.Identity.SetAttribute('Version', $Version)
    $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $Architecture)
    $manifest.Save((Join-Path $payload 'AppxManifest.xml'))
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE'), (Join-Path $repository 'THIRD_PARTY_NOTICES.md') -Destination $payload
    $licenses = Join-Path $payload 'licenses'
    New-Item -ItemType Directory -Path $licenses | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $extracted -File -Recurse | Where-Object { $_.Name -match '(?i)^(license|notice|third.party)' }) {
        $relative = [IO.Path]::GetRelativePath($extracted, $file.FullName)
        $target = Join-Path $licenses $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    Invoke-TargetedScan $payload
    & $makeAppx pack /d $payload /p $packagePath *> (Join-Path $taskRoot 'package.log')
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx did not create a valid package.' }
    if ($signTool) {
        & $signTool sign /sha1 $CertificateThumbprint /s My /fd SHA256 $packagePath *>> (Join-Path $taskRoot 'package.log')
        if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    }
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($packagePath))" | Set-Content -LiteralPath "$packagePath.sha256" -Encoding utf8NoBOM
    $succeeded = $true
} catch {
    $failure = $_.Exception.Message
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
    [ordered]@{ failure = $failure; diagnostics = @($(if (Test-Path -LiteralPath $buildLog) { Get-Content -LiteralPath $buildLog -Tail 30 })) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'failure.json') -Encoding utf8NoBOM
    throw
} finally {
    if ($publishStarted) {
        try {
            & $dotnet clean $project --configuration $configuration --runtime $runtime "-p:PublishDir=$payload" @buildProperties *> (Join-Path $taskRoot 'clean.log')
            if ($LASTEXITCODE -ne 0) { $cleanupFailure = 'dotnet clean returned a failure; task-owned intermediates were still removed where possible.' }
        } catch { $cleanupFailure = 'dotnet clean could not complete; task-owned intermediates were still removed where possible.' }
    }
    foreach ($key in $originalEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $originalEnvironment[$key], 'Process') }
    Pop-Location
    [ordered]@{
        package_version = $Version; architecture = $Architecture; channel = $Channel; configuration = $configuration
        python_version = $PythonVersion; package_created = $succeeded; signed = (-not $Unsigned -and $succeeded)
        tests = 'not run'; installation = 'not run by packaging'; published = $false
        imports = $imports; defender_scans = $records; cleanup_warning = $cleanupFailure
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $recordPath -Encoding utf8NoBOM
    try { & (Join-Path $PSScriptRoot 'clean-artifacts.ps1') -TaskDirectory $taskRoot | Write-Host }
    catch { Write-Warning "Cleanup incomplete at $taskRoot. $($_.Exception.Message)"; throw }
    if ($cleanupFailure) { Write-Warning $cleanupFailure }
}
if ($succeeded) { Write-Output $packagePath }
