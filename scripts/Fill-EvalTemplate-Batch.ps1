# Fill-EvalTemplate-Batch.ps1
# Batch version of Fill-EvalTemplate.ps1.
# Processes all *.json files in a DataFolder in a single PowerShell process,
# reusing the loaded template XML for each PD instead of spawning a new process
# per file.
#
# Usage:
#   pwsh -File Fill-EvalTemplate-Batch.ps1 -DataFolder 'C:/tmp/eval-json' -OutputFolder '../reports/schedule-pc/form-word'

param(
    [Parameter(Mandatory)] [string] $DataFolder,
    [Parameter(Mandatory)] [string] $OutputFolder,
    [string[]] $PdNbr   # Optional: limit to specific PD numbers, e.g. '100001','100002'
)

Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
Add-Type -AssemblyName 'System.IO.Compression'

# Repo root is the parent of this script's folder — resolves regardless of clone location.
$repoRoot     = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repoRoot 'templates/Schedule_PC_Position_Evaluation_Template.docx'
$wNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"

if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
}

# Load template bytes once
$templateBytes = [System.IO.File]::ReadAllBytes($templatePath)

$jsonFiles = Get-ChildItem -Path $DataFolder -Filter "*.json" | Sort-Object Name
if ($PdNbr -and $PdNbr.Count -gt 0) {
    $jsonFiles = $jsonFiles | Where-Object { $PdNbr -contains ($_.BaseName -replace '^PD-', '') }
    Write-Output "PdNbr filter applied: processing $($jsonFiles.Count) file(s) — $($PdNbr -join ', ')"
}

$total     = $jsonFiles.Count
$done      = 0
$yes       = 0
$no        = 0

foreach ($file in $jsonFiles) {
    $pd = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json

    # --- Build output path ---
    $titleSlug  = $pd.PositionTitle -replace '[^a-zA-Z0-9 -]','' -replace '\s+','-'
    $fileName   = "PD-$($pd.PdNbr)_${titleSlug}_$($pd.PayPlan)-$($pd.OccSeries)-$($pd.Grade).docx"
    $outputPath = Join-Path $OutputFolder $fileName

    # --- Extract template into temp dir from in-memory bytes ---
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
    $memStream = New-Object System.IO.MemoryStream(,$templateBytes)
    $zip = [System.IO.Compression.ZipArchive]::new($memStream, [System.IO.Compression.ZipArchiveMode]::Read)
    [System.IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip, $tempDir)
    $zip.Dispose()
    $memStream.Dispose()

    $docPath = Join-Path $tempDir "word/document.xml"
    $xmlDoc  = New-Object System.Xml.XmlDocument
    $xmlDoc.Load($docPath)

    $nsm = New-Object System.Xml.XmlNamespaceManager($xmlDoc.NameTable)
    $nsm.AddNamespace("w",   "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
    $nsm.AddNamespace("w14", "http://schemas.microsoft.com/office/word/2010/wordml")

    function Set-SdtText {
        param([System.Xml.XmlDocument]$doc, [System.Xml.XmlNamespaceManager]$ns,
              [string]$id, [string]$value)
        $sdt = $doc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='$id']", $ns)
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

    function Set-Checkbox {
        param([System.Xml.XmlDocument]$doc, [System.Xml.XmlNamespaceManager]$ns,
              [int]$index, [bool]$checked)
        $boxes = $doc.SelectNodes("//w:sdt[w:sdtPr/w14:checkbox]", $ns)
        if ($index -ge $boxes.Count) { return }
        $box         = $boxes[$index]
        $checkedNode = $box.SelectSingleNode("w:sdtPr/w14:checkbox/w14:checked", $ns)
        if ($checkedNode) {
            $val = if ($checked) { "1" } else { "0" }
            $checkedNode.SetAttribute("val", "http://schemas.microsoft.com/office/word/2010/wordml", $val) | Out-Null
        }
        $symNode = $box.SelectSingleNode("w:sdtContent//w:sym", $ns)
        if ($symNode) {
            $charVal = if ($checked) { "2612" } else { "2610" }
            $symNode.SetAttribute("char", "http://schemas.openxmlformats.org/wordprocessingml/2006/main", $charVal) | Out-Null
        }
    }

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
    # --- Fill fields ---
    Set-SdtText $xmlDoc $nsm "100001" $pd.OrgCode
    Set-SdtText $xmlDoc $nsm "100002" $pd.PositionTitle
    Set-SdtText $xmlDoc $nsm "100003" "$($pd.PayPlan)-$($pd.OccSeries)-$($pd.Grade)"

    $isExcepted = ($pd.ServiceCategory -eq "Excepted")
    Set-Checkbox $xmlDoc $nsm 0 (-not $isExcepted)
    Set-Checkbox $xmlDoc $nsm 1 $isExcepted

    $criterionIds = @("100004","100005","100006","100007")
    for ($i = 0; $i -lt 4; $i++) {
        $c         = $pd.Criteria[$i]
        $triggered = [bool]$c.Triggered
        Set-Checkbox $xmlDoc $nsm (2 + $i * 2)     $triggered
        Set-Checkbox $xmlDoc $nsm (2 + $i * 2 + 1) (-not $triggered)
        Set-SdtText  $xmlDoc $nsm $criterionIds[$i] $c.Evidence
    }

    $exclusionIds = @("100008","100009","100010")
    for ($i = 0; $i -lt 3; $i++) {
        $ex      = $pd.Exclusions[$i]
        $applies = [bool]$ex.Applies
        Set-Checkbox $xmlDoc $nsm (10 + $i * 2)     $applies
        Set-Checkbox $xmlDoc $nsm (10 + $i * 2 + 1) (-not $applies)
        Set-SdtText  $xmlDoc $nsm $exclusionIds[$i] $ex.Evidence
    }

    $ratingText = if ($pd.Rating) { $pd.Rating } else { "LOW" }
    # 508-compliant palette (WCAG AA, min 5.2:1 contrast ratio)
    $ratingBg = switch ($ratingText) {
        "HIGH"   { "E2F0D9" }
        "MEDIUM" { "FFF2CC" }
        default  { "FCE4D6" }
    }
    $ratingFg = switch ($ratingText) {
        "HIGH"   { "1E4620" }
        "MEDIUM" { "5C4300" }
        default  { "801414" }
    }
    $triggeredCount = ($pd.Criteria | Where-Object { [bool]$_.Triggered } | Measure-Object).Count
    $ratingLabel    = "$ratingText -- $triggeredCount of 4 criteria met"

    # Fill Section 1 metadata controls (now content controls, not tokens)
    Set-SdtText $xmlDoc $nsm "100015" ([string]$pd.PdNbr)
    Set-SdtText $xmlDoc $nsm "100016" ([string]$pd.EvalDate)

    # SDT 100017 — Schedule PC Rating in Section 1 header (color + text)
    $sdt100017 = $xmlDoc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='100017']", $nsm)
    if ($sdt100017) {
        $sec1Tc = $sdt100017
        while ($null -ne $sec1Tc -and $sec1Tc.LocalName -ne "tc") { $sec1Tc = $sec1Tc.ParentNode }
        if ($null -ne $sec1Tc) {
            $sec1Shd = $sec1Tc.SelectSingleNode("w:tcPr/w:shd", $nsm)
            if ($sec1Shd) {
                $sec1Shd.SetAttribute("fill", $wNs, $ratingBg) | Out-Null
            }
        }
        $sec1Run = $sdt100017.SelectSingleNode("w:sdtContent//w:r", $nsm)
        if ($sec1Run) {
            $sec1RPr = $sec1Run.SelectSingleNode("w:rPr", $nsm)
            if (-not $sec1RPr) {
                $sec1RPr = $xmlDoc.CreateElement("w", "rPr", $wNs)
                $sec1Run.PrependChild($sec1RPr) | Out-Null
            }
            $sec1Color = $sec1RPr.SelectSingleNode("w:color", $nsm)
            if (-not $sec1Color) {
                $sec1Color = $xmlDoc.CreateElement("w", "color", $wNs)
                $sec1RPr.AppendChild($sec1Color) | Out-Null
            }
            $sec1Color.SetAttribute("val", $wNs, $ratingFg) | Out-Null
        }
    }
    Set-SdtText $xmlDoc $nsm "100017" $ratingLabel

    # Section 4 rating (100014) — same label and color
    Set-SdtText $xmlDoc $nsm "100014" $ratingLabel
    $sdt100014 = $xmlDoc.SelectSingleNode("//w:sdt[w:sdtPr/w:id/@w:val='100014']", $nsm)
    if ($sdt100014) {
        $tc = $sdt100014
        while ($null -ne $tc -and $tc.LocalName -ne "tc") { $tc = $tc.ParentNode }
        if ($null -ne $tc) {
            $shd = $tc.SelectSingleNode("w:tcPr/w:shd", $nsm)
            if ($shd) {
                $shd.SetAttribute("fill", $wNs, $ratingBg) | Out-Null
            }
        }
        # Foreground text color on the rating run inside the SDT (508 compliance)
        $sec4TNode = $sdt100014.SelectSingleNode(".//w:t", $nsm)
        if ($sec4TNode) {
            $sec4Run = $sec4TNode.ParentNode
            if ($sec4Run -and $sec4Run.LocalName -eq "r") {
                $sec4RPr = $sec4Run.SelectSingleNode("w:rPr", $nsm)
                if (-not $sec4RPr) {
                    $sec4RPr = $xmlDoc.CreateElement("w", "rPr", $wNs)
                    $sec4Run.PrependChild($sec4RPr) | Out-Null
                }
                $sec4Color = $sec4RPr.SelectSingleNode("w:color", $nsm)
                if (-not $sec4Color) {
                    $sec4Color = $xmlDoc.CreateElement("w", "color", $wNs)
                    $sec4RPr.AppendChild($sec4Color) | Out-Null
                }
                $sec4Color.SetAttribute("val", $wNs, $ratingFg) | Out-Null
            }
        }
    }

    $convert = ($pd.IsCandidate -eq "YES")
    Set-Checkbox $xmlDoc $nsm 16 $convert
    Set-Checkbox $xmlDoc $nsm 17 (-not $convert)

    Set-SdtText $xmlDoc $nsm "100011" $pd.JustificationSummary
    Set-SdtText $xmlDoc $nsm "100012" "AI Agent (claude-sonnet-4-6) / $($pd.EvalDate)"
    Set-SdtText $xmlDoc $nsm "100013" "(Pending Human Review)"

    Add-Appendix $xmlDoc $nsm $pd

    # --- Save ---
    $xmlDoc.Save($docPath)
    if (Test-Path $outputPath) { Remove-Item $outputPath -Force }
    $zipStream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create)
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

    $done++
    if ($pd.IsCandidate -eq "YES") { $yes++ } else { $no++ }
    Write-Output "[$done/$total] Written: $fileName [IS_CANDIDATE: $($pd.IsCandidate)]"
}

Write-Output ""
Write-Output "Batch complete: $done files written (YES: $yes  NO: $no)"









