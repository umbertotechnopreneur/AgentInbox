# Centers and safely truncates one footer line.
function Format-QSeeFooterLine {
    param(
        [AllowEmptyString()]
        [string] $Text,

        [Parameter(Mandatory = $true)]
        [int] $Width
    )

    $displayText = $Text
    if ($displayText.Length -gt $Width) {
        $displayText = if ($Width -le 3) {
            $displayText.Substring(0, $Width)
        } else {
            $displayText.Substring(0, $Width - 3) + "..."
        }
    }

    $leftPadding = [Math]::Floor(($Width - $displayText.Length) / 2)
    $rightPadding = $Width - $displayText.Length - $leftPadding
    return (" " * $leftPadding) + $displayText + (" " * $rightPadding)
}

# Formats an elapsed interval for the launcher footer.
function Format-QSeeElapsedDuration {
    param(
        [Parameter(Mandatory = $true)]
        [timespan] $Elapsed
    )

    $totalSeconds = [Math]::Max(0, [int] [Math]::Floor($Elapsed.TotalSeconds))
    $hours = [Math]::Floor($totalSeconds / 3600)
    $minutes = [Math]::Floor(($totalSeconds % 3600) / 60)
    $seconds = $totalSeconds % 60

    if ($hours -gt 0) {
        return "{0:00}:{1:00}:{2:00}" -f $hours, $minutes, $seconds
    }
    return "{0:00}:{1:00}" -f $minutes, $seconds
}

# Writes the multicolor border used by QSee completion footers.
function Write-QSeeRainbowBorder {
    param(
        [Parameter(Mandatory = $true)]
        [int] $Width,

        [switch] $Warning
    )

    $colors = if ($Warning) {
        @("Red", "DarkRed", "Magenta", "Red", "Yellow", "Red")
    } else {
        @("Magenta", "Blue", "Cyan", "Green", "Yellow", "Magenta")
    }
    $segmentWidth = [Math]::Ceiling($Width / $colors.Count)

    for ($index = 0; $index -lt $colors.Count; $index++) {
        $chunkWidth = [Math]::Min(
            $segmentWidth,
            $Width - ($index * $segmentWidth)
        )
        if ($chunkWidth -gt 0) {
            Write-Host ("═" * $chunkWidth) `
                -ForegroundColor $colors[$index] `
                -NoNewline
        }
    }
    Write-Host ""
}

# Writes a timed success, failure, or close footer for one launcher action.
function Write-QSeeFooterBanner {
    param(
        [string] $Message = "Launcher ready.",

        [ValidateSet("COMPLETED", "FAILED", "CLOSED")]
        [string] $Status = "COMPLETED",

        [Nullable[datetime]] $StartTime = $null,
        [Nullable[datetime]] $EndTime = $null
    )

    $resolvedEndTime = if ($null -ne $EndTime) {
        [datetime] $EndTime
    } else {
        Get-Date
    }
    $resolvedStartTime = if ($null -ne $StartTime) {
        [datetime] $StartTime
    } else {
        $resolvedEndTime
    }
    $elapsed = $resolvedEndTime - $resolvedStartTime
    $width = Resolve-QSeeCliConsoleWidth
    $isFailure = $Status -eq "FAILED"
    $backgroundColor = if ($isFailure) { "DarkRed" } else { "DarkGray" }
    $statusColor = switch ($Status) {
        "FAILED" { "Red" }
        "CLOSED" { "Cyan" }
        default { "Green" }
    }

    $statusLine = Format-QSeeFooterLine `
        -Text ("[{0}] {1}" -f $Status, $Message) `
        -Width $width
    $timeLine = Format-QSeeFooterLine `
        -Text (
            "Started {0:HH:mm:ss}  ·  Ended {1:HH:mm:ss}  ·  Elapsed {2}" -f
                $resolvedStartTime,
                $resolvedEndTime,
                (Format-QSeeElapsedDuration -Elapsed $elapsed)
        ) `
        -Width $width

    Write-Host ""
    Write-QSeeRainbowBorder -Width $width -Warning:$isFailure
    Write-Host $statusLine `
        -BackgroundColor $backgroundColor `
        -ForegroundColor $statusColor
    Write-Host $timeLine `
        -BackgroundColor $backgroundColor `
        -ForegroundColor White
    Write-QSeeRainbowBorder -Width $width -Warning:$isFailure
}
