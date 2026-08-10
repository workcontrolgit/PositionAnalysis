# Fill-EvalTemplate-Batch-v2.ps1
# Batch fill script for Schedule_PC_Position_Evaluation_Template_Updated.docx.
# The v2 template has no content control IDs; all SDTs are targeted by ordinal
# position (document order).  Criteria names match v2 labels.
#
# SDT ordinal map (all SDTs in document order, 0-based):
#   [0]  PD Number (Section 1)
#   [1]  Effective Date (Section 1)
#   [2]  Position Title (Section 1)
#   [3]  Schedule PC Rating (Section 1, color-filled)
#   [4]  Agency/Subcomponent
#   [5]  Pay Plan / Series / Grade
#   [6]  Service Category (text — long name)
#   [7]  Position Purpose (Section 1)
#   [8]  [CB] Policy-Determining — Yes
#   [9]  [CB] Policy-Determining — No
#   [10] Policy-Determining Evidence
#   [11] [CB] Policy-Making — Yes
#   [12] [CB] Policy-Making — No
#   [13] Policy-Making Evidence
#   [14] [CB] Policy-Advocating — Yes
#   [15] [CB] Policy-Advocating — No
#   [16] Policy-Advocating Evidence
#   [17] [CB] Confidential — Yes
#   [18] [CB] Confidential — No
#   [19] Confidential Evidence
#   [20] (reserved — template field, not populated by this script)
#   [21] (reserved — template field, not populated by this script)
#   [22] [CB] Final Determination — Convert to Schedule P/C
#   [23] [CB] Final Determination — Retain in Current Schedule
#   [24] Schedule PC Rating (Section 3, color-filled)
#   [25] Justification Summary
#   [26] Evaluator Name & Date
#   [27] Agency Head Approving Official
#   [28] Appendix A — PD Number
#   [29] Appendix A — Position Title
#   [30] Appendix A — Agency/Subcomponent
#   [31] Appendix A — Organization Code
#   [32] Appendix A — Pay Plan
#   [33] Appendix A — Job Series
#   [34] Appendix A — Grade
#   [35] Appendix A — Effective Date
#   [36] Appendix A — PD Manager Level
#   [37] Appendix A — Position Sensitivity
#   [38] Appendix A — Public Trust
#   [39] Appendix A — Service Category
#   [40] Appendix B template row — Duty (Verbatim)
#   [41] Appendix B template row — Justification
#
# Usage:
#   pwsh -File Fill-EvalTemplate-Batch-v2.ps1 -DataFolder 'C:/tmp/eval-json' -OutputFolder '../reports/schedule-pc/form-word'

param(
    [Parameter(Mandatory)] [string] $DataFolder,
    [Parameter(Mandatory)] [string] $OutputFolder,
    [string[]] $PdNbr
)

Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
Add-Type -AssemblyName 'System.IO.Compression'

# Repo root is the parent of this script's folder — resolves regardless of clone location.
$repoRoot     = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repoRoot 'templates/Schedule_PC_Position_Evaluation_Template_Updated.docx'
$wNs   = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
$w14Ns = "http://schemas.microsoft.com/office/word/2010/wordml"

# ---------------------------------------------------------------------------
# Helper: set text in SDT by ordinal index into $allSdts
# ---------------------------------------------------------------------------
function Set-SdtText {
    param(
        [System.Xml.XmlNode[]] $allSdts,
        [System.Xml.XmlNamespaceManager] $ns,
        [int]    $index,
        [string] $value
    )
    if ($index -ge $allSdts.Count) { return }
    $sdt = $allSdts[$index]
    if (-not $sdt) { return }
    $plcHdr = $sdt.SelectSingleNode("w:sdtPr/w:showingPlcHdr", $ns)
    if ($plcHdr) { $plcHdr.ParentNode.RemoveChild($plcHdr) | Out-Null }
    $tNode = $sdt.SelectSingleNode("w:sdtContent//w:t", $ns)
    if ($tNode) {
        $tNode.InnerText = $value
        $rPr = $sdt.SelectSingleNode("w:sdtContent//w:rPr", $ns)
        if ($rPr) {
            $i     = $rPr.SelectSingleNode("w:i",     $ns)
            $color = $rPr.SelectSingleNode("w:color", $ns)
            if ($i)     { $rPr.RemoveChild($i)     | Out-Null }
            if ($color) { $rPr.RemoveChild($color) | Out-Null }
        }
    }
}

# ---------------------------------------------------------------------------
# Helper: set checkbox by ordinal index into $allSdts
# ---------------------------------------------------------------------------
function Set-Checkbox {
    param(
        [System.Xml.XmlNode[]] $allSdts,
        [System.Xml.XmlNamespaceManager] $ns,
        [string] $w14NsUri,
        [string] $wNsUri,
        [int]    $index,
        [bool]   $checked
    )
    if ($index -ge $allSdts.Count) { return }
    $sdt = $allSdts[$index]
    if (-not $sdt) { return }
    $checkedNode = $sdt.SelectSingleNode("w:sdtPr/w14:checkbox/w14:checked", $ns)
    if ($checkedNode) {
        $val = if ($checked) { "1" } else { "0" }
        $checkedNode.SetAttribute("val", $w14NsUri, $val) | Out-Null
    }
    # Template uses <w:t> with MS Gothic ☒/☐ — no <w:sym> node exists
    $tNode = $sdt.SelectSingleNode("w:sdtContent//w:t", $ns)
    if ($tNode) {
        $tNode.InnerText = if ($checked) { [char]0x2612 } else { [char]0x2610 }
    }
}

# ---------------------------------------------------------------------------
# Helper: apply background + foreground color to a rating SDT table cell
# ---------------------------------------------------------------------------
function Set-RatingCell {
    param(
        [System.Xml.XmlNode[]] $allSdts,
        [System.Xml.XmlNamespaceManager] $ns,
        [System.Xml.XmlDocument] $doc,
        [string] $wNsUri,
        [int]    $index,
        [string] $label,
        [string] $bgColor,
        [string] $fgColor
    )
    if ($index -ge $allSdts.Count) { return }
    $sdt = $allSdts[$index]
    if (-not $sdt) { return }

    # Background on parent <tc>
    $tc = $sdt
    while ($null -ne $tc -and $tc.LocalName -ne "tc") { $tc = $tc.ParentNode }
    if ($null -ne $tc) {
        $shd = $tc.SelectSingleNode("w:tcPr/w:shd", $ns)
        if ($shd) { $shd.SetAttribute("fill", $wNsUri, $bgColor) | Out-Null }
    }

    # Foreground on the run inside the SDT
    $run = $sdt.SelectSingleNode("w:sdtContent//w:r", $ns)
    if ($run -and $run.LocalName -eq "r") {
        $rPr = $run.SelectSingleNode("w:rPr", $ns)
        if (-not $rPr) {
            $rPr = $doc.CreateElement("w", "rPr", $wNsUri)
            $run.PrependChild($rPr) | Out-Null
        }
        $colorNode = $rPr.SelectSingleNode("w:color", $ns)
        if (-not $colorNode) {
            $colorNode = $doc.CreateElement("w", "color", $wNsUri)
            $rPr.AppendChild($colorNode) | Out-Null
        }
        $colorNode.SetAttribute("val", $wNsUri, $fgColor) | Out-Null
    }

    # Set text (strips italic/gray placeholder)
    $plcHdr = $sdt.SelectSingleNode("w:sdtPr/w:showingPlcHdr", $ns)
    if ($plcHdr) { $plcHdr.ParentNode.RemoveChild($plcHdr) | Out-Null }
    $tNode = $sdt.SelectSingleNode("w:sdtContent//w:t", $ns)
    if ($tNode) { $tNode.InnerText = $label }
}

# ---------------------------------------------------------------------------
# Helper: set text on a specific SDT node (used by Appendix B row cloning)
# ---------------------------------------------------------------------------
function Set-SdtNodeText {
    param(
        [System.Xml.XmlNode] $sdt,
        [System.Xml.XmlNamespaceManager] $ns,
        [string] $value
    )
    $plcHdr = $sdt.SelectSingleNode("w:sdtPr/w:showingPlcHdr", $ns)
    if ($plcHdr) { $plcHdr.ParentNode.RemoveChild($plcHdr) | Out-Null }
    $tNode = $sdt.SelectSingleNode("w:sdtContent//w:t", $ns)
    if ($tNode) {
        $tNode.InnerText = $value
        $rPr = $sdt.SelectSingleNode("w:sdtContent//w:rPr", $ns)
        if ($rPr) {
            $i     = $rPr.SelectSingleNode("w:i",     $ns)
            $color = $rPr.SelectSingleNode("w:color", $ns)
            if ($i)     { $rPr.RemoveChild($i)     | Out-Null }
            if ($color) { $rPr.RemoveChild($color) | Out-Null }
        }
    }
}

# ---------------------------------------------------------------------------
# Helper: create a <w:element> node
# ---------------------------------------------------------------------------
function New-WElement {
    param(
        [System.Xml.XmlDocument] $doc,
        [string] $wNsUri,
        [string] $name
    )
    return $doc.CreateElement("w", $name, $wNsUri)
}

# ---------------------------------------------------------------------------
# Fill Appendix A + B
# ---------------------------------------------------------------------------
function Add-Appendix {
    param(
        [System.Xml.XmlNode[]] $allSdts,
        [System.Xml.XmlNamespaceManager] $ns,
        [System.Xml.XmlDocument] $doc,
        [string] $wNsUri,
        [object] $pdObj
    )

    $evalDate = if ($pdObj.PSObject.Properties['EvalDate'] -and [string]$pdObj.EvalDate) {
        [string]$pdObj.EvalDate
    } else { "(not provided)" }

    $orgValue = if ($pdObj.PSObject.Properties['OrgCode'] -and [string]$pdObj.OrgCode) {
        [string]$pdObj.OrgCode
    } else { "(not provided)" }

    $pdManagerLevel = if ($pdObj.PSObject.Properties['PdManagerLevel'] -and [string]$pdObj.PdManagerLevel) {
        [string]$pdObj.PdManagerLevel
    } else { "(not provided)" }

    $positionSensitivity = if ($pdObj.PSObject.Properties['PositionSensitivity'] -and [string]$pdObj.PositionSensitivity) {
        [string]$pdObj.PositionSensitivity
    } else { "(not provided)" }

    $publicTrust = if ($pdObj.PSObject.Properties['PublicTrust'] -and [string]$pdObj.PublicTrust) {
        [string]$pdObj.PublicTrust
    } else { "(not provided)" }

    $serviceCategory = if ($pdObj.PSObject.Properties['ServiceCategory'] -and [string]$pdObj.ServiceCategory) {
        [string]$pdObj.ServiceCategory
    } else { "(not provided)" }

    # Appendix A (indices 28-39)
    Set-SdtText $allSdts $ns 28 ([string]$pdObj.PdNbr)
    Set-SdtText $allSdts $ns 29 ([string]$pdObj.PositionTitle)
    Set-SdtText $allSdts $ns 30 $orgValue
    Set-SdtText $allSdts $ns 31 $orgValue
    Set-SdtText $allSdts $ns 32 ([string]$pdObj.PayPlan)
    Set-SdtText $allSdts $ns 33 ([string]$pdObj.OccSeries)
    Set-SdtText $allSdts $ns 34 ([string]$pdObj.Grade)
    Set-SdtText $allSdts $ns 35 $evalDate
    Set-SdtText $allSdts $ns 36 $pdManagerLevel
    Set-SdtText $allSdts $ns 37 $positionSensitivity
    Set-SdtText $allSdts $ns 38 $publicTrust
    Set-SdtText $allSdts $ns 39 $serviceCategory

    # Appendix B — duty rows (template SDTs at indices 40/41)
    $duties = @()
    if ($pdObj.PSObject.Properties['Duties'] -and $null -ne $pdObj.Duties) {
        $duties = @($pdObj.Duties)
    }

    $dutyTemplateSdt     = $allSdts[40]
    $evidenceTemplateSdt = $allSdts[41]

    $templateRow = $dutyTemplateSdt
    while ($templateRow -and $templateRow.LocalName -ne "tr") {
        $templateRow = $templateRow.ParentNode
    }

    if ((-not $templateRow) -or (-not $evidenceTemplateSdt)) { return }

    if ($duties.Count -eq 0) {
        Set-SdtNodeText $dutyTemplateSdt     $ns "No duty rows were provided for this evaluation."
        Set-SdtNodeText $evidenceTemplateSdt $ns "(no supporting duty metadata available)"
        return
    }

    foreach ($d in $duties) {
        $seq = if ($d.PSObject.Properties['SeqNum'])   { [string]$d.SeqNum }   else { "" }
        $txt = if ($d.PSObject.Properties['DutyText']) { [string]$d.DutyText } else { "" }
        if ([string]::IsNullOrWhiteSpace($txt)) { $txt = "(duty text not provided)" }

        $meets = $false
        if ($d.PSObject.Properties['MeetsCriteria']) {
            $meets = [bool]$d.MeetsCriteria
        } elseif (($d.PSObject.Properties['MatchedCriteria']) -and ($null -ne $d.MatchedCriteria)) {
            $meets = (@($d.MatchedCriteria).Count -gt 0)
        }

        $label    = if ([string]::IsNullOrWhiteSpace($seq)) { "Duty" } else { "Duty #$seq" }
        $dutyLine = "${label}: $txt"

        $meta = "No direct support finding."
        if (($d.PSObject.Properties['MatchedCriteria']) -and ($null -ne $d.MatchedCriteria) -and (@($d.MatchedCriteria).Count -gt 0)) {
            $supports = (@($d.MatchedCriteria) -join ", ")
            $meta = "Supports: $supports"
        } elseif ($meets) {
            $meta = "Supports: Yes"
        }

        $row            = $templateRow.CloneNode($true)
        $rowDutySdt     = $row.SelectSingleNode(".//w:sdt[1]", $ns)
        $rowEvidenceSdt = $row.SelectSingleNode(".//w:sdt[2]", $ns)

        if ($rowDutySdt)     { Set-SdtNodeText $rowDutySdt     $ns $dutyLine }
        if ($rowEvidenceSdt) { Set-SdtNodeText $rowEvidenceSdt $ns $meta }

        if (($meets) -and ($rowDutySdt)) {
            $run = $rowDutySdt.SelectSingleNode("w:sdtContent//w:r", $ns)
            if (($run) -and ($run.LocalName -eq "r")) {
                $rPr = $run.SelectSingleNode("w:rPr", $ns)
                if (-not $rPr) {
                    $rPr = New-WElement $doc $wNsUri "rPr"
                    $run.PrependChild($rPr) | Out-Null
                }
                $h = $rPr.SelectSingleNode("w:highlight", $ns)
                if (-not $h) {
                    $h = New-WElement $doc $wNsUri "highlight"
                    $rPr.AppendChild($h) | Out-Null
                }
                $h.SetAttribute("val", $wNsUri, "yellow") | Out-Null
            }
        }

        $templateRow.ParentNode.InsertBefore($row, $templateRow) | Out-Null
    }

    $templateRow.ParentNode.RemoveChild($templateRow) | Out-Null
}

# ===========================================================================
# Main
# ===========================================================================

if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
}

$templateBytes = [System.IO.File]::ReadAllBytes($templatePath)

$jsonFiles = Get-ChildItem -Path $DataFolder -Filter "*.json" | Sort-Object Name
if (($PdNbr) -and ($PdNbr.Count -gt 0)) {
    $jsonFiles = $jsonFiles | Where-Object { $PdNbr -contains ($_.BaseName -replace '^PD-', '') }
    Write-Output "PdNbr filter applied: processing $($jsonFiles.Count) file(s) - $($PdNbr -join ', ')"
}

$total      = $jsonFiles.Count
$done       = 0
$yes        = 0
$borderline = 0
$no         = 0

foreach ($file in $jsonFiles) {
    $pd = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json

    # Build output path
    $titleSlug  = $pd.PositionTitle -replace '[^a-zA-Z0-9 -]','' -replace '\s+','-'
    $fileName   = "PD-$($pd.PdNbr)_${titleSlug}_$($pd.PayPlan)-$($pd.OccSeries)-$($pd.Grade).docx"
    $outputPath = Join-Path $OutputFolder $fileName

    # Extract template into temp dir
    $tempDir   = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
    $memStream = New-Object System.IO.MemoryStream(,$templateBytes)
    $zip       = [System.IO.Compression.ZipArchive]::new($memStream, [System.IO.Compression.ZipArchiveMode]::Read)
    [System.IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip, $tempDir)
    $zip.Dispose()
    $memStream.Dispose()

    $docPath = Join-Path $tempDir "word/document.xml"
    $xmlDoc  = New-Object System.Xml.XmlDocument
    $xmlDoc.Load($docPath)

    $nsm = New-Object System.Xml.XmlNamespaceManager($xmlDoc.NameTable)
    $nsm.AddNamespace("w",   $wNs)
    $nsm.AddNamespace("w14", $w14Ns)

    # Collect all SDTs once in document order
    $allSdts = @($xmlDoc.SelectNodes("//w:sdt", $nsm))

    # Rating colors (508-compliant, WCAG AA)
    $ratingText = if ($pd.Rating) { [string]$pd.Rating } else { "LOW" }
    if ($ratingText -eq "HIGH") {
        $ratingBg = "E2F0D9"
        $ratingFg = "1E4620"
    } elseif ($ratingText -eq "MEDIUM") {
        $ratingBg = "FFF2CC"
        $ratingFg = "5C4300"
    } elseif ($ratingText -eq "BORDERLINE") {
        $ratingBg = "FCE8B2"
        $ratingFg = "7B4F00"
    } else {
        $ratingBg = "FCE4D6"
        $ratingFg = "801414"
    }
    $triggeredCount = ($pd.Criteria | Where-Object { [bool]$_.Triggered } | Measure-Object).Count
    $ratingLabel    = "$ratingText -- $triggeredCount of 4 criteria met"

    # Section 1
    Set-SdtText $allSdts $nsm 0 ([string]$pd.PdNbr)
    Set-SdtText $allSdts $nsm 1 ([string]$pd.EvalDate)
    Set-SdtText $allSdts $nsm 2 ([string]$pd.PositionTitle)
    Set-RatingCell $allSdts $nsm $xmlDoc $wNs 3 $ratingLabel $ratingBg $ratingFg
    Set-SdtText $allSdts $nsm 4 ([string]$pd.OrgCode)
    Set-SdtText $allSdts $nsm 5 "$($pd.PayPlan)-$($pd.OccSeries)-$($pd.Grade)"

    # Service Category — plain text long name
    $serviceCategory = if ($pd.PSObject.Properties['ServiceCategory'] -and [string]$pd.ServiceCategory) {
        [string]$pd.ServiceCategory
    } else { "Competitive" }
    Set-SdtText $allSdts $nsm 6 $serviceCategory

    # Position Purpose (index 7)
    $positionPurpose = if ($pd.PSObject.Properties['PositionPurpose'] -and [string]$pd.PositionPurpose) {
        [string]$pd.PositionPurpose
    } else { "" }
    Set-SdtText $allSdts $nsm 7 $positionPurpose

    # Section 2: 4 criteria, each = Yes CB / No CB / Evidence text at indices 8-19
    for ($i = 0; $i -lt 4; $i++) {
        $c         = $pd.Criteria[$i]
        $triggered = [bool]$c.Triggered
        Set-Checkbox $allSdts $nsm $w14Ns $wNs ($i * 3 + 8)  $triggered
        Set-Checkbox $allSdts $nsm $w14Ns $wNs ($i * 3 + 9)  (-not $triggered)
        Set-SdtText  $allSdts $nsm ($i * 3 + 10) ([string]$c.Evidence)
    }

    # Section 3
    $isBorderline = ($pd.IsCandidate -eq "BORDERLINE")
    $convert = ($pd.IsCandidate -eq "YES")
    Set-Checkbox $allSdts $nsm $w14Ns $wNs 22 $convert
    Set-Checkbox $allSdts $nsm $w14Ns $wNs 23 ((-not $convert) -and (-not $isBorderline))
    Set-RatingCell $allSdts $nsm $xmlDoc $wNs 24 $ratingLabel $ratingBg $ratingFg
    Set-SdtText $allSdts $nsm 25 ([string]$pd.JustificationSummary)
    Set-SdtText $allSdts $nsm 26 "AI Agent (claude-sonnet-4-6) / $($pd.EvalDate)"
    Set-SdtText $allSdts $nsm 27 "(Pending Human Review)"

    # Appendix A + B
    Add-Appendix $allSdts $nsm $xmlDoc $wNs $pd

    # Save
    $xmlDoc.Save($docPath)
    if (Test-Path $outputPath) { Remove-Item $outputPath -Force }
    $zipStream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $files = Get-ChildItem -Path $tempDir -Recurse -File
            foreach ($f in $files) {
                $relative  = $f.FullName.Substring($tempDir.Length).TrimStart([char]'\', [char]'/')
                $entryName = $relative.Replace('\', '/')
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $entryName) | Out-Null
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $zipStream.Dispose()
    }
    Remove-Item -Recurse -Force $tempDir

    $done++
    if ($pd.IsCandidate -eq "YES") { $yes++ } elseif ($pd.IsCandidate -eq "BORDERLINE") { $borderline++ } else { $no++ }
    Write-Output "[$done/$total] Written: $fileName [IS_CANDIDATE: $($pd.IsCandidate)]"
}

Write-Output ""
Write-Output "Batch complete: $done files written (YES: $yes  BORDERLINE: $borderline  NO: $no)"
