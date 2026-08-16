# PositionAnalysis.Cli

Interactive AI-powered console client for Schedule Policy/Career position evaluation.

Launches `PositionAnalysis.Mcp` as a child process over stdio and drives it through MCP
tool calls. You never run the MCP server directly.

## Running

```powershell
# From source
cd src\PositionAnalysis.Cli
dotnet run

# From a published build
.\PositionAnalysis.Cli.exe
```

## Startup menu

The CLI shows a numbered menu on startup. Type a number to activate an option, or type any
question in plain English. Press `?` to re-display the menu at any time.

```
 Report Status          Evaluate               Generate Word
 1  All series          5  All pending          9  All
 2  By series           6  By series           10  By series
 3  By org code         7  By org code         11  By org code
 4  By PD number        8  By PD number        12  By PD number

 Export Excel           Manage
13  All                17  Stage PDs
14  By series          18  Clear staged PDs
15  By org code        19  Reset failed → staged
16  By PD number       20  Run unattended scoring
                       21  Unattended queue status
                       22  Re-score by PD number
                       23  Re-score by series
```

For full usage instructions see
[docs/instructions/how-to-positionanalysis-interactive-chat.md](../../docs/instructions/how-to-positionanalysis-interactive-chat.md).

## Unattended mode

Run scoring without the interactive chat:

```powershell
.\PositionAnalysis.Cli.exe --unattended
```

Scores all currently PENDING PDs and exits. Exit codes: `0` = success, `1` = one or more
failed PDs, `2` = cancelled.

## Configuration

`appsettings.json` — AI provider for the chat assistant (`AI:*`). Secrets via
`dotnet user-secrets` (dev) or machine environment variables (server).

```powershell
dotnet user-secrets set "AI:AzureOpenAI:Endpoint"       "https://<resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "AI:AzureOpenAI:DeploymentName" "<deployment>"
dotnet user-secrets set "AI:AzureOpenAI:ApiKey"         "<key>"
```

## Purpose

EO "Implementing Schedule Policy/Career in the Excepted Service" (June 3, 2026)
Legal basis: 5 U.S.C. § 7511(b)(2); 5 U.S.C. § 3302
