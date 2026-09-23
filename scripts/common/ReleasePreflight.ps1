$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Runs one read-only Git query and returns its output lines.
function Invoke-QSeeReleaseGitRead {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    $output = @(& git -C $RepositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $detail = ($output -join " ").Trim()
        throw "Git release preflight failed: $detail"
    }
    return @($output | ForEach-Object { [string] $_ })
}

# Enforces the product-version token expected by each release channel.
function Assert-QSeeReleaseVersionContract {
    param(
        [Parameter(Mandatory = $true)]
        [string] $VersionName,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Internal", "Beta", "Stable")]
        [string] $Channel
    )

    $normalized = $VersionName.Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "VersionName is required for a reproducible release."
    }
    if ($VersionName -ne $normalized) {
        throw "VersionName cannot contain leading or trailing whitespace."
    }
    if ($normalized -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[._+-][A-Za-z0-9][A-Za-z0-9._+-]*)?$') {
        throw "VersionName must start with major.minor.patch and contain only release-safe characters."
    }

    $hasBetaToken = $normalized -match '(?i)(^|[._+-])beta([._+-]|[0-9]|$)'
    $hasInternalToken = $normalized -match '(?i)(^|[._+-])(internal|test|dev)([._+-]|[0-9]|$)'
    $hasPreReleaseToken = $normalized -match '(?i)(^|[._+-])(alpha|beta|rc|preview|internal|test|dev)([._+-]|[0-9]|$)'

    switch ($Channel) {
        "Beta" {
            if (-not $hasBetaToken) {
                throw "Beta releases require a VersionName containing the beta token."
            }
        }
        "Internal" {
            if (-not $hasInternalToken) {
                throw "Internal releases require an internal, test, or dev VersionName token."
            }
        }
        "Stable" {
            if ($hasPreReleaseToken) {
                throw "Stable releases cannot use a prerelease VersionName token."
            }
        }
    }
}

# Returns the canonical uppercase SHA-256 for release text encoded as UTF-8.
function Get-QSeeReleaseTextSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    return [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes($Text)
        )
    )
}

# Captures a clean, named Git source revision for a release operation.
function Get-QSeeReleaseGitState {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedBranch
    )

    $requiredBranch = $ExpectedBranch.Trim()
    if ([string]::IsNullOrWhiteSpace($requiredBranch)) {
        throw "ReleaseSourceBranch is required."
    }

    $branch = (Invoke-QSeeReleaseGitRead `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @("symbolic-ref", "--quiet", "--short", "HEAD") |
            Select-Object -First 1).Trim()
    if ($branch -ne $requiredBranch) {
        throw "Release branch mismatch: expected '$requiredBranch', current '$branch'."
    }

    $status = @(Invoke-QSeeReleaseGitRead `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @("status", "--porcelain=v1", "--untracked-files=all"))
    if ($status.Count -gt 0) {
        throw "Release workspace must be clean; Git reports $($status.Count) changed path(s)."
    }

    [void](Invoke-QSeeReleaseGitRead `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @("diff", "--check"))
    $commit = (Invoke-QSeeReleaseGitRead `
        -RepositoryRoot $RepositoryRoot `
        -Arguments @("rev-parse", "--verify", "HEAD") |
            Select-Object -First 1).Trim()
    if ($commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Release commit is not a full Git object id."
    }

    return [pscustomobject] [ordered] @{
        Branch = $branch
        Commit = $commit.ToLowerInvariant()
        IsClean = $true
    }
}

# Creates the immutable local facts required before release build or upload.
function New-QSeeReleasePreflightPlan {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedBranch,

        [Parameter(Mandatory = $true)]
        [long] $VersionCode,

        [Parameter(Mandatory = $true)]
        [string] $VersionName,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Internal", "Beta", "Stable")]
        [string] $Channel,

        [Parameter(Mandatory = $true)]
        [ValidateSet("QSee", "Panko", "Kiabi", "Aussco")]
        [string] $Backend,

        [Parameter(Mandatory = $true)]
        [string] $ReleaseNotes,

        [Parameter(Mandatory = $true)]
        [string] $ModelLockPath
    )

    if ($VersionCode -le 0 -or $VersionCode -gt [int]::MaxValue) {
        throw "VersionCode must be between 1 and the Android Int32 maximum."
    }
    $notes = $ReleaseNotes.Trim()
    if ([string]::IsNullOrWhiteSpace($notes)) {
        throw "Operator release notes are required."
    }
    Assert-QSeeReleaseVersionContract -VersionName $VersionName -Channel $Channel

    $resolvedLockPath = [System.IO.Path]::GetFullPath($ModelLockPath)
    if (-not (Test-Path -LiteralPath $resolvedLockPath -PathType Leaf)) {
        throw "Model asset lock not found: $resolvedLockPath"
    }
    $gitState = Get-QSeeReleaseGitState `
        -RepositoryRoot $RepositoryRoot `
        -ExpectedBranch $ExpectedBranch
    $canonicalChannel = @{
        internal = "Internal"
        beta = "Beta"
        stable = "Stable"
    }[$Channel.ToLowerInvariant()]
    $canonicalBackend = @{
        qsee = "QSee"
        panko = "Panko"
        kiabi = "Kiabi"
        aussco = "Aussco"
    }[$Backend.ToLowerInvariant()]

    return [pscustomobject] [ordered] @{
        SourceBranch = $gitState.Branch
        SourceCommit = $gitState.Commit
        WorkingTreeClean = $gitState.IsClean
        VersionCode = $VersionCode
        VersionName = $VersionName.Trim()
        ReleaseChannel = $canonicalChannel
        ReleaseBackend = $canonicalBackend
        ReleaseNotes = $notes
        ReleaseNotesSha256 = Get-QSeeReleaseTextSha256 -Text $notes
        ModelLockSha256 = (
            Get-FileHash -LiteralPath $resolvedLockPath -Algorithm SHA256
        ).Hash.ToUpperInvariant()
    }
}
