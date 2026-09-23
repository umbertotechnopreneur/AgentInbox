@{
    RootModule = "MenuManager.psm1"
    ModuleVersion = "0.1.0"
    GUID = "1e1b9f3e-6a4d-4b8c-9c7a-5f5b0b3d9ead"
    Author = "Umberto Giacobbi"
    CompanyName = "QSee.ai"
    Copyright = ""
    Description = "Shared interactive menu helpers used by the QSee PowerShell launcher."
    PowerShellVersion = "5.1"
    FunctionsToExport = @(
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
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    FileList = @("MenuManager.psm1")
    PrivateData = @{
        PSData = @{
            Tags = @("qsee", "menu", "interactive", "ui", "helpers")
        }
    }
}
