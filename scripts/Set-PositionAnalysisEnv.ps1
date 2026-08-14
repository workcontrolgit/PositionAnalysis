#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Sets system-level environment variables for the PositionAnalysis scheduled task.

.DESCRIPTION
    Sets Oracle connection string and Azure OpenAI API key as SYSTEM-level environment
    variables so they are available to Task Scheduler when running unattended.
    Must be run as Administrator.

.PARAMETER OracleConnectionString
    Full Oracle connection string, e.g. hr/password@//server:1521/XEPDB1

.PARAMETER AzureOpenAiApiKey
    Azure OpenAI API key.

.PARAMETER AzureOpenAiEndpoint
    Azure OpenAI endpoint URL. Defaults to the value already in appsettings.json.

.PARAMETER AzureOpenAiDeploymentName
    Azure OpenAI deployment name. Defaults to the value already in appsettings.json.

.EXAMPLE
    .\Set-PositionAnalysisEnv.ps1 `
        -OracleConnectionString "hr/MyPass@//dbserver:1521/XEPDB1" `
        -AzureOpenAiApiKey "abc123"
#>
param(
    [Parameter(Mandatory)]
    [string] $OracleConnectionString,

    [Parameter(Mandatory)]
    [string] $AzureOpenAiApiKey,

    [string] $AzureOpenAiEndpoint = "https://devops-cicd.openai.azure.us/",

    [string] $AzureOpenAiDeploymentName = "gpt-5.1"
)

$ErrorActionPreference = "Stop"

$vars = @{
    "Oracle__ConnectionString"              = $OracleConnectionString
    "AiProvider__AzureOpenAI__ApiKey"       = $AzureOpenAiApiKey
    "AiProvider__AzureOpenAI__Endpoint"     = $AzureOpenAiEndpoint
    "AiProvider__AzureOpenAI__DeploymentName" = $AzureOpenAiDeploymentName
}

Write-Host "Setting system environment variables..." -ForegroundColor Cyan

foreach ($name in $vars.Keys) {
    [System.Environment]::SetEnvironmentVariable($name, $vars[$name], "Machine")
    Write-Host "  [OK] $name" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done. Restart any running Task Scheduler tasks for changes to take effect." -ForegroundColor Yellow
