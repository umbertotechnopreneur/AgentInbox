#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $artifactRoot.StartsWith($repoPrefix, $pathComparison)) {
    throw 'Artifact cleanup must stay inside the repository.'
}

if (Test-Path -LiteralPath $artifactRoot) {
    $artifactDirectory = Get-Item -LiteralPath $artifactRoot -Force
    if (-not $artifactDirectory.PSIsContainer -or
        ($artifactDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Artifact cleanup requires a regular directory, not a link or junction.'
    }

    # Refuse linked descendants before deleting anything; never follow paths outside artifacts.
    $linkedItem = Get-ChildItem -LiteralPath $artifactRoot -Force -Recurse |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
        Select-Object -First 1
    if ($linkedItem) { throw "Remove the link or junction before artifact cleanup: $($linkedItem.FullName)" }

    $artifactPrefix = $artifactRoot + [IO.Path]::DirectorySeparatorChar
    foreach ($item in Get-ChildItem -LiteralPath $artifactRoot -Force) {
        $targetPath = [IO.Path]::GetFullPath($item.FullName)
        if (-not $targetPath.StartsWith($artifactPrefix, $pathComparison)) {
            throw 'Artifact cleanup target escaped the artifact directory.'
        }
        Remove-Item -LiteralPath $targetPath -Recurse -Force
    }
} else {
    New-Item -ItemType Directory -Path $artifactRoot | Out-Null
}

Write-Output "Cleaned build artifacts: $artifactRoot"
