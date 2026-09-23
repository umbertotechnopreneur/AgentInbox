[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$modulePath = Join-Path -Path $PSScriptRoot -ChildPath "SessionReplay.Inventory.psm1"
Import-Module -Name $modulePath -Force

$script:Assertions = 0

# Fails the validation run when a condition is false.
function Assert-SessionReplayTest {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $script:Assertions++
    if (-not $Condition) {
        throw $Message
    }
}

# Confirms that an action throws an error containing the expected text.
function Assert-SessionReplayThrows {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Action,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedText
    )

    $script:Assertions++
    try {
        & $Action
    } catch {
        if ($_.Exception.Message.Contains($ExpectedText, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
        throw "Expected an error containing '$ExpectedText', received '$($_.Exception.Message)'."
    }
    throw "Expected an error containing '$ExpectedText', but no error was raised."
}

# Writes the complete strict configuration used by the local-only tests.
function Write-SessionReplayTestConfiguration {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryRoot,

        [System.Collections.IDictionary] $AdditionalProperties = [ordered]@{}
    )

    $configurationPath = Join-Path -Path $RepositoryRoot -ChildPath "scripts\session-replay.settings.local.json"
    $values = [ordered] @{
        QSEE_REPLAY_AWS_EXE = "$PSHOME\pwsh.exe"
        QSEE_REPLAY_AWS_PROFILE = "default"
        QSEE_REPLAY_AWS_REGION = "ap-southeast-1"
        QSEE_REPLAY_S3_BUCKET = "qseeai"
        QSEE_REPLAY_MIRROR_ROOT = (Join-Path $RepositoryRoot "mirror")
        QSEE_REPLAY_SOURCE = "Local"
        QSEE_REPLAY_IMAGE_SET = "PreviewNotDetected"
        QSEE_REPLAY_CLIENT = "Kiabi"
        QSEE_REPLAY_SIZE = "L"
        QSEE_REPLAY_ROTATION = "90"
        QSEE_REPLAY_MAX_IMAGES = "10"
        QSEE_REPLAY_COVERAGE_TARGET_FRACTION = "0.50"
        QSEE_REPLAY_CORRELATION_SECONDS = "60"
        QSEE_REPLAY_MODEL = "tshirt-panko-detr-pose"
        QSEE_REPLAY_TABLE_PATH = (Join-Path $RepositoryRoot "Kiabi.yaml")
        QSEE_REPLAY_CV_ENDPOINT = "https://testqsee.dopikai.com/panko/api/v1/landmark-estimation/estimate"
        QSEE_REPLAY_CORE_ROOT = (Join-Path $RepositoryRoot "core")
        QSEE_REPLAY_OUTPUT_ROOT = (Join-Path $RepositoryRoot "output")
        QSEE_REPLAY_PYTHON_EXE = (Join-Path $RepositoryRoot "missing\python.exe")
        QSEE_REPLAY_ENV_LOCAL = (Join-Path $RepositoryRoot "scripts\session-replay.env.local")
    }
    foreach ($entry in $AdditionalProperties.GetEnumerator()) {
        $values[$entry.Key] = $entry.Value
    }
    $json = $values | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText(
        $configurationPath,
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false)
    )
}

# Creates a normalized record for correlation-only test cases.
function New-SessionReplayTestRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Key,

        [Parameter(Mandatory = $true)]
        [datetime] $Timestamp
    )

    return [pscustomobject] [ordered] @{
        Key = $Key
        LocalPath = "C:\test\$([System.IO.Path]::GetFileName($Key))"
        LastModifiedUtc = $Timestamp
        Size = [long] 1
        ContentIdentity = $null
    }
}

$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$testRoot = Join-Path -Path $temporaryBase -ChildPath "qsee-session-replay-tests-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path (Join-Path $testRoot "scripts") -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $testRoot "mirror") -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $testRoot "core") -Force
    [System.IO.File]::WriteAllText((Join-Path $testRoot "Kiabi.yaml"), "test: true`n")
    [System.IO.File]::WriteAllText(
        (Join-Path $testRoot "scripts\session-replay.env.local"),
        "QSEE_REPLAY_CV_API_KEY=test-only`n"
    )

    Write-SessionReplayTestConfiguration -RepositoryRoot $testRoot
    $configuration = Read-SessionReplayConfiguration -RepositoryRoot $testRoot
    Assert-SessionReplayTest ($configuration.Source -ceq "Local") "The strict parser did not preserve the explicit source."
    Assert-SessionReplayTest ($configuration.ImageSet -ceq "PreviewNotDetected") "The strict parser did not preserve the image set."
    Assert-SessionReplayTest ($configuration.Rotation -eq 90) "The strict parser did not type the rotation."
    Assert-SessionReplayTest ($configuration.MaxImages -eq 10) "The strict parser did not type the image limit."
    Assert-SessionReplayTest ($configuration.CoverageTargetFraction -eq 0.5) "The strict parser did not preserve the tool-only coverage target."
    Assert-SessionReplayTest (
        $configuration.PythonExe -ceq [System.IO.Path]::GetFullPath((Join-Path $testRoot "missing\python.exe"))
    ) "The strict parser did not allow an absolute missing Python executable."
    $inventoryModule = Get-Module -Name "SessionReplay.Inventory"
    Assert-SessionReplayTest (
        $null -ne $inventoryModule -and $inventoryModule.ExportedFunctions.ContainsKey("Test-SessionReplayS3Access")
    ) "The public S3 access preflight is not exported."

    Write-SessionReplayTestConfiguration `
        -RepositoryRoot $testRoot `
        -AdditionalProperties ([ordered] @{ UNKNOWN_KEY = "value" })
    Assert-SessionReplayThrows `
        -Action { Read-SessionReplayConfiguration -RepositoryRoot $testRoot } `
        -ExpectedText "Unknown"

    Write-SessionReplayTestConfiguration -RepositoryRoot $testRoot
    $configurationPath = Join-Path $testRoot "scripts\session-replay.settings.local.json"
    $validJson = [System.IO.File]::ReadAllText($configurationPath).TrimEnd()
    $duplicateJson = $validJson.Substring(0, $validJson.Length - 1) + ",`n  `"QSEE_REPLAY_SOURCE`": `"S3`"`n}`n"
    [System.IO.File]::WriteAllText(
        $configurationPath,
        $duplicateJson,
        [System.Text.UTF8Encoding]::new($false)
    )
    Assert-SessionReplayThrows `
        -Action { Read-SessionReplayConfiguration -RepositoryRoot $testRoot } `
        -ExpectedText "Duplicate"

    Write-SessionReplayTestConfiguration `
        -RepositoryRoot $testRoot `
        -AdditionalProperties ([ordered] @{ QSEE_REPLAY_COVERAGE_TARGET_FRACTION = "0.60" })
    Assert-SessionReplayThrows `
        -Action { Read-SessionReplayConfiguration -RepositoryRoot $testRoot } `
        -ExpectedText "exactly 0.50 or 0.70"

    Write-SessionReplayTestConfiguration -RepositoryRoot $testRoot
    $configuration = Read-SessionReplayConfiguration -RepositoryRoot $testRoot
    $day = "2026-08-18"
    $imageDirectory = Join-Path $testRoot "mirror\data_collection\preview\object_not_detected\$day"
    $resultDirectory = Join-Path $testRoot "mirror\data_collection\results\measures\$day"
    $lensDirectory = Join-Path $testRoot "mirror\data_collection\debug\lens_calib\round-test\$day"
    $homographyDirectory = Join-Path $testRoot "mirror\data_collection\debug\homography_calib\session-test\$day"
    foreach ($directory in @($imageDirectory, $resultDirectory, $lensDirectory, $homographyDirectory)) {
        $null = New-Item -ItemType Directory -Path $directory -Force
    }

    $emptyInventory = Get-SessionReplayDayInventory `
        -Configuration $configuration `
        -Source Local `
        -UtcDay $day
    Assert-SessionReplayTest (
        $emptyInventory.CalibrationGroups.Count -eq 0
    ) "An empty same-day calibration directory must not fail inventory."

    [System.IO.File]::WriteAllBytes((Join-Path $imageDirectory "capture.jpg"), [byte[]] @(1, 2, 3))
    [System.IO.File]::WriteAllText((Join-Path $resultDirectory "result.json"), "{}`n")
    [System.IO.File]::WriteAllBytes((Join-Path $lensDirectory "lens.jpg"), [byte[]] @(1))
    [System.IO.File]::WriteAllBytes((Join-Path $homographyDirectory "plane.jpg"), [byte[]] @(1, 2))

    $inventory = Get-SessionReplayDayInventory `
        -Configuration $configuration `
        -Source Local `
        -UtcDay $day
    Assert-SessionReplayTest ($inventory.Images.Count -eq 1) "The local image inventory count is wrong."
    Assert-SessionReplayTest ($inventory.Results.Count -eq 1) "The local result inventory count is wrong."
    Assert-SessionReplayTest ($inventory.CalibrationGroups.Count -eq 2) "The calibration grouping count is wrong."
    Assert-SessionReplayTest (
        (@($inventory.Images[0].PSObject.Properties.Name) -join ",") -ceq
        "Key,LocalPath,LastModifiedUtc,Size,ContentIdentity"
    ) "A normalized object record does not have the exact five-field shape."
    Assert-SessionReplayTest (
        $inventory.Images[0].Key -ceq "data_collection/preview/object_not_detected/2026-08-18/capture.jpg"
    ) "The local object key was not normalized."

    Assert-SessionReplayThrows `
        -Action { Get-SessionReplayDayInventory -Configuration $configuration -Source S3 -UtcDay $day } `
        -ExpectedText "does not match"
    Assert-SessionReplayThrows `
        -Action { Get-SessionReplayDayInventory -Configuration $configuration -Source Local -UtcDay "18-08-2026" } `
        -ExpectedText "YYYY-MM-DD"

    $captures = @(
        New-SessionReplayTestRecord -Key "capture/matched.jpg" -Timestamp ([datetime] "2026-08-18T03:25:10Z")
        New-SessionReplayTestRecord -Key "capture/ambiguous-a.jpg" -Timestamp ([datetime] "2026-08-18T03:26:10Z")
        New-SessionReplayTestRecord -Key "capture/ambiguous-b.jpg" -Timestamp ([datetime] "2026-08-18T03:26:20Z")
        New-SessionReplayTestRecord -Key "capture/unmatched.jpg" -Timestamp ([datetime] "2026-08-18T03:27:10Z")
    )
    $results = @(
        New-SessionReplayTestRecord -Key "result/2026-08-18-03-25-17-1111111111111111.json" -Timestamp ([datetime] "2026-08-18T03:25:17Z")
        New-SessionReplayTestRecord -Key "result/2026-08-18-03-26-15-2222222222222222.json" -Timestamp ([datetime] "2026-08-18T03:26:15Z")
        New-SessionReplayTestRecord -Key "result/2026-08-18-03-28-10-3333333333333333.json" -Timestamp ([datetime] "2026-08-18T03:28:10Z")
    )
    $correlations = @(Resolve-SessionReplayCorrelation `
            -Captures $captures `
            -Results $results `
            -MaxDeltaSeconds 60)
    $matched = @($correlations | Where-Object Status -ceq "Matched")
    $ambiguous = @($correlations | Where-Object Status -ceq "Ambiguous")
    $unmatched = @($correlations | Where-Object Status -ceq "Unmatched")
    Assert-SessionReplayTest ($matched.Count -eq 1) "Expected exactly one deterministic correlation."
    Assert-SessionReplayTest ($matched[0].DeltaSeconds -eq 7.0) "The matched delta is wrong."
    Assert-SessionReplayTest ($ambiguous.Count -eq 3) "Expected one ambiguous result and its two ambiguous captures."
    $ambiguousResult = @($ambiguous | Where-Object Subject -ceq "Result")
    Assert-SessionReplayTest ($ambiguousResult.Count -eq 1) "Expected exactly one ambiguous result state."
    Assert-SessionReplayTest ($ambiguousResult[0].CandidateCaptureKeys.Count -eq 2) "The ambiguity candidates are incomplete."
    Assert-SessionReplayTest ($unmatched.Count -eq 2) "Expected unmatched result and capture states."

    $greedyCaptures = @(
        New-SessionReplayTestRecord -Key "capture/first.jpg" -Timestamp ([datetime] "2026-08-18T03:30:10Z")
        New-SessionReplayTestRecord -Key "capture/second.jpg" -Timestamp ([datetime] "2026-08-18T03:30:40Z")
    )
    $greedyResults = @(
        New-SessionReplayTestRecord -Key "result/2026-08-18-03-30-12-4444444444444444.json" -Timestamp ([datetime] "2026-08-18T03:30:12Z")
        New-SessionReplayTestRecord -Key "result/2026-08-18-03-30-42-5555555555555555.json" -Timestamp ([datetime] "2026-08-18T03:30:42Z")
    )
    $greedyCorrelations = @(Resolve-SessionReplayCorrelation `
            -Captures $greedyCaptures `
            -Results $greedyResults `
            -MaxDeltaSeconds 60)
    Assert-SessionReplayTest (
        @($greedyCorrelations | Where-Object Status -ceq "Matched").Count -eq 2
    ) "Minimum-delta assignment did not match both independent pairs in one minute."

    # Confirms correlation never substitutes LastModifiedUtc for a malformed result key.
    $nonCanonicalResult = New-SessionReplayTestRecord `
        -Key "result/result.json" `
        -Timestamp ([datetime] "2026-08-18T03:25:17Z")
    Assert-SessionReplayThrows `
        -Action {
            Resolve-SessionReplayCorrelation `
                -Captures @($captures[0]) `
                -Results @($nonCanonicalResult) `
                -MaxDeltaSeconds 60
        } `
        -ExpectedText "exact timestamped JSON contract"

    $boundaryCapture = New-SessionReplayTestRecord `
        -Key "capture/boundary.jpg" `
        -Timestamp ([datetime] "2026-08-18T03:27:52Z")
    $boundaryResult = New-SessionReplayTestRecord `
        -Key "data_collection/results/measures/2026-08-18/2026-08-18-03-27-59-99f1dd9a2c385044.json" `
        -Timestamp ([datetime] "2026-08-18T03:28:00Z")
    $boundaryCorrelations = @(Resolve-SessionReplayCorrelation `
            -Captures @($boundaryCapture) `
            -Results @($boundaryResult) `
            -MaxDeltaSeconds 60)
    Assert-SessionReplayTest (
        $boundaryCorrelations.Count -eq 1 -and
        $boundaryCorrelations[0].Status -ceq "Matched" -and
        $boundaryCorrelations[0].DeltaSeconds -eq 7.0
    ) "Result-key event time did not preserve the cross-metadata-minute match."

    Write-Host "Session replay inventory validation passed ($script:Assertions assertions)."
} finally {
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $safePrefix = $temporaryBase.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar + "qsee-session-replay-tests-"
    if (
        $resolvedTestRoot.StartsWith($safePrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTestRoot -PathType Container)
    ) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
