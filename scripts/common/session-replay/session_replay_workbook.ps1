[CmdletBinding()]
param(
    [switch] $Preflight,
    [string] $RunDirectory,
    [string] $Output
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:ExcelFileFormatOpenXmlWorkbook = 51
$script:ExcelSourceRange = 1
$script:ExcelHasHeaders = 1
$script:ExcelCellValue = 1
$script:ExcelEqual = 3
$script:ExcelColorNavy = 0x4D3217
$script:ExcelColorBlue = 0x90662F
$script:ExcelColorPaleBlue = 0xF8F3EA
$script:ExcelColorGreen = 0x5A852F
$script:ExcelColorPaleGreen = 0xEAF4E6
$script:ExcelColorRed = 0x1823B4
$script:ExcelColorPaleRed = 0xECECFD
$script:ExcelColorAmber = 0x00679A
$script:ExcelColorPaleAmber = 0xCEF4FF
$script:ExcelColorInk = 0x37291F
$script:ExcelColorMuted = 0x8B7464
$script:ExcelColorLine = 0xE1D5CB
$script:ExcelColorWhite = 0xFFFFFF
$script:RequiredRunFiles = @("plan.json", "run-summary.json", "calibration.json", "README.md")

# Releases one COM reference without hiding an earlier generation error.
function Release-ComReference {
    param([AllowNull()][object] $Reference)

    if ($null -ne $Reference -and [System.Runtime.InteropServices.Marshal]::IsComObject($Reference)) {
        $null = [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($Reference)
    }
}

# Confirms that desktop Excel is registered for strict workbook generation.
function Assert-ExcelAvailable {
    $excelType = [type]::GetTypeFromProgID("Excel.Application")
    if ($null -eq $excelType) {
        throw "Microsoft Excel desktop is required and is not registered for COM automation."
    }
}

# Reads one required JSON evidence file with strict UTF-8 and parse validation.
function Read-RequiredJson {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing required JSON evidence for ${Label}: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Length -le 0) {
        throw "Required JSON evidence is empty for ${Label}: $Path"
    }
    try {
        return Get-Content -Raw -LiteralPath $Path -Encoding utf8 | ConvertFrom-Json -Depth 100
    } catch {
        throw "Required JSON evidence is invalid for ${Label}: $Path"
    }
}

# Returns a required object property and rejects missing or null values.
function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)][object] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "$Label is missing required property $Name."
    }
    return $property.Value
}

# Returns one optional object property without triggering strict-mode access errors.
function Get-OptionalProperty {
    param(
        [AllowNull()][object] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )

    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

# Returns required non-empty text from the evidence contract.
function Get-RequiredText {
    param(
        [AllowNull()][object] $Value,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string] $Value)) {
        throw "$Label must contain non-empty text."
    }
    return [string] $Value
}

# Returns one required finite numeric value from the evidence contract.
function Get-RequiredNumber {
    param(
        [AllowNull()][object] $Value,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if ($null -eq $Value) {
        throw "$Label must contain a finite number."
    }
    $number = [double] $Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) {
        throw "$Label must contain a finite number."
    }
    return $number
}

# Parses one required evidence timestamp and normalizes it to UTC.
function ConvertTo-UtcDateTime {
    param(
        [AllowNull()][object] $Value,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $text = Get-RequiredText -Value $Value -Label $Label
    $parsed = [datetime]::MinValue
    if (-not [datetime]::TryParse(
            $text,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $parsed
        )) {
        throw "$Label must contain a valid ISO timestamp."
    }
    return $parsed.ToUniversalTime()
}

# Converts an optional evidence timestamp to UTC or an empty cell.
function ConvertTo-OptionalUtcDateTime {
    param(
        [AllowNull()][object] $Value,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string] $Value)) {
        return $null
    }
    return ConvertTo-UtcDateTime -Value $Value -Label $Label
}

# Confirms one retained artifact exists as a non-empty regular file.
function Resolve-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Label does not exist: $resolvedPath"
    }
    if ((Get-Item -LiteralPath $resolvedPath -Force).Length -le 0) {
        throw "$Label is empty: $resolvedPath"
    }
    return $resolvedPath
}

# Creates an explicit local file URI for one retained path.
function ConvertTo-FileUri {
    param([AllowNull()][object] $Path)

    if ($null -eq $Path -or [string]::IsNullOrWhiteSpace([string] $Path)) {
        return $null
    }
    $resolvedPath = [System.IO.Path]::GetFullPath([string] $Path)
    return [uri]::new($resolvedPath).AbsoluteUri
}

# Returns the earliest and latest UTC timestamps from one non-empty record set.
function Get-TimeBounds {
    param(
        [Parameter(Mandatory = $true)][object[]] $Records,
        [Parameter(Mandatory = $true)][string] $PropertyName,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if ($Records.Count -eq 0) {
        throw "$Label must contain at least one record."
    }
    $times = @($Records | ForEach-Object {
            ConvertTo-UtcDateTime -Value (Get-RequiredProperty -Object $_ -Name $PropertyName -Label $Label) -Label "$Label timestamp"
        } | Sort-Object)
    return [pscustomobject]@{ First = $times[0]; Last = $times[-1] }
}

# Returns the first retained local path from one non-empty plan collection.
function Get-FirstLocalPath {
    param(
        [Parameter(Mandatory = $true)][object[]] $Records,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if ($Records.Count -eq 0) {
        throw "$Label must contain at least one record."
    }
    return Get-RequiredText -Value (Get-RequiredProperty -Object $Records[0] -Name "LocalPath" -Label $Label) -Label "$Label local path"
}

# Loads and validates the completed replay evidence required by the workbook.
function Read-RunEvidence {
    param(
        [Parameter(Mandatory = $true)][string] $ResolvedRunDirectory,
        [Parameter(Mandatory = $true)][string] $ResolvedOutputPath
    )

    foreach ($relativePath in $script:RequiredRunFiles) {
        $null = Resolve-RequiredFile -Path (Join-Path $ResolvedRunDirectory $relativePath) -Label "Required run file $relativePath"
    }
    $plan = Read-RequiredJson -Path (Join-Path $ResolvedRunDirectory "plan.json") -Label "plan"
    $summary = Read-RequiredJson -Path (Join-Path $ResolvedRunDirectory "run-summary.json") -Label "run summary"
    $calibration = Read-RequiredJson -Path (Join-Path $ResolvedRunDirectory "calibration.json") -Label "calibration"
    if ((Get-RequiredText -Value $summary.Status -Label "run-summary Status") -cne "COMPLETE") {
        throw "Evidence workbook generation requires run-summary Status COMPLETE."
    }
    $summaryItems = @($summary.Items)
    if ($summaryItems.Count -eq 0 -or $summaryItems.Count -ne [int] $plan.SelectedImageCount) {
        throw "Run summary items must match plan.SelectedImageCount."
    }

    $items = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $summaryItems.Count; $index++) {
        $item = $summaryItems[$index]
        if ((Get-RequiredText -Value $item.Status -Label "Items[$index].Status") -cne "COMPLETE") {
            throw "Every run item must be COMPLETE before workbook generation."
        }
        $imagePath = Resolve-RequiredFile -Path $item.ImagePath -Label "Items[$index].ImagePath"
        $overlayPath = Resolve-RequiredFile -Path $item.OverlayPath -Label "Items[$index].OverlayPath"
        $analysisPath = Resolve-RequiredFile -Path $item.AnalysisPath -Label "Items[$index].AnalysisPath"
        $provenancePath = Resolve-RequiredFile -Path $item.ProvenancePath -Label "Items[$index].ProvenancePath"
        $historicalResultPath = if ([string]::IsNullOrWhiteSpace([string] $item.HistoricalResultPath)) {
            $null
        } else {
            Resolve-RequiredFile -Path $item.HistoricalResultPath -Label "Items[$index].HistoricalResultPath"
        }
        $analysis = Read-RequiredJson -Path $analysisPath -Label "Items[$index] analysis"
        $provenance = Read-RequiredJson -Path $provenancePath -Label "Items[$index] provenance"
        if (@($analysis.measurements).Count -eq 0) {
            throw "Items[$index] analysis has no measurements."
        }
        if ([string] $analysis.overallVerdict -cne [string] $item.OverallVerdict) {
            throw "Items[$index] summary and analysis verdicts do not match."
        }
        $items.Add([pscustomobject]@{
                Summary = $item
                Analysis = $analysis
                Provenance = $provenance
                ImagePath = $imagePath
                OverlayPath = $overlayPath
                AnalysisPath = $analysisPath
                ProvenancePath = $provenancePath
                HistoricalResultPath = $historicalResultPath
            })
    }

    return [pscustomobject]@{
        RunDirectory = $ResolvedRunDirectory
        OutputPath = $ResolvedOutputPath
        Plan = $plan
        Summary = $summary
        Calibration = $calibration
        Items = @($items)
    }
}

# Converts row-oriented PowerShell data into the rectangular array expected by Excel.
function ConvertTo-ExcelMatrix {
    param(
        [Parameter(Mandatory = $true)][object[]] $Rows,
        [Parameter(Mandatory = $true)][int] $ColumnCount
    )

    if ($Rows.Count -eq 0) {
        throw "Excel matrix generation requires at least one row."
    }
    $matrix = [object[,]]::new($Rows.Count, $ColumnCount)
    for ($rowIndex = 0; $rowIndex -lt $Rows.Count; $rowIndex++) {
        $row = @($Rows[$rowIndex])
        if ($row.Count -ne $ColumnCount) {
            throw "Excel matrix row $rowIndex has $($row.Count) columns; expected $ColumnCount."
        }
        for ($columnIndex = 0; $columnIndex -lt $ColumnCount; $columnIndex++) {
            $matrix[$rowIndex, $columnIndex] = $row[$columnIndex]
        }
    }
    return ,$matrix
}

# Writes one rectangular value block without per-cell COM round trips.
function Set-ExcelValues {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][string] $TopLeft,
        [Parameter(Mandatory = $true)][object[]] $Rows,
        [Parameter(Mandatory = $true)][int] $ColumnCount
    )

    $matrix = ConvertTo-ExcelMatrix -Rows $Rows -ColumnCount $ColumnCount
    $start = $Sheet.Range($TopLeft)
    $target = $start.Resize($Rows.Count, $ColumnCount)
    $target.Value2 = $matrix
    Release-ComReference -Reference $target
    Release-ComReference -Reference $start
}

# Applies the shared QSee evidence-table presentation and creates an Excel table.
function Set-EvidenceTableStyle {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][string] $LastColumn,
        [Parameter(Mandatory = $true)][int] $LastRow,
        [Parameter(Mandatory = $true)][hashtable] $Widths,
        [Parameter(Mandatory = $true)][string] $TableName
    )

    $header = $Sheet.Range("A1:${LastColumn}1")
    $header.Interior.Color = $script:ExcelColorNavy
    $header.Font.Bold = $true
    $header.Font.Color = $script:ExcelColorWhite
    $header.Font.Size = 10
    $header.WrapText = $true
    $header.VerticalAlignment = -4108
    $header.RowHeight = 34
    Release-ComReference -Reference $header
    if ($LastRow -gt 1) {
        $body = $Sheet.Range("A2:${LastColumn}${LastRow}")
        $body.Font.Color = $script:ExcelColorInk
        $body.Font.Size = 9
        $body.WrapText = $true
        $body.VerticalAlignment = -4160
        $body.RowHeight = 30
        Release-ComReference -Reference $body
    }
    foreach ($column in $Widths.Keys) {
        $Sheet.Columns.Item([string] $column).ColumnWidth = [double] $Widths[$column]
    }
    $tableRange = $Sheet.Range("A1:${LastColumn}${LastRow}")
    $table = $Sheet.ListObjects.Add(
        $script:ExcelSourceRange,
        $tableRange,
        [type]::Missing,
        $script:ExcelHasHeaders
    )
    $table.Name = $TableName
    $table.TableStyle = "TableStyleMedium2"
    Release-ComReference -Reference $table
    Release-ComReference -Reference $tableRange
    $Sheet.Activate()
    $window = $Sheet.Application.ActiveWindow
    $window.DisplayGridlines = $false
    $window.SplitRow = 1
    $window.SplitColumn = 2
    $window.FreezePanes = $true
    Release-ComReference -Reference $window
}

# Adds native Excel conditional formatting for one exact status value.
function Add-TextStatusFormat {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][string] $RangeAddress,
        [Parameter(Mandatory = $true)][string] $Value,
        [Parameter(Mandatory = $true)][int] $FillColor,
        [Parameter(Mandatory = $true)][int] $FontColor
    )

    $range = $Sheet.Range($RangeAddress)
    $condition = $range.FormatConditions.Add(
        $script:ExcelCellValue,
        $script:ExcelEqual,
        ('="{0}"' -f $Value)
    )
    $condition.Interior.Color = $FillColor
    $condition.Font.Bold = $true
    $condition.Font.Color = $FontColor
    Release-ComReference -Reference $condition
    Release-ComReference -Reference $range
}

# Adds one native local-file hyperlink without using a worksheet formula.
function Add-FileHyperlink {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][string] $CellAddress,
        [AllowNull()][object] $Path
    )

    $uri = ConvertTo-FileUri -Path $Path
    if ($null -eq $uri) {
        return
    }
    $cell = $Sheet.Range($CellAddress)
    $null = $Sheet.Hyperlinks.Add(
        $cell,
        $uri,
        [type]::Missing,
        [type]::Missing,
        $uri
    )
    Release-ComReference -Reference $cell
}

# Writes the per-photo sheet with signed deltas and retained-file hyperlinks.
function Add-ImagesSheetContent {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][object] $Evidence
    )

    $headers = ,@(
        "Item", "Image ID", "Photo time UTC", "Historical JSON time UTC", "Photo - JSON (s)",
        "Lens reference UTC", "Photo - lens (s)", "Checkerboard reference UTC",
        "Photo - checkerboard (s)", "Verdict", "Measurements", "Historical association",
        "Original photo URI", "Annotated image URI", "Historical JSON URI", "Analysis JSON URI",
        "Provenance JSON URI"
    )
    Set-ExcelValues -Sheet $Sheet -TopLeft "A1" -Rows $headers -ColumnCount 17
    $rows = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Evidence.Items.Count; $index++) {
        $item = $Evidence.Items[$index]
        $provenance = $item.Provenance
        $historical = $provenance.historicalResult
        $hasHistorical = $null -ne $historical
        $rowValues = [object[]]::new(17)
        $rowValues[0] = $index + 1
        $rowValues[1] = Get-RequiredText -Value $item.Summary.Id -Label "Items[$index].Id"
        $rowValues[2] = ConvertTo-UtcDateTime -Value $provenance.image.timestampUtc -Label "Items[$index] photo timestamp"
        $rowValues[3] = if ($hasHistorical) { ConvertTo-UtcDateTime -Value $historical.timestampUtc -Label "Items[$index] historical timestamp" } else { $null }
        $rowValues[4] = if ($hasHistorical) { Get-RequiredNumber -Value $historical.deltaSeconds -Label "Items[$index] photo/JSON delta" } else { $null }
        $rowValues[5] = ConvertTo-UtcDateTime -Value $provenance.lensCalibration.referenceTimestampUtc -Label "Items[$index] lens reference"
        $rowValues[6] = Get-RequiredNumber -Value $provenance.lensCalibration.deltaSeconds -Label "Items[$index] lens delta"
        $rowValues[7] = ConvertTo-UtcDateTime -Value $provenance.homographyCalibration.referenceTimestampUtc -Label "Items[$index] checkerboard reference"
        $rowValues[8] = Get-RequiredNumber -Value $provenance.homographyCalibration.deltaSeconds -Label "Items[$index] checkerboard delta"
        $rowValues[9] = Get-RequiredText -Value $item.Analysis.overallVerdict -Label "Items[$index] verdict"
        $rowValues[10] = @($item.Analysis.measurements).Count
        $rowValues[11] = if ($hasHistorical) { "Matched" } else { "Missing" }
        $rows.Add($rowValues)
    }
    Set-ExcelValues -Sheet $Sheet -TopLeft "A2" -Rows @($rows) -ColumnCount 17
    $lastRow = $rows.Count + 1
    $Sheet.Range("C2:D${lastRow}").NumberFormat = "yyyy-mm-dd hh:mm:ss"
    $Sheet.Range("F2:F${lastRow}").NumberFormat = "yyyy-mm-dd hh:mm:ss"
    $Sheet.Range("H2:H${lastRow}").NumberFormat = "yyyy-mm-dd hh:mm:ss"
    $Sheet.Range("E2:E${lastRow}").NumberFormat = "0.0"
    $Sheet.Range("G2:G${lastRow}").NumberFormat = "0.0"
    $Sheet.Range("I2:I${lastRow}").NumberFormat = "0.0"
    for ($index = 0; $index -lt $Evidence.Items.Count; $index++) {
        $row = $index + 2
        $item = $Evidence.Items[$index]
        Add-FileHyperlink -Sheet $Sheet -CellAddress "M$row" -Path $item.ImagePath
        Add-FileHyperlink -Sheet $Sheet -CellAddress "N$row" -Path $item.OverlayPath
        Add-FileHyperlink -Sheet $Sheet -CellAddress "O$row" -Path $item.HistoricalResultPath
        Add-FileHyperlink -Sheet $Sheet -CellAddress "P$row" -Path $item.AnalysisPath
        Add-FileHyperlink -Sheet $Sheet -CellAddress "Q$row" -Path $item.ProvenancePath
    }
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "J2:J${lastRow}" -Value "PASS" -FillColor $script:ExcelColorPaleGreen -FontColor $script:ExcelColorGreen
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "J2:J${lastRow}" -Value "FAIL" -FillColor $script:ExcelColorPaleRed -FontColor $script:ExcelColorRed
    Set-EvidenceTableStyle -Sheet $Sheet -LastColumn "Q" -LastRow $lastRow -TableName "ReplayImagesTable" -Widths @{
        A = 7; B = 24; C = 20; D = 20; E = 14; F = 20; G = 14; H = 20; I = 20
        J = 11; K = 12; L = 18; M = 22; N = 22; O = 22; P = 22; Q = 22
    }
    return $lastRow
}

# Writes one auditable row for every current mobile measurement.
function Add-MeasurementsSheetContent {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][object] $Evidence
    )

    $headers = ,@(
        "Item", "Image ID", "Photo time UTC", "Image verdict", "Measurement", "Current cm",
        "Target cm", "Minimum cm", "Maximum cm", "Gap cm", "Measurement verdict",
        "Original photo URI", "Annotated image URI", "Analysis JSON URI"
    )
    Set-ExcelValues -Sheet $Sheet -TopLeft "A1" -Rows $headers -ColumnCount 14
    $rows = [System.Collections.Generic.List[object]]::new()
    $links = [System.Collections.Generic.List[object]]::new()
    for ($itemIndex = 0; $itemIndex -lt $Evidence.Items.Count; $itemIndex++) {
        $item = $Evidence.Items[$itemIndex]
        $photoTime = ConvertTo-UtcDateTime -Value $item.Provenance.image.timestampUtc -Label "Items[$itemIndex] photo timestamp"
        $measurements = @($item.Analysis.measurements)
        for ($measurementIndex = 0; $measurementIndex -lt $measurements.Count; $measurementIndex++) {
            $measurement = $measurements[$measurementIndex]
            $rowValues = [object[]]::new(14)
            $rowValues[0] = $itemIndex + 1
            $rowValues[1] = [string] $item.Summary.Id
            $rowValues[2] = $photoTime
            $rowValues[3] = [string] $item.Analysis.overallVerdict
            $rowValues[4] = Get-RequiredText -Value $measurement.id -Label "Measurement ID"
            $rowValues[5] = Get-RequiredNumber -Value $measurement.cm -Label "Current cm"
            $rowValues[6] = Get-RequiredNumber -Value $measurement.targetCm -Label "Target cm"
            $rowValues[7] = Get-RequiredNumber -Value $measurement.minimumAllowedCm -Label "Minimum cm"
            $rowValues[8] = Get-RequiredNumber -Value $measurement.maximumAllowedCm -Label "Maximum cm"
            $rowValues[9] = Get-RequiredNumber -Value $measurement.gapCm -Label "Gap cm"
            $rowValues[10] = Get-RequiredText -Value $measurement.verdict -Label "Measurement verdict"
            $rows.Add($rowValues)
            $links.Add([pscustomobject]@{
                    Row = $rows.Count + 1
                    ImagePath = $item.ImagePath
                    OverlayPath = $item.OverlayPath
                    AnalysisPath = $item.AnalysisPath
                })
        }
    }
    Set-ExcelValues -Sheet $Sheet -TopLeft "A2" -Rows @($rows) -ColumnCount 14
    $lastRow = $rows.Count + 1
    $Sheet.Range("C2:C${lastRow}").NumberFormat = "yyyy-mm-dd hh:mm:ss"
    $Sheet.Range("F2:J${lastRow}").NumberFormat = "0.00"
    foreach ($link in $links) {
        Add-FileHyperlink -Sheet $Sheet -CellAddress "L$($link.Row)" -Path $link.ImagePath
        Add-FileHyperlink -Sheet $Sheet -CellAddress "M$($link.Row)" -Path $link.OverlayPath
        Add-FileHyperlink -Sheet $Sheet -CellAddress "N$($link.Row)" -Path $link.AnalysisPath
    }
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "D2:D${lastRow}" -Value "PASS" -FillColor $script:ExcelColorPaleGreen -FontColor $script:ExcelColorGreen
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "D2:D${lastRow}" -Value "FAIL" -FillColor $script:ExcelColorPaleRed -FontColor $script:ExcelColorRed
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "K2:K${lastRow}" -Value "PASS" -FillColor $script:ExcelColorPaleGreen -FontColor $script:ExcelColorGreen
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "K2:K${lastRow}" -Value "FAIL" -FillColor $script:ExcelColorPaleRed -FontColor $script:ExcelColorRed
    Set-EvidenceTableStyle -Sheet $Sheet -LastColumn "N" -LastRow $lastRow -TableName "ReplayMeasurementsTable" -Widths @{
        A = 7; B = 24; C = 20; D = 12; E = 14; F = 12; G = 12; H = 12; I = 12
        J = 12; K = 18; L = 22; M = 22; N = 22
    }
    return $lastRow
}

# Builds the normalized lens and checkerboard frame rows.
function Get-CalibrationRows {
    param([Parameter(Mandatory = $true)][object] $Evidence)

    $imageBounds = Get-TimeBounds -Records @($Evidence.Plan.Images) -PropertyName "LastModifiedUtc" -Label "plan.Images"
    $identity = $Evidence.Calibration.identity.calibration
    $diagnostics = $Evidence.Calibration.diagnostics
    if ($null -eq $identity -or $null -eq $diagnostics) {
        throw "Calibration identity and diagnostics are required."
    }
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($groupDefinition in @(
            [pscustomobject]@{ Type = "Lens"; Group = $Evidence.Plan.LensCalibration; Paths = @($identity.lensImages); Diagnostics = @($diagnostics.lensImages) },
            [pscustomobject]@{ Type = "Checkerboard"; Group = $Evidence.Plan.HomographyCalibration; Paths = @($identity.homographyImages); Diagnostics = @($diagnostics.homographyImages) }
        )) {
        $objects = @($groupDefinition.Group.Objects)
        if ($objects.Count -eq 0 -or $objects.Count -ne $groupDefinition.Paths.Count) {
            throw "$($groupDefinition.Type) calibration plan and materialized paths do not match."
        }
        for ($index = 0; $index -lt $objects.Count; $index++) {
            $record = $objects[$index]
            $diagnostic = if ($index -lt $groupDefinition.Diagnostics.Count) { $groupDefinition.Diagnostics[$index] } else { $null }
            $frameTime = ConvertTo-UtcDateTime -Value $record.LastModifiedUtc -Label "$($groupDefinition.Type) frame timestamp"
            $deltaSeconds = ($imageBounds.First - $frameTime).TotalSeconds
            $sharpness = Get-OptionalProperty -Object $diagnostic -Name "sharpnessLaplacianVariance"
            $reason = Get-OptionalProperty -Object $diagnostic -Name "reason"
            $rows.Add([pscustomobject]@{
                    Type = $groupDefinition.Type
                    GroupId = [string] $groupDefinition.Group.GroupId
                    Sequence = $index + 1
                    FrameTime = $frameTime
                    DeltaSeconds = $deltaSeconds
                    Use = if ($null -eq $diagnostic) { "Not evaluated" } elseif ($diagnostic.accepted -eq $true) { "Accepted" } else { "Rejected" }
                    Sharpness = if ($null -ne $sharpness) { [double] $sharpness } else { $null }
                    Reason = if ($null -ne $reason) { [string] $reason } else { "" }
                    NewCoverageCells = Get-OptionalProperty -Object $diagnostic -Name "newCoverageCells"
                    CoverageFraction = Get-OptionalProperty -Object $diagnostic -Name "coverageFraction"
                    Placement = Get-OptionalProperty -Object $diagnostic -Name "placementNumber"
                    ObjectKey = [string] $record.Key
                    MaterializedPath = [string] $groupDefinition.Paths[$index]
                })
        }
    }
    return @($rows)
}

# Writes every selected calibration frame and its diagnostics.
function Add-CalibrationSheetContent {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][object] $Evidence
    )

    $headers = ,@(
        "Calibration type", "Group ID", "Sequence", "Frame time UTC", "First photo - frame (s)",
        "First photo - frame (min)", "Calibration use", "Sharpness", "Reason", "New coverage cells",
        "Coverage fraction", "Placement", "Source object key", "Materialized file URI"
    )
    Set-ExcelValues -Sheet $Sheet -TopLeft "A1" -Rows $headers -ColumnCount 14
    $records = Get-CalibrationRows -Evidence $Evidence
    $rows = @($records | ForEach-Object {
            ,@(
                $_.Type, $_.GroupId, $_.Sequence, $_.FrameTime, $_.DeltaSeconds, ($_.DeltaSeconds / 60),
                $_.Use, $_.Sharpness, $_.Reason, $_.NewCoverageCells, $_.CoverageFraction,
                $_.Placement, $_.ObjectKey, $null
            )
        })
    Set-ExcelValues -Sheet $Sheet -TopLeft "A2" -Rows $rows -ColumnCount 14
    $lastRow = $rows.Count + 1
    $Sheet.Range("D2:D${lastRow}").NumberFormat = "yyyy-mm-dd hh:mm:ss"
    $Sheet.Range("E2:E${lastRow}").NumberFormat = "0.0"
    $Sheet.Range("F2:F${lastRow}").NumberFormat = "0.00"
    $Sheet.Range("H2:H${lastRow}").NumberFormat = "0.0"
    $Sheet.Range("K2:K${lastRow}").NumberFormat = "0.0%"
    for ($index = 0; $index -lt $records.Count; $index++) {
        Add-FileHyperlink -Sheet $Sheet -CellAddress "N$($index + 2)" -Path $records[$index].MaterializedPath
    }
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "G2:G${lastRow}" -Value "Accepted" -FillColor $script:ExcelColorPaleGreen -FontColor $script:ExcelColorGreen
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "G2:G${lastRow}" -Value "Rejected" -FillColor $script:ExcelColorPaleAmber -FontColor $script:ExcelColorAmber
    Add-TextStatusFormat -Sheet $Sheet -RangeAddress "G2:G${lastRow}" -Value "Not evaluated" -FillColor $script:ExcelColorPaleBlue -FontColor $script:ExcelColorBlue
    Set-EvidenceTableStyle -Sheet $Sheet -LastColumn "N" -LastRow $lastRow -TableName "ReplayCalibrationFramesTable" -Widths @{
        A = 18; B = 28; C = 9; D = 20; E = 19; F = 20; G = 16; H = 13; I = 20
        J = 16; K = 16; L = 11; M = 46; N = 24
    }
    return $lastRow
}

# Writes source definitions and exact UTC selection windows.
function Add-SourcesSheetContent {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][object] $Evidence
    )

    $plan = $Evidence.Plan
    $imageBounds = Get-TimeBounds -Records @($plan.Images) -PropertyName "LastModifiedUtc" -Label "plan.Images"
    $resultBounds = Get-TimeBounds -Records @($plan.Results) -PropertyName "LastModifiedUtc" -Label "plan.Results"
    $lensBounds = Get-TimeBounds -Records @($plan.LensCalibration.Objects) -PropertyName "LastModifiedUtc" -Label "lens calibration"
    $checkerBounds = Get-TimeBounds -Records @($plan.HomographyCalibration.Objects) -PropertyName "LastModifiedUtc" -Label "checkerboard calibration"
    $matchedCount = @($Evidence.Items | Where-Object { $null -ne $_.HistoricalResultPath }).Count
    $measurementCount = [int] (@($Evidence.Items | ForEach-Object { @($_.Analysis.measurements).Count } | Measure-Object -Sum).Sum)
    $rows = @(
        [pscustomobject]@{ Source = "Original garment photos"; Role = "Starting images sent to the current remote landmark model."; Rule = "Only $($plan.UtcDay) UTC photos from $($plan.ImageSet); limited to $($plan.SelectedImageCount) selected items."; Start = $imageBounds.First; End = $imageBounds.Last; Count = $Evidence.Items.Count; Location = Split-Path -Parent (Get-FirstLocalPath -Records @($plan.Images) -Label "plan.Images") },
        [pscustomobject]@{ Source = "Historical measurement JSON"; Role = "Comparison evidence only; never used as current landmarks or measurements."; Rule = "One-to-one match inside the same UTC minute, nominal maximum delta $($plan.CorrelationWindowSeconds) seconds."; Start = $resultBounds.First; End = $resultBounds.Last; Count = $matchedCount; Location = Split-Path -Parent (Get-FirstLocalPath -Records @($plan.Results) -Label "plan.Results") },
        [pscustomobject]@{ Source = "Lens calibration photos"; Role = "Correct camera and lens distortion before measurement."; Rule = "Same UTC day; the selected group must finish before the first garment photo."; Start = $lensBounds.First; End = $lensBounds.Last; Count = [int] $plan.LensCalibration.ObjectCount; Location = Join-Path $Evidence.RunDirectory "source\calibration\lens" },
        [pscustomobject]@{ Source = "Checkerboard calibration photos"; Role = "Convert corrected image distances into real millimetres across the work surface."; Rule = "Same UTC day; selected group finishes before the first garment photo and reaches $([math]::Round([double] $plan.CoverageTargetFraction * 100))% coverage."; Start = $checkerBounds.First; End = $checkerBounds.Last; Count = [int] $plan.HomographyCalibration.ObjectCount; Location = Join-Path $Evidence.RunDirectory "source\calibration\homography" },
        [pscustomobject]@{ Source = "Annotated images"; Role = "K1-K17 landmarks, current measurements, verdicts, and signed timing deltas."; Rule = "One JPEG is required for every completed run item."; Start = $imageBounds.First; End = $imageBounds.Last; Count = $Evidence.Items.Count; Location = Join-Path $Evidence.RunDirectory "overlays" },
        [pscustomobject]@{ Source = "Current measurement rows"; Role = "Measurements calculated by the current mobile client and size contract."; Rule = "Read from each retained analysis JSON after landmarks and calibration complete."; Start = $imageBounds.First; End = $imageBounds.Last; Count = $measurementCount; Location = Join-Path $Evidence.RunDirectory "analysis" },
        [pscustomobject]@{ Source = "Run summary"; Role = "Machine-readable status and retained path index for this exact run."; Rule = "Workbook generation requires Status COMPLETE and one COMPLETE item per image."; Start = ConvertTo-OptionalUtcDateTime -Value $Evidence.Summary.UpdatedUtc -Label "run summary updated UTC"; End = ConvertTo-OptionalUtcDateTime -Value $Evidence.Summary.UpdatedUtc -Label "run summary updated UTC"; Count = $Evidence.Items.Count; Location = Join-Path $Evidence.RunDirectory "run-summary.json" }
    )
    $headers = ,@("Source or evidence", "What it contributes", "Selection rule or window", "Start UTC", "End UTC", "Count", "Local path", "File URI")
    Set-ExcelValues -Sheet $Sheet -TopLeft "A1" -Rows $headers -ColumnCount 8
    $values = @($rows | ForEach-Object { ,@($_.Source, $_.Role, $_.Rule, $_.Start, $_.End, $_.Count, $_.Location, $null) })
    Set-ExcelValues -Sheet $Sheet -TopLeft "A2" -Rows $values -ColumnCount 8
    $lastRow = $rows.Count + 1
    $Sheet.Range("D2:E${lastRow}").NumberFormat = "yyyy-mm-dd hh:mm:ss"
    for ($index = 0; $index -lt $rows.Count; $index++) {
        Add-FileHyperlink -Sheet $Sheet -CellAddress "H$($index + 2)" -Path $rows[$index].Location
    }
    Set-EvidenceTableStyle -Sheet $Sheet -LastColumn "H" -LastRow $lastRow -TableName "ReplaySourcesTimingTable" -Widths @{
        A = 28; B = 42; C = 58; D = 20; E = 20; F = 10; G = 58; H = 28
    }
    return $lastRow
}

# Writes the answer-first run overview with live Excel formulas and evidence links.
function Add-OverviewSheetContent {
    param(
        [Parameter(Mandatory = $true)][object] $Sheet,
        [Parameter(Mandatory = $true)][object] $Evidence,
        [Parameter(Mandatory = $true)][int] $ImageLastRow,
        [Parameter(Mandatory = $true)][int] $MeasurementLastRow
    )

    $Sheet.Range("A1:H1").Merge()
    $Sheet.Range("A1").Value2 = "QSee Session Replay Evidence"
    $Sheet.Range("A1:H1").Interior.Color = $script:ExcelColorNavy
    $Sheet.Range("A1:H1").Font.Bold = $true
    $Sheet.Range("A1:H1").Font.Color = $script:ExcelColorWhite
    $Sheet.Range("A1:H1").Font.Size = 20
    $Sheet.Range("A1:H1").RowHeight = 42
    $Sheet.Range("A3:H3").Merge()
    $Sheet.Range("A3").Value2 = "$($Evidence.Summary.Client) / $($Evidence.Summary.Size) / $($Evidence.Summary.UtcDay) UTC - $($Evidence.Summary.CalibrationProfile)"
    $Sheet.Range("A3:H3").Interior.Color = $script:ExcelColorPaleBlue
    $Sheet.Range("A3:H3").Font.Bold = $true
    $Sheet.Range("A3:H3").Font.Color = $script:ExcelColorBlue
    $Sheet.Range("A3:H3").Font.Size = 12

    Set-ExcelValues -Sheet $Sheet -TopLeft "A5" -Rows (, @("Processed images", $null, "Matched historical JSON", $null, "PASS images", $null, "FAIL images", $null)) -ColumnCount 8
    $Sheet.Range("B6").Formula = "=COUNTA('Images'!`$A`$2:`$A`$$ImageLastRow)"
    $Sheet.Range("D6").Formula = "=COUNTIF('Images'!`$L`$2:`$L`$$ImageLastRow,""Matched"")"
    $Sheet.Range("F6").Formula = "=COUNTIF('Images'!`$J`$2:`$J`$$ImageLastRow,""PASS"")"
    $Sheet.Range("H6").Formula = "=COUNTIF('Images'!`$J`$2:`$J`$$ImageLastRow,""FAIL"")"
    foreach ($rangeAddress in @("A5:B6", "C5:D6", "E5:F6", "G5:H6")) {
        $Sheet.Range($rangeAddress).Borders.LineStyle = 1
        $Sheet.Range($rangeAddress).Borders.Color = $script:ExcelColorLine
    }
    $Sheet.Range("A5:H5").Font.Bold = $true
    $Sheet.Range("A5:H5").Font.Color = $script:ExcelColorMuted
    foreach ($cellAddress in @("B6", "D6", "F6", "H6")) {
        $Sheet.Range($cellAddress).Font.Bold = $true
        $Sheet.Range($cellAddress).Font.Color = $script:ExcelColorNavy
        $Sheet.Range($cellAddress).Font.Size = 18
        $Sheet.Range($cellAddress).HorizontalAlignment = -4108
    }

    $runDetailRows = [System.Collections.Generic.List[object]]::new()
    $runDetailRows.Add([object[]]@("Run status", $Evidence.Summary.Status))
    $runDetailRows.Add([object[]]@("Source", $Evidence.Summary.Source))
    $runDetailRows.Add([object[]]@("Image set", $Evidence.Summary.ImageSet))
    $runDetailRows.Add([object[]]@("Remote model", $Evidence.Summary.RemoteModel))
    $runDetailRows.Add([object[]]@("Coverage target", [double] $Evidence.Summary.CoverageTargetFraction))
    $runDetailRows.Add([object[]]@("Achieved coverage", [double] $Evidence.Calibration.homography.coverageFraction))
    $runDetailRows.Add([object[]]@("Measurement rows", $null))
    $runDetailRows.Add([object[]]@("Updated UTC", (ConvertTo-OptionalUtcDateTime -Value $Evidence.Summary.UpdatedUtc -Label "run summary updated UTC")))
    Set-ExcelValues -Sheet $Sheet -TopLeft "A8" -Rows @($runDetailRows) -ColumnCount 2
    $Sheet.Range("B14").Formula = "=COUNTA('Measurements'!`$A`$2:`$A`$$MeasurementLastRow)"
    $Sheet.Range("A8:A15").Interior.Color = $script:ExcelColorPaleBlue
    $Sheet.Range("A8:A15").Font.Bold = $true
    $Sheet.Range("A8:A15").Font.Color = $script:ExcelColorNavy
    $Sheet.Range("B12:B13").NumberFormat = "0.0%"
    $Sheet.Range("B15").NumberFormat = "yyyy-mm-dd hh:mm:ss"

    $Sheet.Range("D8:H8").Merge()
    $Sheet.Range("D8").Value2 = "How to read this workbook"
    $Sheet.Range("D8:H8").Interior.Color = $script:ExcelColorNavy
    $Sheet.Range("D8:H8").Font.Bold = $true
    $Sheet.Range("D8:H8").Font.Color = $script:ExcelColorWhite
    $Sheet.Range("D9:H15").Merge()
    $Sheet.Range("D9").Value2 = "Each row starts from an original garment photo. Lens photos correct optical distortion; checkerboard photos convert pixels into real-world distances. The current remote model finds K1-K17, then the current mobile client and size calculator produces the measurements and PASS/FAIL verdict. Historical JSON is comparison evidence only. Signed time deltas are photo time minus evidence time."
    $Sheet.Range("D9:H15").Interior.Color = $script:ExcelColorPaleBlue
    $Sheet.Range("D9:H15").WrapText = $true
    $Sheet.Range("D9:H15").VerticalAlignment = -4160

    $evidenceRows = [System.Collections.Generic.List[object]]::new()
    $evidenceRows.Add([object[]]@("Evidence", "File URI"))
    $evidenceRows.Add([object[]]@("Plain-language README", $null))
    $evidenceRows.Add([object[]]@("Annotated images", $null))
    $evidenceRows.Add([object[]]@("Run summary", $null))
    $evidenceRows.Add([object[]]@("Timing and selection evidence", $null))
    $evidenceRows.Add([object[]]@("Calibration result", $null))
    Set-ExcelValues -Sheet $Sheet -TopLeft "A17" -Rows @($evidenceRows) -ColumnCount 2
    $Sheet.Range("A17:B17").Interior.Color = $script:ExcelColorNavy
    $Sheet.Range("A17:B17").Font.Bold = $true
    $Sheet.Range("A17:B17").Font.Color = $script:ExcelColorWhite
    $evidenceLinks = @(
        (Join-Path $Evidence.RunDirectory "README.md"),
        (Join-Path $Evidence.RunDirectory "overlays"),
        (Join-Path $Evidence.RunDirectory "run-summary.json"),
        (Join-Path $Evidence.RunDirectory "plan-evidence.md"),
        (Join-Path $Evidence.RunDirectory "calibration.json")
    )
    for ($index = 0; $index -lt $evidenceLinks.Count; $index++) {
        Add-FileHyperlink -Sheet $Sheet -CellAddress "B$($index + 18)" -Path $evidenceLinks[$index]
    }

    $Sheet.Range("D17:H17").Merge()
    $Sheet.Range("D17").Value2 = "Timing warning"
    $Sheet.Range("D17:H17").Interior.Color = $script:ExcelColorAmber
    $Sheet.Range("D17:H17").Font.Bold = $true
    $Sheet.Range("D17:H17").Font.Color = $script:ExcelColorWhite
    $Sheet.Range("D18:H22").Merge()
    $Sheet.Range("D18").Value2 = [string] $Evidence.Summary.AssociationWarning
    $Sheet.Range("D18:H22").Interior.Color = $script:ExcelColorPaleAmber
    $Sheet.Range("D18:H22").WrapText = $true
    $Sheet.Range("D18:H22").VerticalAlignment = -4160

    foreach ($columnWidth in @{
            A = 24; B = 38; C = 4; D = 24; E = 16; F = 16; G = 16; H = 16
        }.GetEnumerator()) {
        $Sheet.Columns.Item([string] $columnWidth.Key).ColumnWidth = [double] $columnWidth.Value
    }
    $Sheet.Activate()
    $window = $Sheet.Application.ActiveWindow
    $window.DisplayGridlines = $false
    $window.SplitRow = 3
    $window.FreezePanes = $true
    Release-ComReference -Reference $window
}

# Adds and names the exact five worksheets required by the evidence contract.
function New-EvidenceWorksheets {
    param([Parameter(Mandatory = $true)][object] $Workbook)

    while ($Workbook.Worksheets.Count -gt 1) {
        $Workbook.Worksheets.Item($Workbook.Worksheets.Count).Delete()
    }
    $overview = $Workbook.Worksheets.Item(1)
    $overview.Name = "Overview"
    $images = $Workbook.Worksheets.Add([type]::Missing, $Workbook.Worksheets.Item($Workbook.Worksheets.Count))
    $images.Name = "Images"
    $measurements = $Workbook.Worksheets.Add([type]::Missing, $Workbook.Worksheets.Item($Workbook.Worksheets.Count))
    $measurements.Name = "Measurements"
    $calibration = $Workbook.Worksheets.Add([type]::Missing, $Workbook.Worksheets.Item($Workbook.Worksheets.Count))
    $calibration.Name = "Calibration Frames"
    $sources = $Workbook.Worksheets.Add([type]::Missing, $Workbook.Worksheets.Item($Workbook.Worksheets.Count))
    $sources.Name = "Sources & Timing"
    return [pscustomobject]@{
        Overview = $overview
        Images = $images
        Measurements = $measurements
        Calibration = $calibration
        Sources = $sources
    }
}

# Reopens the saved file in Excel and verifies sheets, formulas, and key counts.
function Test-SavedWorkbookWithExcel {
    param(
        [Parameter(Mandatory = $true)][object] $Excel,
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][object] $Evidence
    )

    $workbook = $null
    try {
        $workbook = $Excel.Workbooks.Open($Path, 0, $true)
        $expectedNames = @("Overview", "Images", "Measurements", "Calibration Frames", "Sources & Timing")
        $actualNames = @($workbook.Worksheets | ForEach-Object { [string] $_.Name })
        if ($actualNames.Count -ne $expectedNames.Count -or (Compare-Object $expectedNames $actualNames)) {
            throw "Saved workbook does not contain the exact required worksheets."
        }
        $overview = $workbook.Worksheets.Item("Overview")
        $matchedCount = @($Evidence.Items | Where-Object { $null -ne $_.HistoricalResultPath }).Count
        $passCount = @($Evidence.Items | Where-Object { $_.Analysis.overallVerdict -ceq "PASS" }).Count
        $failCount = @($Evidence.Items | Where-Object { $_.Analysis.overallVerdict -ceq "FAIL" }).Count
        $measurementCount = [int] (@($Evidence.Items | ForEach-Object { @($_.Analysis.measurements).Count } | Measure-Object -Sum).Sum)
        $formulaChecks = [ordered]@{
            B6 = $Evidence.Items.Count
            D6 = $matchedCount
            F6 = $passCount
            H6 = $failCount
            B14 = $measurementCount
        }
        foreach ($cellAddress in $formulaChecks.Keys) {
            $cell = $overview.Range($cellAddress)
            try {
                if (-not $cell.HasFormula -or [string] $cell.Formula -clike "*HYPERLINK*") {
                    throw "Saved workbook has an invalid overview formula in $cellAddress."
                }
                $actualValue = [int] $cell.Value2
                if ($actualValue -ne [int] $formulaChecks[$cellAddress]) {
                    throw "Saved workbook formula $cellAddress returned $actualValue instead of $($formulaChecks[$cellAddress])."
                }
            } finally {
                Release-ComReference -Reference $cell
            }
        }
        Release-ComReference -Reference $overview
    } finally {
        if ($null -ne $workbook) {
            $workbook.Close($false)
            Release-ComReference -Reference $workbook
        }
    }
}

# Creates, saves, reopens, and atomically publishes one native Excel workbook.
function Export-EvidenceWorkbook {
    param([Parameter(Mandatory = $true)][object] $Evidence)

    $excel = $null
    $workbook = $null
    $worksheets = $null
    $temporaryPath = Join-Path $Evidence.RunDirectory (".{0}.tmp.xlsx" -f [guid]::NewGuid().ToString("N"))
    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = $false
        $excel.DisplayAlerts = $false
        $excel.ScreenUpdating = $false
        $excel.EnableEvents = $false
        $workbook = $excel.Workbooks.Add()
        $worksheets = New-EvidenceWorksheets -Workbook $workbook
        $imageLastRow = Add-ImagesSheetContent -Sheet $worksheets.Images -Evidence $Evidence
        $measurementLastRow = Add-MeasurementsSheetContent -Sheet $worksheets.Measurements -Evidence $Evidence
        $null = Add-CalibrationSheetContent -Sheet $worksheets.Calibration -Evidence $Evidence
        $null = Add-SourcesSheetContent -Sheet $worksheets.Sources -Evidence $Evidence
        Add-OverviewSheetContent -Sheet $worksheets.Overview -Evidence $Evidence -ImageLastRow $imageLastRow -MeasurementLastRow $measurementLastRow
        $worksheets.Overview.Activate()
        $workbook.SaveAs($temporaryPath, $script:ExcelFileFormatOpenXmlWorkbook)
        $workbook.Close($false)
        Release-ComReference -Reference $workbook
        $workbook = $null
        Test-SavedWorkbookWithExcel -Excel $excel -Path $temporaryPath -Evidence $Evidence
        Move-Item -LiteralPath $temporaryPath -Destination $Evidence.OutputPath -Force
    } finally {
        if ($null -ne $workbook) {
            $workbook.Close($false)
            Release-ComReference -Reference $workbook
        }
        if ($null -ne $worksheets) {
            foreach ($sheet in $worksheets.PSObject.Properties.Value) {
                Release-ComReference -Reference $sheet
            }
        }
        if ($null -ne $excel) {
            $excel.Quit()
            Release-ComReference -Reference $excel
        }
        [gc]::Collect()
        [gc]::WaitForPendingFinalizers()
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

Assert-ExcelAvailable
if ($Preflight) {
    Write-Output "QSee session replay Microsoft Excel runtime: OK"
    return
}
if ([string]::IsNullOrWhiteSpace($RunDirectory) -or [string]::IsNullOrWhiteSpace($Output)) {
    throw "-RunDirectory and -Output are required unless -Preflight is used."
}
$resolvedRunDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
$resolvedOutputPath = [System.IO.Path]::GetFullPath($Output)
if (-not (Test-Path -LiteralPath $resolvedRunDirectory -PathType Container)) {
    throw "Run directory does not exist: $resolvedRunDirectory"
}
if ([System.IO.Path]::GetDirectoryName($resolvedOutputPath) -cne $resolvedRunDirectory) {
    throw "The evidence workbook must be written directly in the run root."
}
if ([System.IO.Path]::GetExtension($resolvedOutputPath) -cne ".xlsx") {
    throw "The evidence workbook output must use the .xlsx extension."
}
$evidence = Read-RunEvidence -ResolvedRunDirectory $resolvedRunDirectory -ResolvedOutputPath $resolvedOutputPath
Export-EvidenceWorkbook -Evidence $evidence
Write-Output "Evidence workbook: $resolvedOutputPath"
