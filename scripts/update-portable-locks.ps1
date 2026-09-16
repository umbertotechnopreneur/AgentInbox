[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string[]]$Runtime = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    foreach ($runtimeId in $Runtime) {
        dotnet restore src/MailMeUp.Cli/MailMeUp.Cli.csproj "-p:RuntimeIdentifier=$runtimeId" --force-evaluate `
            -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:PublishTrimmed=false -p:MailMeUpPortableBuild=true "-p:MailMeUpPortableRuntime=$runtimeId"
        if ($LASTEXITCODE -ne 0) { throw "Portable dependency restore failed: $runtimeId" }
        foreach ($lockFile in Get-ChildItem -LiteralPath "eng/locks/$runtimeId" -Filter '*.json') {
            $graph = Get-Content -LiteralPath $lockFile.FullName -Raw | ConvertFrom-Json
            $unexpected = $graph.dependencies.PSObject.Properties.Name | Where-Object { $_.Contains('/') -and -not $_.EndsWith("/$runtimeId") }
            if ($unexpected) { throw "Portable graph includes an unexpected host runtime: $($lockFile.Name)" }
        }
        if ($runtimeId.StartsWith('win-')) {
            foreach ($project in @('MailMeUp.Desktop', 'MailMeUp.Cli')) {
                dotnet restore "src/$project/$project.csproj" "-p:RuntimeIdentifier=$runtimeId" --force-evaluate `
                    -p:SelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false `
                    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true `
                    -p:MailMeUpPortableBuild=true "-p:MailMeUpPortableRuntime=windows-portable/$runtimeId"
                if ($LASTEXITCODE -ne 0) { throw "Windows desktop portable dependency restore failed: $runtimeId ($project)" }
            }
        }
    }
    dotnet restore MailMeUp.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Standard dependency restore failed.' }
} finally {
    Pop-Location
}
