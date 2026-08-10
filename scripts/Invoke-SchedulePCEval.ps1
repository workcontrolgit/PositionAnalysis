<#
.SYNOPSIS
    Runs the Schedule PC evaluation loop for a RunId.
    Each PD is scored via a fresh claude CLI call — zero context accumulation.

.DESCRIPTION
    Fetches pending PDs from Oracle, scores each one using claude -p (or Ollama),
    writes results back to Oracle, and generates Word evaluation forms at the end.
    No LLM context is held between PDs — each scoring call is stateless.

.PARAMETER RunId
    Run identifier (e.g. 2026-08-09-1500). Must already exist in SCHEDULE_PC_EVAL.

.PARAMETER Series
    Comma-separated series codes, or "all". Default: all.

.PARAMETER Scorer
    claude (default) | ollama | ollama:{model} | ollama:{model}@{endpoint}

.PARAMETER OutputRoot
    Repository root. Defaults to the directory two levels above this script.

.EXAMPLE
    .\Invoke-SchedulePCEval.ps1 -RunId 2026-08-09-1500
    .\Invoke-SchedulePCEval.ps1 -RunId 2026-08-09-1500 -Series 0110,0301 -Scorer claude
    .\Invoke-SchedulePCEval.ps1 -RunId 2026-08-09-1500 -Scorer ollama:gemma4:latest
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunId,
    [string]$Series     = 'all',
    [string]$Scorer     = 'claude',
    [string]$OutputRoot = (Resolve-Path "$PSScriptRoot\.." -EA Stop).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Configuration ──────────────────────────────────────────────────────────────
$SqlclBin   = 'C:\Users\Fuji Nguyen\.vscode\extensions\oracle.sql-developer-26.2.1-win32-x64\dbtools\sqlcl\bin\sql.exe'
$ConnStr    = 'hr/HrUser_2026@//localhost:1521/XEPDB1'
$DataJson   = Join-Path $OutputRoot 'reports\schedule-pc\data-json'
$FormWord   = Join-Path $OutputRoot 'reports\schedule-pc\form-word'
$FillScript = Join-Path $OutputRoot 'scripts\Fill-EvalTemplate-Batch-v2.ps1'
$PromptTpl  = Join-Path $OutputRoot 'templates\score-prompt.txt'

[void](New-Item -ItemType Directory -Force -Path $DataJson)
[void](New-Item -ItemType Directory -Force -Path $FormWord)

# ── Code → label maps ──────────────────────────────────────────────────────────
$ManagerLevelMap = @{
    '2'='Supervisor or Manager'; '4'='Supervisor (CSRA)'
    '5'='Management Official (CSRA)'; '6'='Leader'
    '7'='Team Leader'; '8'='All Other Positions'
}
$SensitivityMap = @{
    '1'='Non-Sensitive'; '2'='Non-Critical Sensitive'
    '3'='Critical Sensitive'; '4'='Special Sensitive'
}
$PublicTrustMap = @{ '9'='High Risk'; '10'='Mod Risk'; '11'='Low Risk'; '99'='No Risk' }
$ServiceCatMap  = @{
    '1'='Competitive'; '2'='Excepted'; '3'='SES General'
    '4'='SES Career Reserved'; '5'='Federal Wage System'
}

function Get-Label([hashtable]$map, $code, [string]$default = '') {
    $k = "$code"
    if ($map.ContainsKey($k)) { return $map[$k] }
    return $default
}

# ── SQL helpers ────────────────────────────────────────────────────────────────
$NoBom = New-Object System.Text.UTF8Encoding $false

function Invoke-SqlJson([string]$sql) {
    $tmpSql = [IO.Path]::GetTempFileName() + '.sql'
    $tmpOut = [IO.Path]::GetTempFileName() + '.json'
    $fwdOut = $tmpOut.Replace('\', '/')
    $sqlBody = $sql.TrimEnd(';',' ',"`r","`n")
    $script = @"
SET FEEDBACK OFF
SET SQLFORMAT JSON
SPOOL "$fwdOut"
$sqlBody
;
SPOOL OFF
EXIT;
"@
    [System.IO.File]::WriteAllText($tmpSql, $script, $NoBom)
    try {
        & $SqlclBin -S $ConnStr "@$tmpSql" 2>&1 | Out-Null
        if (-not (Test-Path $tmpOut)) { return @() }
        $raw = Get-Content $tmpOut -Raw -Encoding UTF8
        if ([string]::IsNullOrWhiteSpace($raw)) { return @() }
        $parsed = $raw | ConvertFrom-Json
        # sqlcl JSON format varies — handle array or wrapped object
        if ($parsed -is [array])           { return $parsed }
        if ($parsed.results)               { return $parsed.results[0].items }
        return @($parsed)
    } finally {
        Remove-Item $tmpSql, $tmpOut -Force -EA SilentlyContinue
    }
}

function Invoke-SqlDml([string]$sql) {
    $tmpSql = [IO.Path]::GetTempFileName() + '.sql'
    $sqlBody = $sql.TrimEnd(';',' ',"`r","`n")
    $script = @"
SET FEEDBACK OFF
SET DEFINE OFF
SET ESCAPE OFF
$sqlBody
;
COMMIT;
EXIT;
"@
    [System.IO.File]::WriteAllText($tmpSql, $script, $NoBom)
    try {
        & $SqlclBin -S $ConnStr "@$tmpSql" 2>&1 | Out-Null
    } finally {
        Remove-Item $tmpSql -Force -EA SilentlyContinue
    }
}

# Escape single quotes for Oracle string literals
function esc([string]$s) { $s.Replace("'", "''") }

# ── Build scoring prompt ───────────────────────────────────────────────────────
function Build-Prompt([object[]]$rows) {
    $h         = $rows[0]
    $evalDate  = Get-Date -Format 'yyyy-MM-dd'
    $mlCode    = "$($h.PD_MANAGER_LEVEL)"
    $psCode    = "$($h.POSITION_SENSITIVITY)"
    $ptCode    = "$($h.GM_PUBLIC_TRUST)"
    $scCode    = "$($h.QRP_POSITION_OCCUPIED_CODE)"

    $duties = ($rows | ForEach-Object {
        "[$($_.PDD_SEQ_NUM)] $($_.PDD_MAJOR_DUTIES_TEXT) ($($_.PDD_PERCENT_TIME_SPENT)%, Critical: $($_.PDD_CRITICAL_DUTY_IND))"
    }) -join "`n"

    $tpl = Get-Content $PromptTpl -Raw -Encoding UTF8
    $tpl = $tpl.Replace('{{PD_NBR}}',             "$($h.PD_NBR)")
    $tpl = $tpl.Replace('{{TITLE}}',              "$($h.PD_POSITION_TITLE_TEXT)")
    $tpl = $tpl.Replace('{{PAY_PLAN}}',           "$($h.GVT_PAY_PLAN)")
    $tpl = $tpl.Replace('{{OCC_SERIES}}',         "$($h.GVT_OCC_SERIES)")
    $tpl = $tpl.Replace('{{GRADE}}',              "$($h.GRD_CODE)")
    $tpl = $tpl.Replace('{{ORG_CODE}}',           "$($h.PD_ORIGIN_ORG_CODE)")
    $tpl = $tpl.Replace('{{MANAGER_LEVEL_CODE}}', $mlCode)
    $tpl = $tpl.Replace('{{MANAGER_LEVEL_LABEL}}',(Get-Label $ManagerLevelMap $mlCode))
    $tpl = $tpl.Replace('{{SENSITIVITY_CODE}}',   $psCode)
    $tpl = $tpl.Replace('{{SENSITIVITY_LABEL}}',  (Get-Label $SensitivityMap $psCode))
    $tpl = $tpl.Replace('{{PUBLIC_TRUST_CODE}}',  $ptCode)
    $tpl = $tpl.Replace('{{PUBLIC_TRUST_LABEL}}', (Get-Label $PublicTrustMap $ptCode 'No Risk'))
    $tpl = $tpl.Replace('{{SERVICE_CATEGORY}}',   (Get-Label $ServiceCatMap  $scCode 'Competitive'))
    $tpl = $tpl.Replace('{{EVAL_DATE}}',          $evalDate)
    $tpl = $tpl.Replace('{{DUTIES_BLOCK}}',       $duties)
    return $tpl
}

# ── Call LLM ──────────────────────────────────────────────────────────────────
function Invoke-Score([string]$prompt) {
    if ($Scorer -eq 'claude') {
        $result = $prompt | & claude -p --output-format text 2>&1
        return ($result -join "`n").Trim()
    }
    # ollama:{model} or ollama:{model}@{endpoint}
    $model    = 'gemma4:latest'
    $endpoint = 'http://localhost:11434'
    if ($Scorer -match '^ollama:([^@]+)(?:@(.+))?$') {
        $model    = $Matches[1]
        if ($Matches[2]) { $endpoint = $Matches[2] }
    }
    $body = @{ model=$model; messages=@(@{role='user';content=$prompt}); stream=$false } | ConvertTo-Json -Compress -Depth 5
    $resp = Invoke-RestMethod -Uri "$endpoint/api/chat" -Method Post -Body $body -ContentType 'application/json'
    return $resp.message.content.Trim()
}

# ── Validate LLM result ───────────────────────────────────────────────────────
function Test-Score([string]$raw, [ref]$out) {
    try {
        $clean = $raw -replace '(?s)^```json\s*', '' -replace '\s*```$', ''
        $obj   = $clean | ConvertFrom-Json -EA Stop
        if (-not $obj.PdNbr)                             { return 'Missing PdNbr' }
        if ($obj.IsCandidate -notin 'YES','BORDERLINE','NO') { return "Invalid IsCandidate: $($obj.IsCandidate)" }
        if ($obj.Rating      -notin 'HIGH','MEDIUM','BORDERLINE','LOW') { return "Invalid Rating: $($obj.Rating)" }
        if (-not $obj.Criteria -or $obj.Criteria.Count -ne 4) { return "Expected 4 Criteria, got $($obj.Criteria.Count)" }
        $valid = 'Policy-Determining','Policy-Making','Policy-Advocating','Confidential'
        foreach ($c in $obj.Criteria) {
            if ($c.Name -notin $valid) { return "Unknown criterion: $($c.Name)" }
            if (-not $c.Evidence)      { return "Empty Evidence for $($c.Name)" }
        }
        if (-not $obj.JustificationSummary) { return 'Missing JustificationSummary' }
        $out.Value = $obj
        return $null
    } catch { return "JSON parse error: $_" }
}

# ── Write scored result to Oracle ─────────────────────────────────────────────
function Write-ScoreToOracle([object]$j, [string]$pdNbr, [string]$jsonFile) {
    $crit = @{}
    foreach ($c in $j.Criteria) { $crit[$c.Name] = $c }
    $yn   = { param($n) if ($crit[$n].Triggered) { 'Y' } else { 'N' } }
    $resultJson = (Get-Content $jsonFile -Raw -Encoding UTF8).Trim()

    $sql = @"
UPDATE SCHEDULE_PC_EVAL SET
    IS_CANDIDATE            = '$(esc $j.IsCandidate)',
    RATING                  = '$(esc $j.Rating)',
    POSITION_PURPOSE        = '$(esc $j.PositionPurpose)',
    JUSTIFICATION_SUMMARY   = '$(esc $j.JustificationSummary)',
    CRIT_POLICY_DETERMINING = '$(& $yn 'Policy-Determining')',
    EVID_POLICY_DETERMINING = '$(esc $crit['Policy-Determining'].Evidence)',
    CRIT_POLICY_MAKING      = '$(& $yn 'Policy-Making')',
    EVID_POLICY_MAKING      = '$(esc $crit['Policy-Making'].Evidence)',
    CRIT_POLICY_ADVOCATING  = '$(& $yn 'Policy-Advocating')',
    EVID_POLICY_ADVOCATING  = '$(esc $crit['Policy-Advocating'].Evidence)',
    CRIT_CONFIDENTIAL       = '$(& $yn 'Confidential')',
    EVID_CONFIDENTIAL       = '$(esc $crit['Confidential'].Evidence)',
    RESULT_JSON             = '$(esc $resultJson)',
    STATUS                  = 'done',
    SCORED_AT               = SYSTIMESTAMP,
    ERROR_MSG               = NULL
WHERE PD_NBR = '$pdNbr' AND RUN_ID = '$(esc $RunId)'
"@
    Invoke-SqlDml $sql
}

function Write-FailToOracle([string]$pdNbr, [string]$errMsg) {
    $safe = esc ($errMsg.Substring(0, [Math]::Min($errMsg.Length, 950)))
    Invoke-SqlDml "UPDATE SCHEDULE_PC_EVAL SET STATUS='failed', ERROR_MSG='$safe', SCORED_AT=SYSTIMESTAMP WHERE PD_NBR='$pdNbr' AND RUN_ID='$(esc $RunId)'"
}

# ── Main ──────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "╔══ Schedule PC Eval ══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  RunId:  $RunId" -ForegroundColor Cyan
Write-Host "║  Series: $Series   Scorer: $Scorer" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ''

# Build series WHERE filter
$seriesFilter = ''
if ($Series -ne 'all') {
    $list = ($Series -split ',') | ForEach-Object { "'$($_.Trim())'" }
    $seriesFilter = "AND SERIES IN ($($list -join ','))"
}

# Fetch pending PDs
$pds = Invoke-SqlJson @"
SELECT PD_NBR, TITLE, SERIES
FROM   SCHEDULE_PC_EVAL
WHERE  RUN_ID = '$(esc $RunId)'
  AND  STATUS IN ('pending','failed')
  $seriesFilter
ORDER  BY SERIES, PD_NBR
"@

if (-not $pds -or $pds.Count -eq 0) {
    Write-Host "No pending PDs found for RunId $RunId  Series=$Series" -ForegroundColor Yellow
    exit 0
}

$total     = $pds.Count
$n         = 0
$cntDone   = 0
$cntFailed = 0
$curSeries = ''

Write-Host "$total PDs to process" -ForegroundColor White
Write-Host ''

foreach ($pd in $pds) {
    $n++
    $pdNbr = $pd.PD_NBR
    $title = $pd.TITLE

    if ($pd.SERIES -ne $curSeries) {
        $curSeries = $pd.SERIES
        Write-Host "── Series $curSeries ──" -ForegroundColor DarkCyan
    }

    # Mark in_progress
    Invoke-SqlDml "UPDATE SCHEDULE_PC_EVAL SET STATUS='in_progress' WHERE PD_NBR='$pdNbr' AND RUN_ID='$(esc $RunId)'"

    try {
        # Fetch PD data
        $rows = Invoke-SqlJson @"
SELECT v.PD_SEQ_NUM, v.PD_NBR, v.PD_POSITION_TITLE_TEXT, v.GVT_OCC_SERIES,
       v.GRD_CODE, v.GVT_PAY_PLAN, v.PD_ORIGIN_ORG_CODE, v.PD_MANAGER_LEVEL,
       pdpd.POSITION_SENSITIVITY, pdpd.GM_PUBLIC_TRUST, pdpd.QRP_POSITION_OCCUPIED_CODE,
       d.PDD_SEQ_NUM, d.PDD_MAJOR_DUTIES_TEXT, d.PDD_PERCENT_TIME_SPENT, d.PDD_CRITICAL_DUTY_IND
FROM   MAX_PD_VW v
JOIN   PD_DUTIES d ON d.PD_SEQ_NUM = v.PD_SEQ_NUM
LEFT JOIN PD_POSITION_DATA pdpd ON pdpd.PD_SEQ_NUM = v.PD_SEQ_NUM
WHERE  v.PD_NBR = '$pdNbr'
ORDER  BY d.PDD_PERCENT_TIME_SPENT DESC NULLS LAST
"@
        if (-not $rows -or $rows.Count -eq 0) { throw "PD $pdNbr not found in MAX_PD_VW" }

        # Score
        $prompt = Build-Prompt $rows
        $raw    = Invoke-Score $prompt

        # Validate
        $scoreObj = $null
        $err      = Test-Score $raw ([ref]$scoreObj)
        if ($err) {
            Write-FailToOracle $pdNbr $err
            $cntFailed++
            Write-Host "  ✗ [$n/$total] PD-$pdNbr — SCORING_FAILED: $err" -ForegroundColor Red
            continue
        }

        # Write JSON file
        $jsonFile = Join-Path $DataJson "PD-$pdNbr.json"
        $scoreObj | ConvertTo-Json -Depth 10 -Compress | Out-File $jsonFile -Encoding UTF8 -NoNewline

        # Write to Oracle
        Write-ScoreToOracle $scoreObj $pdNbr $jsonFile

        $cntDone++
        $label = "$($scoreObj.IsCandidate)/$($scoreObj.Rating)"
        Write-Host "  ✓ [$n/$total] PD-$pdNbr — $title — $label" -ForegroundColor Green

    } catch {
        $errMsg = $_.Exception.Message
        Write-FailToOracle $pdNbr $errMsg
        $cntFailed++
        Write-Host "  ✗ [$n/$total] PD-$pdNbr — ERROR: $errMsg" -ForegroundColor Red
    }
}

# Generate Word forms
Write-Host ''
Write-Host 'Generating Word forms...' -ForegroundColor Cyan
& powershell.exe -File $FillScript -DataFolder $DataJson -OutputFolder $FormWord

# Summary
Write-Host ''
Write-Host "── Done: $cntDone   Failed: $cntFailed   Total: $total" -ForegroundColor Cyan
