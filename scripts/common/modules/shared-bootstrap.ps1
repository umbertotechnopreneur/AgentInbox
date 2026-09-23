# Resolves a safe width for QSee CLI banners and menu content.
function Resolve-QSeeCliConsoleWidth {
    param(
        [int] $Minimum = 72,
        [int] $Maximum = 112,
        [int] $Fallback = 96
    )

    $resolvedWidth = $Fallback
    try {
        $consoleWidth = [Console]::WindowWidth
        if ($consoleWidth -gt 0) {
            $resolvedWidth = $consoleWidth - 2
        }
    } catch {
        try {
            $rawWidth = $Host.UI.RawUI.WindowSize.Width
            if ($rawWidth -gt 0) {
                $resolvedWidth = $rawWidth - 2
            }
        } catch {
            $resolvedWidth = $Fallback
        }
    }

    return [Math]::Max($Minimum, [Math]::Min($resolvedWidth, $Maximum))
}

# Clears an interactive terminal without emitting control codes into redirected output.
function Clear-QSeeTerminal {
    [CmdletBinding()]
    param()

    try {
        if ([Console]::IsOutputRedirected) {
            return
        }

        $supportsVirtualTerminal = $false
        $supportProperty = $Host.UI.PSObject.Properties["SupportsVirtualTerminal"]
        if ($supportProperty) {
            $supportsVirtualTerminal = [bool] $supportProperty.Value
        }

        if ($supportsVirtualTerminal) {
            Write-Host "`e[3J`e[H`e[2J" -NoNewline
        } else {
            Clear-Host
        }
    } catch {
        # Clearing is cosmetic and some headless hosts expose no usable console handle.
    }
}

# Prepares UTF-8 console output for the QSee interactive launcher.
function Initialize-QSeeCliUi {
    [CmdletBinding()]
    param(
        [switch] $ClearScreen
    )

    try {
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $OutputEncoding = [System.Text.Encoding]::UTF8
    } catch {
        # Continue with the host encoding when the output stream is unavailable.
    }

    if ($ClearScreen) {
        Clear-QSeeTerminal
    }
}
