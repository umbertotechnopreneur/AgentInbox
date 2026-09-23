# Detects whether the host provides the optional shared color writer.
function Use-WriteColor {
    return (Get-Command -Name Write-Color -ErrorAction SilentlyContinue) -ne $null
}

# Reads a compact menu choice with raw-key input when the choice set allows it.
function Read-MenuChoice {
    param(
        [string] $Prompt = "Enter choice",
        [int] $MaxIndex = 9,
        [string[]] $AllowedChoices,
        [switch] $IgnoreEmptyInput
    )

    $useSingleKey = $false
    if ($AllowedChoices -and $AllowedChoices.Count -gt 0) {
        $useSingleKey = (
            $AllowedChoices |
                Where-Object { $_.Length -ne 1 }
        ).Count -eq 0
    } elseif ($MaxIndex -lt 10) {
        $useSingleKey = $true
    }

    if (Use-WriteColor) {
        Write-Color " " -NoNewLine
        Write-Color ("  $Prompt") -Color DarkGray -NoNewLine
        Write-Color ": " -Color DarkGray -NoNewLine
    } else {
        Write-Host ""
        Write-Host ("  {0}: " -f $Prompt) `
            -ForegroundColor DarkGray `
            -NoNewline
    }

    if (
        $useSingleKey -and
        [Environment]::UserInteractive -and
        -not [Console]::IsInputRedirected
    ) {
        try {
            while ($true) {
                $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
                if ($key.VirtualKeyCode -eq 27) {
                    Write-Host ""
                    return $null
                }
                if ($key.VirtualKeyCode -eq 13) {
                    if ($IgnoreEmptyInput) {
                        continue
                    }
                    Write-Host ""
                    return ""
                }

                $character = [string] $key.Character
                if (
                    [string]::IsNullOrEmpty($character) -and
                    $key.VirtualKeyCode -ge 96 -and
                    $key.VirtualKeyCode -le 105
                ) {
                    $character = ($key.VirtualKeyCode - 96).ToString()
                }

                if ([string]::IsNullOrEmpty($character)) {
                    continue
                }
                $codePoint = [int] [char] $character
                if ($codePoint -lt 32 -or $codePoint -eq 127) {
                    continue
                }
                if (
                    $AllowedChoices -and
                    $AllowedChoices.Count -gt 0 -and
                    $AllowedChoices -notcontains $character
                ) {
                    continue
                }
                if (
                    (-not $AllowedChoices -or $AllowedChoices.Count -eq 0) -and
                    $character -notmatch "^[0-9]$"
                ) {
                    continue
                }

                Write-Host $character
                return $character
            }
        } catch {
            # Some hosts expose RawUI but cannot read from it; use Read-Host below.
        }
    }

    while ($true) {
        try {
            $rawValue = Read-Host
        } catch {
            return $null
        }
        if ($null -eq $rawValue) {
            return $null
        }

        if ([string]::IsNullOrWhiteSpace($rawValue)) {
            if ($IgnoreEmptyInput) {
                continue
            }
            return ""
        }

        $trimmedValue = $rawValue.Trim()
        if ($trimmedValue.ToLowerInvariant() -in @("esc", "exit", "quit", "back")) {
            return $null
        }
        if (
            $AllowedChoices -and
            $AllowedChoices.Count -gt 0 -and
            $AllowedChoices -notcontains $trimmedValue
        ) {
            continue
        }
        return $trimmedValue
    }
}

# Reads and validates a numeric menu index, with 0 or Esc treated as cancel.
function Read-IndexChoice {
    param(
        [Parameter(Position = 0)]
        [AllowEmptyString()]
        [string] $Prompt = "",

        [Parameter(Mandatory = $true)]
        [int] $Min,

        [Parameter(Mandatory = $true)]
        [int] $Max
    )

    while ($true) {
        $promptText = if ([string]::IsNullOrWhiteSpace($Prompt)) {
            "  Enter number [{0}-{1}], or 0 / Esc to go back: " -f $Min, $Max
        } else {
            "  {0}: " -f $Prompt
        }

        if (Use-WriteColor) {
            Write-Color $promptText -Color Yellow -NoNewLine
        } else {
            Write-Host $promptText -ForegroundColor Yellow -NoNewline
        }

        if (
            $Host.UI -and
            $Host.UI.RawUI -and
            [Environment]::UserInteractive -and
            -not [Console]::IsInputRedirected
        ) {
            try {
                # Buffer digits so multi-digit menus retain immediate Esc and 0 handling.
                $buffer = ""
                while ($true) {
                    $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
                    if ($key.VirtualKeyCode -eq 27) {
                        Write-Host ""
                        return $null
                    }
                    if ($key.VirtualKeyCode -eq 13) {
                        Write-Host ""
                        if ([string]::IsNullOrWhiteSpace($buffer)) {
                            return ""
                        }
                        if ($buffer -eq "0") {
                            return $null
                        }

                        $number = 0
                        if (
                            [int]::TryParse($buffer, [ref] $number) -and
                            $number -ge $Min -and
                            $number -le $Max
                        ) {
                            return $number
                        }
                        break
                    }
                    if ($key.VirtualKeyCode -eq 8) {
                        if ($buffer.Length -gt 0) {
                            $buffer = $buffer.Substring(0, $buffer.Length - 1)
                            Write-Host -NoNewline "`b `b"
                        }
                        continue
                    }

                    $character = ([string] $key.Character).Trim()
                    if (
                        [string]::IsNullOrEmpty($character) -and
                        $key.VirtualKeyCode -ge 96 -and
                        $key.VirtualKeyCode -le 105
                    ) {
                        $character = ($key.VirtualKeyCode - 96).ToString()
                    }
                    if ($character -match "^[0-9]$") {
                        if ($character -eq "0" -and $buffer.Length -eq 0) {
                            Write-Host ""
                            return $null
                        }
                        $buffer += $character
                        Write-Host -NoNewline $character
                    }
                }

                Write-Host (
                    "  ✗ Please enter a number between {0} and {1}." -f
                        $Min,
                        $Max
                ) -ForegroundColor Red
                continue
            } catch {
                # Fall back to line input when RawUI is unavailable or redirected.
            }
        }

        try {
            $rawValue = Read-Host
        } catch {
            return $null
        }
        if ($null -eq $rawValue) {
            return $null
        }
        if ([string]::IsNullOrWhiteSpace($rawValue)) {
            return ""
        }

        $trimmedValue = $rawValue.Trim()
        if (
            $trimmedValue.ToLowerInvariant() -in
                @("esc", "exit", "quit", "back", "0")
        ) {
            return $null
        }

        $number = 0
        if (
            [int]::TryParse($trimmedValue, [ref] $number) -and
            $number -ge $Min -and
            $number -le $Max
        ) {
            return $number
        }
        Write-Host (
            "  ✗ Please enter a number between {0} and {1}." -f
                $Min,
                $Max
        ) -ForegroundColor Red
    }
}

# Resolves a bounded console width for shared menu renderers.
function Resolve-MenuConsoleWidth {
    param(
        [int] $Minimum = 80,
        [int] $Maximum = 180,
        [int] $Fallback = 110
    )

    $resolvedWidth = $Fallback
    try {
        $consoleWidth = [Console]::WindowWidth
        if ($consoleWidth -gt 0) {
            $resolvedWidth = $consoleWidth
        }
    } catch {
        try {
            $rawWidth = $Host.UI.RawUI.WindowSize.Width
            if ($rawWidth -gt 0) {
                $resolvedWidth = $rawWidth
            }
        } catch {
            $resolvedWidth = $Fallback
        }
    }

    return [Math]::Max($Minimum, [Math]::Min($resolvedWidth, $Maximum))
}

# Truncates menu copy to the requested display width.
function Get-MenuTruncatedText {
    param(
        [AllowNull()]
        [string] $Text,

        [int] $MaxLength
    )

    if ($null -eq $Text -or $MaxLength -le 0) {
        return ""
    }
    if ($Text.Length -le $MaxLength) {
        return $Text
    }
    if ($MaxLength -le 3) {
        return $Text.Substring(0, $MaxLength)
    }
    return $Text.Substring(0, $MaxLength - 3) + "..."
}

# Writes a full-width section heading for a shared menu.
function Write-MenuSectionHeader {
    param(
        [string] $Title
    )

    $width = Resolve-MenuConsoleWidth `
        -Minimum 66 `
        -Maximum 110 `
        -Fallback 80
    Write-Host ""
    Write-Host ("  " + ("-" * [Math]::Min($width, 58))) `
        -ForegroundColor DarkCyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ("  " + ("-" * [Math]::Min($width, 58))) `
        -ForegroundColor DarkCyan
    Write-Host ""
}

# Writes one detailed shared menu row.
function Write-MenuItem {
    param(
        [int] $Number,
        [string] $Emoji,
        [string] $Label,
        [string] $Desc,
        [ConsoleColor] $LabelColor = "Yellow"
    )

    $numberText = "{0,2}" -f $Number
    Write-Host "  [$numberText]  " -ForegroundColor DarkGray -NoNewline
    Write-Host "$Emoji  " -NoNewline
    Write-Host ("{0,-28}" -f $Label) `
        -ForegroundColor $LabelColor `
        -NoNewline
    Write-Host "  $Desc" -ForegroundColor DarkGray
}

# Writes a compact section heading sized for the current console.
function Write-MenuCompactSectionHeader {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Title,

        [ConsoleColor] $AccentColor = "Cyan",
        [int] $ConsoleWidth = 110
    )

    $ruleLength = [Math]::Max(
        26,
        [Math]::Min(64, $ConsoleWidth - 8)
    )
    $titleText = "  -- $Title "
    $ruleRemainder = [Math]::Max(1, $ruleLength - $titleText.Length)

    Write-Host ""
    Write-Host $titleText -ForegroundColor $AccentColor -NoNewline
    Write-Host ("-" * $ruleRemainder) -ForegroundColor DarkGray
}

# Writes one compact menu row with bounded label and description columns.
function Write-MenuItemCompact {
    param(
        [Parameter(Mandatory = $true)]
        [int] $Number,

        [Parameter(Mandatory = $true)]
        [string] $Emoji,

        [Parameter(Mandatory = $true)]
        [string] $Label,

        [Parameter(Mandatory = $true)]
        [string] $Desc,

        [ConsoleColor] $LabelColor = "Yellow",
        [int] $ConsoleWidth = 110
    )

    $labelWidth = [Math]::Min(
        28,
        [Math]::Max(16, [int] (($ConsoleWidth - 32) / 3))
    )
    $descriptionWidth = [Math]::Min(
        78,
        [Math]::Max(24, $ConsoleWidth - 14 - $labelWidth)
    )
    $displayLabel = Get-MenuTruncatedText `
        -Text $Label `
        -MaxLength $labelWidth
    $displayDescription = Get-MenuTruncatedText `
        -Text $Desc `
        -MaxLength $descriptionWidth
    $numberText = "{0,2}" -f $Number

    Write-Host "  [$numberText]  " -ForegroundColor DarkGray -NoNewline
    Write-Host "$Emoji  " -NoNewline
    Write-Host ("{0,-$labelWidth}" -f $displayLabel) `
        -ForegroundColor $LabelColor `
        -NoNewline
    Write-Host "  $displayDescription" -ForegroundColor DarkGray
}

# Writes two compact menu rows side by side when the console is wide enough.
function Write-MenuItemCompactPair {
    param(
        [Parameter(Mandatory = $true)]
        $LeftItem,

        $RightItem,
        [ConsoleColor] $LabelColor = "Yellow",
        [int] $ConsoleWidth = 120
    )

    if ($null -eq $RightItem) {
        Write-MenuItemCompact `
            -Number $LeftItem.Number `
            -Emoji $LeftItem.Emoji `
            -Label $LeftItem.Label `
            -Desc $LeftItem.Desc `
            -LabelColor $LabelColor `
            -ConsoleWidth $ConsoleWidth
        return
    }

    $columnGap = 4
    $usableWidth = [Math]::Max(80, $ConsoleWidth - 4)
    $columnWidth = [Math]::Max(
        36,
        [int] (($usableWidth - $columnGap) / 2)
    )
    $labelWidth = [Math]::Min(
        20,
        [Math]::Max(14, [int] (($columnWidth - 14) / 2))
    )
    $descriptionWidth = [Math]::Max(
        12,
        $columnWidth - 14 - $labelWidth
    )

    $leftNumber = "{0,2}" -f [int] $LeftItem.Number
    $leftLabel = Get-MenuTruncatedText `
        -Text ([string] $LeftItem.Label) `
        -MaxLength $labelWidth
    $leftDescription = Get-MenuTruncatedText `
        -Text ([string] $LeftItem.Desc) `
        -MaxLength $descriptionWidth
    $rightNumber = "{0,2}" -f [int] $RightItem.Number
    $rightLabel = Get-MenuTruncatedText `
        -Text ([string] $RightItem.Label) `
        -MaxLength $labelWidth
    $rightDescription = Get-MenuTruncatedText `
        -Text ([string] $RightItem.Desc) `
        -MaxLength $descriptionWidth

    $leftPrefix = "  [$leftNumber]  $($LeftItem.Emoji)  "
    $leftPaddingLength = [Math]::Max(
        0,
        $columnWidth - (
            $leftPrefix.Length +
            $labelWidth +
            2 +
            $leftDescription.Length
        )
    )

    Write-Host $leftPrefix -ForegroundColor DarkGray -NoNewline
    Write-Host ("{0,-$labelWidth}" -f $leftLabel) `
        -ForegroundColor $LabelColor `
        -NoNewline
    Write-Host "  $leftDescription" -ForegroundColor DarkGray -NoNewline
    if ($leftPaddingLength -gt 0) {
        Write-Host (" " * $leftPaddingLength) -NoNewline
    }

    Write-Host (" " * $columnGap) -NoNewline
    Write-Host "[$rightNumber]  " -ForegroundColor DarkGray -NoNewline
    Write-Host "$($RightItem.Emoji)  " -NoNewline
    Write-Host ("{0,-$labelWidth}" -f $rightLabel) `
        -ForegroundColor $LabelColor `
        -NoNewline
    Write-Host "  $rightDescription" -ForegroundColor DarkGray
}

# Writes the standard zero-key navigation row.
function Write-BackItem {
    param(
        [string] $Label = "Back to Main Menu"
    )

    Write-Host ""
    Write-Host "  [ 0]  " -ForegroundColor DarkGray -NoNewline
    Write-Host "←  $Label" -ForegroundColor DarkGray
}

# Pauses an interactive workflow until the operator presses Enter.
function Wait-ForEnter {
    param(
        [string] $Prompt = "  Press Enter to return to the menu..."
    )

    if (Use-WriteColor) {
        Write-Color $Prompt -Color DarkGray -NoNewLine
    } else {
        Write-Host $Prompt -ForegroundColor DarkGray -NoNewline
    }
    try {
        $null = Read-Host
    } catch {
        # A redirected or closed input stream should not fail a completed action.
    }
}

Export-ModuleMember -Function @(
    "Read-MenuChoice",
    "Read-IndexChoice",
    "Write-MenuItem",
    "Write-MenuSectionHeader",
    "Write-BackItem",
    "Wait-ForEnter",
    "Resolve-MenuConsoleWidth",
    "Write-MenuCompactSectionHeader",
    "Write-MenuItemCompact",
    "Write-MenuItemCompactPair"
)
