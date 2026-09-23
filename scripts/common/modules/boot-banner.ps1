# Fits one line of banner text within the requested width.
function Format-QSeeBannerLine {
    param(
        [AllowEmptyString()]
        [string] $Text,

        [Parameter(Mandatory = $true)]
        [int] $Width
    )

    if ($Text.Length -le $Width) {
        return $Text.PadRight($Width)
    }
    if ($Width -le 3) {
        return $Text.Substring(0, $Width)
    }
    return $Text.Substring(0, $Width - 3) + "..."
}

# Writes the branded QSee header for the interactive launcher.
function Write-QSeeBootBanner {
    param(
        [string] $Title = "Android Toolkit",
        [string] $Subtitle = "Build, release, device, and asset workflows",
        [string] $Context = "Customer QSee"
    )

    $width = Resolve-QSeeCliConsoleWidth
    $leftText = "  QSee.ai  /  $Title"
    $rightText = "PowerShell $($PSVersionTable.PSVersion.Major)  "
    $fillLength = [Math]::Max(2, $width - $leftText.Length - $rightText.Length)
    $titleBand = Format-QSeeBannerLine `
        -Text ($leftText + (" " * $fillLength) + $rightText) `
        -Width $width

    Write-Host $titleBand -BackgroundColor DarkMagenta -ForegroundColor White
    Write-Host ""
    Write-Host "  📱  $Subtitle" -ForegroundColor Cyan
    Write-Host "  🎯  $Context" -ForegroundColor Gray
    Write-Host ""
    Write-Host ("─" * $width) -ForegroundColor DarkGray
}

# Writes the title and interaction hint above the QSee menu.
function Write-QSeeMenuHeader {
    param(
        [string] $Title = "Choose an action",
        [string] $Hint = "Enter a number, or use 0 / Esc to close the launcher."
    )

    Write-Host ""
    Write-Host "  ✦  $Title" -ForegroundColor Cyan
    Write-Host "  $Hint" -ForegroundColor DarkGray
}
