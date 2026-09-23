$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "ReleasePreflight.ps1")

# Fails the focused contract test with one actionable message.
function Assert-QSeeTest {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

# Requires the supplied release-contract action to fail closed.
function Assert-QSeeThrows {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Action,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    try {
        & $Action
    } catch {
        return
    }
    throw $Message
}

# Returns one named function body from a parsed PowerShell launcher.
function Get-QSeeFunctionText {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.Language.ScriptBlockAst] $Ast,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $functionAst = $Ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $Name
    }, $true) | Select-Object -First 1
    if ($null -eq $functionAst) {
        throw "Launcher function not found: $Name"
    }
    return $functionAst.Extent.Text
}

$temporaryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) (
        "qsee-release-preflight-" + [guid]::NewGuid().ToString("N")
    ))
)
$expectedTemporaryParent = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::GetTempPath()
)
if (-not $temporaryRoot.StartsWith(
    $expectedTemporaryParent,
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw "Temporary release-test root escaped the system temp directory."
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    & git -C $temporaryRoot init -b optimization/release-one | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to initialize the focused release-test repository."
    }
    & git -C $temporaryRoot config user.email "qsee-release-test@example.invalid"
    & git -C $temporaryRoot config user.name "QSee Release Test"
    Set-Content -LiteralPath (Join-Path $temporaryRoot "tracked.txt") -Value "QSee"
    Set-Content -LiteralPath (Join-Path $temporaryRoot "model-assets.lock.json") -Value "{}"
    & git -C $temporaryRoot add tracked.txt model-assets.lock.json
    & git -C $temporaryRoot commit -m "test: seed release preflight" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to commit the focused release-test fixture."
    }

    $plan = New-QSeeReleasePreflightPlan `
        -RepositoryRoot $temporaryRoot `
        -ExpectedBranch "optimization/release-one" `
        -VersionCode 100 `
        -VersionName "1.0.0-beta.1" `
        -Channel "beta" `
        -Backend "qsee" `
        -ReleaseNotes "QSee release preflight contract." `
        -ModelLockPath (Join-Path $temporaryRoot "model-assets.lock.json")
    Assert-QSeeTest `
        -Condition ($plan.SourceBranch -eq "optimization/release-one") `
        -Message "The release plan did not retain the expected branch."
    Assert-QSeeTest `
        -Condition ($plan.SourceCommit -match '^[0-9a-f]{40}$') `
        -Message "The release plan did not retain a full commit id."
    Assert-QSeeTest `
        -Condition ($plan.ReleaseChannel -eq "Beta" -and
            $plan.ReleaseBackend -eq "QSee") `
        -Message "The release plan did not canonicalize channel and backend."
    Assert-QSeeTest `
        -Condition ($plan.ReleaseNotes -eq "QSee release preflight contract.") `
        -Message "The release plan did not retain its validated notes."

    Assert-QSeeThrows `
        -Action {
            Assert-QSeeReleaseVersionContract `
                -VersionName "1.0.0-beta.1" `
                -Channel "Stable"
        } `
        -Message "Stable accepted a Beta VersionName."
    Assert-QSeeThrows `
        -Action {
            Assert-QSeeReleaseVersionContract `
                -VersionName "1.0.0" `
                -Channel "Beta"
        } `
        -Message "Beta accepted a stable VersionName."
    Assert-QSeeThrows `
        -Action {
            Assert-QSeeReleaseVersionContract `
                -VersionName " 1.0.0-beta.1" `
                -Channel "Beta"
        } `
        -Message "Release version accepted surrounding whitespace."
    Assert-QSeeThrows `
        -Action {
            [void](New-QSeeReleasePreflightPlan `
                -RepositoryRoot $temporaryRoot `
                -ExpectedBranch "optimization/release-one" `
                -VersionCode ([long] [int]::MaxValue + 1L) `
                -VersionName "1.0.0-beta.1" `
                -Channel "Beta" `
                -Backend "QSee" `
                -ReleaseNotes "QSee" `
                -ModelLockPath (Join-Path $temporaryRoot "model-assets.lock.json"))
        } `
        -Message "Release preflight accepted a versionCode above the Android limit."
    Assert-QSeeThrows `
        -Action {
            [void](New-QSeeReleasePreflightPlan `
                -RepositoryRoot $temporaryRoot `
                -ExpectedBranch "optimization/release-one" `
                -VersionCode 100 `
                -VersionName "1.0.0-beta.1" `
                -Channel "Beta" `
                -Backend "QSee" `
                -ReleaseNotes "   " `
                -ModelLockPath (Join-Path $temporaryRoot "model-assets.lock.json"))
        } `
        -Message "Release preflight accepted blank operator notes."
    Assert-QSeeThrows `
        -Action {
            [void](New-QSeeReleasePreflightPlan `
                -RepositoryRoot $temporaryRoot `
                -ExpectedBranch "main" `
                -VersionCode 100 `
                -VersionName "1.0.0-beta.1" `
                -Channel "Beta" `
                -Backend "QSee" `
                -ReleaseNotes "QSee" `
                -ModelLockPath (Join-Path $temporaryRoot "model-assets.lock.json"))
        } `
        -Message "Release preflight accepted the wrong branch."

    Add-Content -LiteralPath (Join-Path $temporaryRoot "tracked.txt") -Value "dirty"
    Assert-QSeeThrows `
        -Action {
            [void](New-QSeeReleasePreflightPlan `
                -RepositoryRoot $temporaryRoot `
                -ExpectedBranch "optimization/release-one" `
                -VersionCode 100 `
                -VersionName "1.0.0-beta.1" `
                -Channel "Beta" `
                -Backend "QSee" `
                -ReleaseNotes "QSee" `
                -ModelLockPath (Join-Path $temporaryRoot "model-assets.lock.json"))
        } `
        -Message "Release preflight accepted a dirty workspace."

    Set-Content -LiteralPath (Join-Path $temporaryRoot "tracked.txt") -Value "QSee"
    $restoredStatus = @(& git -C $temporaryRoot status --porcelain=v1)
    Assert-QSeeTest `
        -Condition ($LASTEXITCODE -eq 0 -and $restoredStatus.Count -eq 0) `
        -Message "The release-test fixture could not restore its clean tracked state."
    Set-Content -LiteralPath (Join-Path $temporaryRoot "untracked.txt") -Value "QSee"
    Assert-QSeeThrows `
        -Action {
            [void](New-QSeeReleasePreflightPlan `
                -RepositoryRoot $temporaryRoot `
                -ExpectedBranch "optimization/release-one" `
                -VersionCode 100 `
                -VersionName "1.0.0-beta.1" `
                -Channel "Beta" `
                -Backend "QSee" `
                -ReleaseNotes "QSee" `
                -ModelLockPath (Join-Path $temporaryRoot "model-assets.lock.json"))
        } `
        -Message "Release preflight accepted an untracked path."

    $launcherPath = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot "..\QSee.ps1")
    )
    $tokens = $null
    $parseErrors = $null
    $launcherAst = [System.Management.Automation.Language.Parser]::ParseFile(
        $launcherPath,
        [ref] $tokens,
        [ref] $parseErrors
    )
    Assert-QSeeTest `
        -Condition ($parseErrors.Count -eq 0) `
        -Message "QSee launcher contains PowerShell parser errors."

    $modelContractText = Get-QSeeFunctionText `
        -Ast $launcherAst `
        -Name "Invoke-QSeeModelAssetContract"
    Assert-QSeeTest `
        -Condition ($modelContractText -match '(?s)Invoke-QSeeScriptFile.*\|\s*Out-Host') `
        -Message "Release-plan model verification must not leak helper output into the plan."

    $prepareText = Get-QSeeFunctionText `
        -Ast $launcherAst `
        -Name "Invoke-QSeePrepareRelease"
    $prepareDryRun = $prepareText.IndexOf('$ReleaseDryRun')
    $prepareTests = $prepareText.IndexOf('Invoke-SafeDebugUnitTestSuite')
    $prepareBuild = $prepareText.IndexOf('Invoke-Build')
    Assert-QSeeTest `
        -Condition ($prepareDryRun -ge 0 -and
            $prepareDryRun -lt $prepareTests -and
            $prepareDryRun -lt $prepareBuild) `
        -Message "Prepare-release dry-run no longer precedes test and build actions."

    $publishText = Get-QSeeFunctionText `
        -Ast $launcherAst `
        -Name "Invoke-QSeePublishRelease"
    $publishDryRun = $publishText.IndexOf('$ReleaseDryRun')
    $publishShouldProcess = $publishText.IndexOf('$PSCmdlet.ShouldProcess')
    $publishApiKey = $publishText.IndexOf('Resolve-ReleaseAutomationApiKey')
    $publishUpload = $publishText.IndexOf('Invoke-ReleaseAutomationUpload')
    Assert-QSeeTest `
        -Condition ($publishText.IndexOf('Invoke-Build') -lt 0 -and
            $publishText.IndexOf('Invoke-SafeDebugUnitTestSuite') -lt 0 -and
            $publishText.IndexOf('Invoke-RestMethod') -lt 0) `
        -Message "Publish-release must not build or run the Gradle test suite."
    Assert-QSeeTest `
        -Condition ($publishDryRun -ge 0 -and
            $publishShouldProcess -gt $publishDryRun -and
            $publishApiKey -gt $publishShouldProcess -and
            $publishUpload -gt $publishApiKey) `
        -Message "Publish-release may reach credentials or upload before dry-run/ShouldProcess gates."

    Write-Output "Release preflight contract passed."
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
