Set-StrictMode -Version Latest

$script:AllowedConfigurationKeys = @(
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

$script:RequiredConfigurationKeys = @(
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

$script:AwsEnvironmentVariablesToRemove = @(
    "AWS_ACCESS_KEY_ID",
    "AWS_SECRET_ACCESS_KEY",
    "AWS_SESSION_TOKEN",
    "AWS_SECURITY_TOKEN",
    "AWS_PROFILE",
    "AWS_DEFAULT_PROFILE",
    "AWS_REGION",
    "AWS_DEFAULT_REGION",
    "AWS_CONFIG_FILE",
    "AWS_SHARED_CREDENTIALS_FILE",
    "AWS_WEB_IDENTITY_TOKEN_FILE",
    "AWS_ROLE_ARN",
    "AWS_ROLE_SESSION_NAME",
    "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI",
    "AWS_CONTAINER_CREDENTIALS_FULL_URI",
    "AWS_CONTAINER_AUTHORIZATION_TOKEN",
    "AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE",
    "AWS_ENDPOINT_URL",
    "AWS_ENDPOINT_URL_S3"
)

# Resolves the repository root from this module's fixed location.
function Get-SessionReplayRepositoryRoot {
    [CmdletBinding()]
    param()

    return [System.IO.Path]::GetFullPath(
        (Join-Path -Path $PSScriptRoot -ChildPath "..\..\..")
    )
}

# Validates that a configured path is absolute and optionally exists with the required type.
function Assert-SessionReplayPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Key,

        [Parameter(Mandatory = $true)]
        [string] $Value,

        [ValidateSet("File", "Directory", "Any")]
        [string] $PathType = "Any",

        [switch] $AllowMissing
    )

    if (-not [System.IO.Path]::IsPathFullyQualified($Value)) {
        throw "Configuration key $Key must contain an absolute path."
    }

    if ($AllowMissing) {
        return
    }

    $expectedPathType = switch ($PathType) {
        "File" { "Leaf" }
        "Directory" { "Container" }
        default { "Any" }
    }

    if ($expectedPathType -eq "Any") {
        if (-not (Test-Path -LiteralPath $Value)) {
            throw "The path configured by $Key does not exist."
        }
        return
    }

    if (-not (Test-Path -LiteralPath $Value -PathType $expectedPathType)) {
        throw "The path configured by $Key is not an existing $($PathType.ToLowerInvariant())."
    }
}

# Parses an integer setting using invariant, exact decimal syntax and an explicit range.
function ConvertTo-SessionReplayInteger {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Key,

        [Parameter(Mandatory = $true)]
        [string] $Value,

        [Parameter(Mandatory = $true)]
        [int] $Minimum,

        [Parameter(Mandatory = $true)]
        [int] $Maximum
    )

    $parsed = 0
    $style = [System.Globalization.NumberStyles]::None
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    if (-not [int]::TryParse($Value, $style, $culture, [ref] $parsed)) {
        throw "Configuration key $Key must be an integer."
    }
    if ($parsed -lt $Minimum -or $parsed -gt $Maximum) {
        throw "Configuration key $Key must be between $Minimum and $Maximum."
    }

    return $parsed
}

# Parses a decimal setting using invariant syntax and an explicit range.
function ConvertTo-SessionReplayDouble {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Key,

        [Parameter(Mandatory = $true)]
        [string] $Value,

        [Parameter(Mandatory = $true)]
        [double] $Minimum,

        [Parameter(Mandatory = $true)]
        [double] $Maximum
    )

    $parsed = 0.0
    $style = [System.Globalization.NumberStyles]::AllowDecimalPoint
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    if (-not [double]::TryParse($Value, $style, $culture, [ref] $parsed)) {
        throw "Configuration key $Key must be a decimal number."
    }
    if (-not [double]::IsFinite($parsed) -or $parsed -lt $Minimum -or $parsed -gt $Maximum) {
        throw "Configuration key $Key must be between $Minimum and $Maximum."
    }

    return $parsed
}

# Validates and canonicalizes the configured HTTPS landmark endpoint.
function ConvertTo-SessionReplayEndpoint {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string] $Value
    )

    if ($null -eq $Value) {
        return $null
    }

    $uri = $null
    if (-not [System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref] $uri)) {
        throw "QSEE_REPLAY_CV_ENDPOINT must be an absolute HTTPS URL."
    }
    if ($uri.Scheme -cne "https" -or -not [string]::IsNullOrEmpty($uri.UserInfo)) {
        throw "QSEE_REPLAY_CV_ENDPOINT must be HTTPS and must not contain credentials."
    }
    if (-not [string]::IsNullOrEmpty($uri.Query) -or -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw "QSEE_REPLAY_CV_ENDPOINT must not contain a query string or fragment."
    }
    if (-not $uri.AbsolutePath.EndsWith(
            "/api/v1/landmark-estimation/estimate",
            [System.StringComparison]::Ordinal
        )) {
        throw "QSEE_REPLAY_CV_ENDPOINT must target the landmark-estimation estimate route."
    }

    return $uri.AbsoluteUri.TrimEnd("/")
}

# Reads the exact repository-local JSON settings file as inert string data.
function Read-SessionReplayConfiguration {
    [CmdletBinding()]
    param(
        [Parameter()]
        [string] $RepositoryRoot = (Get-SessionReplayRepositoryRoot)
    )

    $repositoryPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
    if (-not (Test-Path -LiteralPath $repositoryPath -PathType Container)) {
        throw "The session replay repository root does not exist."
    }

    $configurationPath = Join-Path `
        -Path $repositoryPath `
        -ChildPath "scripts\session-replay.settings.local.json"
    if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
        throw "Missing scripts/session-replay.settings.local.json."
    }

    $configurationItem = Get-Item -LiteralPath $configurationPath -Force
    if (($configurationItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "scripts/session-replay.settings.local.json must not be a symbolic link or reparse point."
    }
    if ($configurationItem.Length -gt 65536) {
        throw "scripts/session-replay.settings.local.json exceeds the 64 KiB safety limit."
    }

    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        $jsonText = $encoding.GetString([System.IO.File]::ReadAllBytes($configurationPath))
    } catch {
        throw "scripts/session-replay.settings.local.json must contain valid UTF-8 text."
    }

    $values = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($jsonText)
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "Session replay settings JSON root must be an object."
        }
        foreach ($property in $document.RootElement.EnumerateObject()) {
            $key = $property.Name
            if ($script:AllowedConfigurationKeys -cnotcontains $key) {
                throw "Unknown session replay configuration key '$key'."
            }
            if ($values.ContainsKey($key)) {
                throw "Duplicate session replay configuration key '$key'."
            }
            if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                throw "Configuration key $key must contain a JSON string."
            }
            $value = $property.Value.GetString().Trim()
            if ($value.Length -eq 0 -or $value.IndexOf([char] 0) -ge 0) {
                throw "Configuration key $key must have a non-empty text value."
            }
            $values.Add($key, $value)
        }
    } catch [System.Text.Json.JsonException] {
        throw "scripts/session-replay.settings.local.json contains invalid JSON."
    } finally {
        if ($null -ne $document) {
            $document.Dispose()
        }
    }

    foreach ($requiredKey in $script:RequiredConfigurationKeys) {
        if (-not $values.ContainsKey($requiredKey)) {
            throw "Missing required session replay configuration key $requiredKey."
        }
    }

    Assert-SessionReplayPath -Key "QSEE_REPLAY_AWS_EXE" -Value $values["QSEE_REPLAY_AWS_EXE"] -PathType File
    Assert-SessionReplayPath -Key "QSEE_REPLAY_MIRROR_ROOT" -Value $values["QSEE_REPLAY_MIRROR_ROOT"] -PathType Directory
    Assert-SessionReplayPath -Key "QSEE_REPLAY_TABLE_PATH" -Value $values["QSEE_REPLAY_TABLE_PATH"] -PathType File

    if ($values["QSEE_REPLAY_AWS_PROFILE"] -cnotmatch "^[A-Za-z0-9][A-Za-z0-9_.@+-]{0,127}$") {
        throw "QSEE_REPLAY_AWS_PROFILE contains unsupported characters."
    }
    if ($values["QSEE_REPLAY_AWS_REGION"] -cnotmatch "^[a-z]{2}(?:-gov)?-[a-z]+-[0-9]+$") {
        throw "QSEE_REPLAY_AWS_REGION is not a valid explicit AWS region name."
    }

    $bucket = $values["QSEE_REPLAY_S3_BUCKET"]
    $bucketLooksLikeIpAddress = $false
    $discardedAddress = $null
    $bucketLooksLikeIpAddress = [System.Net.IPAddress]::TryParse($bucket, [ref] $discardedAddress)
    if (
        $bucket.Length -lt 3 -or
        $bucket.Length -gt 63 -or
        $bucket -cnotmatch "^[a-z0-9][a-z0-9.-]*[a-z0-9]$" -or
        $bucket.Contains("..", [System.StringComparison]::Ordinal) -or
        $bucket.Contains(".-", [System.StringComparison]::Ordinal) -or
        $bucket.Contains("-.", [System.StringComparison]::Ordinal) -or
        $bucketLooksLikeIpAddress
    ) {
        throw "QSEE_REPLAY_S3_BUCKET is not a valid DNS-style S3 bucket name."
    }

    $source = $values["QSEE_REPLAY_SOURCE"]
    if (@("Local", "S3") -cnotcontains $source) {
        throw "QSEE_REPLAY_SOURCE must be exactly Local or S3."
    }

    $imageSet = $values["QSEE_REPLAY_IMAGE_SET"]
    if (@("Captured", "PreviewDetected", "PreviewNotDetected") -cnotcontains $imageSet) {
        throw "QSEE_REPLAY_IMAGE_SET must be Captured, PreviewDetected, or PreviewNotDetected."
    }

    foreach ($tokenKey in @("QSEE_REPLAY_CLIENT", "QSEE_REPLAY_SIZE", "QSEE_REPLAY_MODEL")) {
        if ($values[$tokenKey] -cnotmatch "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$") {
            throw "Configuration key $tokenKey contains unsupported characters."
        }
    }

    $rotation = ConvertTo-SessionReplayInteger `
        -Key "QSEE_REPLAY_ROTATION" `
        -Value $values["QSEE_REPLAY_ROTATION"] `
        -Minimum 0 `
        -Maximum 270
    if (@(0, 90, 180, 270) -notcontains $rotation) {
        throw "QSEE_REPLAY_ROTATION must be 0, 90, 180, or 270."
    }

    $maxImages = ConvertTo-SessionReplayInteger `
        -Key "QSEE_REPLAY_MAX_IMAGES" `
        -Value $values["QSEE_REPLAY_MAX_IMAGES"] `
        -Minimum 1 `
        -Maximum 10000
    $coverageTargetFraction = ConvertTo-SessionReplayDouble `
        -Key "QSEE_REPLAY_COVERAGE_TARGET_FRACTION" `
        -Value $values["QSEE_REPLAY_COVERAGE_TARGET_FRACTION"] `
        -Minimum 0.5 `
        -Maximum 0.7
    if (@(0.5, 0.7) -notcontains $coverageTargetFraction) {
        throw "QSEE_REPLAY_COVERAGE_TARGET_FRACTION must be exactly 0.50 or 0.70."
    }
    $correlationSeconds = ConvertTo-SessionReplayDouble `
        -Key "QSEE_REPLAY_CORRELATION_SECONDS" `
        -Value $values["QSEE_REPLAY_CORRELATION_SECONDS"] `
        -Minimum 0.0 `
        -Maximum 60.0

    Assert-SessionReplayPath -Key "QSEE_REPLAY_CORE_ROOT" -Value $values["QSEE_REPLAY_CORE_ROOT"] -PathType Directory
    Assert-SessionReplayPath -Key "QSEE_REPLAY_OUTPUT_ROOT" -Value $values["QSEE_REPLAY_OUTPUT_ROOT"] -AllowMissing
    Assert-SessionReplayPath -Key "QSEE_REPLAY_PYTHON_EXE" -Value $values["QSEE_REPLAY_PYTHON_EXE"] -AllowMissing
    Assert-SessionReplayPath -Key "QSEE_REPLAY_ENV_LOCAL" -Value $values["QSEE_REPLAY_ENV_LOCAL"] -PathType File

    $configuration = [pscustomobject] [ordered] @{
        ConfigPath = [System.IO.Path]::GetFullPath($configurationPath)
        AwsExe = [System.IO.Path]::GetFullPath($values["QSEE_REPLAY_AWS_EXE"])
        AwsProfile = $values["QSEE_REPLAY_AWS_PROFILE"]
        AwsRegion = $values["QSEE_REPLAY_AWS_REGION"]
        S3Bucket = $bucket
        MirrorRoot = [System.IO.Path]::GetFullPath($values["QSEE_REPLAY_MIRROR_ROOT"])
        Source = $source
        ImageSet = $imageSet
        Client = $values["QSEE_REPLAY_CLIENT"]
        Size = $values["QSEE_REPLAY_SIZE"]
        Rotation = $rotation
        MaxImages = $maxImages
        CoverageTargetFraction = $coverageTargetFraction
        CorrelationSeconds = $correlationSeconds
        Model = $values["QSEE_REPLAY_MODEL"]
        TablePath = [System.IO.Path]::GetFullPath($values["QSEE_REPLAY_TABLE_PATH"])
        CvEndpoint = ConvertTo-SessionReplayEndpoint -Value $values["QSEE_REPLAY_CV_ENDPOINT"]
        CoreRoot = [System.IO.Path]::GetFullPath($values["QSEE_REPLAY_CORE_ROOT"])
        OutputRoot = [System.IO.Path]::GetFullPath($values["QSEE_REPLAY_OUTPUT_ROOT"])
        PythonExe = [System.IO.Path]::GetFullPath($values["QSEE_REPLAY_PYTHON_EXE"])
        EnvLocal = [System.IO.Path]::GetFullPath($values["QSEE_REPLAY_ENV_LOCAL"])
    }
    $configuration.PSObject.TypeNames.Insert(0, "QSee.SessionReplay.Configuration")
    return $configuration
}

# Requires the exact YYYY-MM-DD UTC day syntax and returns its canonical text.
function ConvertTo-SessionReplayUtcDay {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $UtcDay
    )

    $parsed = [datetime]::MinValue
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $style = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor `
        [System.Globalization.DateTimeStyles]::AdjustToUniversal
    if (-not [datetime]::TryParseExact($UtcDay, "yyyy-MM-dd", $culture, $style, [ref] $parsed)) {
        throw "UtcDay must use the exact YYYY-MM-DD form."
    }

    return $parsed.ToString("yyyy-MM-dd", $culture)
}

# Returns the fixed storage prefix associated with an allowed image set.
function Get-SessionReplayImagePrefix {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Captured", "PreviewDetected", "PreviewNotDetected")]
        [string] $ImageSet,

        [Parameter(Mandatory = $true)]
        [string] $UtcDay
    )

    $root = switch ($ImageSet) {
        "Captured" { "data_collection/captured" }
        "PreviewDetected" { "data_collection/preview/object_detected" }
        "PreviewNotDetected" { "data_collection/preview/object_not_detected" }
    }

    return "$root/$UtcDay/"
}

# Creates the content-identifiable normalized local/S3 object record.
function New-SessionReplayObjectRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Key,

        [AllowNull()]
        [string] $LocalPath,

        [Parameter(Mandatory = $true)]
        [datetime] $LastModifiedUtc,

        [Parameter(Mandatory = $true)]
        [long] $Size,

        [AllowNull()]
        [object] $ContentIdentity = $null
    )

    if ($Key.Length -eq 0 -or $Key.StartsWith("/", [System.StringComparison]::Ordinal)) {
        throw "A normalized object key must be non-empty and relative."
    }
    if ($Size -lt 0) {
        throw "A normalized object size must not be negative."
    }
    if (
        -not [string]::IsNullOrEmpty($ContentIdentity) -and
        $ContentIdentity -cnotmatch '^(?:sha256:[0-9a-f]{64}|s3-etag:"[0-9a-fA-F]{32}(?:-[0-9]+)?")$'
    ) {
        throw "A normalized object content identity is invalid."
    }

    if ([string]::IsNullOrEmpty($ContentIdentity)) {
        $ContentIdentity = $null
    } else {
        $ContentIdentity = [string] $ContentIdentity
    }

    $utc = if ($LastModifiedUtc.Kind -eq [System.DateTimeKind]::Utc) {
        $LastModifiedUtc
    } else {
        $LastModifiedUtc.ToUniversalTime()
    }

    $record = [pscustomobject] [ordered] @{
        Key = $Key.Replace("\", "/")
        LocalPath = $LocalPath
        LastModifiedUtc = $utc
        Size = [long] $Size
        ContentIdentity = $ContentIdentity
    }
    $record.PSObject.TypeNames.Insert(0, "QSee.SessionReplay.ObjectRecord")
    return $record
}

# Lists normalized local objects below one logical mirror prefix.
function Get-SessionReplayLocalObjects {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $MirrorRoot,

        [Parameter(Mandatory = $true)]
        [string] $Prefix,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Include
    )

    $mirrorPath = [System.IO.Path]::GetFullPath($MirrorRoot)
    $prefixPath = $Prefix.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    $directory = [System.IO.Path]::GetFullPath((Join-Path -Path $mirrorPath -ChildPath $prefixPath))
    $mirrorBoundary = $mirrorPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $directory.StartsWith($mirrorBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "A local inventory prefix resolved outside the configured mirror root."
    }
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        return @()
    }

    $records = [System.Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $directory -File -Recurse) {
        $localPath = [System.IO.Path]::GetFullPath($file.FullName)
        if (-not $localPath.StartsWith($mirrorBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A local inventory object resolved outside the configured mirror root."
        }

        $relative = [System.IO.Path]::GetRelativePath($mirrorPath, $localPath).Replace("\", "/")
        if (-not (& $Include $relative)) {
            continue
        }

        $records.Add((New-SessionReplayObjectRecord `
                    -Key $relative `
                    -LocalPath $localPath `
                    -LastModifiedUtc $file.LastWriteTimeUtc `
                    -Size $file.Length))
    }

    return @($records | Sort-Object -Property LastModifiedUtc, Key)
}

# Runs one AWS CLI read operation in a credential-scrubbed child environment.
function Invoke-SessionReplayAwsCli {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [Parameter(Mandatory = $true)]
        [string] $Operation,

        [ValidateRange(1, 300)]
        [int] $TimeoutSeconds = 120
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Configuration.AwsExe
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $null = $startInfo.ArgumentList.Add($argument)
    }

    foreach ($name in $script:AwsEnvironmentVariablesToRemove) {
        $null = $startInfo.Environment.Remove($name)
    }
    $startInfo.Environment["AWS_EC2_METADATA_DISABLED"] = "true"
    $startInfo.Environment["AWS_PAGER"] = ""

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "The AWS CLI process did not start."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
                $process.WaitForExit()
            } catch {
                # The process may already have exited between the timeout and kill attempt.
            }
            $timeoutFailure = [System.TimeoutException]::new(
                "AWS CLI $Operation timed out."
            )
            if ($process.HasExited) {
                $timeoutFailure.Data["StandardError"] = $stderrTask.GetAwaiter().GetResult().Trim()
                $timeoutFailure.Data["StandardOutput"] = $stdoutTask.GetAwaiter().GetResult().Trim()
            }
            throw $timeoutFailure
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            $failure = [System.InvalidOperationException]::new(
                "AWS CLI $Operation failed with exit code $($process.ExitCode)."
            )
            $failure.Data["ExitCode"] = $process.ExitCode
            $failure.Data["StandardError"] = $stderr.Trim()
            $failure.Data["StandardOutput"] = $stdout.Trim()
            throw $failure
        }

        return $stdout
    } finally {
        $process.Dispose()
    }
}

# Verifies the configured named AWS profile without returning caller metadata.
function Assert-SessionReplayAwsIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    $arguments = @(
        "sts",
        "get-caller-identity",
        "--profile", $Configuration.AwsProfile,
        "--region", $Configuration.AwsRegion,
        "--output", "json",
        "--no-cli-pager"
    )
    $null = Invoke-SessionReplayAwsCli `
        -Configuration $Configuration `
        -Arguments $arguments `
        -Operation "identity preflight" `
        -TimeoutSeconds 30
}

# Performs fail-closed named-profile identity and read access checks for the configured bucket.
function Test-SessionReplayS3Access {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    Assert-SessionReplayConfigurationObject -Configuration $Configuration
    Assert-SessionReplayAwsIdentity -Configuration $Configuration
    $arguments = @(
        "s3api",
        "list-objects-v2",
        "--bucket", $Configuration.S3Bucket,
        "--prefix", "data_collection/",
        "--max-keys", "1",
        "--no-paginate",
        "--profile", $Configuration.AwsProfile,
        "--region", $Configuration.AwsRegion,
        "--output", "json",
        "--no-cli-pager"
    )
    $null = Invoke-SessionReplayAwsCli `
        -Configuration $Configuration `
        -Arguments $arguments `
        -Operation "S3 read-access preflight" `
        -TimeoutSeconds 30
    return $true
}

# Lists one S3 prefix through explicit list-objects-v2 pagination.
function Get-SessionReplayS3Objects {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [string] $Prefix,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Include
    )

    $records = [System.Collections.Generic.List[object]]::new()
    $continuationToken = $null
    $pageCount = 0
    do {
        $pageCount++
        if ($pageCount -gt 10000) {
            throw "S3 listing exceeded the pagination safety limit."
        }

        $arguments = [System.Collections.Generic.List[string]]::new()
        foreach ($argument in @(
                "s3api",
                "list-objects-v2",
                "--bucket", $Configuration.S3Bucket,
                "--prefix", $Prefix,
                "--max-keys", "1000",
                "--no-paginate",
                "--profile", $Configuration.AwsProfile,
                "--region", $Configuration.AwsRegion,
                "--output", "json",
                "--no-cli-pager"
            )) {
            $arguments.Add([string] $argument)
        }
        if ($null -ne $continuationToken) {
            $arguments.Add("--continuation-token")
            $arguments.Add($continuationToken)
        }

        $json = Invoke-SessionReplayAwsCli `
            -Configuration $Configuration `
            -Arguments $arguments.ToArray() `
            -Operation "S3 prefix listing"
        try {
            # Preserve the explicit AWS offset; automatic DateTime conversion applies the workstation timezone.
            $response = $json | ConvertFrom-Json -Depth 32 -DateKind String
        } catch {
            throw "AWS CLI returned malformed JSON for an S3 prefix listing."
        }

        $contentsProperty = $response.PSObject.Properties["Contents"]
        if ($null -ne $contentsProperty -and $null -ne $contentsProperty.Value) {
            foreach ($entry in @($contentsProperty.Value)) {
                $key = [string] $entry.Key
                if (-not $key.StartsWith($Prefix, [System.StringComparison]::Ordinal)) {
                    throw "S3 returned an object outside the requested prefix."
                }
                if (-not (& $Include $key)) {
                    continue
                }

                $lastModified = [datetime]::MinValue
                $culture = [System.Globalization.CultureInfo]::InvariantCulture
                $style = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor `
                    [System.Globalization.DateTimeStyles]::AdjustToUniversal
                if (-not [datetime]::TryParse([string] $entry.LastModified, $culture, $style, [ref] $lastModified)) {
                    throw "S3 returned an invalid LastModified value."
                }

                $etag = [string] $entry.ETag
                if ($etag -cnotmatch '^"[0-9a-fA-F]{32}(?:-[0-9]+)?"$') {
                    throw "S3 returned an invalid ETag value."
                }
                $records.Add((New-SessionReplayObjectRecord `
                            -Key $key `
                            -LocalPath $null `
                            -LastModifiedUtc $lastModified `
                            -Size ([long] $entry.Size) `
                            -ContentIdentity "s3-etag:$etag"))
            }
        }

        $isTruncatedProperty = $response.PSObject.Properties["IsTruncated"]
        $isTruncated = $null -ne $isTruncatedProperty -and [bool] $isTruncatedProperty.Value
        if ($isTruncated) {
            $nextTokenProperty = $response.PSObject.Properties["NextContinuationToken"]
            if ($null -eq $nextTokenProperty -or [string]::IsNullOrWhiteSpace([string] $nextTokenProperty.Value)) {
                throw "S3 truncated a listing without returning a continuation token."
            }
            $continuationToken = [string] $nextTokenProperty.Value
        } else {
            $continuationToken = $null
        }
    } while ($null -ne $continuationToken)

    return @($records | Sort-Object -Property LastModifiedUtc, Key)
}

# Downloads one normalized S3 object through get-object without overwriting local data.
function Save-SessionReplayS3Object {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [object] $Record,

        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    Assert-SessionReplayConfigurationObject -Configuration $Configuration
    if ($Configuration.Source -cne "S3") {
        throw "Save-SessionReplayS3Object requires an explicit S3 configuration source."
    }
    Assert-SessionReplayObjectRecord -Record $Record
    if ($null -ne $Record.LocalPath) {
        throw "Save-SessionReplayS3Object requires a normalized S3 record with no LocalPath."
    }
    if ([long] $Record.Size -le 0) {
        throw "The requested S3 object must have a positive expected size."
    }
    if ([string] $Record.ContentIdentity -cnotmatch '^s3-etag:"[0-9a-fA-F]{32}(?:-[0-9]+)?"$') {
        throw "The requested S3 object has no valid listed ETag identity."
    }
    if (-not [System.IO.Path]::IsPathFullyQualified($Destination)) {
        throw "The S3 download destination must be an absolute path."
    }

    $destinationPath = [System.IO.Path]::GetFullPath($Destination)
    if (Test-Path -LiteralPath $destinationPath) {
        throw "The S3 download destination already exists; overwrite is refused."
    }
    $destinationDirectory = [System.IO.Path]::GetDirectoryName($destinationPath)
    if (
        [string]::IsNullOrWhiteSpace($destinationDirectory) -or
        -not (Test-Path -LiteralPath $destinationDirectory -PathType Container)
    ) {
        throw "The S3 download destination directory does not exist."
    }

    $temporaryPath = Join-Path `
        -Path $destinationDirectory `
        -ChildPath (".{0}.{1}.download.tmp" -f [System.IO.Path]::GetFileName($destinationPath), [guid]::NewGuid())
    try {
        Assert-SessionReplayAwsIdentity -Configuration $Configuration
        $arguments = @(
            "s3api",
            "get-object",
            "--bucket", $Configuration.S3Bucket,
            "--key", $Record.Key,
            "--if-match", ([string] $Record.ContentIdentity).Substring(8),
            $temporaryPath,
            "--profile", $Configuration.AwsProfile,
            "--region", $Configuration.AwsRegion,
            "--output", "json",
            "--no-cli-pager"
        )
        $null = Invoke-SessionReplayAwsCli `
            -Configuration $Configuration `
            -Arguments $arguments `
            -Operation "S3 object download"

        if (-not (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
            throw "AWS CLI did not create the requested S3 object file."
        }
        $downloadedFile = Get-Item -LiteralPath $temporaryPath
        if ($downloadedFile.Length -le 0 -or $downloadedFile.Length -ne [long] $Record.Size) {
            throw "The downloaded S3 object size does not match the listed metadata."
        }

        $downloadedFile.LastWriteTimeUtc = Get-SessionReplayRecordUtcTime -Record $Record
        [System.IO.File]::Move($temporaryPath, $destinationPath, $false)
        return New-SessionReplayObjectRecord `
            -Key $Record.Key `
            -LocalPath $destinationPath `
            -LastModifiedUtc $Record.LastModifiedUtc `
            -Size $Record.Size `
            -ContentIdentity $Record.ContentIdentity
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

# Groups normalized calibration input objects by their round or session identifier.
function Group-SessionReplayCalibrationObjects {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]] $Objects,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Lens", "Homography")]
        [string] $Kind,

        [Parameter(Mandatory = $true)]
        [string] $UtcDay
    )

    $kindRoot = if ($Kind -ceq "Lens") {
        "data_collection/debug/lens_calib"
    } else {
        "data_collection/debug/homography_calib"
    }
    $groupPrefix = if ($Kind -ceq "Lens") { "round" } else { "session" }
    $dayPattern = [regex]::Escape($UtcDay)
    $rootPattern = [regex]::Escape($kindRoot)
    $pattern = "^$rootPattern/(?<group>$groupPrefix[A-Za-z0-9-]+)/$dayPattern/[^/]+[.]jpg$"

    $groups = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($object in $Objects) {
        $match = [regex]::Match($object.Key, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $match.Success) {
            continue
        }

        $groupId = $match.Groups["group"].Value
        if (-not $groups.ContainsKey($groupId)) {
            $groups.Add($groupId, [System.Collections.Generic.List[object]]::new())
        }
        $groups[$groupId].Add($object)
    }

    $output = [System.Collections.Generic.List[object]]::new()
    foreach ($groupId in @($groups.Keys | Sort-Object)) {
        $groupObjects = @($groups[$groupId] | Sort-Object -Property LastModifiedUtc, Key)
        $totalSize = [long] 0
        foreach ($object in $groupObjects) {
            $totalSize += [long] $object.Size
        }

        $output.Add([pscustomobject] [ordered] @{
                Kind = $Kind
                GroupId = $groupId
                Prefix = "$kindRoot/$groupId/$UtcDay/"
                ObjectCount = $groupObjects.Count
                TotalSize = $totalSize
                Objects = $groupObjects
            })
    }

    return @($output)
}

# Lists local calibration groups without scanning unrelated files outside the requested day.
function Get-SessionReplayLocalCalibrationGroups {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [string] $UtcDay
    )

    $allGroups = [System.Collections.Generic.List[object]]::new()
    foreach ($descriptor in @(
            [pscustomobject] @{ Kind = "Lens"; Root = "data_collection/debug/lens_calib"; Prefix = "round" },
            [pscustomobject] @{ Kind = "Homography"; Root = "data_collection/debug/homography_calib"; Prefix = "session" }
        )) {
        $nativeRoot = $descriptor.Root.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        $rootPath = Join-Path -Path $Configuration.MirrorRoot -ChildPath $nativeRoot
        if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
            continue
        }

        foreach ($groupDirectory in Get-ChildItem -LiteralPath $rootPath -Directory) {
            if (-not $groupDirectory.Name.StartsWith($descriptor.Prefix, [System.StringComparison]::Ordinal)) {
                continue
            }
            $prefix = "$($descriptor.Root)/$($groupDirectory.Name)/$UtcDay/"
            $objects = @(Get-SessionReplayLocalObjects `
                    -MirrorRoot $Configuration.MirrorRoot `
                    -Prefix $prefix `
                    -Include { param($key) $key.EndsWith(".jpg", [System.StringComparison]::OrdinalIgnoreCase) })
            foreach ($group in Group-SessionReplayCalibrationObjects `
                    -Objects $objects `
                    -Kind $descriptor.Kind `
                    -UtcDay $UtcDay) {
                $allGroups.Add($group)
            }
        }
    }

    return @($allGroups | Sort-Object -Property Kind, GroupId)
}

# Lists S3 calibration groups using only list-objects-v2 after the STS preflight.
function Get-SessionReplayS3CalibrationGroups {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [string] $UtcDay
    )

    $allGroups = [System.Collections.Generic.List[object]]::new()
    foreach ($descriptor in @(
            [pscustomobject] @{ Kind = "Lens"; Root = "data_collection/debug/lens_calib/" },
            [pscustomobject] @{ Kind = "Homography"; Root = "data_collection/debug/homography_calib/" }
        )) {
        $objects = @(Get-SessionReplayS3Objects `
                -Configuration $Configuration `
                -Prefix $descriptor.Root `
                -Include { param($key) $key.EndsWith(".jpg", [System.StringComparison]::OrdinalIgnoreCase) })
        foreach ($group in Group-SessionReplayCalibrationObjects `
                -Objects $objects `
                -Kind $descriptor.Kind `
                -UtcDay $UtcDay) {
            $allGroups.Add($group)
        }
    }

    return @($allGroups | Sort-Object -Property Kind, GroupId)
}

# Verifies that a configuration object originated from the strict reader.
function Assert-SessionReplayConfigurationObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration
    )

    if ($Configuration.PSObject.TypeNames -cnotcontains "QSee.SessionReplay.Configuration") {
        throw "Configuration must come from Read-SessionReplayConfiguration."
    }
    Assert-SessionReplayPath -Key "QSEE_REPLAY_AWS_EXE" -Value $Configuration.AwsExe -PathType File
    Assert-SessionReplayPath -Key "QSEE_REPLAY_MIRROR_ROOT" -Value $Configuration.MirrorRoot -PathType Directory
    if (@("Local", "S3") -cnotcontains [string] $Configuration.Source) {
        throw "The parsed session replay source is invalid."
    }
}

# Builds the normalized image, result, and calibration inventory for one exact UTC day.
function Get-SessionReplayDayInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Configuration,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Local", "S3")]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $UtcDay
    )

    Assert-SessionReplayConfigurationObject -Configuration $Configuration
    if ($Source -cne $Configuration.Source) {
        throw "The explicit inventory source does not match QSEE_REPLAY_SOURCE."
    }
    $day = ConvertTo-SessionReplayUtcDay -UtcDay $UtcDay
    $imagePrefix = Get-SessionReplayImagePrefix `
        -ImageSet $Configuration.ImageSet `
        -UtcDay $day
    $resultPrefix = "data_collection/results/measures/$day/"

    if ($Source -ceq "S3") {
        Assert-SessionReplayAwsIdentity -Configuration $Configuration
        $allImages = @(Get-SessionReplayS3Objects `
                -Configuration $Configuration `
                -Prefix $imagePrefix `
                -Include { param($key) $key.EndsWith(".jpg", [System.StringComparison]::OrdinalIgnoreCase) })
        $results = @(Get-SessionReplayS3Objects `
                -Configuration $Configuration `
                -Prefix $resultPrefix `
                -Include { param($key) $key.EndsWith(".json", [System.StringComparison]::OrdinalIgnoreCase) })
        $calibrationGroups = @(Get-SessionReplayS3CalibrationGroups `
                -Configuration $Configuration `
                -UtcDay $day)
    } else {
        $allImages = @(Get-SessionReplayLocalObjects `
                -MirrorRoot $Configuration.MirrorRoot `
                -Prefix $imagePrefix `
                -Include { param($key) $key.EndsWith(".jpg", [System.StringComparison]::OrdinalIgnoreCase) })
        $results = @(Get-SessionReplayLocalObjects `
                -MirrorRoot $Configuration.MirrorRoot `
                -Prefix $resultPrefix `
                -Include { param($key) $key.EndsWith(".json", [System.StringComparison]::OrdinalIgnoreCase) })
        $calibrationGroups = @(Get-SessionReplayLocalCalibrationGroups `
                -Configuration $Configuration `
                -UtcDay $day)
    }

    $selectedImages = @($allImages | Select-Object -First $Configuration.MaxImages)
    $inventory = [pscustomobject] [ordered] @{
        Source = $Source
        UtcDay = $day
        ImageSet = $Configuration.ImageSet
        MatchingImageCount = $allImages.Count
        Images = $selectedImages
        Results = $results
        CalibrationGroups = $calibrationGroups
    }
    $inventory.PSObject.TypeNames.Insert(0, "QSee.SessionReplay.DayInventory")
    return $inventory
}

# Validates the exact content-identifiable object record consumed by correlation.
function Assert-SessionReplayObjectRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Record
    )

    $expectedNames = @("Key", "LocalPath", "LastModifiedUtc", "Size", "ContentIdentity")
    $actualNames = @($Record.PSObject.Properties.Name)
    if ($actualNames.Count -ne $expectedNames.Count) {
        throw "Correlation requires normalized content-identifiable object records."
    }
    for ($index = 0; $index -lt $expectedNames.Count; $index++) {
        if ($actualNames[$index] -cne $expectedNames[$index]) {
            throw "Correlation requires normalized content-identifiable object records."
        }
    }
    if ([string]::IsNullOrWhiteSpace([string] $Record.Key)) {
        throw "A correlation object record has an empty key."
    }
    if ($null -eq $Record.LastModifiedUtc) {
        throw "A correlation object record has no UTC modification timestamp."
    }
    if (
        $null -ne $Record.ContentIdentity -and
        [string] $Record.ContentIdentity -cnotmatch '^(?:sha256:[0-9a-f]{64}|s3-etag:"[0-9a-fA-F]{32}(?:-[0-9]+)?")$'
    ) {
        throw "A correlation object record has an invalid content identity."
    }
}

# Converts a normalized record timestamp to UTC without changing the record.
function Get-SessionReplayRecordUtcTime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Record
    )

    $time = [datetime] $Record.LastModifiedUtc
    if ($time.Kind -eq [System.DateTimeKind]::Utc) {
        return $time
    }
    return $time.ToUniversalTime()
}

# Gets correlation event time and requires the exact timestamped result-key contract.
function Get-SessionReplayCorrelationUtcTime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Record,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Capture", "Result")]
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
            throw "A result object key does not match the exact timestamped JSON contract: $($Record.Key)"
        }
        $parsed = [datetime]::MinValue
        $culture = [System.Globalization.CultureInfo]::InvariantCulture
        $style = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor `
            [System.Globalization.DateTimeStyles]::AdjustToUniversal
        if (-not [datetime]::TryParseExact(
                $match.Groups["timestamp"].Value,
                "yyyy-MM-dd-HH-mm-ss",
                $culture,
                $style,
                [ref] $parsed
            )) {
            throw "A result object key contains an invalid UTC event timestamp."
        }
        return $parsed
    }

    return Get-SessionReplayRecordUtcTime -Record $Record
}

# Creates one consistent correlation output record without exposing configuration values.
function New-SessionReplayCorrelationRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Matched", "Unmatched", "Ambiguous")]
        [string] $Status,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Result", "Capture")]
        [string] $Subject,

        [AllowNull()]
        [object] $Result,

        [AllowNull()]
        [object] $Capture,

        [AllowNull()]
        [Nullable[double]] $DeltaSeconds,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $CandidateCaptureKeys,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $CandidateResultKeys,

        [AllowNull()]
        [string] $Reason
    )

    return [pscustomobject] [ordered] @{
        Status = $Status
        Subject = $Subject
        ResultKey = if ($null -eq $Result) { $null } else { $Result.Key }
        CaptureKey = if ($null -eq $Capture) { $null } else { $Capture.Key }
        Result = $Result
        Capture = $Capture
        DeltaSeconds = $DeltaSeconds
        CandidateCaptureKeys = @($CandidateCaptureKeys | Sort-Object -Unique)
        CandidateResultKeys = @($CandidateResultKeys | Sort-Object -Unique)
        Reason = $Reason
    }
}

# Correlates records through deterministic minimum-delta assignment inside each UTC minute.
function Resolve-SessionReplayCorrelation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]] $Captures,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]] $Results,

        [Parameter(Mandatory = $true)]
        [ValidateRange(0.0, 60.0)]
        [double] $MaxDeltaSeconds
    )

    $captureByKey = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($capture in $Captures) {
        Assert-SessionReplayObjectRecord -Record $capture
        if ($captureByKey.ContainsKey($capture.Key)) {
            throw "Capture inventory contains a duplicate object key."
        }
        $captureByKey.Add($capture.Key, $capture)
    }

    $resultByKey = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($result in $Results) {
        Assert-SessionReplayObjectRecord -Record $result
        if ($resultByKey.ContainsKey($result.Key)) {
            throw "Result inventory contains a duplicate object key."
        }
        $resultByKey.Add($result.Key, $result)
    }

    $edgesByResult = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new(
        [System.StringComparer]::Ordinal
    )
    $edgesByCapture = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[object]]]::new(
        [System.StringComparer]::Ordinal
    )
    $edges = [System.Collections.Generic.List[object]]::new()
    foreach ($capture in $Captures) {
        $edgesByCapture.Add($capture.Key, [System.Collections.Generic.List[object]]::new())
    }

    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    foreach ($result in $Results) {
        $resultEdges = [System.Collections.Generic.List[object]]::new()
        $resultTime = Get-SessionReplayCorrelationUtcTime -Record $result -Role Result
        $resultMinute = $resultTime.ToString("yyyyMMddHHmm", $culture)
        foreach ($capture in $Captures) {
            $captureTime = Get-SessionReplayCorrelationUtcTime -Record $capture -Role Capture
            if ($captureTime.ToString("yyyyMMddHHmm", $culture) -cne $resultMinute) {
                continue
            }

            $deltaTicks = [Math]::Abs($resultTime.Ticks - $captureTime.Ticks)
            $deltaSeconds = $deltaTicks / [double] [System.TimeSpan]::TicksPerSecond
            if ($deltaSeconds -gt $MaxDeltaSeconds) {
                continue
            }

            $edge = [pscustomobject] @{
                    Result = $result
                    Capture = $capture
                    DeltaTicks = [long] $deltaTicks
                    DeltaSeconds = [double] $deltaSeconds
                }
            $edges.Add($edge)
            $resultEdges.Add($edge)
            $edgesByCapture[$capture.Key].Add($edge)
        }
        $edgesByResult.Add($result.Key, $resultEdges)
    }

    $matchedByResult = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal
    )
    $matchedByCapture = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal
    )
    $ambiguousResults = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal
    )
    $ambiguousCaptures = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal
    )

    $sortedEdges = @($edges | Sort-Object -Property `
            DeltaTicks,
            @{ Expression = { $_.Result.Key } },
            @{ Expression = { $_.Capture.Key } })
    foreach ($edge in $sortedEdges) {
        if (
            $matchedByResult.ContainsKey($edge.Result.Key) -or
            $matchedByCapture.ContainsKey($edge.Capture.Key) -or
            $ambiguousResults.Contains($edge.Result.Key) -or
            $ambiguousCaptures.Contains($edge.Capture.Key)
        ) {
            continue
        }

        # Expand the equal-delta connected component among still-unassigned records.
        $componentResults = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal
        )
        $componentCaptures = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal
        )
        $null = $componentResults.Add($edge.Result.Key)
        $null = $componentCaptures.Add($edge.Capture.Key)
        $componentEdges = [System.Collections.Generic.List[object]]::new()
        $changed = $true
        while ($changed) {
            $changed = $false
            foreach ($peer in $sortedEdges) {
                if (
                    $peer.DeltaTicks -ne $edge.DeltaTicks -or
                    $matchedByResult.ContainsKey($peer.Result.Key) -or
                    $matchedByCapture.ContainsKey($peer.Capture.Key) -or
                    $ambiguousResults.Contains($peer.Result.Key) -or
                    $ambiguousCaptures.Contains($peer.Capture.Key)
                ) {
                    continue
                }
                if (
                    -not $componentResults.Contains($peer.Result.Key) -and
                    -not $componentCaptures.Contains($peer.Capture.Key)
                ) {
                    continue
                }

                if (-not ($componentEdges | Where-Object {
                            $_.Result.Key -ceq $peer.Result.Key -and
                            $_.Capture.Key -ceq $peer.Capture.Key
                        })) {
                    $componentEdges.Add($peer)
                }
                if ($componentResults.Add($peer.Result.Key)) {
                    $changed = $true
                }
                if ($componentCaptures.Add($peer.Capture.Key)) {
                    $changed = $true
                }
            }
        }

        if ($componentEdges.Count -gt 1) {
            foreach ($resultKey in $componentResults) {
                $null = $ambiguousResults.Add($resultKey)
            }
            foreach ($captureKey in $componentCaptures) {
                $null = $ambiguousCaptures.Add($captureKey)
            }
            continue
        }

        $matchedByResult.Add($edge.Result.Key, $edge)
        $matchedByCapture.Add($edge.Capture.Key, $edge)
    }

    $output = [System.Collections.Generic.List[object]]::new()
    $sortedResults = @($Results | Sort-Object -Property LastModifiedUtc, Key)
    foreach ($result in $sortedResults) {
        $resultEdges = @($edgesByResult[$result.Key] | Sort-Object -Property `
                DeltaTicks,
                @{ Expression = { $_.Capture.Key } })
        $candidateCaptureKeys = @($resultEdges | ForEach-Object { $_.Capture.Key })
        $candidateResultKeys = [System.Collections.Generic.List[string]]::new()
        foreach ($resultEdge in $resultEdges) {
            foreach ($captureEdge in $edgesByCapture[$resultEdge.Capture.Key]) {
                $candidateResultKeys.Add($captureEdge.Result.Key)
            }
        }

        if ($matchedByResult.ContainsKey($result.Key)) {
            $match = $matchedByResult[$result.Key]
            $output.Add((New-SessionReplayCorrelationRecord `
                        -Status "Matched" `
                        -Subject "Result" `
                        -Result $result `
                        -Capture $match.Capture `
                        -DeltaSeconds ([Math]::Round([double] $match.DeltaSeconds, 3)) `
                        -CandidateCaptureKeys $candidateCaptureKeys `
                        -CandidateResultKeys @($result.Key) `
                        -Reason $null))
            continue
        }

        if ($ambiguousResults.Contains($result.Key)) {
            $output.Add((New-SessionReplayCorrelationRecord `
                        -Status "Ambiguous" `
                        -Subject "Result" `
                        -Result $result `
                        -Capture $null `
                        -DeltaSeconds $null `
                        -CandidateCaptureKeys $candidateCaptureKeys `
                        -CandidateResultKeys $candidateResultKeys.ToArray() `
                        -Reason "An equal-delta competing edge prevents a unique minimum assignment."))
            continue
        }

        $output.Add((New-SessionReplayCorrelationRecord `
                    -Status "Unmatched" `
                    -Subject "Result" `
                    -Result $result `
                    -Capture $null `
                    -DeltaSeconds $null `
                    -CandidateCaptureKeys $candidateCaptureKeys `
                    -CandidateResultKeys $candidateResultKeys.ToArray() `
                    -Reason "No unassigned capture remained in the same UTC minute within the configured delta."))
    }

    $sortedCaptures = @($Captures | Sort-Object -Property LastModifiedUtc, Key)
    foreach ($capture in $sortedCaptures) {
        if ($matchedByCapture.ContainsKey($capture.Key)) {
            continue
        }

        $captureEdges = @($edgesByCapture[$capture.Key] | Sort-Object -Property `
                DeltaTicks,
                @{ Expression = { $_.Result.Key } })
        $candidateResultKeys = @($captureEdges | ForEach-Object { $_.Result.Key })
        $candidateCaptureKeys = [System.Collections.Generic.List[string]]::new()
        foreach ($captureEdge in $captureEdges) {
            foreach ($resultEdge in $edgesByResult[$captureEdge.Result.Key]) {
                $candidateCaptureKeys.Add($resultEdge.Capture.Key)
            }
        }

        if ($ambiguousCaptures.Contains($capture.Key)) {
            $output.Add((New-SessionReplayCorrelationRecord `
                        -Status "Ambiguous" `
                        -Subject "Capture" `
                        -Result $null `
                        -Capture $capture `
                        -DeltaSeconds $null `
                        -CandidateCaptureKeys $candidateCaptureKeys.ToArray() `
                        -CandidateResultKeys $candidateResultKeys `
                        -Reason "An equal-delta competing edge prevents a unique minimum assignment."))
            continue
        }

        $output.Add((New-SessionReplayCorrelationRecord `
                    -Status "Unmatched" `
                    -Subject "Capture" `
                    -Result $null `
                    -Capture $capture `
                    -DeltaSeconds $null `
                    -CandidateCaptureKeys @($capture.Key) `
                    -CandidateResultKeys $candidateResultKeys `
                    -Reason "No unassigned result remained in the same UTC minute within the configured delta."))
    }

    return @($output)
}

Export-ModuleMember -Function @(
    "Read-SessionReplayConfiguration",
    "Get-SessionReplayDayInventory",
    "Test-SessionReplayS3Access",
    "Save-SessionReplayS3Object",
    "Resolve-SessionReplayCorrelation"
)
