# Fill-EvalTemplate.ps1
# Fills Schedule_PC_Position_Evaluation_Template.docx with one PD evaluation.
# Copies the template, replaces content controls (text) and checkboxes via XML.
#
# Usage:
#   pwsh -File Fill-EvalTemplate.ps1 -Data '<json>' -OutputPath 'path/to/output.docx'
#
# JSON schema for -Data:  see SKILL.md (schedulepc-scan) for full spec.

param(
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $Data,
    [string] $DataFile
)

Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
Add-Type -AssemblyName 'System.IO.Compression'

if ($DataFile) {
    $pd = Get-Content $DataFile -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    $pd = $Data | ConvertFrom-Json
}
# Repo root is the parent of this script's folder — resolves regardless of clone location.
$repoRoot     = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repoRoot 'templates/Schedule_PC_Position_Evaluation_Template.docx'
$tempDir      = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())

# --- Extract template ---
[System.IO.Compression.ZipFile]::ExtractToDirectory($templatePath, $tempDir)

$docPath = Join-Path $tempDir "word/document.xml"
$xmlDoc  = New-Object System.Xml.XmlDocument
$xmlDoc.Load($docPath)

$nsm = New-Object System.Xml.XmlNamespaceManager($xmlDoc.NameTable)
$nsm.AddNamespace("w",   "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
$nsm.AddNamespace("w14", "http://schemas.microsoft.com/office/word/2010/wordml")
$wNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"

function Get-BodyNode {
    param([System.Xml.XmlDocument]$doc, [System.Xml.XmlNamespaceManager]$ns)
    return $doc.SelectSingleNode("/w:document/w:body", $ns)
}

function Add-BodyChild {
    param(
        [System.Xml.XmlNode]$body,
        [System.Xml.XmlNode]$node,
        [System.Xml.XmlNamespaceManager]$ns
    )

    $sectPr = $body.SelectSingleNode("w:sectPr", $ns)
    if ($sectPr) {
        $body.InsertBefore($node, $sectPr) | Out-Null
    } else {
        $body.AppendChild($node) | Out-Null
    }
}

function New-WElement {
    param([System.Xml.XmlDocument]$doc, [string]$name)
    return $doc.CreateElement("w", $name, $wNs)
}

function New-WParagraph {
    param(
        [System.Xml.XmlDocument]$doc,
        [string]$text,
        [bool]$bold = $false,
        [bool]$highlight = $false,
        [bool]$pageBreakBefore = $false
    )

    $p = New-WElement $doc "p"
    $r = New-WElement $doc "r"

    if ($pageBreakBefore) {
        $br = New-WElement $doc "br"
        $br.SetAttribute("type", $wNs, "page") | Out-Null
        $r.AppendChild($br) | Out-Null
    }

    if ($bold -or $highlight) {
        $rPr = New-WElement $doc "rPr"
        if ($bold) {
            $b = New-WElement $doc "b"
            $rPr.AppendChild($b) | Out-Null
        }
        if ($highlight) {
            $h = New-WElement $doc "highlight"
            $h.SetAttribute("val", $wNs, "yellow") | Out-Null
            $rPr.AppendChild($h) | Out-Null
        }
        $r.AppendChild($rPr) | Out-Null
    }

    $lines = @($text -split "`r?`n")
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = New-WElement $doc "t"
        $spaceAttr = $doc.CreateAttribute("xml", "space", "http://www.w3.org/XML/1998/namespace")
        $spaceAttr.Value = "preserve"
        $t.Attributes.Append($spaceAttr) | Out-Null
        $t.InnerText = [string]$lines[$i]
        $r.AppendChild($t) | Out-Null
        if ($i -lt ($lines.Count - 1)) {
            $lineBreak = New-WElement $doc "br"
            $r.AppendChild($lineBreak) | Out-Null
        }
    }

    $p.AppendChild($r) | Out-Null
    return $p
}

function New-WTable {
    param([System.Xml.XmlDocument]$doc, [System.Object[]]$rows)

    $tbl = New-WElement $doc "tbl"
    $tblPr = New-WElement $doc "tblPr"
    $tblW = New-WElement $doc "tblW"
    $tblW.SetAttribute("type", $wNs, "auto") | Out-Null
    $tblW.SetAttribute("w", $wNs, "0") | Out-Null
    $tblPr.AppendChild($tblW) | Out-Null
    $tbl.AppendChild($tblPr) | Out-Null

    foreach ($row in $rows) {
        $tr = New-WElement $doc "tr"
        foreach ($cellText in @([string]$row[0], [string]$row[1])) {
            $tc = New-WElement $doc "tc"
            $tcPr = New-WElement $doc "tcPr"
            $tcW = New-WElement $doc "tcW"
            $tcW.SetAttribute("type", $wNs, "auto") | Out-Null
            $tcW.SetAttribute("w", $wNs, "0") | Out-Null
            $tcPr.AppendChild($tcW) | Out-Null
            $tc.AppendChild($tcPr) | Out-Null
            $tc.AppendChild((New-WParagraph $doc $cellText $false $false $false)) | Out-Null
            $tr.AppendChild($tc) | Out-Null
        }
        $tbl.AppendChild($tr) | Out-Null
    }

    return $tbl
}

function Add-Appendix {
    param([System.Xml.XmlDocument]$doc, [System.Xml.XmlNamespaceManager]$ns, [object]$pdObj)

    $evalDate = if ($pdObj.PSObject.Properties['EvalDate'] -and [string]$pdObj.EvalDate) {
        [string]$pdObj.EvalDate
    } else {
        "(not provided)"
    }

    $orgValue = if ($pdObj.PSObject.Properties['OrgCode'] -and [string]$pdObj.OrgCode) {
        [string]$pdObj.OrgCode
    } else {
        "(not provided)"
    }

    $pdManagerLevel = if ($pdObj.PSObject.Properties['PdManagerLevel'] -and [string]$pdObj.PdManagerLevel) {
        [string]$pdObj.PdManagerLevel
    } else {
        "(not provided)"
    }

    $positionSensitivity = if ($pdObj.PSObject.Properties['PositionSensitivity'] -and [string]$pdObj.PositionSensitivity) {
        [string]$pdObj.PositionSensitivity
    } else {
        "(not provided)"
    }

    $publicTrust = if ($pdObj.PSObject.Properties['PublicTrust'] -and [string]$pdObj.PublicTrust) {
        [string]$pdObj.PublicTrust
    } else {
        "(not provided)"
    }

    $serviceCategory = if ($pdObj.PSObject.Properties['ServiceCategory'] -and [string]$pdObj.ServiceCategory) {
        [string]$pdObj.ServiceCategory
    } else {
        "(not provided)"
    }

    Set-SdtText $doc $ns "100016" $evalDate

    # Color SDT 100017 (Schedule PC Rating in Section 1) — same 508-compliant palette as 100014
    $sec1RatingBg = switch ([string]$pdObj.Rating) {
        "HIGH"   { "E2F0D9" }   # Soft Green Mint
        "MEDIUM" { "FFF2CC" }   # Soft Cream Yellow
        default  { "FCE4D6" }   # Soft Peach Red (LOW)
    }
    $sec1RatingFg = switch ([string]$pdObj.Rating) {
        "HIGH"   { "1E4620" }   # Dark Forest Green  — 6.1:1 on E2F0D9
        "MEDIUM" { "5C4300" }   # Dark Gold/Amber    — 5.4:1 on FFF2CC
        default  { "801414" }   # Deep Maroon Red    — 5.2:1 on FCE4D6 (LOW)
    }
    $sdt100017 = $doc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='100017']", $ns)
    if ($sdt100017) {
        # Set cell background color
        $sec1Tc = $sdt100017
        while ($null -ne $sec1Tc -and $sec1Tc.LocalName -ne "tc") { $sec1Tc = $sec1Tc.ParentNode }
        if ($null -ne $sec1Tc) {
            $sec1Shd = $sec1Tc.SelectSingleNode("w:tcPr/w:shd", $ns)
            if ($sec1Shd) {
                $sec1Shd.SetAttribute("fill", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", $sec1RatingBg) | Out-Null
            }
        }
        # Set foreground text color (508 compliance)
        $sec1Run = $sdt100017.SelectSingleNode("w:sdtContent//w:r", $ns)
        if ($sec1Run) {
            $sec1RPr = $sec1Run.SelectSingleNode("w:rPr", $ns)
            if (-not $sec1RPr) {
                $sec1RPr = $doc.CreateElement("w", "rPr", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
                $sec1Run.PrependChild($sec1RPr) | Out-Null
            }
            $sec1Color = $sec1RPr.SelectSingleNode("w:color", $ns)
            if (-not $sec1Color) {
                $sec1Color = $doc.CreateElement("w", "color", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
                $sec1RPr.AppendChild($sec1Color) | Out-Null
            }
            $sec1Color.SetAttribute("val", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", $sec1RatingFg) | Out-Null
        }
    }
    $sec1TriggeredCount = ($pdObj.Criteria | Where-Object { [bool]$_.Triggered } | Measure-Object).Count
    $sec1RatingLabel    = "$([string]$pdObj.Rating) -- $sec1TriggeredCount of 4 criteria met"
    Set-SdtText $doc $ns "100017" $sec1RatingLabel

    Set-SdtText $doc $ns "100018" ([string]$pdObj.PdNbr)
    Set-SdtText $doc $ns "100019" ([string]$pdObj.PositionTitle)
    Set-SdtText $doc $ns "100020" $orgValue
    Set-SdtText $doc $ns "100021" $orgValue
    Set-SdtText $doc $ns "100022" ([string]$pdObj.PayPlan)
    Set-SdtText $doc $ns "100023" ([string]$pdObj.OccSeries)
    Set-SdtText $doc $ns "100024" ([string]$pdObj.Grade)
    Set-SdtText $doc $ns "100025" $evalDate
    Set-SdtText $doc $ns "100026" $pdManagerLevel
    Set-SdtText $doc $ns "100027" $positionSensitivity
    Set-SdtText $doc $ns "100028" $publicTrust
    Set-SdtText $doc $ns "100029" $serviceCategory

    $duties = @()
    if ($pdObj.PSObject.Properties['Duties'] -and $null -ne $pdObj.Duties) {
        $duties = @($pdObj.Duties)
    }

    # Fill an SDT node in-place (removes showingPlcHdr, sets text, strips italic/gray)
    function Set-SdtNodeText {
        param([System.Xml.XmlNode]$sdt, [string]$value)
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

    $dutyTemplateSdt     = $doc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='100030']", $ns)
    $evidenceTemplateSdt = $doc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='100031']", $ns)

    $templateRow = $dutyTemplateSdt
    while ($templateRow -and $templateRow.LocalName -ne "tr") { $templateRow = $templateRow.ParentNode }

    if (-not $templateRow -or -not $evidenceTemplateSdt) {
        return
    }

    if ($duties.Count -eq 0) {
        Set-SdtNodeText $dutyTemplateSdt     "No duty rows were provided for this evaluation."
        Set-SdtNodeText $evidenceTemplateSdt "(no supporting duty metadata available)"
        return
    }

    foreach ($d in $duties) {
        $seq = if ($d.PSObject.Properties['SeqNum']) { [string]$d.SeqNum } else { "" }
        $txt = if ($d.PSObject.Properties['DutyText']) { [string]$d.DutyText } else { "" }

        if ([string]::IsNullOrWhiteSpace($txt)) {
            $txt = "(duty text not provided)"
        }

        $meets = $false
        if ($d.PSObject.Properties['MeetsCriteria']) {
            $meets = [bool]$d.MeetsCriteria
        } elseif ($d.PSObject.Properties['MatchedCriteria'] -and $null -ne $d.MatchedCriteria) {
            $meets = (@($d.MatchedCriteria).Count -gt 0)
        }

        $label = if ([string]::IsNullOrWhiteSpace($seq)) { "Duty" } else { "Duty #$seq" }
        $dutyLine = "${label}: $txt"

        $meta = "No direct support finding."
        if ($d.PSObject.Properties['MatchedCriteria'] -and $null -ne $d.MatchedCriteria -and @($d.MatchedCriteria).Count -gt 0) {
            $supports = (@($d.MatchedCriteria) -join ", ")
            $meta = "Supports: $supports"
        } elseif ($meets) {
            $meta = "Supports: Yes"
        }

        $row = $templateRow.CloneNode($true)
        $rowDutySdt     = $row.SelectSingleNode(".//w:sdt[w:sdtPr/w:id/@w:val='100030']", $ns)
        $rowEvidenceSdt = $row.SelectSingleNode(".//w:sdt[w:sdtPr/w:id/@w:val='100031']", $ns)

        if ($rowDutySdt)     { Set-SdtNodeText $rowDutySdt     $dutyLine }
        if ($rowEvidenceSdt) { Set-SdtNodeText $rowEvidenceSdt $meta }

        if ($meets -and $rowDutySdt) {
            $run = $rowDutySdt.SelectSingleNode("w:sdtContent//w:r", $ns)
            if ($run -and $run.LocalName -eq "r") {
                $rPr = $run.SelectSingleNode("w:rPr", $ns)
                if (-not $rPr) {
                    $rPr = New-WElement $doc "rPr"
                    $run.PrependChild($rPr) | Out-Null
                }
                $h = $rPr.SelectSingleNode("w:highlight", $ns)
                if (-not $h) {
                    $h = New-WElement $doc "highlight"
                    $rPr.AppendChild($h) | Out-Null
                }
                $h.SetAttribute("val", $wNs, "yellow") | Out-Null
            }
        }

        $templateRow.ParentNode.InsertBefore($row, $templateRow) | Out-Null
    }

    $templateRow.ParentNode.RemoveChild($templateRow) | Out-Null
}
# --- Helper: replace a text content control (by w:id) ---
# sdtContent structure: <w:sdtContent><w:r><w:rPr>...</w:rPr><w:t>placeholder</w:t></w:r></w:sdtContent>
function Set-SdtText {
    param([System.Xml.XmlDocument]$doc, [System.Xml.XmlNamespaceManager]$ns,
          [string]$id, [string]$value)

    $sdt = $doc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='$id']", $ns)
    if (-not $sdt) { Write-Warning "Content control $id not found"; return }

    # Remove showingPlcHdr so Word treats this as real content, not placeholder
    $plcHdr = $sdt.SelectSingleNode("w:sdtPr/w:showingPlcHdr", $ns)
    if ($plcHdr) { $plcHdr.ParentNode.RemoveChild($plcHdr) | Out-Null }

    # Replace the text in the existing <w:t> node (sdtContent > w:r > w:t)
    $tNode = $sdt.SelectSingleNode("w:sdtContent//w:t", $ns)
    if ($tNode) {
        $tNode.InnerText = $value
        # Strip placeholder formatting (italic + gray color) from parent rPr
        $rPr = $sdt.SelectSingleNode("w:sdtContent//w:rPr", $ns)
        if ($rPr) {
            $i     = $rPr.SelectSingleNode("w:i",     $ns)
            $color = $rPr.SelectSingleNode("w:color", $ns)
            if ($i)     { $rPr.RemoveChild($i)     | Out-Null }
            if ($color) { $rPr.RemoveChild($color) | Out-Null }
        }
    } else {
        Write-Warning "Text node not found in content control $id"
    }
}

# --- Helper: set a checkbox by zero-based document order ---
function Set-Checkbox {
    param([System.Xml.XmlDocument]$doc, [System.Xml.XmlNamespaceManager]$ns,
          [int]$index, [bool]$checked)

    $boxes = $doc.SelectNodes("//w:sdt[w:sdtPr/w14:checkbox]", $ns)
    if ($index -ge $boxes.Count) { Write-Warning "Checkbox index $index out of range ($($boxes.Count) total)"; return }

    $box         = $boxes[$index]
    $checkedNode = $box.SelectSingleNode("w:sdtPr/w14:checkbox/w14:checked", $ns)
    if ($checkedNode) {
        $val = if ($checked) { "1" } else { "0" }
        $checkedNode.SetAttribute("val", "http://schemas.microsoft.com/office/word/2010/wordml", $val) | Out-Null
    }

    # Update the visible glyph in sdtContent ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â template uses <w:sym w:char="2612|2610" />
    $symNode = $box.SelectSingleNode("w:sdtContent//w:sym", $ns)
    if ($symNode) {
        $charVal = if ($checked) { "2612" } else { "2610" }
        $symNode.SetAttribute("char", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", $charVal) | Out-Null
    }
}

# =====================================================================
# SECTION 1 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Position metadata
# =====================================================================
Set-SdtText $xmlDoc $nsm "100001" $pd.OrgCode
Set-SdtText $xmlDoc $nsm "100002" $pd.PositionTitle
Set-SdtText $xmlDoc $nsm "100003" "$($pd.PayPlan)-$($pd.OccSeries)-$($pd.Grade)"
Set-SdtText $xmlDoc $nsm "100015" $pd.PdNbr

# Service Category checkboxes (index 0 = Competitive, 1 = Excepted)
$isExcepted = ($pd.ServiceCategory -eq "Excepted")
Set-Checkbox $xmlDoc $nsm 0 (-not $isExcepted)
Set-Checkbox $xmlDoc $nsm 1 $isExcepted

# =====================================================================
# SECTION 2 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Evaluation criteria (4 criteria)
# Checkbox order: each criterion occupies two consecutive checkboxes (Yes, No)
#   0-1  Service Category (above)
#   2-3  Criterion 1 (Determining policy)
#   4-5  Criterion 2 (Making policy)
#   6-7  Criterion 3 (Advocating policy)
#   8-9  Criterion 4 (Confidential work)
# Text control IDs: 100004-100007 (PD Evidence per criterion)
# =====================================================================
$criterionIds = @("100004","100005","100006","100007")
for ($i = 0; $i -lt 4; $i++) {
    $c         = $pd.Criteria[$i]
    $triggered = [bool]$c.Triggered
    Set-Checkbox $xmlDoc $nsm (2 + $i * 2)     $triggered          # Yes
    Set-Checkbox $xmlDoc $nsm (2 + $i * 2 + 1) (-not $triggered)   # No
    Set-SdtText  $xmlDoc $nsm $criterionIds[$i] $c.Evidence
}

# =====================================================================
# SECTION 3 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Exclusion checks (3 exclusions)
# Checkbox order:
#   10-11  Exclusion 0 (Routine Administration)
#   12-13  Exclusion 1 (Pure Technical/Scientific)
#   14-15  Exclusion 2 (Routine Legal Counsel)
# Text control IDs: 100008-100010
# =====================================================================
$exclusionIds = @("100008","100009","100010")
for ($i = 0; $i -lt 3; $i++) {
    $ex      = $pd.Exclusions[$i]
    $applies = [bool]$ex.Applies
    Set-Checkbox $xmlDoc $nsm (10 + $i * 2)     $applies          # Yes (Applies)
    Set-Checkbox $xmlDoc $nsm (10 + $i * 2 + 1) (-not $applies)   # No
    Set-SdtText  $xmlDoc $nsm $exclusionIds[$i] $ex.Evidence
}

# =====================================================================
# SECTION 4 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Rating (content control 100014 + cell background color)
# Colors: HIGH=00B050 (green), MEDIUM=FFC000 (yellow), LOW=FF0000 (red)
# =====================================================================
$ratingText  = if ($pd.Rating) { $pd.Rating } else { "LOW" }
# 508-compliant palette — soft tinted backgrounds with dark matching foregrounds
$ratingBg = switch ($ratingText) {
    "HIGH"   { "E2F0D9" }   # Soft Green Mint
    "MEDIUM" { "FFF2CC" }   # Soft Cream Yellow
    default  { "FCE4D6" }   # Soft Peach Red (LOW)
}
$ratingFg = switch ($ratingText) {
    "HIGH"   { "1E4620" }   # Dark Forest Green  — 6.1:1 on E2F0D9
    "MEDIUM" { "5C4300" }   # Dark Gold/Amber    — 5.4:1 on FFF2CC
    default  { "801414" }   # Deep Maroon Red    — 5.2:1 on FCE4D6 (LOW)
}

# Count triggered criteria and include in the displayed rating label
$triggeredCount = ($pd.Criteria | Where-Object { [bool]$_.Triggered } | Measure-Object).Count
$ratingLabel    = "$ratingText -- $triggeredCount of 4 criteria met"

Set-SdtText $xmlDoc $nsm "100014" $ratingLabel

# Color the right-hand cell of the Rating row by finding the SDT 100014,
# navigating up to its <w:tc>, then setting <w:tcPr><w:shd w:fill="...">
$sdt100014 = $xmlDoc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='100014']", $nsm)
if ($sdt100014) {
    # Set cell background color
    $tc = $sdt100014
    while ($null -ne $tc -and $tc.LocalName -ne "tc") { $tc = $tc.ParentNode }
    if ($null -ne $tc) {
        $shd = $tc.SelectSingleNode("w:tcPr/w:shd", $nsm)
        if ($shd) {
            $shd.SetAttribute("fill", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", $ratingBg) | Out-Null
        }
    }
    # Set foreground text color (508 compliance)
    $ratingRun = $sdt100014.SelectSingleNode("w:sdtContent//w:r", $nsm)
    if ($ratingRun) {
        $ratingRPr = $ratingRun.SelectSingleNode("w:rPr", $nsm)
        if (-not $ratingRPr) {
            $ratingRPr = $xmlDoc.CreateElement("w", "rPr", $wNs)
            $ratingRun.PrependChild($ratingRPr) | Out-Null
        }
        $ratingColorEl = $ratingRPr.SelectSingleNode("w:color", $nsm)
        if (-not $ratingColorEl) {
            $ratingColorEl = $xmlDoc.CreateElement("w", "color", $wNs)
            $ratingRPr.AppendChild($ratingColorEl) | Out-Null
        }
        $ratingColorEl.SetAttribute("val", $wNs, $ratingFg) | Out-Null
    }
}

# =====================================================================
# SECTION 5 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Final determination
# Checkbox order:
#   16  Convert to Schedule P/C
#   17  Retain in Current Schedule
# Text control IDs: 100011 (Justification), 100012 (Evaluator), 100013 (Approving)
# =====================================================================
$convert = ($pd.IsCandidate -eq "YES")
Set-Checkbox $xmlDoc $nsm 16 $convert
Set-Checkbox $xmlDoc $nsm 17 (-not $convert)

Set-SdtText $xmlDoc $nsm "100011" $pd.JustificationSummary
Set-SdtText $xmlDoc $nsm "100012" "AI Agent (claude-sonnet-4-6) / $($pd.EvalDate)"
Set-SdtText $xmlDoc $nsm "100013" "(Pending Human Review)"

# =====================================================================
# SECTION 6 -- Appendix (Data Attributes + Duty Evidence)
# =====================================================================
Add-Appendix $xmlDoc $nsm $pd

# --- Save modified XML and repackage ---
$xmlDoc.Save($docPath)

$outputDir = Split-Path $OutputPath -Parent
if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }
if (Test-Path $OutputPath)       { Remove-Item $OutputPath -Force }

$zipStream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $files = Get-ChildItem -Path $tempDir -Recurse -File
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($tempDir.Length).TrimStart([char]'\', [char]'/')
            $entryName = $relative.Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
} finally {
    $zipStream.Dispose()
}
Remove-Item -Recurse -Force $tempDir

Write-Output "Saved: $OutputPath"









