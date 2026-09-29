[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    $stagedFiles = @(git diff --cached --name-only --diff-filter=ACMR -- '*.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect staged C# files.' }
    if ($stagedFiles.Count -eq 0) { return }

    $unstagedFiles = @(git diff --name-only --diff-filter=ACMR -- '*.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect unstaged C# files.' }
    $unstagedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($unstagedFile in $unstagedFiles) {
        [void]$unstagedSet.Add($unstagedFile)
    }
    $partiallyStagedFiles = @($stagedFiles | Where-Object { $unstagedSet.Contains($_) })
    if ($partiallyStagedFiles.Count -gt 0) {
        $fileList = $partiallyStagedFiles -join ', '
        throw "Automatic formatting stopped because these C# files are only partially staged: $fileList. Stage or stash their remaining changes, then commit again."
    }

    & (Join-Path $PSScriptRoot 'format.ps1') -Include $stagedFiles
    git add -- $stagedFiles
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage the formatted C# files.' }
} finally {
    Pop-Location
}
