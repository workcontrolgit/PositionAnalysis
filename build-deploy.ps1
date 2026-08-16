#Requires -Version 5.1
<#
.SYNOPSIS
    Builds PositionAnalysis for deployment.

.DESCRIPTION
    Publishes PositionAnalysis.Mcp and PositionAnalysis.Cli in Release mode,
    outputs to C:\deploy, and copies the scripts folder alongside the binaries.

.PARAMETER OutputPath
    Destination folder. Defaults to C:\deploy.

.EXAMPLE
    .\build-deploy.ps1
    .\build-deploy.ps1 -OutputPath D:\releases\positionanalysis
#>
param(
    [string]$OutputPath = "C:\deploy"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root      = $PSScriptRoot
$McpProj   = Join-Path $Root "src\PositionAnalysis.Mcp\PositionAnalysis.Mcp.csproj"
$CliProj   = Join-Path $Root "src\PositionAnalysis.Cli\PositionAnalysis.Cli.csproj"
$ScriptsDir = Join-Path $Root "scripts"

$McpOut    = Join-Path $OutputPath "mcp"
$CliOut    = Join-Path $OutputPath "cli"
$ScriptsDst = Join-Path $OutputPath "scripts"

function Write-Step($msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

# ── Clean output ─────────────────────────────────────────────────────────────
Write-Step "Cleaning output folder: $OutputPath"
if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Recurse -Force
}
New-Item $OutputPath -ItemType Directory -Force | Out-Null

# ── Publish MCP server ────────────────────────────────────────────────────────
Write-Step "Publishing PositionAnalysis.Mcp → $McpOut"
dotnet publish $McpProj `
    --configuration Release `
    --output $McpOut `
    --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "MCP publish failed (exit $LASTEXITCODE)" }

# ── Publish CLI ───────────────────────────────────────────────────────────────
Write-Step "Publishing PositionAnalysis.Cli → $CliOut"
dotnet publish $CliProj `
    --configuration Release `
    --output $CliOut `
    --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed (exit $LASTEXITCODE)" }

# ── Copy scripts folder ───────────────────────────────────────────────────────
Write-Step "Copying scripts → $ScriptsDst"
Copy-Item $ScriptsDir $ScriptsDst -Recurse -Force

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "  MCP server : $McpOut"
Write-Host "  CLI        : $CliOut"
Write-Host "  Scripts    : $ScriptsDst"
Write-Host ""
