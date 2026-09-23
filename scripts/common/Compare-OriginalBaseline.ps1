[CmdletBinding()]
param(
    [string] $RepoPath,
    [string] $OriginalPath,
    [string] $BaselineCommit = "415ba0c"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Resolves one optional path to a stable absolute location.
function Resolve-ComparisonPath {
    param(
        [string] $Candidate,
        [Parameter(Mandatory = $true)]
        [string] $Fallback
    )

    $selected = if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Fallback
    } else {
        $Candidate
    }
    return [System.IO.Path]::GetFullPath($selected)
}

$defaultRepoPath = Split-Path -Parent (
    Split-Path -Parent $PSScriptRoot
)
$resolvedRepoPath = Resolve-ComparisonPath `
    -Candidate $RepoPath `
    -Fallback $defaultRepoPath
$defaultOriginalPath = Join-Path (
    Split-Path -Parent $resolvedRepoPath
) "qsee-mobile-app-original"
$resolvedOriginalPath = Resolve-ComparisonPath `
    -Candidate $OriginalPath `
    -Fallback $defaultOriginalPath

if (-not (Test-Path -LiteralPath $resolvedRepoPath -PathType Container)) {
    throw "Repository path not found: $resolvedRepoPath"
}
if (-not (Test-Path -LiteralPath $resolvedOriginalPath -PathType Container)) {
    throw "Original source path not found: $resolvedOriginalPath"
}
if (-not (Get-Command -Name git -ErrorAction SilentlyContinue)) {
    throw "git was not found in PATH."
}

& git -C $resolvedRepoPath cat-file -e "$BaselineCommit^{commit}"
if ($LASTEXITCODE -ne 0) {
    throw "Baseline commit not found: $BaselineCommit"
}

$baselineEntries = @(
    & git -C $resolvedRepoPath ls-tree -r $BaselineCommit
)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to enumerate baseline commit $BaselineCommit."
}
$baselineFiles = [System.Collections.Generic.List[string]]::new()
$baselineBlobs = @{}
foreach ($entry in $baselineEntries) {
    if ($entry -notmatch "^\d+\s+blob\s+([0-9a-f]+)\t(.+)$") {
        continue
    }
    $relativePath = $Matches[2]
    $baselineFiles.Add($relativePath)
    $baselineBlobs[$relativePath] = $Matches[1]
}
$baselineSet = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($relativePath in $baselineFiles) {
    [void] $baselineSet.Add($relativePath)
}

$matchingOriginal = 0
$differentOriginal = [System.Collections.Generic.List[string]]::new()
$missingOriginal = [System.Collections.Generic.List[string]]::new()
$comparableFiles = [System.Collections.Generic.List[string]]::new()
foreach ($relativePath in $baselineFiles) {
    $nativeRelativePath = $relativePath.Replace(
        "/",
        [System.IO.Path]::DirectorySeparatorChar
    )
    $originalFile = Join-Path $resolvedOriginalPath $nativeRelativePath
    if (-not (Test-Path -LiteralPath $originalFile -PathType Leaf)) {
        $missingOriginal.Add($relativePath)
        continue
    }
    $comparableFiles.Add($relativePath)
}

$originalBlobs = @(
    $comparableFiles |
        & git -C $resolvedOriginalPath hash-object --stdin-paths
)
if ($LASTEXITCODE -ne 0 -or
        $originalBlobs.Count -ne $comparableFiles.Count) {
    throw "Unable to hash the original source tree."
}
for ($index = 0; $index -lt $comparableFiles.Count; $index++) {
    $relativePath = $comparableFiles[$index]
    if ($baselineBlobs[$relativePath] -eq $originalBlobs[$index]) {
        $matchingOriginal++
    } else {
        $differentOriginal.Add($relativePath)
    }
}

$inheritedChanges = [System.Collections.Generic.List[string]]::new()
$currentChanges = @(
    & git -C $resolvedRepoPath diff --name-only `
        --diff-filter=MD $BaselineCommit -- 2>$null
)
$currentInheritedFiles =
        [System.Collections.Generic.List[string]]::new()
foreach ($relativePath in $currentChanges) {
    if (-not $baselineSet.Contains($relativePath)) {
        continue
    }
    $nativeRelativePath = $relativePath.Replace(
        "/",
        [System.IO.Path]::DirectorySeparatorChar
    )
    if (-not (Test-Path -LiteralPath (
        Join-Path $resolvedRepoPath $nativeRelativePath
    ) -PathType Leaf)) {
        $inheritedChanges.Add($relativePath)
        continue
    }
    $currentInheritedFiles.Add($relativePath)
}
$currentInheritedBlobs = @(
    $currentInheritedFiles |
        & git -C $resolvedRepoPath hash-object --stdin-paths
)
if ($LASTEXITCODE -ne 0 -or
        $currentInheritedBlobs.Count -ne
        $currentInheritedFiles.Count) {
    throw "Unable to hash current inherited files."
}
for ($index = 0; $index -lt $currentInheritedFiles.Count; $index++) {
    $relativePath = $currentInheritedFiles[$index]
    if ($baselineBlobs[$relativePath] -ne
            $currentInheritedBlobs[$index]) {
        $inheritedChanges.Add($relativePath)
    }
}

$introducedFiles = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($relativePath in @(
    & git -C $resolvedRepoPath diff --name-only `
        --diff-filter=A $BaselineCommit -- 2>$null
)) {
    [void] $introducedFiles.Add($relativePath)
}
foreach ($relativePath in @(
    & git -C $resolvedRepoPath ls-files --others --exclude-standard
)) {
    if (-not $baselineSet.Contains($relativePath)) {
        [void] $introducedFiles.Add($relativePath)
    }
}

Write-Host "QSee original-boundary comparison" -ForegroundColor Blue
Write-Host ("  Repository: {0}" -f $resolvedRepoPath)
Write-Host ("  Original:   {0}" -f $resolvedOriginalPath)
Write-Host ("  Baseline:   {0}" -f $BaselineCommit)
Write-Host (
    "  Original files matching baseline: {0}" -f $matchingOriginal
) -ForegroundColor Green
Write-Host (
    "  Original files different from baseline: {0}" -f
        $differentOriginal.Count
) -ForegroundColor Yellow
Write-Host (
    "  Baseline files absent from original: {0}" -f
        $missingOriginal.Count
) -ForegroundColor Yellow
Write-Host (
    "  Inherited files changed in current worktree: {0}" -f
        $inheritedChanges.Count
) -ForegroundColor Cyan
Write-Host (
    "  Files introduced after baseline: {0}" -f
        $introducedFiles.Count
) -ForegroundColor Cyan

if ($differentOriginal.Count -gt 0) {
    Write-Host "  Original differences:" -ForegroundColor Yellow
    $differentOriginal | Select-Object -First 20 | ForEach-Object {
        Write-Host ("    - {0}" -f $_)
    }
}
if ($missingOriginal.Count -gt 0) {
    Write-Host "  Missing from original:" -ForegroundColor Yellow
    $missingOriginal | Select-Object -First 20 | ForEach-Object {
        Write-Host ("    - {0}" -f $_)
    }
}
if ($inheritedChanges.Count -gt 0) {
    Write-Host "  Current inherited-file changes:" -ForegroundColor Cyan
    $inheritedChanges | Sort-Object | ForEach-Object {
        Write-Host ("    - {0}" -f $_)
    }
}
