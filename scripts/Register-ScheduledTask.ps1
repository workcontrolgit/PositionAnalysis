#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Registers a Windows Scheduled Task to run the PositionAnalysis CLI unattended.

.DESCRIPTION
    Creates a Task Scheduler task that runs PositionAnalysis.Cli.exe --process-all
    on a configurable daily schedule. Runs as SYSTEM so no user login is required.
    Must be run as Administrator.

.PARAMETER ExePath
    Full path to the published PositionAnalysis.Cli.exe.
    Example: C:\apps\PositionAnalysis\publish\PositionAnalysis.Cli.exe

.PARAMETER TaskName
    Name of the scheduled task. Default: PositionAnalysis-ProcessAll

.PARAMETER TaskFolder
    Task Scheduler folder. Default: \PositionAnalysis

.PARAMETER RunAt
    Time of day to run (24-hour). Default: 02:00 (2 AM)

.PARAMETER RunAsUser
    Account to run under. Default: SYSTEM

.EXAMPLE
    .\Register-ScheduledTask.ps1 -ExePath "C:\apps\PositionAnalysis\publish\PositionAnalysis.Cli.exe"

.EXAMPLE
    .\Register-ScheduledTask.ps1 `
        -ExePath "C:\apps\PositionAnalysis\publish\PositionAnalysis.Cli.exe" `
        -RunAt "03:30" `
        -TaskName "PositionAnalysis-NightlyScore"
#>
param(
    [Parameter(Mandatory)]
    [string] $ExePath,

    [string] $TaskName   = "PositionAnalysis-ProcessAll",
    [string] $TaskFolder = "\PositionAnalysis",
    [string] $RunAt      = "17:00",
    [string] $RunAsUser  = "SYSTEM"
)

$ErrorActionPreference = "Stop"

# Validate the exe exists
if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found: $ExePath`nPublish the CLI first with: dotnet publish src/PositionAnalysis.Cli/PositionAnalysis.Cli.csproj -c Release -r win-x64 --self-contained"
    exit 1
}

$workingDir = Split-Path $ExePath -Parent

Write-Host "Registering scheduled task..." -ForegroundColor Cyan
Write-Host "  Exe:     $ExePath"
Write-Host "  Trigger: Daily at $RunAt"
Write-Host "  User:    $RunAsUser"

# Action: run the exe with --process-all
$action = New-ScheduledTaskAction `
    -Execute $ExePath `
    -Argument "--process-all" `
    -WorkingDirectory $workingDir

# Trigger: daily at the specified time
$trigger = New-ScheduledTaskTrigger -Daily -At $RunAt

# Settings: run whether logged on or not, restart on failure, 4-hour execution time limit
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Hours 4) `
    -RestartCount 2 `
    -RestartInterval (New-TimeSpan -Minutes 10) `
    -StartWhenAvailable `
    -RunOnlyIfNetworkAvailable

# Principal: run as SYSTEM (or specified user) with highest privileges
$principal = New-ScheduledTaskPrincipal `
    -UserId $RunAsUser `
    -LogonType ServiceAccount `
    -RunLevel Highest

# Register (or update if already exists)
$fullTaskPath = "$TaskFolder\$TaskName"

$existingTask = Get-ScheduledTask -TaskPath "$TaskFolder\" -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Write-Host "  Task already exists — updating..." -ForegroundColor Yellow
    Set-ScheduledTask -TaskPath $TaskFolder -TaskName $TaskName `
        -Action $action -Trigger $trigger -Settings $settings -Principal $principal | Out-Null
} else {
    Register-ScheduledTask `
        -TaskPath $TaskFolder `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Principal $principal | Out-Null
}

Write-Host ""
Write-Host "[OK] Task registered: $fullTaskPath" -ForegroundColor Green
Write-Host ""
Write-Host "To run immediately:" -ForegroundColor Cyan
Write-Host "  Start-ScheduledTask -TaskPath '$TaskFolder\' -TaskName '$TaskName'"
Write-Host ""
Write-Host "To view task history:" -ForegroundColor Cyan
Write-Host "  Get-ScheduledTaskInfo -TaskPath '$TaskFolder\' -TaskName '$TaskName'"
