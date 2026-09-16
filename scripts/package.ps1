[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$repoRoot = Split-Path -Parent $PSScriptRoot
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
$version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$artifactRoot = Join-Path $repoRoot 'artifacts'
$packageName = "mailmeup-$version-$Runtime"
$payload = Join-Path $artifactRoot $packageName

$hostOs = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if (-not $Runtime.StartsWith("$hostOs-")) { throw 'Package on the target operating system to preserve archive and executable behavior.' }
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$isNative = $Runtime -eq "$hostOs-$hostArchitecture"
$smokeStatus = if ($isNative) { 'native-before-and-after-extraction' } else { 'not-run-cross-architecture' }

Push-Location $repoRoot
try {
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'A Git commit is required for package provenance.' }
    $dirty = git status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git state.' }
    if ($dirty) { throw 'Commit or move aside repository changes before packaging. Artifacts are ignored.' }
    $runtimeLocks = Join-Path $repoRoot "eng/locks/$Runtime"
    foreach ($module in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Directory | Where-Object Name -ne 'MailMeUp.Desktop') {
        if (-not (Test-Path -LiteralPath (Join-Path $runtimeLocks "$($module.Name).json"))) { throw "Missing portable dependency graph for $($module.Name)." }
    }

    & (Join-Path $PSScriptRoot 'clean-artifacts.ps1')
    dotnet publish src/MailMeUp.Cli/MailMeUp.Cli.csproj -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false `
        -p:ContinuousIntegrationBuild=true -p:MailMeUpPortableBuild=true "-p:MailMeUpPortableRuntime=$Runtime" -p:RestoreLockedMode=true --output $payload
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Copy-Item -LiteralPath LICENSE, THIRD_PARTY_NOTICES.md, docs/DEPENDENCIES.md -Destination $payload
    Copy-Item -LiteralPath docs/RELEASE_README.md -Destination (Join-Path $payload 'README.md')
    Copy-Item -LiteralPath docs/licenses -Destination (Join-Path $payload 'licenses') -Recurse
    python scripts/export-notices.py --check --copy-to (Join-Path $payload 'licenses/packages')
    if ($LASTEXITCODE -ne 0) { throw 'Notice export failed.' }

    $executable = Join-Path $payload $(if ($IsWindows) { 'mailmeup.exe' } else { 'mailmeup' })
    if ($isNative) {
        python scripts/smoke-test.py $executable
        if ($LASTEXITCODE -ne 0) { throw 'Published executable smoke test failed.' }
    }
    "Version=$version`nRuntime=$Runtime`nCommit=$commit`nSmokeTest=$smokeStatus`nStage=read_only_mvp" |
        Set-Content -LiteralPath (Join-Path $payload 'BUILD_INFO.txt') -Encoding utf8NoBOM

    $extension = if ($IsWindows) { 'zip' } else { 'tar.gz' }
    $archive = Join-Path $artifactRoot "$packageName.$extension"
    if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
    if ($IsWindows) {
        Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive
    } else {
        tar -czf $archive -C $payload .
        if ($LASTEXITCODE -ne 0) { throw 'Archive creation failed.' }
    }
    if ($isNative) {
        $verificationRoot = Join-Path (Join-Path $artifactRoot 'verification') "$packageName-$([Guid]::NewGuid().ToString('N'))"
        $artifactPrefix = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $verificationPath = [IO.Path]::GetFullPath($verificationRoot)
        if (-not $verificationPath.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Archive verification path escaped the artifact directory.'
        }

        try {
            New-Item -ItemType Directory -Path $verificationPath -Force | Out-Null
            if ($IsWindows) {
                Expand-Archive -LiteralPath $archive -DestinationPath $verificationPath
            } else {
                tar -xzf $archive -C $verificationPath
                if ($LASTEXITCODE -ne 0) { throw 'Archive extraction failed.' }
            }

            $verifiedExecutable = Join-Path $verificationPath $(if ($IsWindows) { 'mailmeup.exe' } else { 'mailmeup' })
            python scripts/smoke-test.py $verifiedExecutable
            if ($LASTEXITCODE -ne 0) { throw 'Extracted executable smoke test failed.' }
        } finally {
            if (Test-Path -LiteralPath $verificationPath) {
                Remove-Item -LiteralPath $verificationPath -Recurse -Force
            }
        }
    }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath "$archive.sha256" -Encoding utf8NoBOM
    Write-Output "Created $archive (smoke: $smokeStatus)"
} finally {
    Pop-Location
}
