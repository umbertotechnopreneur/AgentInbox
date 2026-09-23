[CmdletBinding()]
param(
    [string] $Date,
    [ValidateSet("Local", "S3")]
    [string] $Source,
    [ValidateSet("Captured", "PreviewDetected", "PreviewNotDetected")]
    [string] $ImageSet,
    [ValidateSet("Kiabi", "Panko")]
    [string] $Client,
    [string] $Size,
    [ValidateSet("Menu", "Settings", "Preflight", "Setup", "Plan", "Run")]
    [string] $Action,
    [ValidateSet(0, 90, 180, 270)]
    [Nullable[int]] $Rotation,
    [ValidateRange(1, 10000)]
    [Nullable[int]] $MaxImages,
    [string] $LensGroup,
    [string] $HomographyGroup,
    [string] $OutputPath,
    [switch] $Interactive
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:RepositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath "..\..")
)
$script:InventoryModulePath = Join-Path -Path $PSScriptRoot -ChildPath "session-replay\SessionReplay.Inventory.psm1"
$script:ConfigurationPath = Join-Path -Path $script:RepositoryRoot -ChildPath "scripts\session-replay.settings.local.json"
$script:ExampleConfigurationPath = Join-Path -Path $script:RepositoryRoot -ChildPath "scripts\session-replay.settings.example.json"
$script:RequiredRemoteEndpoint = "https://testqsee.dopikai.com/panko/api/v1/landmark-estimation/estimate"
$script:RequiredRemoteModel = "tshirt-panko-detr-pose"
$script:ReplayScriptRelativePath = "scripts\replay_mobile_landmarks.py"
$script:ReplayRequirementsPath = Join-Path -Path $PSScriptRoot -ChildPath "session-replay\requirements.txt"
$script:ReplayEnginePath = Join-Path -Path $PSScriptRoot -ChildPath "session-replay\session_replay_engine.py"
$script:ReplayWorkbookPath = Join-Path -Path $PSScriptRoot -ChildPath "session-replay\session_replay_workbook.ps1"
$script:SharedModuleRoot = Join-Path -Path $PSScriptRoot -ChildPath "modules"
$script:MenuManagerManifest = Join-Path -Path $script:SharedModuleRoot -ChildPath "MenuManager\MenuManager.psd1"
$script:SettingsKeys = @(
    "QSEE_REPLAY_AWS_EXE",
    "QSEE_REPLAY_AWS_PROFILE",
    "QSEE_REPLAY_AWS_REGION",
    "QSEE_REPLAY_S3_BUCKET",
    "QSEE_REPLAY_MIRROR_ROOT",
    "QSEE_REPLAY_SOURCE",
    "QSEE_REPLAY_IMAGE_SET",
    "QSEE_REPLAY_CLIENT",
    "QSEE_REPLAY_SIZE",
    "QSEE_REPLAY_ROTATION",
    "QSEE_REPLAY_MAX_IMAGES",
    "QSEE_REPLAY_COVERAGE_TARGET_FRACTION",
    "QSEE_REPLAY_CORRELATION_SECONDS",
    "QSEE_REPLAY_MODEL",
    "QSEE_REPLAY_TABLE_PATH",
    "QSEE_REPLAY_CV_ENDPOINT",
    "QSEE_REPLAY_CORE_ROOT",
    "QSEE_REPLAY_OUTPUT_ROOT",
    "QSEE_REPLAY_PYTHON_EXE",
    "QSEE_REPLAY_ENV_LOCAL"
)
$script:LauncherArguments = $PSBoundParameters

if ($script:LauncherArguments.Count -eq 0) {
    $Interactive = $true
}

if (-not (Test-Path -LiteralPath $script:InventoryModulePath -PathType Leaf)) {
    throw "Missing session replay inventory module: $script:InventoryModulePath"
}
Import-Module -Name $script:InventoryModulePath -Force -ErrorAction Stop

foreach ($sharedHelper in @("shared-bootstrap.ps1", "boot-banner.ps1", "footer-banner.ps1")) {
    $sharedHelperPath = Join-Path -Path $script:SharedModuleRoot -ChildPath $sharedHelper
    if (-not (Test-Path -LiteralPath $sharedHelperPath -PathType Leaf)) {
        throw "Missing shared QSee CLI helper: $sharedHelperPath"
    }
    . $sharedHelperPath
}
if (-not (Test-Path -LiteralPath $script:MenuManagerManifest -PathType Leaf)) {
    throw "Missing shared MenuManager module: $script:MenuManagerManifest"
}
Import-Module `
    -Name $script:MenuManagerManifest `
    -Force `
    -Prefix QSee `
    -Scope Local `
    -ErrorAction Stop

# Reads one exact inert JSON settings object without accepting aliases or extra keys.
function Read-SessionReplaySettingsData {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Settings file does not exist: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Settings file must not be a symbolic link or reparse point: $Path"
    }
    if ($item.Length -gt 65536) {
        throw "Settings file exceeds the 64 KiB safety limit: $Path"
    }
    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        $jsonText = $encoding.GetString([System.IO.File]::ReadAllBytes($Path))
    } catch {
        throw "Settings file must contain valid UTF-8 text: $Path"
    }
    $values = [ordered] @{}
    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($jsonText)
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "Settings file root must be a JSON object: $Path"
        }
        foreach ($property in $document.RootElement.EnumerateObject()) {
            $key = $property.Name
            if ($script:SettingsKeys -cnotcontains $key) {
                throw "Unknown settings key '$key'."
            }
            if ($values.Contains($key)) {
                throw "Duplicate settings key '$key'."
            }
            if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                throw "Settings key $key must contain a JSON string."
            }
            $value = $property.Value.GetString().Trim()
            if ([string]::IsNullOrWhiteSpace($value) -or $value.IndexOf([char] 0) -ge 0) {
                throw "Settings key $key must contain a non-empty JSON string."
            }
            $values.Add($key, $value)
        }
    } catch [System.Text.Json.JsonException] {
        throw "Settings file contains invalid JSON: $Path"
    } finally {
        if ($null -ne $document) {
            $document.Dispose()
        }
    }
    foreach ($requiredKey in $script:SettingsKeys) {
        if (-not $values.Contains($requiredKey)) {
            throw "Missing required settings key $requiredKey."
        }
    }
    return $values
}

# Writes settings atomically as UTF-8 data without executing any value.
function Write-SessionReplaySettingsData {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary] $Values
    )

    if ($Values.Count -ne $script:SettingsKeys.Count) {
        throw "Settings must contain exactly $($script:SettingsKeys.Count) keys."
    }
    foreach ($requiredKey in $script:SettingsKeys) {
        if (-not $Values.Contains($requiredKey)) {
            throw "Missing required settings key $requiredKey."
        }
        if ($Values[$requiredKey] -isnot [string] -or [string]::IsNullOrWhiteSpace($Values[$requiredKey])) {
            throw "Settings key $requiredKey must contain a non-empty JSON string."
        }
    }

    $parent = Split-Path -Parent $script:ConfigurationPath
    $temporaryPath = Join-Path -Path $parent -ChildPath (
        "session-replay.settings.local.{0}.tmp" -f [guid]::NewGuid().ToString("N")
    )
    $json = $Values | ConvertTo-Json -Depth 4
    $originalBytes = if (Test-Path -LiteralPath $script:ConfigurationPath -PathType Leaf) {
        [System.IO.File]::ReadAllBytes($script:ConfigurationPath)
    } else {
        $null
    }
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            $json + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false)
        )
        [System.IO.File]::Move($temporaryPath, $script:ConfigurationPath, $true)
        $null = Read-SessionReplayConfiguration -RepositoryRoot $script:RepositoryRoot
    } catch {
        $writeFailure = $_
        if ($null -eq $originalBytes) {
            if (Test-Path -LiteralPath $script:ConfigurationPath -PathType Leaf) {
                Remove-Item -LiteralPath $script:ConfigurationPath -Force
            }
        } else {
            [System.IO.File]::WriteAllBytes($temporaryPath, $originalBytes)
            [System.IO.File]::Move($temporaryPath, $script:ConfigurationPath, $true)
        }
        throw $writeFailure
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

# Returns the tracked non-secret example values used to initialize the local settings file.
function New-SessionReplaySettingsData {
    if (-not (Test-Path -LiteralPath $script:ExampleConfigurationPath -PathType Leaf)) {
        throw "Missing settings example: $script:ExampleConfigurationPath"
    }
    return Read-SessionReplaySettingsData -Path $script:ExampleConfigurationPath
}

# Creates the ignored adjacent JSON settings file from the tracked non-secret example when absent.
function Initialize-SessionReplaySettingsFile {
    if (Test-Path -LiteralPath $script:ConfigurationPath -PathType Leaf) {
        return
    }
    $settings = New-SessionReplaySettingsData
    Write-SessionReplaySettingsData -Values $settings
    $null = Read-SessionReplayConfiguration -RepositoryRoot $script:RepositoryRoot
    Write-Host "Created local session replay settings: $script:ConfigurationPath" -ForegroundColor DarkCyan
}

# Returns a settings value entered by the operator or keeps its current value.
function Read-SessionReplaySetting {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Label,

        [Parameter(Mandatory = $true)]
        [string] $CurrentValue
    )

    $entered = Read-Host "$Label [$CurrentValue]"
    if ([string]::IsNullOrWhiteSpace($entered)) {
        return $CurrentValue
    }
    return $entered.Trim()
}

# Maps one explicit client to its current bundled measurement table.
function Get-SessionReplayTablePath {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Kiabi", "Panko")]
        [string] $ClientId
    )

    return Join-Path -Path $script:RepositoryRoot -ChildPath (
        "app\src\main\assets\measurement_tables\TShirt\$ClientId.yaml"
    )
}

# Shows and edits the Local/S3 replay settings without displaying any secret value.
function Edit-SessionReplaySettings {
    if (-not $Interactive) {
        throw "Settings is an interactive action. Run QSee.ps1 without redirection."
    }

    $settings = if (Test-Path -LiteralPath $script:ConfigurationPath -PathType Leaf) {
        Read-SessionReplaySettingsData -Path $script:ConfigurationPath
    } else {
        New-SessionReplaySettingsData
    }

    while ($true) {
        Initialize-QSeeCliUi -ClearScreen
        Write-QSeeBootBanner `
            -Title "Historical Replay Settings" `
            -Subtitle "Evidence source, calibration, and output controls" `
            -Context "Local settings: $script:ConfigurationPath"
        Write-Host "  The CV secret is read only from the ignored replay env file." -ForegroundColor DarkGray
        Write-QSeeMenuHeader -Title "Choose a setting" -Hint "Enter a number, or use 0 / Esc to cancel."
        $menuWidth = Resolve-QSeeMenuConsoleWidth
        Write-QSeeMenuItemCompact -Number 1 -Emoji "📦" -Label "Source" -Desc $settings['QSEE_REPLAY_SOURCE'] -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 2 -Emoji "🖼" -Label "Image set" -Desc $settings['QSEE_REPLAY_IMAGE_SET'] -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 3 -Emoji "🏷" -Label "Client / size" -Desc "$($settings['QSEE_REPLAY_CLIENT']) / $($settings['QSEE_REPLAY_SIZE'])" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 4 -Emoji "↻" -Label "Rotation" -Desc "$($settings['QSEE_REPLAY_ROTATION']) degrees clockwise" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 5 -Emoji "#" -Label "Maximum images" -Desc $settings['QSEE_REPLAY_MAX_IMAGES'] -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 6 -Emoji "📁" -Label "Local mirror" -Desc $settings['QSEE_REPLAY_MIRROR_ROOT'] -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 7 -Emoji "☁" -Label "S3 bucket / profile" -Desc "$($settings['QSEE_REPLAY_S3_BUCKET']) / $($settings['QSEE_REPLAY_AWS_PROFILE'])" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 8 -Emoji "🌐" -Label "S3 region / CLI" -Desc "$($settings['QSEE_REPLAY_AWS_REGION']) / $($settings['QSEE_REPLAY_AWS_EXE'])" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 9 -Emoji "⚙" -Label "Runtime paths" -Desc "Core, Python, output, and protected env" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 10 -Emoji "⏱" -Label "Correlation window" -Desc "$($settings['QSEE_REPLAY_CORRELATION_SECONDS']) seconds" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 11 -Emoji "▦" -Label "Coverage target" -Desc "$([double]::Parse($settings['QSEE_REPLAY_COVERAGE_TARGET_FRACTION'], [System.Globalization.CultureInfo]::InvariantCulture) * 100)% (replay tool only)" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 12 -Emoji "💾" -Label "Save settings" -Desc "Validate and write the local JSON" -ConsoleWidth $menuWidth
        Write-QSeeBackItem -Label "Cancel without saving"
        $choice = Read-QSeeIndexChoice -Min 1 -Max 12
        if ($null -eq $choice) {
            return
        }
        switch ([int] $choice) {
            1 {
                $value = Read-SessionReplaySetting -Label "Source (Local or S3)" -CurrentValue $settings["QSEE_REPLAY_SOURCE"]
                if (@("Local", "S3") -cnotcontains $value) {
                    throw "Source must be exactly Local or S3."
                }
                $settings["QSEE_REPLAY_SOURCE"] = $value
            }
            2 {
                $value = Read-SessionReplaySetting -Label "Image set (Captured, PreviewDetected, PreviewNotDetected)" -CurrentValue $settings["QSEE_REPLAY_IMAGE_SET"]
                if (@("Captured", "PreviewDetected", "PreviewNotDetected") -cnotcontains $value) {
                    throw "Unsupported image set."
                }
                $settings["QSEE_REPLAY_IMAGE_SET"] = $value
            }
            3 {
                $clientValue = Read-SessionReplaySetting -Label "Client (Kiabi or Panko)" -CurrentValue $settings["QSEE_REPLAY_CLIENT"]
                if (@("Kiabi", "Panko") -cnotcontains $clientValue) {
                    throw "Client must be exactly Kiabi or Panko."
                }
                $settings["QSEE_REPLAY_CLIENT"] = $clientValue
                $settings["QSEE_REPLAY_SIZE"] = Read-SessionReplaySetting -Label "Reference size" -CurrentValue $settings["QSEE_REPLAY_SIZE"]
                $settings["QSEE_REPLAY_TABLE_PATH"] = Get-SessionReplayTablePath -ClientId $clientValue
            }
            4 {
                $settings["QSEE_REPLAY_ROTATION"] = Read-SessionReplaySetting -Label "Clockwise rotation (0, 90, 180, 270)" -CurrentValue $settings["QSEE_REPLAY_ROTATION"]
            }
            5 {
                $settings["QSEE_REPLAY_MAX_IMAGES"] = Read-SessionReplaySetting -Label "Maximum remote submissions" -CurrentValue $settings["QSEE_REPLAY_MAX_IMAGES"]
            }
            6 {
                $settings["QSEE_REPLAY_MIRROR_ROOT"] = Read-SessionReplaySetting -Label "Local S3 mirror root" -CurrentValue $settings["QSEE_REPLAY_MIRROR_ROOT"]
            }
            7 {
                $settings["QSEE_REPLAY_S3_BUCKET"] = Read-SessionReplaySetting -Label "S3 bucket" -CurrentValue $settings["QSEE_REPLAY_S3_BUCKET"]
                $settings["QSEE_REPLAY_AWS_PROFILE"] = Read-SessionReplaySetting -Label "AWS profile" -CurrentValue $settings["QSEE_REPLAY_AWS_PROFILE"]
            }
            8 {
                $settings["QSEE_REPLAY_AWS_REGION"] = Read-SessionReplaySetting -Label "AWS region" -CurrentValue $settings["QSEE_REPLAY_AWS_REGION"]
                $settings["QSEE_REPLAY_AWS_EXE"] = Read-SessionReplaySetting -Label "Absolute AWS CLI path" -CurrentValue $settings["QSEE_REPLAY_AWS_EXE"]
            }
            9 {
                $settings["QSEE_REPLAY_CORE_ROOT"] = Read-SessionReplaySetting -Label "qsee-ai-core root" -CurrentValue $settings["QSEE_REPLAY_CORE_ROOT"]
                $settings["QSEE_REPLAY_PYTHON_EXE"] = Read-SessionReplaySetting -Label "Replay Python executable" -CurrentValue $settings["QSEE_REPLAY_PYTHON_EXE"]
                $settings["QSEE_REPLAY_OUTPUT_ROOT"] = Read-SessionReplaySetting -Label "Generated output root" -CurrentValue $settings["QSEE_REPLAY_OUTPUT_ROOT"]
                $settings["QSEE_REPLAY_ENV_LOCAL"] = Read-SessionReplaySetting -Label "Protected replay env.local path" -CurrentValue $settings["QSEE_REPLAY_ENV_LOCAL"]
            }
            10 {
                $settings["QSEE_REPLAY_CORRELATION_SECONDS"] = Read-SessionReplaySetting -Label "Maximum same-minute delta (0-60 seconds)" -CurrentValue $settings["QSEE_REPLAY_CORRELATION_SECONDS"]
            }
            11 {
                $coverageValue = Read-SessionReplaySetting -Label "Coverage target fraction (exactly 0.50 or 0.70)" -CurrentValue $settings["QSEE_REPLAY_COVERAGE_TARGET_FRACTION"]
                if (@("0.50", "0.70") -cnotcontains $coverageValue) {
                    throw "Coverage target fraction must be exactly 0.50 or 0.70."
                }
                $settings["QSEE_REPLAY_COVERAGE_TARGET_FRACTION"] = $coverageValue
            }
            12 {
                Write-SessionReplaySettingsData -Values $settings
                $null = Read-SessionReplayConfiguration -RepositoryRoot $script:RepositoryRoot
                Write-Host "Settings saved and validated." -ForegroundColor Green
                return
            }
        }
    }
}

# Applies explicit CLI overrides to the validated inert settings object.
function Resolve-SessionReplayConfiguration {
    $configuration = Read-SessionReplayConfiguration -RepositoryRoot $script:RepositoryRoot
    if ($script:LauncherArguments.ContainsKey("Source")) {
        $configuration.Source = $Source
    }
    if ($script:LauncherArguments.ContainsKey("ImageSet")) {
        $configuration.ImageSet = $ImageSet
    }
    if ($script:LauncherArguments.ContainsKey("Client")) {
        $configuration.Client = $Client
        $configuration.TablePath = Get-SessionReplayTablePath -ClientId $Client
    }
    if ($script:LauncherArguments.ContainsKey("Size")) {
        $configuration.Size = $Size
    }
    if ($script:LauncherArguments.ContainsKey("Rotation")) {
        $configuration.Rotation = [int] $Rotation
    }
    if ($script:LauncherArguments.ContainsKey("MaxImages")) {
        $configuration.MaxImages = [int] $MaxImages
    }
    if ($script:LauncherArguments.ContainsKey("OutputPath")) {
        $configuration.OutputRoot = [System.IO.Path]::GetFullPath($OutputPath)
    }
    return $configuration
}

# Names the exact calibration contract selected by the replay settings JSON.
function Get-SessionReplayCalibrationProfile {
    param(
        [Parameter(Mandatory = $true)]
        [double] $CoverageTargetFraction
    )

    if ($CoverageTargetFraction -eq 0.5) {
        return "REPLAY_TOOL_RELAXED_50_PERCENT"
    }
    if ($CoverageTargetFraction -eq 0.7) {
        return "CURRENT_MOBILE_70_PERCENT"
    }
    throw "Replay calibration coverage must be exactly 0.50 or 0.70."
}

# Returns the evidence-stage label for the selected replay calibration contract.
function Get-SessionReplayCalibrationStage {
    param(
        [Parameter(Mandatory = $true)]
        [double] $CoverageTargetFraction
    )

    if ($CoverageTargetFraction -eq 0.5) {
        return "Replay-tool relaxed calibration (50 percent coverage)"
    }
    if ($CoverageTargetFraction -eq 0.7) {
        return "Mobile-equivalent calibration"
    }
    throw "Replay calibration coverage must be exactly 0.50 or 0.70."
}

# Builds an artifact stem that keeps client, size, and calibration profile distinct.
function Get-SessionReplayPlanStem {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    $coveragePercent = [int] [Math]::Round(
        [double] $Configuration.CoverageTargetFraction * 100.0,
        0
    )
    return "{0}-{1}-{2}-{3}-coverage{4}" -f `
        $Configuration.Source, `
        $Configuration.ImageSet, `
        $Configuration.Client, `
        $Configuration.Size, `
        $coveragePercent
}

# Reads one exact key from the protected replay env file without printing its content.
function Get-SessionReplayProtectedEnvValue {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Protected replay env file does not exist: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Protected replay env file must not be a symbolic link or reparse point: $Path"
    }
    if ($item.Length -gt 65536) {
        throw "Protected replay env file exceeds the 64 KiB safety limit: $Path"
    }

    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        $text = $encoding.GetString([System.IO.File]::ReadAllBytes($Path))
    } catch {
        throw "Protected replay env file must contain valid UTF-8 text: $Path"
    }
    $values = [ordered] @{}
    foreach ($line in $text -split "`r?`n") {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }
        if ($line -notmatch '^([A-Z][A-Z0-9_]*)=(.*)$') {
            throw "Protected replay env file contains an invalid assignment."
        }
        $key = $Matches[1]
        $value = $Matches[2]
        if ($values.Contains($key)) {
            throw "Protected replay env file contains duplicate key $key."
        }
        if ($key -cne $Name) {
            throw "Protected replay env file contains unsupported key $key."
        }
        if ([string]::IsNullOrWhiteSpace($value) -or $value.IndexOf([char] 0) -ge 0) {
            throw "Protected replay env key $key must contain a non-empty value."
        }
        $values.Add($key, $value)
    }
    if ($values.Count -ne 1 -or -not $values.Contains($Name)) {
        throw "Protected replay env file must contain exactly $Name."
    }
    return [string] $values[$Name]
}

# Confirms that the selected size exists exactly once as a top-level measurement-table key.
function Test-SessionReplayMeasurementSize {
    param(
        [Parameter(Mandatory = $true)]
        [string] $TablePath,

        [Parameter(Mandatory = $true)]
        [string] $RequestedSize
    )

    if (-not (Test-Path -LiteralPath $TablePath -PathType Leaf)) {
        return $false
    }
    $pattern = "^{0}:\s*$" -f [regex]::Escape($RequestedSize)
    $matches = @(Select-String -LiteralPath $TablePath -Pattern $pattern -CaseSensitive)
    return $matches.Count -eq 1
}

# Runs the authoritative current Java client/table/size contract without processing an image.
function Test-SessionReplayMobileConfiguration {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    $manifestPath = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("qsee-replay-configuration-{0}.json" -f [guid]::NewGuid().ToString("N"))
    try {
        $manifest = [pscustomobject] [ordered] @{
            schema = "qsee.session-replay.configuration.v1"
            client = $Configuration.Client
            size = $Configuration.Size
        }
        Write-SessionReplayJson -Value $manifest -Path $manifestPath
        Invoke-SessionReplayCheckedProcess `
            -FilePath (Join-Path $script:RepositoryRoot "gradlew.bat") `
            -Arguments @(
                ":app:testDebugUnitTest",
                "--tests", "com.example.qsee.SessionReplayAnalysis",
                "-Dqsee.replayConfigurationManifest=$manifestPath"
            ) `
            -Stage "Current mobile client/table/size preflight"
        return $true
    } finally {
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            Remove-Item -LiteralPath $manifestPath -Force
        }
    }
}

# Runs one ordered preflight condition and stops immediately when strict mode is enabled.
function Invoke-SessionReplayPreflightCheck {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]] $Checks,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Check,

        [switch] $ThrowOnFailure
    )

    $ok = $false
    $failure = $null
    $diagnostic = $null
    try {
        $ok = [bool] (& $Check)
    } catch {
        $failure = $_
        $diagnostic = Get-SessionReplayFailureDiagnostic -Failure $_
    }

    $status = if ($ok) { "OK" } else { "FAILED" }
    $color = if ($ok) { "Green" } else { "Yellow" }
    Write-Host ("  {0,-28} {1}" -f ($Name + ":"), $status) -ForegroundColor $color
    $Checks.Add([pscustomobject] [ordered] @{
            Name = $Name
            Ok = $ok
            Diagnostic = $diagnostic
        })

    if ($ThrowOnFailure -and -not $ok) {
        $message = "Session replay preflight failed at '$Name'."
        if (-not [string]::IsNullOrWhiteSpace($diagnostic)) {
            $message += " $diagnostic"
        }
        $innerException = if ($null -eq $failure) { $null } else { $failure.Exception }
        $exception = [System.InvalidOperationException]::new($message, $innerException)
        if (-not [string]::IsNullOrWhiteSpace($diagnostic)) {
            $exception.Data["StandardError"] = $diagnostic
        }
        throw $exception
    }
}

# Runs ordered dependency and protected-configuration checks without compatibility fallbacks.
function Test-SessionReplayPreflight {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [switch] $ThrowOnFailure
    )

    Write-Host "Session replay preflight" -ForegroundColor Cyan
    $checks = [System.Collections.Generic.List[object]]::new()
    $expectedTablePath = if (@("Kiabi", "Panko") -ccontains [string] $Configuration.Client) {
        Get-SessionReplayTablePath -ClientId $Configuration.Client
    } else {
        $null
    }

    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Explicit supported client" -ThrowOnFailure:$ThrowOnFailure -Check {
        return @("Kiabi", "Panko") -ccontains [string] $Configuration.Client
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Client measurement table" -ThrowOnFailure:$ThrowOnFailure -Check {
        return $null -ne $expectedTablePath -and $Configuration.TablePath -ceq $expectedTablePath
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Remote endpoint" -ThrowOnFailure:$ThrowOnFailure -Check {
        return $Configuration.CvEndpoint -ceq $script:RequiredRemoteEndpoint
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Remote model" -ThrowOnFailure:$ThrowOnFailure -Check {
        return $Configuration.Model -ceq $script:RequiredRemoteModel
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "qsee-ai-core" -ThrowOnFailure:$ThrowOnFailure -Check {
        return $null -ne $Configuration.CoreRoot -and
            (Test-Path -LiteralPath $Configuration.CoreRoot -PathType Container)
    }
    $replayScript = if ($null -eq $Configuration.CoreRoot) { $null } else { Join-Path $Configuration.CoreRoot $script:ReplayScriptRelativePath }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "CV replay script" -ThrowOnFailure:$ThrowOnFailure -Check {
        return $null -ne $replayScript -and (Test-Path -LiteralPath $replayScript -PathType Leaf)
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Replay Python" -ThrowOnFailure:$ThrowOnFailure -Check {
        return $null -ne $Configuration.PythonExe -and
            (Test-Path -LiteralPath $Configuration.PythonExe -PathType Leaf)
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Replay engine" -ThrowOnFailure:$ThrowOnFailure -Check {
        return Test-Path -LiteralPath $script:ReplayEnginePath -PathType Leaf
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Workbook generator" -ThrowOnFailure:$ThrowOnFailure -Check {
        return Test-Path -LiteralPath $script:ReplayWorkbookPath -PathType Leaf
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Microsoft Excel workbook runtime" -ThrowOnFailure:$ThrowOnFailure -Check {
        if (-not (Test-Path -LiteralPath $script:ReplayWorkbookPath -PathType Leaf)) {
            return $false
        }
        & $script:ReplayWorkbookPath -Preflight | Out-Host
        return $true
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Gradle wrapper" -ThrowOnFailure:$ThrowOnFailure -Check {
        return Test-Path -LiteralPath (Join-Path $script:RepositoryRoot "gradlew.bat") -PathType Leaf
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Measurement table" -ThrowOnFailure:$ThrowOnFailure -Check {
        return Test-Path -LiteralPath $Configuration.TablePath -PathType Leaf
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Exact measurement size" -ThrowOnFailure:$ThrowOnFailure -Check {
        return Test-SessionReplayMeasurementSize `
            -TablePath $Configuration.TablePath `
            -RequestedSize $Configuration.Size
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Current mobile contract" -ThrowOnFailure:$ThrowOnFailure -Check {
        if (
            -not (Test-Path -LiteralPath (Join-Path $script:RepositoryRoot "gradlew.bat") -PathType Leaf) -or
            -not (Test-Path -LiteralPath $Configuration.TablePath -PathType Leaf)
        ) {
            return $false
        }
        return Test-SessionReplayMobileConfiguration -Configuration $Configuration
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Protected replay env" -ThrowOnFailure:$ThrowOnFailure -Check {
        return $null -ne $Configuration.EnvLocal -and
            (Test-Path -LiteralPath $Configuration.EnvLocal -PathType Leaf)
    }
    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Remote API key" -ThrowOnFailure:$ThrowOnFailure -Check {
        $apiKey = Get-SessionReplayProtectedEnvValue `
            -Path $Configuration.EnvLocal `
            -Name "QSEE_REPLAY_CV_API_KEY"
        return -not [string]::IsNullOrWhiteSpace($apiKey)
    }

    if ($Configuration.Source -ceq "Local") {
        Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Local mirror" -ThrowOnFailure:$ThrowOnFailure -Check {
            return Test-Path -LiteralPath $Configuration.MirrorRoot -PathType Container
        }
    } else {
        Invoke-SessionReplayPreflightCheck -Checks $checks -Name "AWS CLI" -ThrowOnFailure:$ThrowOnFailure -Check {
            return Test-Path -LiteralPath $Configuration.AwsExe -PathType Leaf
        }
        Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Named-profile S3 access" -ThrowOnFailure:$ThrowOnFailure -Check {
            if (
                -not (Test-Path -LiteralPath $Configuration.AwsExe -PathType Leaf) -or
                -not (Get-Command Test-SessionReplayS3Access -ErrorAction SilentlyContinue)
            ) {
                return $false
            }
            $null = Test-SessionReplayS3Access -Configuration $Configuration
            return $true
        }
    }

    Invoke-SessionReplayPreflightCheck -Checks $checks -Name "Python packages" -ThrowOnFailure:$ThrowOnFailure -Check {
        if (
            $null -eq $Configuration.PythonExe -or
            -not (Test-Path -LiteralPath $Configuration.PythonExe -PathType Leaf)
        ) {
            return $false
        }
        Invoke-SessionReplayCheckedProcess `
            -FilePath $Configuration.PythonExe `
            -Arguments @("-c", "import cv2, httpx, numpy") `
            -Stage "Replay Python package preflight"
        return $true
    }

    $failed = @($checks | Where-Object { -not $_.Ok })
    return $failed.Count -eq 0
}

# Creates the isolated replay Python environment only after an explicit Setup action.
function Initialize-SessionReplayPython {
    $settings = if (Test-Path -LiteralPath $script:ConfigurationPath -PathType Leaf) {
        Read-SessionReplaySettingsData -Path $script:ConfigurationPath
    } else {
        New-SessionReplaySettingsData
    }
    if (-not (Test-Path -LiteralPath $script:ReplayRequirementsPath -PathType Leaf)) {
        throw "Missing replay requirements: $script:ReplayRequirementsPath"
    }
    $uv = Get-Command uv -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $uv) {
        throw "uv is required for the explicit replay Setup action."
    }
    $outputRoot = [System.IO.Path]::GetFullPath($settings["QSEE_REPLAY_OUTPUT_ROOT"])
    $environmentRoot = Join-Path $outputRoot ".venv"
    $pythonPath = Join-Path $environmentRoot "Scripts\python.exe"
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
        & $uv.Source venv $environmentRoot --python 3.13
        if ($LASTEXITCODE -ne 0) {
            throw "uv failed to create the replay environment."
        }
    }
    & $uv.Source pip install --python $pythonPath --requirement $script:ReplayRequirementsPath
    if ($LASTEXITCODE -ne 0) {
        throw "uv failed to install the replay requirements."
    }
    $settings["QSEE_REPLAY_PYTHON_EXE"] = $pythonPath
    Write-SessionReplaySettingsData -Values $settings
    Write-Host "Replay Python environment prepared and saved in the local JSON settings." -ForegroundColor Green
}

# Selects one explicit or latest prior same-day group without trying an older alternative.
function Select-SessionReplayCalibrationGroup {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $Groups,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Lens", "Homography")]
        [string] $Kind,

        [Parameter(Mandatory = $true)]
        [datetime] $FirstImageUtc,

        [string] $ExplicitGroupId
    )

    $kindGroups = @($Groups | Where-Object Kind -eq $Kind | ForEach-Object {
            $last = @($_.Objects | Sort-Object LastModifiedUtc -Descending | Select-Object -First 1)
            [pscustomobject] @{
                Group = $_
                LastModifiedUtc = [datetime] $last[0].LastModifiedUtc
            }
        })
    if (-not [string]::IsNullOrWhiteSpace($ExplicitGroupId)) {
        $selected = @($kindGroups | Where-Object { $_.Group.GroupId -ceq $ExplicitGroupId })
        if ($selected.Count -ne 1) {
            throw "$Kind calibration group '$ExplicitGroupId' does not exist in this UTC day."
        }
        if ($selected[0].LastModifiedUtc -ge $FirstImageUtc) {
            throw "$Kind calibration group '$ExplicitGroupId' is not earlier than the first selected image."
        }
        return $selected[0].Group
    }

    $prior = @($kindGroups | Where-Object {
            $_.LastModifiedUtc -lt $FirstImageUtc
        } | Sort-Object LastModifiedUtc -Descending)
    if ($prior.Count -eq 0) {
        return $null
    }
    return $prior[0].Group
}

# Binds every selected record to exact local bytes or its immutable listed S3 ETag.
function Set-SessionReplayContentIdentities {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]] $Records
    )

    foreach ($record in $Records) {
        if ($Configuration.Source -ceq "Local") {
            $path = [System.IO.Path]::GetFullPath([string] $record.LocalPath)
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "A selected local evidence file is missing: $path"
            }
            $file = Get-Item -LiteralPath $path
            if ($file.Length -ne [long] $record.Size) {
                throw "A selected local evidence file changed size during planning: $path"
            }
            $record.ContentIdentity = "sha256:" + (
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            )
        } elseif ([string] $record.ContentIdentity -cnotmatch '^s3-etag:"[0-9a-fA-F]{32}(?:-[0-9]+)?"$') {
            throw "A selected S3 evidence object has no valid listed ETag: $($record.Key)"
        }
    }
}

# Builds the source, temporal-correlation, and calibration plan for one UTC day.
function New-SessionReplayPlan {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [string] $UtcDay
    )

    $inventoryArguments = @{
        Configuration = $Configuration
        Source = $Configuration.Source
        UtcDay = $UtcDay
    }
    $inventory = Get-SessionReplayDayInventory @inventoryArguments
    if ($inventory.Images.Count -eq 0) {
        throw "No $($Configuration.ImageSet) images exist for UTC day $UtcDay."
    }
    $correlationArguments = @{
        Captures = $inventory.Images
        Results = $inventory.Results
        MaxDeltaSeconds = $Configuration.CorrelationSeconds
    }
    $correlations = @(Resolve-SessionReplayCorrelation @correlationArguments)
    $firstImageUtc = [datetime] ($inventory.Images | Sort-Object LastModifiedUtc | Select-Object -First 1).LastModifiedUtc
    $lensArguments = @{
        Groups = $inventory.CalibrationGroups
        Kind = "Lens"
        FirstImageUtc = $firstImageUtc
        ExplicitGroupId = $LensGroup
    }
    $lens = Select-SessionReplayCalibrationGroup @lensArguments
    $homographyArguments = @{
        Groups = $inventory.CalibrationGroups
        Kind = "Homography"
        FirstImageUtc = $firstImageUtc
        ExplicitGroupId = $HomographyGroup
    }
    $homography = Select-SessionReplayCalibrationGroup @homographyArguments
    $lensInputReady = $null -ne $lens -and $lens.ObjectCount -ge 10
    $homographyInputReady = (
        $null -ne $homography -and
        $homography.ObjectCount -ge 2 -and
        $homography.ObjectCount -le 12
    )
    $selectedRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($record in @($inventory.Images) + @($inventory.Results)) {
        $selectedRecords.Add($record)
    }
    foreach ($group in @($lens, $homography)) {
        if ($null -ne $group) {
            foreach ($record in $group.Objects) {
                $selectedRecords.Add($record)
            }
        }
    }
    Set-SessionReplayContentIdentities `
        -Configuration $Configuration `
        -Records $selectedRecords.ToArray()
    $calibrationProfile = Get-SessionReplayCalibrationProfile `
        -CoverageTargetFraction $Configuration.CoverageTargetFraction

    return [pscustomobject] [ordered] @{
        Schema = "qsee.session-replay.plan.v1"
        UtcDay = $UtcDay
        Source = $Configuration.Source
        ImageSet = $Configuration.ImageSet
        Client = $Configuration.Client
        Size = $Configuration.Size
        Rotation = $Configuration.Rotation
        CoverageTargetFraction = $Configuration.CoverageTargetFraction
        CalibrationProfile = $calibrationProfile
        CorrelationWindowSeconds = $Configuration.CorrelationSeconds
        MatchingImageCount = $inventory.MatchingImageCount
        SelectedImageCount = $inventory.Images.Count
        Images = $inventory.Images
        Results = $inventory.Results
        Correlations = $correlations
        LensCalibration = $lens
        HomographyCalibration = $homography
        LensCalibrationInputReady = $lensInputReady
        HomographyCalibrationInputReady = $homographyInputReady
        MetricReplayReady = $lensInputReady -and $homographyInputReady
        CalibrationAssociation = "inferred-from-same-UTC-day-and-prior-upload-time"
        ResultAssociation = "inferred-from-one-to-one-same-UTC-minute-upload-time"
    }
}

# Writes a JSON artifact atomically under the ignored replay output root.
function Write-SessionReplayJson {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Value,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $temporaryPath = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $Value | ConvertTo-Json -Depth 20
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            $json + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false)
        )
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

# Writes a UTF-8 Markdown evidence artifact atomically.
function Write-SessionReplayMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]] $Lines,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $temporaryPath = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllLines(
            $temporaryPath,
            $Lines,
            [System.Text.UTF8Encoding]::new($false)
        )
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

# Formats one signed image-minus-reference delta in minutes and seconds.
function Format-SessionReplaySignedDelta {
    param(
        [AllowNull()]
        [Nullable[double]] $Seconds
    )

    if ($null -eq $Seconds) {
        return "n/a"
    }
    $numericSeconds = [double] $Seconds
    $sign = if ($numericSeconds -ge 0.0) { "+" } else { "-" }
    $absolute = [Math]::Abs($numericSeconds)
    $minutes = [Math]::Floor($absolute / 60.0)
    $remaining = $absolute - ($minutes * 60.0)
    return [string]::Format(
        [System.Globalization.CultureInfo]::InvariantCulture,
        "{0}{1:00}m {2:00.00}s",
        $sign,
        $minutes,
        $remaining
    )
}

# Returns the last timestamped frame of one selected calibration group as its reference.
function Get-SessionReplayCalibrationReference {
    param(
        [AllowNull()]
        [object] $Group
    )

    if ($null -eq $Group -or $Group.ObjectCount -eq 0) {
        return $null
    }
    return @($Group.Objects | Sort-Object LastModifiedUtc -Descending | Select-Object -First 1)[0]
}

# Builds the per-image time-association rows used by plan and failure evidence.
function Get-SessionReplayTemporalEvidenceRows {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan
    )

    $lensReference = Get-SessionReplayCalibrationReference -Group $Plan.LensCalibration
    $homographyReference = Get-SessionReplayCalibrationReference -Group $Plan.HomographyCalibration
    $lensUtc = if ($null -eq $lensReference) {
        $null
    } else {
        Get-SessionReplayRecordEventUtc -Record $lensReference -Role "Calibration"
    }
    $homographyUtc = if ($null -eq $homographyReference) {
        $null
    } else {
        Get-SessionReplayRecordEventUtc -Record $homographyReference -Role "Calibration"
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($imageRecord in $Plan.Images) {
        $imageUtc = Get-SessionReplayRecordEventUtc -Record $imageRecord -Role "Image"
        $correlation = Get-SessionReplayHistoricalCorrelation -Plan $Plan -CaptureKey $imageRecord.Key
        $ambiguousResults = @($Plan.Correlations | Where-Object {
                $_.Status -ceq "Ambiguous" -and
                $_.Subject -ceq "Result" -and
                @($_.CandidateCaptureKeys) -ccontains $imageRecord.Key
            })
        $resultRecord = if ($null -ne $correlation) {
            $correlation.Result
        } elseif ($ambiguousResults.Count -eq 1) {
            $ambiguousResults[0].Result
        } else {
            $null
        }
        $resultUtc = if ($null -eq $resultRecord) {
            $null
        } else {
            Get-SessionReplayRecordEventUtc -Record $resultRecord -Role "Result"
        }
        $resultLabel = if ($null -ne $correlation) {
            [System.IO.Path]::GetFileName($correlation.ResultKey)
        } elseif ($ambiguousResults.Count -gt 0) {
            "AMBIGUOUS: " + (@($ambiguousResults | ForEach-Object {
                        [System.IO.Path]::GetFileName($_.ResultKey)
                    }) -join "<br>")
        } else {
            "none"
        }
        $rows.Add([pscustomobject] [ordered] @{
                Image = [System.IO.Path]::GetFileName($imageRecord.Key)
                ImageUtc = $imageUtc.ToString("yyyy-MM-dd HH:mm:ss.fff 'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
                Result = $resultLabel
                ResultDelta = Format-SessionReplaySignedDelta -Seconds $(if ($null -eq $resultUtc) { $null } else { ($imageUtc - $resultUtc).TotalSeconds })
                LensDelta = Format-SessionReplaySignedDelta -Seconds $(if ($null -eq $lensUtc) { $null } else { ($imageUtc - $lensUtc).TotalSeconds })
                HomographyDelta = Format-SessionReplaySignedDelta -Seconds $(if ($null -eq $homographyUtc) { $null } else { ($imageUtc - $homographyUtc).TotalSeconds })
            })
    }
    return $rows.ToArray()
}

# Returns exact Python, OpenCV, and NumPy binary identities for negative-cache binding.
function Get-SessionReplayRuntimeIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    if (-not (Test-Path -LiteralPath $Configuration.PythonExe -PathType Leaf)) {
        throw "Replay Python is missing while computing the evidence fingerprint."
    }
    try {
        $identityLines = @(& $Configuration.PythonExe -c (
                "import cv2,numpy,platform,sys;" +
                "import numpy._core._multiarray_umath as numpy_core;" +
                "print(platform.platform()+';python='+platform.python_version()+" +
                "';opencv='+cv2.__version__+';numpy='+numpy.__version__);" +
                "print(sys.executable);print(cv2.__file__);print(numpy_core.__file__)"
            ) 2>$null)
        if ($LASTEXITCODE -ne 0 -or $identityLines.Count -ne 4) {
            throw "Replay Python returned an incomplete runtime identity."
        }
        $entries = [System.Collections.Generic.List[string]]::new()
        $entries.Add([string] $identityLines[0])
        foreach ($runtimePathText in $identityLines[1..3]) {
            $runtimePath = [System.IO.Path]::GetFullPath([string] $runtimePathText)
            if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) {
                throw "A replay runtime binary is missing: $runtimePath"
            }
            $hash = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $entries.Add("$runtimePath=$hash")
        }
        return $entries -join ";"
    } catch {
        $failure = [System.InvalidOperationException]::new(
            "Replay runtime identity could not be established.",
            $_.Exception
        )
        throw $failure
    }
}

# Hashes an exact ordered set of replay source files into one implementation identity.
function Get-SessionReplaySourceSetHash {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Paths
    )

    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @($Paths | Sort-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Replay implementation source is missing: $path"
        }
        $relative = [System.IO.Path]::GetRelativePath($script:RepositoryRoot, $path).Replace("\", "/")
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $entries.Add("$relative=$hash")
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($entries -join "`n")
    return [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData($bytes)
    ).Replace("-", "").ToLowerInvariant()
}

# Computes a content- and implementation-sensitive identifier for one replay plan.
function Get-SessionReplayEvidenceFingerprint {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    $configPath = Join-Path $script:RepositoryRoot "app\src\main\assets\qsee_config.yaml"
    $mobileCvPath = Join-Path $PSScriptRoot "session-replay\mobile_cv.py"
    $clientDirectory = Join-Path `
        $script:RepositoryRoot `
        ("app\src\main\java\com\example\qsee\client\{0}" -f $Plan.Client.ToLowerInvariant())
    $mobileSourcePaths = @(
        (Join-Path $script:RepositoryRoot "app\src\test\java\com\example\qsee\SessionReplayAnalysis.java"),
        (Join-Path $script:RepositoryRoot "app\src\main\java\com\example\qsee\DetectionResult.java"),
        (Join-Path $script:RepositoryRoot "app\src\main\java\com\example\qsee\DistanceMapper.java"),
        (Join-Path $script:RepositoryRoot "app\src\main\java\com\example\qsee\EdgeGarmentOutline.java"),
        (Join-Path $script:RepositoryRoot "app\src\main\java\com\example\qsee\GarmentOutline.java"),
        (Join-Path $script:RepositoryRoot "app\src\main\java\com\example\qsee\Measure.java"),
        (Join-Path $script:RepositoryRoot "app\src\main\java\com\example\qsee\MeasurementSet.java"),
        (Join-Path $script:RepositoryRoot "app\src\main\java\com\example\qsee\ProductInfo.java")
    ) + @(Get-ChildItem -LiteralPath $clientDirectory -File -Filter "*.java" | ForEach-Object FullName)
    $groups = foreach ($group in @($Plan.LensCalibration, $Plan.HomographyCalibration)) {
        if ($null -eq $group) {
            continue
        }
        [pscustomobject] [ordered] @{
            kind = $group.Kind
            id = $group.GroupId
            objects = @($group.Objects | ForEach-Object {
                    [pscustomobject] [ordered] @{
                        key = $_.Key
                        size = $_.Size
                        timestampUtc = ([datetime] $_.LastModifiedUtc).ToUniversalTime().ToString("o")
                        contentIdentity = $_.ContentIdentity
                    }
                })
        }
    }
    $descriptor = [pscustomobject] [ordered] @{
        schema = "qsee.session-replay.evidence-key.v1"
        day = $Plan.UtcDay
        source = $Plan.Source
        imageSet = $Plan.ImageSet
        client = $Plan.Client
        size = $Plan.Size
        rotation = $Plan.Rotation
        coverageTargetFraction = $Plan.CoverageTargetFraction
        calibrationProfile = $Plan.CalibrationProfile
        correlationWindowSeconds = $Plan.CorrelationWindowSeconds
        images = @($Plan.Images | ForEach-Object {
                [pscustomobject] [ordered] @{
                    key = $_.Key
                    size = $_.Size
                    timestampUtc = ([datetime] $_.LastModifiedUtc).ToUniversalTime().ToString("o")
                    contentIdentity = $_.ContentIdentity
                }
            })
        results = @($Plan.Results | ForEach-Object {
                [pscustomobject] [ordered] @{
                    key = $_.Key
                    size = $_.Size
                    timestampUtc = ([datetime] $_.LastModifiedUtc).ToUniversalTime().ToString("o")
                    contentIdentity = $_.ContentIdentity
                }
            })
        groups = @($groups)
        runnerSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        engineSha256 = (Get-FileHash -LiteralPath $script:ReplayEnginePath -Algorithm SHA256).Hash
        mobileCvSha256 = (Get-FileHash -LiteralPath $mobileCvPath -Algorithm SHA256).Hash
        requirementsSha256 = (Get-FileHash -LiteralPath $script:ReplayRequirementsPath -Algorithm SHA256).Hash
        mobileConfigurationSha256 = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash
        measurementTableSha256 = (Get-FileHash -LiteralPath $Configuration.TablePath -Algorithm SHA256).Hash
        mobileSourceSetSha256 = Get-SessionReplaySourceSetHash -Paths $mobileSourcePaths
        runtimeIdentity = Get-SessionReplayRuntimeIdentity -Configuration $Configuration
    }
    $json = $descriptor | ConvertTo-Json -Depth 12 -Compress
    $digest = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($json)
    )
    return [System.BitConverter]::ToString($digest).Replace("-", "").ToLowerInvariant()
}

# Returns explicit readiness blockers without treating stored files as proof of calibration success.
function Get-SessionReplayPlanBlockers {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan
    )

    $blockers = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Plan.LensCalibration) {
        $blockers.Add("No same-day lens calibration group was selected before the first image.")
    } elseif (-not $Plan.LensCalibrationInputReady) {
        $blockers.Add("Lens group $($Plan.LensCalibration.GroupId) has $($Plan.LensCalibration.ObjectCount) frame(s); at least 10 are required.")
    }
    if ($null -eq $Plan.HomographyCalibration) {
        $blockers.Add("No same-day checkerboard calibration group was selected before the first image.")
    } elseif (-not $Plan.HomographyCalibrationInputReady) {
        $blockers.Add("Checkerboard group $($Plan.HomographyCalibration.GroupId) has $($Plan.HomographyCalibration.ObjectCount) frame(s); 2 through 12 are allowed.")
    }
    return $blockers.ToArray()
}

# Creates durable human-readable evidence for a plan or failed execution stage.
function New-SessionReplayEvidenceMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [string] $Status,

        [Parameter(Mandatory = $true)]
        [string] $Stage,

        [Parameter(Mandatory = $true)]
        [string] $Fingerprint,

        [AllowNull()]
        [string] $Diagnostic
    )

    $matched = @($Plan.Correlations | Where-Object { $_.Status -ceq "Matched" -and $_.Subject -ceq "Result" }).Count
    $ambiguous = @($Plan.Correlations | Where-Object { $_.Status -ceq "Ambiguous" -and $_.Subject -ceq "Result" }).Count
    $lensName = if ($null -eq $Plan.LensCalibration) { "MISSING" } else { "$($Plan.LensCalibration.GroupId) ($($Plan.LensCalibration.ObjectCount) frames)" }
    $homographyName = if ($null -eq $Plan.HomographyCalibration) { "MISSING" } else { "$($Plan.HomographyCalibration.GroupId) ($($Plan.HomographyCalibration.ObjectCount) frames)" }
    $coveragePercent = [Math]::Round([double] $Plan.CoverageTargetFraction * 100.0, 2)
    $coverageDescription = if ($Plan.CalibrationProfile -ceq "REPLAY_TOOL_RELAXED_50_PERCENT") {
        "$coveragePercent% (replay-tool relaxed; current mobile is 70%)"
    } else {
        "$coveragePercent% (current mobile contract)"
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(
            "# QSee session replay evidence",
            "",
            "- Status: **$Status**",
            "- Stage: **$Stage**",
            "- UTC day: **$($Plan.UtcDay)**",
            "- Source / image set: **$($Plan.Source) / $($Plan.ImageSet)**",
            "- Client / size / rotation: **$($Plan.Client) / $($Plan.Size) / $($Plan.Rotation) degrees**",
            "- Images: **$($Plan.SelectedImageCount) selected of $($Plan.MatchingImageCount)**",
            "- Historical result associations: **$matched matched, $ambiguous ambiguous**",
            "- Lens calibration: **$lensName**",
            "- Checkerboard calibration: **$homographyName**",
            "- Checkerboard coverage target: **$coverageDescription**",
            "- Calibration profile: **$($Plan.CalibrationProfile)**",
            ('- Evidence fingerprint: `{0}`' -f $Fingerprint),
            "",
            "All associations below are inferred from UTC timestamps. They are not database session identities. Calibration deltas use the last timestamped frame in each selected group as the reference.",
            ""
        )) {
        $lines.Add($line)
    }
    $blockers = @(Get-SessionReplayPlanBlockers -Plan $Plan)
    if ($blockers.Count -gt 0) {
        $lines.Add("## Blocking evidence")
        $lines.Add("")
        foreach ($blocker in $blockers) {
            $lines.Add("- $blocker")
        }
        $lines.Add("")
    }
    if (-not [string]::IsNullOrWhiteSpace($Diagnostic)) {
        $codeFence = [string]::new([char] 96, 3)
        $diagnosticFence = [string]::new([char] 96, 4)
        $safeDiagnostic = $Diagnostic.Replace($codeFence, "'''").Trim()
        $lines.Add("## Exact diagnostic")
        $lines.Add("")
        $lines.Add($diagnosticFence + "text")
        foreach ($diagnosticLine in $safeDiagnostic -split "\r?\n") {
            $lines.Add($diagnosticLine)
        }
        $lines.Add($diagnosticFence)
        $lines.Add("")
    }
    $lines.Add("## Per-image UTC evidence")
    $lines.Add("")
    $lines.Add("| Image | Photo UTC | Measures JSON | Photo - JSON | Photo - lens | Photo - checkerboard |")
    $lines.Add("| --- | --- | --- | ---: | ---: | ---: |")
    foreach ($row in Get-SessionReplayTemporalEvidenceRows -Plan $Plan) {
        $lines.Add("| $($row.Image) | $($row.ImageUtc) | $($row.Result) | $($row.ResultDelta) | $($row.LensDelta) | $($row.HomographyDelta) |")
    }
    $lines.Add("")
    $lines.Add("This artifact is evidence, not a PASS/FAIL result. No metric verdict is emitted unless calibration, frame validation, remote landmarks, edge extraction, and the current mobile calculator all pass.")
    return $lines.ToArray()
}

# Saves plan evidence next to its JSON manifest.
function Save-SessionReplayPlanEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    $fingerprint = Get-SessionReplayEvidenceFingerprint `
        -Plan $Plan `
        -Configuration $Configuration
    $status = if ($Plan.MetricReplayReady) { "READY FOR LOCAL CALIBRATION" } else { "BLOCKED BEFORE REMOTE SUBMISSION" }
    $lines = New-SessionReplayEvidenceMarkdown `
        -Plan $Plan `
        -Status $status `
        -Stage "PLAN" `
        -Fingerprint $fingerprint `
        -Diagnostic $null
    Write-SessionReplayMarkdown -Lines $lines -Path $Path
    return $fingerprint
}

# Returns the deterministic workbook filename stored directly in one run root.
function Get-SessionReplayWorkbookFileName {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan
    )

    $clientToken = ([string] $Plan.Client).ToLowerInvariant()
    if ($clientToken -cnotmatch '^[a-z0-9][a-z0-9.-]{0,127}$') {
        throw "The replay client cannot be used in the workbook filename."
    }
    return "{0}-{1}-session-replay-evidence.xlsx" -f $clientToken, $Plan.UtcDay
}

# Creates a plain-language guide for one daily replay result folder.
function New-SessionReplayPlainLanguageReadme {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan
    )

    $matched = @($Plan.Correlations | Where-Object {
            $_.Status -ceq "Matched" -and $_.Subject -ceq "Result"
        }).Count
    $missing = [Math]::Max(0, [int] $Plan.SelectedImageCount - $matched)
    $workbookFileName = Get-SessionReplayWorkbookFileName -Plan $Plan
    $imageTimes = @($Plan.Images | ForEach-Object { ([datetime] $_.LastModifiedUtc).ToUniversalTime() } | Sort-Object)
    $firstImageUtc = $imageTimes[0].ToString("yyyy-MM-dd HH:mm:ss 'UTC'", [System.Globalization.CultureInfo]::InvariantCulture)
    $lastImageUtc = $imageTimes[-1].ToString("yyyy-MM-dd HH:mm:ss 'UTC'", [System.Globalization.CultureInfo]::InvariantCulture)
    $coveragePercent = [Math]::Round([double] $Plan.CoverageTargetFraction * 100.0, 2)
    $lensDescription = if ($null -eq $Plan.LensCalibration) {
        "No eligible same-day lens photo group was found."
    } else {
        $lensTimes = @($Plan.LensCalibration.Objects | ForEach-Object {
                ([datetime] $_.LastModifiedUtc).ToUniversalTime()
            } | Sort-Object)
        "The selected lens group contains $($Plan.LensCalibration.ObjectCount) photos taken from $($lensTimes[0].ToString("yyyy-MM-dd HH:mm:ss 'UTC'")) to $($lensTimes[-1].ToString("yyyy-MM-dd HH:mm:ss 'UTC'" ))."
    }
    $checkerboardDescription = if ($null -eq $Plan.HomographyCalibration) {
        "No eligible same-day checkerboard photo group was found."
    } else {
        $checkerboardTimes = @($Plan.HomographyCalibration.Objects | ForEach-Object {
                ([datetime] $_.LastModifiedUtc).ToUniversalTime()
            } | Sort-Object)
        "The selected checkerboard group contains $($Plan.HomographyCalibration.ObjectCount) photos taken from $($checkerboardTimes[0].ToString("yyyy-MM-dd HH:mm:ss 'UTC'")) to $($checkerboardTimes[-1].ToString("yyyy-MM-dd HH:mm:ss 'UTC'" ))."
    }

    return @(
        "# How this daily replay works",
        "",
        "This folder contains the reconstructed analysis for **$($Plan.UtcDay)**, client **$($Plan.Client)**, size **$($Plan.Size)**. It starts from $($Plan.SelectedImageCount) original garment photos captured between **$firstImageUtc** and **$lastImageUtc**.",
        "",
        "The replay is a practical way to review a day when the database session is unavailable. It uses the retained files and their UTC times. The links between photos, calibration files, and historical results are therefore time-based evidence, not a recovered database identity.",
        "",
        "## What we use as the starting point",
        "",
        "For every garment we begin with the original camera photo. We do not begin with the old measurement JSON and we do not copy old landmark positions. The historical JSON, when available, is shown only as a comparison with the newly calculated result.",
        "",
        "The inputs are:",
        "",
        "1. the original garment photo;",
        "2. lens-calibration photos from the same UTC day and uploaded before the garment photos;",
        "3. checkerboard photos from the same UTC day and uploaded before the garment photos;",
        "4. an optional historical measurement JSON matched to the photo within the same UTC minute.",
        "",
        "## Why we calibrate the camera twice",
        "",
        "The **lens photos** teach the system how this specific camera bends the image near the centre and edges. This lets us correct lens distortion before measuring.",
        "",
        "The **checkerboard photos** teach the system how distances in the corrected image relate to real millimetres on the working surface. The board is photographed in several positions so that the useful area of the table is covered, not just one small spot.",
        "",
        $lensDescription,
        "",
        $checkerboardDescription,
        "",
        "This replay uses a checkerboard coverage target of **$coveragePercent%**. A 50% target is explicitly marked as a temporary replay setting; the current mobile app remains configured for 70%.",
        "",
        "## How the garment points are found",
        "",
        "After calibration is ready, the original garment photo is sent to the current remote computer-vision model. The model finds 17 visible garment landmarks, labelled **K1 to K17**. These are features such as neckline, shoulder, sleeve, side, and hem locations. They come from the pixels in the current photo, not from the historical JSON.",
        "",
        "The calibrated image and the detected garment outline are then passed to the same client-specific measurement calculator used by the mobile app. The calculator combines landmark-to-landmark distances, garment edges, the selected client, the selected size, and the current measurement table. Because the camera has been calibrated, those image distances can be expressed in centimetres and compared with the allowed range.",
        "",
        "A green result means the current measurement is inside the allowed range. A red result means it is outside the range. A FAIL verdict is a measurement outcome; it does not mean that the replay pipeline failed.",
        "",
        "## How files are matched in time",
        "",
        "- Garment photos are selected only from the UTC day **$($Plan.UtcDay)**.",
        "- Historical JSON files are matched one-to-one inside the same UTC calendar minute. The configured nominal window is $($Plan.CorrelationWindowSeconds) seconds, but the replay does not cross into a different minute and does not guess when more than one candidate is possible.",
        "- Lens and checkerboard groups must come from the same UTC day and must have completed uploading before the first selected garment photo.",
        "- This day has **$matched matched historical JSON files** and **$missing photos without an unambiguous historical JSON**.",
        "- Every annotated image shows the photo-to-JSON, photo-to-lens, and photo-to-checkerboard time differences.",
        "",
        "## Where to look",
        "",
        "- [Spreadsheet evidence]($workbookFileName)",
        "- [Annotated garment images](overlays/)",
        "- [Run summary](run-summary.json)",
        "- [Timing and selection evidence](plan-evidence.md)",
        "- [Calibration result](calibration.json)",
        "- [Per-image measurements](analysis/)",
        "- [Remote landmark evidence](remote/)",
        "",
        "All times in this folder are UTC."
    )
}

# Writes the plain-language daily guide at the root of one replay result folder.
function Save-SessionReplayPlainLanguageReadme {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $lines = New-SessionReplayPlainLanguageReadme -Plan $Plan
    Write-SessionReplayMarkdown -Lines $lines -Path $Path
}

# Rejects worksheet constructs that Excel repairs even when generic OOXML parsing succeeds.
function Assert-SessionReplayWorkbookExcelCompatibility {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath)
    try {
        $worksheetEntries = @($archive.Entries | Where-Object {
                $_.FullName -clike "xl/worksheets/sheet*.xml"
            })
        if ($worksheetEntries.Count -ne 5) {
            throw "The evidence workbook must contain exactly five worksheet XML parts."
        }
        foreach ($entry in $worksheetEntries) {
            $reader = [System.IO.StreamReader]::new($entry.Open())
            try {
                $document = [System.Xml.XmlDocument]::new()
                $document.PreserveWhitespace = $true
                $document.LoadXml($reader.ReadToEnd())
            } finally {
                $reader.Dispose()
            }
            $namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
            $namespaces.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
            $errorCells = $document.SelectNodes("//x:c[@t='e']", $namespaces)
            if ($errorCells.Count -gt 0) {
                throw "Excel compatibility rejected $($errorCells.Count) cached error cell(s) in $($entry.FullName)."
            }
            foreach ($formula in $document.SelectNodes("//x:c/x:f", $namespaces)) {
                if ($formula.InnerText.StartsWith("HYPERLINK(", [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Excel compatibility rejected an unsupported HYPERLINK formula in $($entry.FullName)."
                }
            }
        }
    } finally {
        $archive.Dispose()
    }
}

# Generates and verifies the styled Excel evidence workbook directly in one completed run root.
function Save-SessionReplayEvidenceWorkbook {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [string] $RunDirectory
    )

    $resolvedRunDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
    $workbookPath = Join-Path `
        $resolvedRunDirectory `
        (Get-SessionReplayWorkbookFileName -Plan $Plan)
    if (
        [System.IO.Path]::GetFullPath((Split-Path -Parent $workbookPath)) -cne
        $resolvedRunDirectory
    ) {
        throw "The evidence workbook path must resolve directly inside the run root."
    }
    & $script:ReplayWorkbookPath `
        -RunDirectory $resolvedRunDirectory `
        -Output $workbookPath `
        | Out-Host
    if (
        -not (Test-Path -LiteralPath $workbookPath -PathType Leaf) -or
        (Get-Item -LiteralPath $workbookPath -Force).Length -le 0
    ) {
        throw "The strict workbook generator did not emit one non-empty .xlsx file."
    }
    Assert-SessionReplayWorkbookExcelCompatibility -Path $workbookPath
    return $workbookPath
}

# Returns the deterministic failure-cache artifact for one output root and evidence fingerprint.
function Get-SessionReplayEvidenceCachePath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $OutputRoot,

        [Parameter(Mandatory = $true)]
        [string] $Fingerprint
    )

    return Join-Path $OutputRoot ("evidence-cache\{0}.md" -f $Fingerprint)
}

# Validates a cached negative-evidence artifact before it is allowed to stop repeated work.
function Assert-SessionReplayCachedFailureEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Fingerprint
    )

    $item = Get-Item -LiteralPath $Path -Force
    if (
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -le 0 -or
        $item.Length -gt 131072
    ) {
        throw "Cached replay evidence has invalid file metadata: $Path"
    }
    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        $text = $encoding.GetString([System.IO.File]::ReadAllBytes($Path))
    } catch {
        throw "Cached replay evidence is not valid UTF-8: $Path"
    }
    $tick = [char] 96
    $fingerprintLine = "- Evidence fingerprint: $tick$Fingerprint$tick"
    $validStage = (
        $text.Contains("- Stage: **Mobile-equivalent calibration**", [System.StringComparison]::Ordinal) -or
        $text.Contains("- Stage: **Replay-tool relaxed calibration (50 percent coverage)**", [System.StringComparison]::Ordinal) -or
        $text.Contains("- Stage: **Garment/calibration frame validation**", [System.StringComparison]::Ordinal)
    )
    if (
        -not $text.Contains("- Status: **FAILED**", [System.StringComparison]::Ordinal) -or
        -not $text.Contains($fingerprintLine, [System.StringComparison]::Ordinal) -or
        -not $validStage
    ) {
        throw "Cached replay evidence does not match the current negative-evidence contract: $Path"
    }
}

# Extracts a bounded process diagnostic from a caught failure without exposing configuration secrets.
function Get-SessionReplayFailureDiagnostic {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.ErrorRecord] $Failure
    )

    $parts = [System.Collections.Generic.List[string]]::new()
    $stderr = ([string] $Failure.Exception.Data["StandardError"]).Trim()
    $stdout = ([string] $Failure.Exception.Data["StandardOutput"]).Trim()
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        $parts.Add($stderr)
    }
    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        $parts.Add($stdout)
    }
    if ($parts.Count -eq 0) {
        $parts.Add($Failure.Exception.Message)
    }
    $diagnostic = $parts -join "`n"
    if ($diagnostic.Length -gt 12000) {
        return $diagnostic.Substring(0, 12000) + "`n[diagnostic truncated]"
    }
    return $diagnostic
}

# Saves a failed-stage Markdown artifact and optionally indexes deterministic local failures.
function Save-SessionReplayFailureEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [string] $RunDirectory,

        [Parameter(Mandatory = $true)]
        [string] $Stage,

        [Parameter(Mandatory = $true)]
        [string] $Diagnostic,

        [Parameter(Mandatory = $true)]
        [string] $Fingerprint,

        [Parameter(Mandatory = $true)]
        [string] $OutputRoot,

        [switch] $CacheDeterministicFailure
    )

    $lines = New-SessionReplayEvidenceMarkdown `
        -Plan $Plan `
        -Status "FAILED" `
        -Stage $Stage `
        -Fingerprint $Fingerprint `
        -Diagnostic $Diagnostic
    $runEvidencePath = Join-Path $RunDirectory "evidence.md"
    Write-SessionReplayMarkdown -Lines $lines -Path $runEvidencePath
    if ($CacheDeterministicFailure) {
        $cachePath = Get-SessionReplayEvidenceCachePath `
            -OutputRoot $OutputRoot `
            -Fingerprint $Fingerprint
        try {
            Write-SessionReplayMarkdown -Lines $lines -Path $cachePath
        } catch {
            Write-Warning "Negative evidence cache could not be written: $($_.Exception.Message)"
        }
    }
    return $runEvidencePath
}

# Prints a concise evidence-first replay plan without exposing credentials.
function Show-SessionReplayPlan {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan
    )

    $matched = @($Plan.Correlations | Where-Object { $_.Status -ceq "Matched" -and $_.Subject -ceq "Result" }).Count
    $ambiguous = @($Plan.Correlations | Where-Object { $_.Status -ceq "Ambiguous" -and $_.Subject -ceq "Result" }).Count
    Write-Host "Session replay plan" -ForegroundColor Cyan
    Write-Host "  UTC day:                 $($Plan.UtcDay)"
    Write-Host "  Source / image set:      $($Plan.Source) / $($Plan.ImageSet)"
    Write-Host "  Client / size:           $($Plan.Client) / $($Plan.Size)"
    Write-Host "  Coverage target:         $([Math]::Round([double] $Plan.CoverageTargetFraction * 100.0, 2))% / $($Plan.CalibrationProfile)"
    Write-Host "  Images selected:         $($Plan.SelectedImageCount) of $($Plan.MatchingImageCount)"
    Write-Host "  Historical correlations: $matched matched, $ambiguous ambiguous"
    Write-Host "  Lens group:              $(if ($null -eq $Plan.LensCalibration) { 'MISSING' } else { "$($Plan.LensCalibration.GroupId) ($($Plan.LensCalibration.ObjectCount) frames)" })"
    Write-Host "  Homography group:        $(if ($null -eq $Plan.HomographyCalibration) { 'MISSING' } else { "$($Plan.HomographyCalibration.GroupId) ($($Plan.HomographyCalibration.ObjectCount) frames)" })"
    $readyColor = if ($Plan.MetricReplayReady) { "Green" } else { "Yellow" }
    Write-Host "  Metric replay ready:     $($Plan.MetricReplayReady)" -ForegroundColor $readyColor
    foreach ($blocker in Get-SessionReplayPlanBlockers -Plan $Plan) {
        Write-Host "  Blocker:                 $blocker" -ForegroundColor Yellow
    }
    Write-Host "  Associations are reconstructed from UTC upload time, not database identity." -ForegroundColor DarkYellow
}

# Resolves a required UTC day from the direct argument or an interactive prompt.
function Resolve-SessionReplayDay {
    $candidate = if (-not [string]::IsNullOrWhiteSpace($Date)) {
        $Date
    } elseif ($Interactive) {
        (Read-Host "UTC day (YYYY-MM-DD)").Trim()
    } else {
        throw "ReplayDate is required for Plan and Run."
    }
    $parsed = [datetime]::MinValue
    if (
        $candidate -cnotmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' -or
        -not [datetime]::TryParseExact(
            $candidate,
            "yyyy-MM-dd",
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref] $parsed
        )
    ) {
        throw "ReplayDate must be an exact valid UTC partition in YYYY-MM-DD form."
    }
    return $candidate
}

# Runs the read-only plan action and saves its inspectable manifest.
function Invoke-SessionReplayPlanAction {
    $configuration = Resolve-SessionReplayConfiguration
    $utcDay = Resolve-SessionReplayDay
    $plan = New-SessionReplayPlan -Configuration $configuration -UtcDay $utcDay
    Show-SessionReplayPlan -Plan $plan
    if ($null -eq $configuration.OutputRoot) {
        throw "QSEE_REPLAY_OUTPUT_ROOT is required to save the plan."
    }
    $planDirectory = Join-Path $configuration.OutputRoot ("plans\{0}" -f $utcDay)
    $planStem = Get-SessionReplayPlanStem -Configuration $configuration
    $planPath = Join-Path $planDirectory "$planStem.json"
    $evidencePath = Join-Path $planDirectory "$planStem.md"
    Write-SessionReplayJson -Value $plan -Path $planPath
    $null = Save-SessionReplayPlanEvidence `
        -Plan $plan `
        -Path $evidencePath `
        -Configuration $configuration
    Write-Host "  Plan artifact:           $planPath" -ForegroundColor DarkCyan
    Write-Host "  Evidence artifact:       $evidencePath" -ForegroundColor DarkCyan
    return $plan
}

# Displays the session replay submenu behind the single public QSee launcher.
function Show-SessionReplayMenu {
    if (-not $Interactive) {
        throw "The replay menu requires an interactive console."
    }
    while ($true) {
        Initialize-QSeeCliUi -ClearScreen
        $settings = Read-SessionReplaySettingsData -Path $script:ConfigurationPath
        $coveragePercent = [double]::Parse(
            $settings["QSEE_REPLAY_COVERAGE_TARGET_FRACTION"],
            [System.Globalization.CultureInfo]::InvariantCulture
        ) * 100.0
        Write-QSeeBootBanner `
            -Title "Historical Session Replay" `
            -Subtitle "Plan, validate, and replay mirrored garment evidence" `
            -Context "$($settings['QSEE_REPLAY_SOURCE']) / $($settings['QSEE_REPLAY_CLIENT']) $($settings['QSEE_REPLAY_SIZE']) / coverage $coveragePercent%"
        Write-QSeeMenuHeader -Title "Choose a replay action" -Hint "Enter a number, or use 0 / Esc to return."
        $menuWidth = Resolve-QSeeMenuConsoleWidth
        Write-QSeeMenuItemCompact -Number 1 -Emoji "🧭" -Label "Plan UTC day" -Desc "Inventory and correlate evidence without remote calls" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 2 -Emoji "▶" -Label "Run replay" -Desc "Calibrate, call remote landmarks, measure, and render" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 3 -Emoji "⚙" -Label "Settings" -Desc "Local/S3 source and explicit calibration coverage" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 4 -Emoji "✓" -Label "Preflight" -Desc "Validate the selected runtime and remote contract" -ConsoleWidth $menuWidth
        Write-QSeeMenuItemCompact -Number 5 -Emoji "🐍" -Label "Setup Python" -Desc "Prepare the isolated replay environment" -ConsoleWidth $menuWidth
        Write-QSeeBackItem -Label "Back to the main QSee launcher"
        $choice = Read-QSeeIndexChoice -Min 1 -Max 5
        if ($null -eq $choice) {
            return
        }
        switch ([int] $choice) {
            1 { $null = Invoke-SessionReplayPlanAction; Wait-QSeeForEnter }
            2 { Invoke-SessionReplayRunAction; Wait-QSeeForEnter }
            3 { Edit-SessionReplaySettings }
            4 { $configuration = Resolve-SessionReplayConfiguration; $null = Test-SessionReplayPreflight -Configuration $configuration -ThrowOnFailure; Wait-QSeeForEnter }
            5 { Initialize-SessionReplayPython; Wait-QSeeForEnter }
        }
    }
}

# Executes the full remote replay after calibration and confirmation.
# Runs one child process without interpolating its arguments into a second shell command.
function Invoke-SessionReplayCheckedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $Arguments,

        [Parameter(Mandatory = $true)]
        [string] $Stage
    )

    $temporaryRoot = [System.IO.Path]::GetTempPath()
    $token = [guid]::NewGuid().ToString("N")
    $stdoutPath = Join-Path $temporaryRoot "qsee-replay-$token.stdout.log"
    $stderrPath = Join-Path $temporaryRoot "qsee-replay-$token.stderr.log"
    try {
        & $FilePath @Arguments 1> $stdoutPath 2> $stderrPath
        $exitCode = $LASTEXITCODE
        $stdout = if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) {
            [System.IO.File]::ReadAllText($stdoutPath)
        } else {
            ""
        }
        $stderr = if (Test-Path -LiteralPath $stderrPath -PathType Leaf) {
            [System.IO.File]::ReadAllText($stderrPath)
        } else {
            ""
        }
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            Write-Host $stdout.TrimEnd()
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Host $stderr.TrimEnd() -ForegroundColor DarkYellow
        }
        if ($exitCode -ne 0) {
            $failure = [System.InvalidOperationException]::new(
                "$Stage failed with exit code $exitCode."
            )
            $failure.Data["Stage"] = $Stage
            $failure.Data["ExitCode"] = $exitCode
            $failure.Data["StandardError"] = $stderr.Trim()
            $failure.Data["StandardOutput"] = $stdout.Trim()
            throw $failure
        }
    } finally {
        foreach ($temporaryPath in @($stdoutPath, $stderrPath)) {
            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }
}

# Produces a filesystem-safe, collision-resistant stem from one normalized object key.
function Get-SessionReplaySafeStem {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Key
    )

    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($Key)
    $safeLeaf = [regex]::Replace($leaf, "[^A-Za-z0-9_.-]", "_").Trim(".")
    if ([string]::IsNullOrWhiteSpace($safeLeaf)) {
        $safeLeaf = "object"
    }
    if ($safeLeaf.Length -gt 72) {
        $safeLeaf = $safeLeaf.Substring(0, 72)
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Key)
    $digest = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $suffix = [System.BitConverter]::ToString($digest).Replace("-", "").Substring(0, 12).ToLowerInvariant()
    return "$safeLeaf-$suffix"
}

# Resolves a local mirror record or downloads one explicitly selected S3 object into the run.
function Resolve-SessionReplayRecordFile {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [object] $Record,

        [Parameter(Mandatory = $true)]
        [string] $DestinationDirectory,

        [switch] $PreserveLeafName
    )

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    $leafName = [System.IO.Path]::GetFileName([string] $Record.Key)
    if ($PreserveLeafName) {
        if (
            [string]::IsNullOrWhiteSpace($leafName) -or
            $leafName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0
        ) {
            throw "A selected image key has no safe source filename: $($Record.Key)"
        }
        $destinationName = $leafName
    } else {
        $extension = [System.IO.Path]::GetExtension([string] $Record.Key)
        $destinationName = (Get-SessionReplaySafeStem -Key $Record.Key) + $extension
    }
    $destination = Join-Path $DestinationDirectory $destinationName
    if ($Configuration.Source -ceq "Local") {
        if ([string]::IsNullOrWhiteSpace([string] $Record.LocalPath)) {
            throw "A local inventory record has no local path: $($Record.Key)"
        }
        if ([string] $Record.ContentIdentity -cnotmatch '^sha256:[0-9a-f]{64}$') {
            throw "A selected local evidence record has no SHA-256 identity: $($Record.Key)"
        }
        $sourcePath = [System.IO.Path]::GetFullPath([string] $Record.LocalPath)
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "A selected local source file is missing: $sourcePath"
        }
        $sourceFile = Get-Item -LiteralPath $sourcePath -Force
        if (
            ($sourceFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $sourceFile.Length -le 0 -or
            $sourceFile.Length -ne [long] $Record.Size
        ) {
            throw "A selected local source file no longer matches inventory metadata: $sourcePath"
        }
        if (Test-Path -LiteralPath $destination) {
            throw "The staged local evidence destination already exists: $destination"
        }
        $expectedHash = ([string] $Record.ContentIdentity).Substring(7)
        $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sourceHash -cne $expectedHash) {
            throw "A selected local source file changed after planning: $sourcePath"
        }
        $temporaryPath = "$destination.$([guid]::NewGuid().ToString('N')).tmp"
        try {
            [System.IO.File]::Copy($sourcePath, $temporaryPath, $false)
            $copy = Get-Item -LiteralPath $temporaryPath
            $copyHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($copy.Length -ne [long] $Record.Size -or $copyHash -cne $expectedHash) {
                throw "The staged local evidence copy does not match its planned identity."
            }
            $copy.LastWriteTimeUtc = [datetime] $Record.LastModifiedUtc
            [System.IO.File]::Move($temporaryPath, $destination, $false)
        } finally {
            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
        return $destination
    }

    $downloaded = Save-SessionReplayS3Object `
        -Configuration $Configuration `
        -Record $Record `
        -Destination $destination
    return [string] $downloaded.LocalPath
}

# Freezes the selected strict calibration gates and exact evidence into one manifest block.
function New-SessionReplayCalibrationBlock {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $LensImages,

        [Parameter(Mandatory = $true)]
        [string[]] $HomographyImages,

        [Parameter(Mandatory = $true)]
        [double] $CoverageTargetFraction
    )

    $null = Get-SessionReplayCalibrationProfile `
        -CoverageTargetFraction $CoverageTargetFraction

    return [pscustomobject] [ordered] @{
        lensImages = @($LensImages)
        homographyImages = @($HomographyImages)
        patternColumns = 10
        patternRows = 14
        squareMm = 50.0
        maxIntrinsicRmsPx = 2.0
        minIntrinsicViews = 10
        maxHomographyErrorMm = 2.0
        minHomographyPlacements = 2
        maxHomographyPlacements = 12
        lensSharpnessThreshold = 100.0
        homographySharpnessThreshold = 200.0
        coverageGridSize = [pscustomobject] [ordered] @{
            columns = 20
            rows = 20
        }
        coverageNewCells = 17
        coverageTargetFraction = $CoverageTargetFraction
        homographyStabilityToleranceFraction = 0.01
    }
}

# Returns the unique matched historical result for one selected image, if one exists.
function Get-SessionReplayHistoricalCorrelation {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [string] $CaptureKey
    )

    $matches = @($Plan.Correlations | Where-Object {
            $_.Status -ceq "Matched" -and
            $_.Subject -ceq "Result" -and
            $_.CaptureKey -ceq $CaptureKey
        })
    if ($matches.Count -gt 1) {
        throw "Correlation produced more than one historical result for $CaptureKey."
    }
    if ($matches.Count -eq 0) {
        return $null
    }
    return $matches[0]
}

# Resolves UTC evidence time while requiring the exact timestamped result-key contract.
function Get-SessionReplayRecordEventUtc {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Record,

        [ValidateSet("Image", "Result", "Calibration")]
        [string] $Role
    )

    if ($Role -ceq "Result") {
        $fileName = [System.IO.Path]::GetFileName([string] $Record.Key)
        $match = [regex]::Match(
            $fileName,
            "^(?<timestamp>[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9]{2}-[0-9]{2}-[0-9]{2})-[0-9A-Fa-f]{16}[.]json$",
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
        )
        if (-not $match.Success) {
            throw "A result key does not match the exact timestamped JSON contract: $($Record.Key)"
        }
        $parsed = [datetime]::MinValue
        $style = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor `
            [System.Globalization.DateTimeStyles]::AdjustToUniversal
        if (-not [datetime]::TryParseExact(
                $match.Groups["timestamp"].Value,
                "yyyy-MM-dd-HH-mm-ss",
                [System.Globalization.CultureInfo]::InvariantCulture,
                $style,
                [ref] $parsed
            )) {
            throw "A matched result key contains an invalid UTC event timestamp."
        }
        return $parsed
    }

    $timestamp = [datetime] $Record.LastModifiedUtc
    if ($timestamp.Kind -eq [System.DateTimeKind]::Utc) {
        return $timestamp
    }
    return $timestamp.ToUniversalTime()
}

# Builds per-image signed time deltas against the inferred historical and calibration evidence.
function New-SessionReplayProvenance {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [object] $ImageRecord,

        [AllowNull()]
        [object] $Correlation,

        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyString()]
        [string] $HistoricalSha256
    )

    $imageUtc = Get-SessionReplayRecordEventUtc -Record $ImageRecord -Role "Image"
    $lensReference = @($Plan.LensCalibration.Objects | Sort-Object LastModifiedUtc -Descending | Select-Object -First 1)[0]
    $homographyReference = @($Plan.HomographyCalibration.Objects | Sort-Object LastModifiedUtc -Descending | Select-Object -First 1)[0]
    $lensUtc = Get-SessionReplayRecordEventUtc -Record $lensReference -Role "Calibration"
    $homographyUtc = Get-SessionReplayRecordEventUtc -Record $homographyReference -Role "Calibration"
    $historical = $null
    if ($null -ne $Correlation) {
        if ($HistoricalSha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "Matched historical evidence requires an exact lowercase SHA-256."
        }
        $resultUtc = Get-SessionReplayRecordEventUtc -Record $Correlation.Result -Role "Result"
        $historical = [pscustomobject] [ordered] @{
            key = $Correlation.ResultKey
            timestampUtc = $resultUtc.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
            deltaSeconds = [Math]::Round(($imageUtc - $resultUtc).TotalSeconds, 3)
            sha256 = $HistoricalSha256
        }
    } elseif (-not [string]::IsNullOrWhiteSpace($HistoricalSha256)) {
        throw "Historical SHA-256 was supplied without a matched result."
    }

    return [pscustomobject] [ordered] @{
        schema = "qsee.session-replay.provenance.v1"
        image = [pscustomobject] [ordered] @{
            key = $ImageRecord.Key
            timestampUtc = $imageUtc.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
        }
        historicalResult = $historical
        lensCalibration = [pscustomobject] [ordered] @{
            groupId = $Plan.LensCalibration.GroupId
            referenceTimestampUtc = $lensUtc.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
            deltaSeconds = [Math]::Round(($imageUtc - $lensUtc).TotalSeconds, 3)
        }
        homographyCalibration = [pscustomobject] [ordered] @{
            groupId = $Plan.HomographyCalibration.GroupId
            referenceTimestampUtc = $homographyUtc.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
            deltaSeconds = [Math]::Round(($imageUtc - $homographyUtc).TotalSeconds, 3)
        }
        association = "inferred-from-UTC-time-not-database-identity"
    }
}

# Creates one mutable run-state record without embedding historical result payloads or secrets.
function New-SessionReplayRunItem {
    param(
        [Parameter(Mandatory = $true)]
        [object] $ImageRecord,

        [Parameter(Mandatory = $true)]
        [string] $ImagePath,

        [AllowNull()]
        [object] $Correlation
    )

    $itemId = Get-SessionReplaySafeStem -Key $ImageRecord.Key
    return [pscustomobject] [ordered] @{
        Id = $itemId
        ImageKey = $ImageRecord.Key
        ImagePath = $ImagePath
        HistoricalResultKey = if ($null -eq $Correlation) { $null } else { $Correlation.ResultKey }
        HistoricalResultPath = $null
        HistoricalResultSha256 = $null
        CorrelationDeltaSeconds = if ($null -eq $Correlation) { $null } else { $Correlation.DeltaSeconds }
        CorrelationMethod = if ($null -eq $Correlation) { $null } else { "inferred-one-to-one-UTC-time" }
        ProvenancePath = $null
        RawResponsePath = $null
        LandmarksPath = $null
        LandmarkOverlayPath = $null
        PipePath = $null
        AnalysisPath = $null
        OverlayPath = $null
        Status = "SELECTED"
        OverallVerdict = $null
        Error = $null
    }
}

# Calls the audited remote-only landmark client for one already validated image frame.
function Invoke-SessionReplayRemoteLandmarks {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [object] $Item,

        [Parameter(Mandatory = $true)]
        [string] $RemoteOutputRoot
    )

    $remoteDirectory = Join-Path $RemoteOutputRoot $Item.Id
    New-Item -ItemType Directory -Path $remoteDirectory | Out-Null
    $replayScript = Join-Path $Configuration.CoreRoot $script:ReplayScriptRelativePath
    Invoke-SessionReplayCheckedProcess `
        -FilePath $Configuration.PythonExe `
        -Arguments @(
            $replayScript,
            $Item.ImagePath,
            "--endpoint", $Configuration.CvEndpoint,
            "--rotation", [string] $Configuration.Rotation,
            "--output-dir", $remoteDirectory
        ) `
        -Stage "Remote landmark estimation for $($Item.ImageKey)"

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($Item.ImagePath)
    $rawResponse = Join-Path $remoteDirectory "$stem.cv-response.json"
    $landmarks = Join-Path $remoteDirectory "$stem.landmarks.json"
    $annotated = Join-Path $remoteDirectory "$stem.landmarks.jpg"
    foreach ($requiredPath in @($rawResponse, $landmarks, $annotated)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -or (Get-Item -LiteralPath $requiredPath).Length -le 0) {
            throw "Remote landmark estimation did not emit a complete artifact set."
        }
    }
    $Item.RawResponsePath = $rawResponse
    $Item.LandmarksPath = $landmarks
    $Item.LandmarkOverlayPath = $annotated
    $Item.Status = "LANDMARKS_READY"
}

# Saves a compact CSV index next to the full versioned JSON run summary.
function Write-SessionReplayCsv {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]] $Items,

        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $rows = @($Items | Select-Object `
            Id,
            ImageKey,
            HistoricalResultKey,
            CorrelationDeltaSeconds,
            Status,
            OverallVerdict,
            OverlayPath,
            Error)
    if ($rows.Count -eq 0) {
        $emptyRow = [pscustomobject] [ordered] @{
            Id = $null
            ImageKey = $null
            HistoricalResultKey = $null
            CorrelationDeltaSeconds = $null
            Status = $null
            OverallVerdict = $null
            OverlayPath = $null
            Error = $null
        }
        $lines = @($emptyRow | ConvertTo-Csv -NoTypeInformation | Select-Object -First 1)
    } else {
        $lines = @($rows | ConvertTo-Csv -NoTypeInformation)
    }
    [System.IO.File]::WriteAllLines($Path, $lines, [System.Text.UTF8Encoding]::new($false))
}

# Writes inspectable run state after each material stage without including any credential value.
function Save-SessionReplayRunSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Plan,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]] $Items,

        [Parameter(Mandatory = $true)]
        [string] $RunDirectory,

        [Parameter(Mandatory = $true)]
        [string] $Status,

        [AllowNull()]
        [string] $CalibrationPath
    )

    $summary = [pscustomobject] [ordered] @{
        Schema = "qsee.session-replay.run.v1"
        Status = $Status
        UtcDay = $Plan.UtcDay
        Source = $Plan.Source
        ImageSet = $Plan.ImageSet
        Client = $Plan.Client
        Size = $Plan.Size
        Rotation = $Plan.Rotation
        CoverageTargetFraction = $Plan.CoverageTargetFraction
        CalibrationProfile = $Plan.CalibrationProfile
        RemoteModel = $script:RequiredRemoteModel
        RemoteEndpoint = $script:RequiredRemoteEndpoint
        CalibrationPath = $CalibrationPath
        CalibrationAssociation = $Plan.CalibrationAssociation
        HistoricalResultAssociation = $Plan.ResultAssociation
        AssociationWarning = "All photo/result and photo/calibration associations are inferred; no database session identity exists."
        Items = @($Items)
        UpdatedUtc = [datetime]::UtcNow.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
    }
    $summaryPath = Join-Path $RunDirectory "run-summary.json"
    Write-SessionReplayJson -Value $summary -Path $summaryPath
    Write-SessionReplayCsv -Items $Items -Path (Join-Path $RunDirectory "run-summary.csv")
}

# Executes the full remote-only replay after calibration, frame validation, and confirmation.
function Invoke-SessionReplayRunAction {
    $configuration = Resolve-SessionReplayConfiguration
    $utcDay = Resolve-SessionReplayDay
    $null = Test-SessionReplayPreflight -Configuration $configuration -ThrowOnFailure
    $plan = New-SessionReplayPlan -Configuration $configuration -UtcDay $utcDay
    Show-SessionReplayPlan -Plan $plan
    $planDirectory = Join-Path $configuration.OutputRoot ("plans\{0}" -f $utcDay)
    $planStem = Get-SessionReplayPlanStem -Configuration $configuration
    $planPath = Join-Path $planDirectory "$planStem.json"
    $planEvidencePath = Join-Path $planDirectory "$planStem.md"
    Write-SessionReplayJson -Value $plan -Path $planPath
    $fingerprint = Save-SessionReplayPlanEvidence `
        -Plan $plan `
        -Path $planEvidencePath `
        -Configuration $configuration
    if (-not $plan.MetricReplayReady) {
        throw "Metric replay is blocked before remote submission. Evidence: $planEvidencePath"
    }

    # Do not repeat a deterministic calibration or frame failure for identical evidence and code.
    $cachedFailurePath = Get-SessionReplayEvidenceCachePath `
        -OutputRoot $configuration.OutputRoot `
        -Fingerprint $fingerprint
    if (Test-Path -LiteralPath $cachedFailurePath -PathType Leaf) {
        Assert-SessionReplayCachedFailureEvidence `
            -Path $cachedFailurePath `
            -Fingerprint $fingerprint
        throw "Replay stopped on cached deterministic evidence; no calibration or remote request was repeated. Evidence: $cachedFailurePath"
    }
    if (-not $Interactive) {
        throw "Run requires an interactive console confirmation before remote image submission."
    }
    $confirmation = (Read-Host "Type RUN to use $($plan.CalibrationProfile) and submit $($plan.SelectedImageCount) image(s) to the remote model").Trim()
    if ($confirmation -cne "RUN") {
        throw "Remote replay was cancelled before any image submission."
    }

    $coveragePercent = [int] [Math]::Round(
        [double] $configuration.CoverageTargetFraction * 100.0,
        0
    )
    $runName = "{0}-{1}-{2}-coverage{3}-{4}-{5}" -f (
        $utcDay,
        $configuration.Client.ToLowerInvariant(),
        $configuration.Size.ToLowerInvariant(),
        $coveragePercent,
        [datetime]::UtcNow.ToString("yyyyMMddTHHmmssZ", [System.Globalization.CultureInfo]::InvariantCulture),
        [guid]::NewGuid().ToString("N").Substring(0, 8)
    )
    $runsRoot = Join-Path $configuration.OutputRoot "runs"
    New-Item -ItemType Directory -Path $runsRoot -Force | Out-Null
    $runDirectory = Join-Path $runsRoot $runName
    if (Test-Path -LiteralPath $runDirectory) {
        throw "The unique run directory unexpectedly already exists: $runDirectory"
    }
    New-Item -ItemType Directory -Path $runDirectory | Out-Null
    Write-SessionReplayJson -Value $plan -Path (Join-Path $runDirectory "plan.json")
    $null = Save-SessionReplayPlanEvidence `
        -Plan $plan `
        -Path (Join-Path $runDirectory "plan-evidence.md") `
        -Configuration $configuration
    Save-SessionReplayPlainLanguageReadme `
        -Plan $plan `
        -Path (Join-Path $runDirectory "README.md")

    $items = [System.Collections.Generic.List[object]]::new()
    $calibrationPath = $null
    $calibrationSha256 = $null
    $currentItem = $null
    $currentStage = "Calibration evidence materialization"
    $failureStatus = "FAILED_INPUT"
    $cacheDeterministicFailure = $false

    try {
        Write-Host "Materializing calibration evidence..." -ForegroundColor Cyan
        $lensPaths = @($plan.LensCalibration.Objects | ForEach-Object {
            Resolve-SessionReplayRecordFile `
                -Configuration $configuration `
                -Record $_ `
                -DestinationDirectory (Join-Path $runDirectory "source\calibration\lens")
        })
        $homographyPaths = @($plan.HomographyCalibration.Objects | ForEach-Object {
            Resolve-SessionReplayRecordFile `
                -Configuration $configuration `
                -Record $_ `
                -DestinationDirectory (Join-Path $runDirectory "source\calibration\homography")
        })
        $calibrationBlock = New-SessionReplayCalibrationBlock `
            -LensImages $lensPaths `
            -HomographyImages $homographyPaths `
            -CoverageTargetFraction $configuration.CoverageTargetFraction
        $calibrationManifest = [pscustomobject] [ordered] @{
            schema = "qsee.session-replay.extract.v1"
            rotation = $configuration.Rotation
            calibration = $calibrationBlock
            items = @()
        }
        $calibrationManifestPath = Join-Path $runDirectory "calibration-manifest.json"
        $calibrationPath = Join-Path $runDirectory "calibration.json"
        Write-SessionReplayJson -Value $calibrationManifest -Path $calibrationManifestPath

        $currentStage = Get-SessionReplayCalibrationStage `
            -CoverageTargetFraction $configuration.CoverageTargetFraction
        $failureStatus = "FAILED_CALIBRATION"
        try {
            Invoke-SessionReplayCheckedProcess `
                -FilePath $configuration.PythonExe `
                -Arguments @(
                    $script:ReplayEnginePath,
                    "calibrate",
                    "--manifest", $calibrationManifestPath,
                    "--output", $calibrationPath
                ) `
                -Stage $currentStage
        } catch {
            # Only the engine's reserved evidence-rejection code is deterministic.
            $cacheDeterministicFailure = [int] $_.Exception.Data["ExitCode"] -eq 23
            throw
        }
        $cacheDeterministicFailure = $false
        $calibrationSha256 = (
            Get-FileHash -LiteralPath $calibrationPath -Algorithm SHA256
        ).Hash.ToLowerInvariant()

        $currentStage = "Selected image and result materialization"
        $failureStatus = "FAILED_INPUT"
        Write-Host "Materializing selected images and inferred historical results..." -ForegroundColor Cyan
        foreach ($imageRecord in $plan.Images) {
            $imagePath = Resolve-SessionReplayRecordFile `
                -Configuration $configuration `
                -Record $imageRecord `
                -DestinationDirectory (Join-Path $runDirectory "source\images") `
                -PreserveLeafName
            $correlation = Get-SessionReplayHistoricalCorrelation -Plan $plan -CaptureKey $imageRecord.Key
            $item = New-SessionReplayRunItem `
                -ImageRecord $imageRecord `
                -ImagePath $imagePath `
                -Correlation $correlation
            if ($null -ne $correlation) {
                $item.HistoricalResultPath = Resolve-SessionReplayRecordFile `
                    -Configuration $configuration `
                    -Record $correlation.Result `
                    -DestinationDirectory (Join-Path $runDirectory "source\historical-results") `
                    -PreserveLeafName
                $item.HistoricalResultSha256 = (
                    Get-FileHash -LiteralPath $item.HistoricalResultPath -Algorithm SHA256
                ).Hash.ToLowerInvariant()
            }
            $item.ProvenancePath = Join-Path $runDirectory ("provenance\{0}.json" -f $item.Id)
            $provenance = New-SessionReplayProvenance `
                -Plan $plan `
                -ImageRecord $imageRecord `
                -Correlation $correlation `
                -HistoricalSha256 $item.HistoricalResultSha256
            Write-SessionReplayJson -Value $provenance -Path $item.ProvenancePath
            $items.Add($item)
        }
        Save-SessionReplayRunSummary `
            -Plan $plan `
            -Items $items.ToArray() `
            -RunDirectory $runDirectory `
            -Status "CALIBRATED" `
            -CalibrationPath $calibrationPath

        $historicalPaths = @($items | Where-Object {
                -not [string]::IsNullOrWhiteSpace([string] $_.HistoricalResultPath)
            } | ForEach-Object HistoricalResultPath)
        if ($historicalPaths.Count -gt 0) {
            $historicalArguments = [System.Collections.Generic.List[string]]::new()
            foreach ($argument in @(
                    $script:ReplayEnginePath,
                    "validate-historical",
                    "--client", $configuration.Client,
                    "--size", $configuration.Size
                )) {
                $historicalArguments.Add([string] $argument)
            }
            foreach ($historicalPath in $historicalPaths) {
                $historicalArguments.Add("--historical-json")
                $historicalArguments.Add([string] $historicalPath)
            }
            $currentStage = "Historical result validation"
            $failureStatus = "FAILED_INPUT"
            Invoke-SessionReplayCheckedProcess `
                -FilePath $configuration.PythonExe `
                -Arguments $historicalArguments.ToArray() `
                -Stage $currentStage
        }

        $frameArguments = [System.Collections.Generic.List[string]]::new()
        foreach ($argument in @(
                $script:ReplayEnginePath,
                "validate-frames",
                "--calibration", $calibrationPath,
                "--rotation", [string] $configuration.Rotation
            )) {
            $frameArguments.Add([string] $argument)
        }
        foreach ($item in $items) {
            $frameArguments.Add("--image")
            $frameArguments.Add([string] $item.ImagePath)
        }
        $currentStage = "Garment/calibration frame validation"
        $failureStatus = "FAILED_FRAME_VALIDATION"
        try {
            Invoke-SessionReplayCheckedProcess `
                -FilePath $configuration.PythonExe `
                -Arguments $frameArguments.ToArray() `
                -Stage $currentStage
        } catch {
            # Only the engine's reserved evidence-rejection code is deterministic.
            $cacheDeterministicFailure = [int] $_.Exception.Data["ExitCode"] -eq 23
            throw
        }
        $cacheDeterministicFailure = $false

        Write-Host "Running strict remote-to-geometry processing per image..." -ForegroundColor Cyan
        foreach ($item in $items) {
            # Expose the protected key only to this image's remote child process.
            $currentItem = $item
            $currentStage = "Remote API key acquisition for $($item.ImageKey)"
            $failureStatus = "FAILED_REMOTE_AUTH"
            $apiKey = Get-SessionReplayProtectedEnvValue `
                -Path $configuration.EnvLocal `
                -Name "QSEE_REPLAY_CV_API_KEY"
            if ([string]::IsNullOrWhiteSpace($apiKey)) {
                throw "The protected remote API key is missing."
            }
            $previousApiKey = [System.Environment]::GetEnvironmentVariable("QSEE_API_KEY", "Process")
            try {
                [System.Environment]::SetEnvironmentVariable("QSEE_API_KEY", $apiKey, "Process")
                $currentStage = "Remote landmark estimation for $($item.ImageKey)"
                $failureStatus = "FAILED_REMOTE"
                Invoke-SessionReplayRemoteLandmarks `
                    -Configuration $configuration `
                    -Item $item `
                    -RemoteOutputRoot (Join-Path $runDirectory "remote")
            } finally {
                [System.Environment]::SetEnvironmentVariable("QSEE_API_KEY", $previousApiKey, "Process")
                $apiKey = $null
            }

            # Extract immediately so a bad response prevents every later remote request.
            $currentStage = "Mobile geometry extraction for $($item.ImageKey)"
            $failureStatus = "FAILED_EXTRACTION"
            if ($item.Status -cne "LANDMARKS_READY") {
                throw "Item $($item.Id) is not ready for geometry extraction."
            }
            $pipePath = Join-Path $runDirectory ("pipes\{0}.pipe" -f $item.Id)
            $extractManifest = [pscustomobject] [ordered] @{
                schema = "qsee.session-replay.extract.v1"
                rotation = $configuration.Rotation
                calibration = $calibrationBlock
                items = @(
                    [pscustomobject] [ordered] @{
                        id = $item.Id
                        image = $item.ImagePath
                        landmarks = $item.LandmarksPath
                        pipe = $pipePath
                    }
                )
            }
            $extractManifestPath = Join-Path $runDirectory ("extract-manifests\{0}.json" -f $item.Id)
            Write-SessionReplayJson -Value $extractManifest -Path $extractManifestPath
            Invoke-SessionReplayCheckedProcess `
                -FilePath $configuration.PythonExe `
                -Arguments @(
                    $script:ReplayEnginePath,
                    "extract",
                    "--manifest", $extractManifestPath,
                    "--calibration", $calibrationPath
                ) `
                -Stage $currentStage
            $item.PipePath = $pipePath
            $item.Status = "PIPE_READY"

            # Complete authoritative measurement and rendering before the next remote request.
            $currentStage = "Current mobile measurement calculation for $($item.ImageKey)"
            $failureStatus = "FAILED_MEASUREMENT"
            $item.AnalysisPath = Join-Path $runDirectory ("analysis\{0}.json" -f $item.Id)
            $analysisItem = [ordered] @{
                pipe = $item.PipePath
                output = $item.AnalysisPath
            }
            if (-not [string]::IsNullOrWhiteSpace([string] $item.HistoricalResultKey)) {
                $analysisItem["historicalResult"] = [pscustomobject] [ordered] @{
                    key = $item.HistoricalResultKey
                    deltaSeconds = $item.CorrelationDeltaSeconds
                    association = $item.CorrelationMethod
                }
            }
            $analysisManifest = [pscustomobject] [ordered] @{
                schema = "qsee.session-replay.manifest.v1"
                client = $configuration.Client
                size = $configuration.Size
                calibrationSha256 = $calibrationSha256
                items = @([pscustomobject] $analysisItem)
            }
            $analysisManifestPath = Join-Path `
                $runDirectory `
                ("analysis-manifests\{0}.json" -f $item.Id)
            Write-SessionReplayJson -Value $analysisManifest -Path $analysisManifestPath
            Invoke-SessionReplayCheckedProcess `
                -FilePath (Join-Path $script:RepositoryRoot "gradlew.bat") `
                -Arguments @(
                    ":app:testDebugUnitTest",
                    "--tests", "com.example.qsee.SessionReplayAnalysis",
                    "-Dqsee.replayManifest=$analysisManifestPath"
                ) `
                -Stage $currentStage

            $currentStage = "Overlay rendering for $($item.ImageKey)"
            $failureStatus = "FAILED_RENDER"
            $analysis = Get-Content -LiteralPath $item.AnalysisPath -Raw |
                ConvertFrom-Json -Depth 100
            $item.OverallVerdict = [string] $analysis.overallVerdict
            $item.OverlayPath = Join-Path $runDirectory ("overlays\{0}.jpg" -f $item.Id)
            $renderArguments = [System.Collections.Generic.List[string]]::new()
            foreach ($argument in @(
                    $script:ReplayEnginePath,
                    "render",
                    "--image", $item.ImagePath,
                    "--landmarks", $item.LandmarksPath,
                    "--analysis", $item.AnalysisPath,
                    "--pipe", $item.PipePath,
                    "--calibration", $calibrationPath,
                    "--expected-client", $configuration.Client,
                    "--expected-size", $configuration.Size,
                    "--provenance", $item.ProvenancePath,
                    "--output", $item.OverlayPath,
                    "--rotation", [string] $configuration.Rotation
                )) {
                $renderArguments.Add([string] $argument)
            }
            if (-not [string]::IsNullOrWhiteSpace([string] $item.HistoricalResultPath)) {
                $renderArguments.Add("--historical-json")
                $renderArguments.Add([string] $item.HistoricalResultPath)
            }
            Invoke-SessionReplayCheckedProcess `
                -FilePath $configuration.PythonExe `
                -Arguments $renderArguments.ToArray() `
                -Stage $currentStage
            if (
                -not (Test-Path -LiteralPath $item.OverlayPath -PathType Leaf) -or
                (Get-Item -LiteralPath $item.OverlayPath -Force).Length -le 0
            ) {
                throw "The strict renderer did not emit one non-empty overlay."
            }
            $item.Status = "COMPLETE"
            $currentStage = "Run summary update after $($item.ImageKey)"
            $failureStatus = "FAILED_SUMMARY"
            Save-SessionReplayRunSummary `
                -Plan $plan `
                -Items $items.ToArray() `
                -RunDirectory $runDirectory `
                -Status "PROCESSING" `
                -CalibrationPath $calibrationPath
        }
        $currentItem = $null
        $currentStage = "Final run summary"
        $failureStatus = "FAILED_SUMMARY"
        Save-SessionReplayRunSummary `
            -Plan $plan `
            -Items $items.ToArray() `
            -RunDirectory $runDirectory `
            -Status "COMPLETE" `
            -CalibrationPath $calibrationPath

        $currentStage = "Run completion invariant"
        $failureStatus = "FAILED_INVARIANT"
        $completeCount = @($items | Where-Object Status -ceq "COMPLETE").Count
        if ($completeCount -ne $items.Count) {
            throw "Run completion invariant failed: every selected image must have one completed overlay."
        }
        $currentStage = "Evidence workbook generation"
        $failureStatus = "FAILED_WORKBOOK"
        $workbookPath = Save-SessionReplayEvidenceWorkbook `
            -Plan $plan `
            -RunDirectory $runDirectory
        Write-Host "Session replay completed: $completeCount overlay(s), zero item failures." -ForegroundColor Green
        Write-Host "Evidence workbook: $workbookPath" -ForegroundColor DarkCyan
        Write-Host "Run directory: $runDirectory" -ForegroundColor DarkCyan
    } catch {
        $failure = $_
        $diagnostic = Get-SessionReplayFailureDiagnostic -Failure $failure
        if ($null -ne $currentItem) {
            $currentItem.Status = $failureStatus
            $currentItem.Error = $diagnostic
        }
        try {
            Save-SessionReplayRunSummary `
                -Plan $plan `
                -Items $items.ToArray() `
                -RunDirectory $runDirectory `
                -Status "FAILED" `
                -CalibrationPath $calibrationPath
        } catch {
            $diagnostic += "`nFailed to update run-summary.json: $($_.Exception.Message)"
        }
        $failureEvidencePath = $null
        $evidenceDiagnostic = $null
        try {
            $failureEvidencePath = Save-SessionReplayFailureEvidence `
                -Plan $plan `
                -RunDirectory $runDirectory `
                -Stage $currentStage `
                -Diagnostic $diagnostic `
                -Fingerprint $fingerprint `
                -OutputRoot $configuration.OutputRoot `
                -CacheDeterministicFailure:$cacheDeterministicFailure
        } catch {
            $evidenceDiagnostic = $_.Exception.Message
            Write-Warning "Failure evidence could not be written: $evidenceDiagnostic"
        }
        $failureMessage = if ([string]::IsNullOrWhiteSpace($failureEvidencePath)) {
            "$currentStage failed; failure evidence could not be written: $evidenceDiagnostic"
        } else {
            "$currentStage failed. Evidence: $failureEvidencePath"
        }
        $wrappedFailure = [System.InvalidOperationException]::new(
            $failureMessage,
            $failure.Exception
        )
        throw $wrappedFailure
    }
}

$resolvedAction = if (-not [string]::IsNullOrWhiteSpace($Action)) {
    $Action
} elseif ($Interactive) {
    "Menu"
} else {
    "Plan"
}

Initialize-SessionReplaySettingsFile

switch ($resolvedAction) {
    "Menu" { Show-SessionReplayMenu }
    "Settings" { Edit-SessionReplaySettings }
    "Preflight" {
        $configuration = Resolve-SessionReplayConfiguration
        $null = Test-SessionReplayPreflight -Configuration $configuration -ThrowOnFailure
    }
    "Setup" { Initialize-SessionReplayPython }
    "Plan" { $null = Invoke-SessionReplayPlanAction }
    "Run" { Invoke-SessionReplayRunAction }
    default { throw "Unsupported replay action: $resolvedAction" }
}
