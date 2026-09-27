#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Build Windows portable packages on Windows.' }
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $PSScriptRoot '..\common\windows-package-support.ps1')
$previousCliLanguage = $env:DOTNET_CLI_UI_LANGUAGE
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
Push-Location $repoRoot
try {
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'A Git commit is required for package provenance.' }
    $dirty = git status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git state.' }
    if ($dirty) { throw 'Commit or move aside repository changes before packaging. Artifacts are ignored.' }
    [xml]$properties = Get-Content -LiteralPath 'Directory.Build.props'
    $version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    $packageName = "agentinbox-$version-$Runtime"
    $artifactRoot = Join-Path $repoRoot 'artifacts'
    $payload = Join-Path $artifactRoot $packageName
    $cliPayload = Join-Path $payload 'cli'
    $lockFamily = "windows-portable/$Runtime"
    foreach ($module in Get-ChildItem -LiteralPath 'src' -Directory) {
        if (-not (Test-Path -LiteralPath "eng/locks/$lockFamily/$($module.Name).json" -PathType Leaf)) {
            throw "Missing Windows portable dependency graph for $($module.Name). Run pwsh -NoProfile -File scripts/AgentInbox.ps1 -Command update-locks and commit the results."
        }
    }

    & (Join-Path $PSScriptRoot 'clean-artifacts.ps1')
    New-Item -ItemType Directory -Path $cliPayload -Force | Out-Null
    foreach ($project in @('MailMeUp.Desktop', 'MailMeUp.Cli')) {
        $destination = if ($project -eq 'MailMeUp.Desktop') { $payload } else { $cliPayload }
        dotnet publish "src/$project/$project.csproj" -c Release -r $Runtime --self-contained true `
            -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false `
            -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false `
            -p:ContinuousIntegrationBuild=true -p:MailMeUpPortableBuild=true "-p:MailMeUpPortableRuntime=$lockFamily" `
            -p:RestoreLockedMode=true --output $destination
        if ($LASTEXITCODE -ne 0) { throw "Windows portable publish failed: $project ($Runtime)" }
    }

    Copy-Item -LiteralPath 'LICENSE', 'THIRD_PARTY_NOTICES.md' -Destination $payload
    Copy-Item -LiteralPath 'docs/WINDOWS_PORTABLE.md' -Destination (Join-Path $payload 'README.md')
    Copy-Item -LiteralPath 'docs/licenses' -Destination (Join-Path $payload 'licenses') -Recurse
    Copy-DependencyNotices -AssetFiles @(
        (Join-Path $repoRoot 'src/MailMeUp.Desktop/obj/project.assets.json'),
        (Join-Path $repoRoot 'src/MailMeUp.Cli/obj/project.assets.json')
    ) -Destination (Join-Path $payload 'licenses')
    'Portable Windows edition. Keep this marker and the complete cli folder beside AgentInbox.Desktop.exe.' |
        Set-Content -LiteralPath (Join-Path $payload 'agentinbox-portable.txt') -Encoding utf8NoBOM

    $hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $isNative = $Runtime -eq "win-$hostArchitecture"
    $smokeStatus = if ($isNative) { 'native-before-and-after-extraction' } else { 'not-run-cross-architecture' }
    $executable = Join-Path $cliPayload 'agentinbox.exe'
    if ($isNative) {
        python (Join-Path $PSScriptRoot 'smoke-test.py') $executable
        if ($LASTEXITCODE -ne 0) { throw 'Published portable CLI/MCP smoke test failed.' }
    }
    @(
        "Version=$version", "Runtime=$Runtime", "Commit=$commit", 'Edition=windows-desktop-portable',
        "CliMcpSmokeTest=$smokeStatus", 'DesktopInteraction=not-tested-by-packaging',
        'Data=OS-protected-local-profile-unless-AGENTINBOX_DATA_DIR-is-set'
    ) | Set-Content -LiteralPath (Join-Path $payload 'BUILD_INFO.txt') -Encoding utf8NoBOM

    $archive = Join-Path $artifactRoot "$packageName.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    if ($isNative) {
        $verificationPath = [IO.Path]::GetFullPath((Join-Path $artifactRoot "verification/$packageName-$([Guid]::NewGuid().ToString('N'))"))
        $artifactPrefix = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $verificationPath.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Archive verification path escaped the artifact directory.'
        }
        try {
            Expand-Archive -LiteralPath $archive -DestinationPath $verificationPath
            foreach ($required in @('AgentInbox.Desktop.exe', 'cli/agentinbox.exe', 'agentinbox-portable.txt', 'README.md', 'BUILD_INFO.txt', 'CodexPlugin/.agents/plugins/marketplace.json', 'CodexPlugin/plugins/agentinbox/.mcp.json', 'CodexPlugin/plugins/agentinbox/.codex-plugin/plugin.json')) {
                if (-not (Test-Path -LiteralPath (Join-Path $verificationPath $required) -PathType Leaf)) {
                    throw "Portable archive is incomplete: $required"
                }
            }
            python (Join-Path $PSScriptRoot 'smoke-test.py') (Join-Path $verificationPath 'cli/agentinbox.exe')
            if ($LASTEXITCODE -ne 0) { throw 'Extracted portable CLI/MCP smoke test failed.' }
        } finally {
            if (Test-Path -LiteralPath $verificationPath) { Remove-Item -LiteralPath $verificationPath -Recurse -Force }
        }
    }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath "$archive.sha256" -Encoding utf8NoBOM
    Write-Output "Created $archive (CLI/MCP smoke: $smokeStatus; desktop interaction not tested by packaging)"
} finally {
    Pop-Location
    $env:DOTNET_CLI_UI_LANGUAGE = $previousCliLanguage
}
