$pds = @(
"D06116","S00733","S00779","S00805","S01011","S02604","S03857","S07060","S07225","S07967",
"S08282","S08297","S08314","S08387","S08393","S08731","S09154","S09243","S09295","S09375",
"S09720","S09733","S09862","S09987","S10597","S10609","S10724","S11581","S11675","S12070",
"S12531","S13133","S13605","S13739","S13981","S14150","S14991","S15043","S15332","S15672",
"S15718","S15773","S15865","S16008","S16029","S16095","S16118","S16123","S16482","S16588",
"S16794","S16875","S17045","S17046","S17212","S17239","S17270","S17625","S17699","S17939",
"S18964","S19729","S19913","S20385","S98136","S14077","S08846","S10043","S14378","S02833",
"S14736","S16960","S01948","S04318","S06326","S06451","S11873","S15251","S15317","S78720",
"S09089","S15071","S15072","X00355","S15686","D13144","S11853","S17397","D07798","S08562"
)

$src = Join-Path $PSScriptRoot "..\src\PositionAnalysis.Mcp\reports\schedule-pc\form-word"
$dst = Join-Path $PSScriptRoot "..\src\PositionAnalysis.Mcp\reports\schedule-pc\artifacts"

if (-not (Test-Path $dst)) {
    New-Item -Path $dst -ItemType Directory -Force | Out-Null
}

$found = @()
$missing = @()

foreach ($pd in $pds) {
    $matches = Get-ChildItem -Path $src -Filter ("PD-{0}_*.docx" -f $pd) -ErrorAction SilentlyContinue
    if ($matches) {
        foreach ($m in $matches) {
            Copy-Item -Path $m.FullName -Destination $dst -Force
            $found += $m.Name
        }
    } else {
        $missing += $pd
    }
}

Write-Output ("Total PDs: {0}" -f $pds.Count)
Write-Output ("Copied: {0}" -f $found.Count)
Write-Output ("Missing: {0}" -f $missing.Count)
if ($missing.Count -gt 0) {
    Write-Output ("Missing PDs: {0}" -f ($missing -join ', '))
}
