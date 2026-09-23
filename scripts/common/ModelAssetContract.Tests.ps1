$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$verifier = Join-Path $PSScriptRoot "..\verify-configured-model-assets.ps1"
$temporaryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) (
        "qsee-model-contract-" + [guid]::NewGuid().ToString("N")
    ))
)
$expectedTemporaryParent = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::GetTempPath()
)
if (-not $temporaryRoot.StartsWith(
    $expectedTemporaryParent,
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw "Temporary model-test root escaped the system temp directory."
}

# Runs the verifier and returns both its exit code and output.
function Invoke-QSeeModelVerifierFixture {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConfigPath,

        [Parameter(Mandatory = $true)]
        [string] $AssetsRoot,

        [Parameter(Mandatory = $true)]
        [string] $LockPath
    )

    $output = @(& pwsh -NoProfile -NonInteractive -File $verifier `
        -ConfigPath $ConfigPath `
        -AssetsRoot $AssetsRoot `
        -LockPath $LockPath 2>&1)
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output -join [Environment]::NewLine)
    }
}

try {
    $assetsRoot = Join-Path $temporaryRoot "assets"
    $modelsRoot = Join-Path $assetsRoot "models"
    New-Item -ItemType Directory -Path $modelsRoot -Force | Out-Null
    $configPath = Join-Path $temporaryRoot "qsee_config.yaml"
    $lockPath = Join-Path $modelsRoot "model-assets.lock.json"
    $modelPath = Join-Path $modelsRoot "qsee-test-model.tflite"
    $modelBytes = [System.Text.Encoding]::UTF8.GetBytes("QSee model fixture")
    [System.IO.File]::WriteAllBytes(
        $modelPath,
        $modelBytes
    )
    Set-Content -LiteralPath $configPath -Value @(
        "customer: QSee",
        "inference:",
        "  models:",
        "    TEST:",
        "      asset_path: models/qsee-test-model.tflite"
    )
    $modelFile = Get-Item -LiteralPath $modelPath
    $modelHash = (
        Get-FileHash -LiteralPath $modelPath -Algorithm SHA256
    ).Hash.ToUpperInvariant()
    $validLockJson = [ordered]@{
        schemaVersion = 1
        models = @(
            [ordered]@{
                assetPath = "models/qsee-test-model.tflite"
                sizeBytes = $modelFile.Length
                sha256 = $modelHash
            }
        )
    } | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath $lockPath -Value $validLockJson

    $valid = Invoke-QSeeModelVerifierFixture `
        -ConfigPath $configPath `
        -AssetsRoot $assetsRoot `
        -LockPath $lockPath
    if ($valid.ExitCode -ne 0) {
        throw "Valid model contract failed: $($valid.Output)"
    }

    Set-Content -LiteralPath (Join-Path $modelsRoot "extra.tflite") -Value "extra"
    $extra = Invoke-QSeeModelVerifierFixture `
        -ConfigPath $configPath `
        -AssetsRoot $assetsRoot `
        -LockPath $lockPath
    if ($extra.ExitCode -eq 0 -or $extra.Output -notmatch "Unconfigured TFLite") {
        throw "The verifier did not reject an unconfigured model."
    }
    Remove-Item -LiteralPath (Join-Path $modelsRoot "extra.tflite") -Force

    Remove-Item -LiteralPath $modelPath -Force
    $missing = Invoke-QSeeModelVerifierFixture `
        -ConfigPath $configPath `
        -AssetsRoot $assetsRoot `
        -LockPath $lockPath
    if ($missing.ExitCode -eq 0 -or $missing.Output -notmatch "is missing") {
        throw "The verifier did not reject a missing configured model."
    }
    [System.IO.File]::WriteAllBytes($modelPath, $modelBytes)

    $mismatchedLockJson = [ordered]@{
        schemaVersion = 1
        models = @(
            [ordered]@{
                assetPath = "models/different-model.tflite"
                sizeBytes = $modelFile.Length
                sha256 = $modelHash
            }
        )
    } | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath $lockPath -Value $mismatchedLockJson
    $mismatched = Invoke-QSeeModelVerifierFixture `
        -ConfigPath $configPath `
        -AssetsRoot $assetsRoot `
        -LockPath $lockPath
    if ($mismatched.ExitCode -eq 0 -or
        $mismatched.Output -notmatch "do not exactly match") {
        throw "The verifier did not reject a config/lock path mismatch."
    }
    Set-Content -LiteralPath $lockPath -Value $validLockJson

    Add-Content -LiteralPath $modelPath -Value "changed"
    $changed = Invoke-QSeeModelVerifierFixture `
        -ConfigPath $configPath `
        -AssetsRoot $assetsRoot `
        -LockPath $lockPath
    if ($changed.ExitCode -eq 0 -or
        $changed.Output -notmatch "differs from lock") {
        throw "The verifier did not reject a changed locked model."
    }

    Write-Output "Model asset contract passed."
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
