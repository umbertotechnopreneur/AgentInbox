# Shared dependency license collection for Windows MSIX and portable packages.

function Copy-DependencyNotices {
    param([string[]]$AssetFiles, [string]$Destination)

    $packages = @{}
    foreach ($assetFile in $AssetFiles) {
        $graph = Get-Content -LiteralPath $assetFile -Raw | ConvertFrom-Json -AsHashtable
        foreach ($entry in $graph.libraries.GetEnumerator()) {
            if ($entry.Value.type -ne 'package' -or $packages.ContainsKey($entry.Key)) { continue }
            $packageFolder = $null
            foreach ($packageRoot in $graph.packageFolders.Keys) {
                $candidate = Join-Path $packageRoot $entry.Value.path
                if (Test-Path -LiteralPath $candidate -PathType Container) {
                    $packageFolder = $candidate
                    break
                }
            }
            if (-not $packageFolder) { throw "Cannot include dependency notices for $($entry.Key): restored package not found." }
            $packages[$entry.Key] = $packageFolder
        }
    }

    $rows = [System.Collections.Generic.List[string]]::new()
    $rows.Add('# Windows dependency inventory')
    $rows.Add('')
    $rows.Add('Generated from the published desktop and CLI dependency graphs. Includes build dependencies; the self-contained .NET and Windows App SDK runtime payloads also carry their notices.')
    $rows.Add('')
    $rows.Add('| Package | Version | Declared license |')
    $rows.Add('| --- | --- | --- |')
    foreach ($key in $packages.Keys | Sort-Object) {
        $packageFolder = $packages[$key]
        $parts = $key.Split('/')
        $noticeDirectory = Join-Path $Destination "packages/$($parts[0]).$($parts[1])"
        New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
        $specification = Get-ChildItem -LiteralPath $packageFolder -Filter '*.nuspec' -File | Select-Object -First 1
        if (-not $specification) { throw "Missing dependency metadata for $key." }
        Copy-Item -LiteralPath $specification.FullName -Destination $noticeDirectory
        [xml]$spec = Get-Content -LiteralPath $specification.FullName -Raw
        $license = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="license"]')
        $licenseUrl = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="licenseUrl"]')
        $declaredLicense = if ($license) { $license.InnerText } elseif ($licenseUrl) { $licenseUrl.InnerText } else { 'See package metadata; license review required.' }
        $rows.Add("| $($parts[0]) | $($parts[1]) | $declaredLicense |")

        foreach ($noticeFile in Get-ChildItem -LiteralPath $packageFolder -File | Where-Object Name -Match '(?i)license|notice|copying') {
            Copy-Item -LiteralPath $noticeFile.FullName -Destination $noticeDirectory
        }
        if ($license -and $license.GetAttribute('type') -eq 'file') {
            $licensePath = [IO.Path]::GetFullPath((Join-Path $packageFolder $license.InnerText))
            $packagePrefix = [IO.Path]::GetFullPath($packageFolder).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $licensePath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid dependency license path for $key." }
            Copy-Item -LiteralPath $licensePath -Destination $noticeDirectory
        }
    }
    $rows | Set-Content -LiteralPath (Join-Path $Destination 'DEPENDENCIES.md') -Encoding utf8NoBOM
}

