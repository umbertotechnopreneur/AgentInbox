[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false)]
    [string]
    $WorkspacePath = (Get-Location).Path,

    [Parameter(Mandatory = $false)]
    [string]
    $TargetPath = '.',

    [Parameter(Mandatory = $false)]
    [string]
    $OutputPath,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 25)]
    [int]
    $TopFiles = 5,

    [Parameter(Mandatory = $false)]
    [switch]
    $DeleteEmptyDirectories,

    [Parameter(Mandatory = $false)]
    [switch]
    $Help
)

$ErrorActionPreference = 'Stop'

function Write-InventoryHelp {
    @"
SYNOPSIS
  Scope-restricted workspace inventory helper with optional safe cleanup.

DESCRIPTION
  Reads files and directories under a requested target path, restricted to WorkspacePath.
  By default it is read-only; to remove empty directories, pass -DeleteEmptyDirectories.

PARAMETERS
  -WorkspacePath           Root workspace used as security scope.
  -TargetPath              Path under WorkspacePath to scan.
  -OutputPath              Optional path inside WorkspacePath for output log.
  -TopFiles                Number of largest files to show.
  -DeleteEmptyDirectories  Opt-in cleanup, only when explicitly set.
  -WhatIf                  Preview cleanup actions.

EXAMPLE
  pwsh -NoProfile -File .\scripts\common\Invoke-WorkspaceInventory.ps1 `
    -WorkspacePath 'E:\QSee.ai\qsee-mobile-app' -TargetPath '.'
"@
}

function Resolve-ScopePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]
        $BasePath,

        [Parameter(Mandatory = $true)]
        [string]
        $CandidatePath
    )

    $baseResolved = (Resolve-Path -LiteralPath $BasePath).Path
    $baseRoot = $baseResolved.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $candidateRaw = if ([IO.Path]::IsPathRooted($CandidatePath)) {
        (Resolve-Path -LiteralPath $CandidatePath).Path
    } else {
        (Resolve-Path -LiteralPath (Join-Path $baseResolved $CandidatePath)).Path
    }

    $normalizedCandidate = $candidateRaw.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $scope = $baseRoot.ToLowerInvariant() + [IO.Path]::DirectorySeparatorChar

    if ($normalizedCandidate -ne $baseRoot.ToLowerInvariant() -and -not $normalizedCandidate.ToLowerInvariant().StartsWith($scope)) {
        throw \"Path '$candidateRaw' is outside workspace '$baseResolved'.\"
    }

    $candidateRaw
}

if ($Help) {
    Write-InventoryHelp | Write-Output
    return
}

$workspaceRoot = Resolve-ScopePath -BasePath $WorkspacePath -CandidatePath $WorkspacePath
$targetRoot = Resolve-ScopePath -BasePath $workspaceRoot -CandidatePath $TargetPath

$allFiles = Get-ChildItem -LiteralPath $targetRoot -File -Recurse -Force -ErrorAction SilentlyContinue
$allDirs = Get-ChildItem -LiteralPath $targetRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue

$filesByExt = if ($allFiles) {
    $allFiles |
        Group-Object -Property Extension |
        Sort-Object Count -Descending |
        Select-Object -First 10 |
        ForEach-Object {
            [pscustomobject]@{
                Extension  = if ([string]::IsNullOrWhiteSpace($_.Name)) { '[no extension]' } else { $_.Name }
                Count      = $_.Count
                TotalBytes = ($_.Group | Measure-Object -Property Length -Sum).Sum
            }
        }
}

$largestFiles = if ($allFiles) {
    $allFiles | Sort-Object -Property Length -Descending | Select-Object -First $TopFiles
}

$sizeBytes = if ($allFiles) { ($allFiles | Measure-Object -Property Length -Sum).Sum } else { 0 }
$sizeMb = [Math]::Round([double]$sizeBytes / 1MB, 2)

$reportText = [System.Collections.Generic.List[string]]::new()
$reportText.Add('Workspace inventory')
$reportText.Add(('Date: {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
$reportText.Add(('Workspace: {0}' -f $workspaceRoot))
$reportText.Add(('Target: {0}' -f $targetRoot))
$reportText.Add(('Files: {0}' -f $allFiles.Count))
$reportText.Add(('Directories: {0}' -f $allDirs.Count))
$reportText.Add(('Total size: {0} bytes ({1} MB)' -f $sizeBytes, $sizeMb))
$reportText.Add('Top file extensions:')

if ($filesByExt) {
    foreach ($entry in $filesByExt) {
        $reportText.Add(('{0}  count={1}  bytes={2}' -f $entry.Extension, $entry.Count, $entry.TotalBytes))
    }
} else {
    $reportText.Add('none')
}

$reportText.Add(('Largest files (top {0}):' -f $TopFiles))
if ($largestFiles) {
    foreach ($entry in $largestFiles) {
        $reportText.Add(('{0}' -f $entry.FullName))
    }
} else {
    $reportText.Add('none')
}

$reportText | ForEach-Object { Write-Output $_ }

if ($DeleteEmptyDirectories) {
    $emptyDirs = Get-ChildItem -LiteralPath $targetRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0 } |
        Sort-Object -Property FullName -Descending

    foreach ($dir in $emptyDirs) {
        if ($PSCmdlet.ShouldProcess($dir.FullName, 'Remove empty directory')) {
            Remove-Item -LiteralPath $dir.FullName -Force
        }
    }

    Write-Output ('Empty directories removed: {0}' -f $emptyDirs.Count)
}

if ($OutputPath) {
    $resolvedOutput = Resolve-ScopePath -BasePath $workspaceRoot -CandidatePath $OutputPath
    $reportText | Set-Content -Path $resolvedOutput -Encoding UTF8
    Write-Output ('Report saved: {0}' -f $resolvedOutput)
}
