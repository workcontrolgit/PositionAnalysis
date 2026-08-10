# Export-EvalToExcel.ps1
# Exports all PD-*.json files from a staging folder to a color-coded .xlsx
# for HR staff review. Requires the ImportExcel PowerShell module (no Excel needed).
#
# Usage:
#   pwsh -File Export-EvalToExcel.ps1 -DataFolder '../reports/schedule-pc/data-json' `
#                                      -OutputFolder '../reports/schedule-pc/tracker-excel'

param(
    [Parameter(Mandatory)] [string] $DataFolder,
    [Parameter(Mandatory)] [string] $OutputFolder,
    [string] $Date   # optional override for filename date (default: today)
)

if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    Write-Error "ImportExcel module not found. Install with: Install-Module ImportExcel -Scope CurrentUser"
    exit 1
}
Import-Module ImportExcel

$dateStr     = if ($Date) { $Date } else { (Get-Date).ToString('yyyy-MM-dd') }
$outputPath  = Join-Path $OutputFolder "SchedulePC-Eval-Tracker-$dateStr.xlsx"

if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
}

$jsonFiles = Get-ChildItem -Path $DataFolder -Filter 'PD-*.json' | Sort-Object Name
if ($jsonFiles.Count -eq 0) {
    Write-Error "No PD-*.json files found in: $DataFolder"
    exit 1
}

# Build row objects sorted by PayPlan ASC, Grade DESC, PdNbr ASC
$rows = $jsonFiles | ForEach-Object {
    $pd = Get-Content $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json

    $criteriaCount = (@($pd.Criteria) | Where-Object { [bool]$_.Triggered } | Measure-Object).Count

    $titleSlug   = $pd.PositionTitle -replace '[^a-zA-Z0-9 -]','' -replace '\s+','-'
    $wordFile    = "PD-$($pd.PdNbr)_${titleSlug}_$($pd.PayPlan)-$($pd.OccSeries)-$($pd.Grade).docx"

    [PSCustomObject]@{
        'PD Number'                        = [string]$pd.PdNbr
        'Position Title'                   = [string]$pd.PositionTitle
        'Org Code'                         = [string]$pd.OrgCode
        'Pay Plan'                         = [string]$pd.PayPlan
        'Series'                           = [string]$pd.OccSeries
        'Grade'                            = [string]$pd.Grade
        'Manager Level'                    = [string]$pd.PdManagerLevel
        'Position Sensitivity'             = [string]$pd.PositionSensitivity
        'Public Trust'                     = [string]$pd.PublicTrust
        'Service Category'                 = [string]$pd.ServiceCategory
        'Is Candidate'                     = [string]$pd.IsCandidate
        'Rating'                           = [string]$pd.Rating
        'Criteria Met Count'               = $criteriaCount
        'Policy-Determining'               = [string]$pd.Criteria[0].Triggered
        'Policy-Determining Evidence'      = [string]$pd.Criteria[0].Evidence
        'Policy-Making'                    = [string]$pd.Criteria[1].Triggered
        'Policy-Making Evidence'           = [string]$pd.Criteria[1].Evidence
        'Policy-Advocating'                = [string]$pd.Criteria[2].Triggered
        'Policy-Advocating Evidence'       = [string]$pd.Criteria[2].Evidence
        'Confidential'                     = [string]$pd.Criteria[3].Triggered
        'Confidential Evidence'            = [string]$pd.Criteria[3].Evidence
        'Justification Summary'            = [string]$pd.JustificationSummary
        'Eval Date'                        = [string]$pd.EvalDate
        'Word Form Filename'               = $wordFile
        # hidden sort keys — stripped before export
        _PayPlan = [string]$pd.PayPlan
        _Grade   = [int]([string]$pd.Grade -replace '[^0-9]','0')
        _PdNbr   = [string]$pd.PdNbr
    }
} | Sort-Object _PayPlan, @{ Expression = '_Grade'; Descending = $true }, _PdNbr

# Drop sort key columns before writing
$exportRows = $rows | Select-Object -Property * -ExcludeProperty _PayPlan, _Grade, _PdNbr

# Remove existing file so ImportExcel doesn't append to stale sheet
if (Test-Path $outputPath) { Remove-Item $outputPath -Force }

# Write the xlsx
$excel = $exportRows | Export-Excel -Path $outputPath `
    -WorksheetName 'Evaluation Results' `
    -AutoSize `
    -FreezeTopRow `
    -BoldTopRow `
    -PassThru

# Color-code the Rating column (column 12, L)
$ws       = $excel.Workbook.Worksheets['Evaluation Results']
$dataRows = $ws.Dimension.Rows  # includes header

for ($r = 2; $r -le $dataRows; $r++) {
    $ratingVal = $ws.Cells[$r, 12].Text
    $bg   = switch ($ratingVal) {
        'HIGH'   { [System.Drawing.ColorTranslator]::FromHtml('#E2F0D9') }
        'MEDIUM' { [System.Drawing.ColorTranslator]::FromHtml('#FFF2CC') }
        default  { [System.Drawing.ColorTranslator]::FromHtml('#FCE4D6') }
    }
    $fg   = switch ($ratingVal) {
        'HIGH'   { [System.Drawing.ColorTranslator]::FromHtml('#1E4620') }
        'MEDIUM' { [System.Drawing.ColorTranslator]::FromHtml('#5C4300') }
        default  { [System.Drawing.ColorTranslator]::FromHtml('#801414') }
    }
    $ws.Cells[$r, 12].Style.Fill.PatternType = [OfficeOpenXml.Style.ExcelFillStyle]::Solid
    $ws.Cells[$r, 12].Style.Fill.BackgroundColor.SetColor($bg)
    $ws.Cells[$r, 12].Style.Font.Color.SetColor($fg)
}

Close-ExcelPackage $excel

Write-Output "Excel exported: $(Split-Path $outputPath -Leaf)"
Write-Output "Path: $outputPath"
