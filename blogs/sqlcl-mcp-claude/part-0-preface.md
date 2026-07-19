# Talk to Oracle with Claude AI — Series Preface

This series shows how to connect Claude AI to a live Oracle database using
**SQLcl's built-in MCP server** — no middleware, no custom APIs, no wrappers.
You ask a plain-English question; Claude runs the SQL and answers you.

The series builds in two distinct layers. Parts 1–3 cover the zero-code path:
wire Claude Code to Oracle and start querying immediately. Part 4 goes further
and builds a standalone C# console agent you can deploy independently.

---

## Who This Series Is For

- .NET developers who want to add AI-powered database querying to their workflow
- Teams evaluating how to expose Oracle data to AI assistants
- Engineers curious about the **Model Context Protocol (MCP)** and how it
  bridges AI models to structured tools
- Anyone who wants a local Oracle sandbox to experiment with AI + SQL

---

## What You'll Need

- **Docker Desktop** — to run Oracle XE locally
- **VS Code** with the Oracle SQL Developer extension — bundles the SQLcl binary
- **Claude Code** — for Parts 1–3 (AI client with built-in MCP support)
- **.NET 10 SDK** — for Part 4 only
- An **AI provider account** for Part 4: Azure AI Foundry (Claude or Azure
  OpenAI) or a local Ollama installation

---

## The Series

### [Part 1: Setup — VS Code Extension, Docker HR Schema](part-1-setup.md)

Get the foundation running. Install the Oracle SQL Developer extension (which
bundles SQLcl), spin up an Oracle XE container with the HR sample schema using
Docker, and save the `hr_local` named connection that the rest of the series
uses. By the end, you have a running Oracle database ready for AI queries.

**Covers:** SQLcl installation, Docker Compose, HR schema, named connections.

---

### [Part 2: MCP Server Configuration + Claude Skills](part-2-mcp-skills.md)

Wire Claude Code to your Oracle database. Configure the `.mcp.json` project
file to start the `sqlcl -mcp` subprocess, then load the nine Oracle Claude
Skills that give Claude focused guidance for specific database tasks. By the
end, Claude Code is connected to Oracle and you can ask it database questions
in plain English.

**Covers:** `.mcp.json`, MCP tool list, Claude Skills, smoke test.

---

### [Part 3: Prompt Demos — Querying Oracle in Plain English](part-3-prompt-demos.md)

Six end-to-end demos against the live Docker HR schema. See exactly what you
type, what SQL Claude generates, and what comes back — from simple SELECTs
to full schema relationship maps to CSV export. Includes a copy-paste prompt
reference for common Oracle tasks.

**Covers:** Schema exploration, table queries, constraint inspection, FK mapping, CSV/Excel export.

---

### [Part 4: C# Console Agent with Microsoft Agent Framework (MAF)](part-4-maf-agent.md)

Build a standalone .NET 10 console application that does everything Parts 1–3
do — without Claude Code. The agent uses `Microsoft.Agents.AI`'s `AIAgent`
class, which wraps an `IChatClient` and handles the full tool-call loop
automatically. Supports three AI backends switchable via config: Azure Claude,
Azure OpenAI, or local Ollama.

**Covers:** `Microsoft.Agents.AI`, `IChatClient`, MCP subprocess, Oracle Skills
as C# classes, `AgentSession`, Spectre.Console TUI, Markdig rendering, Serilog.

---

## How the Pieces Fit Together

```
┌─────────────────────────────────────────────────────────────┐
│                     AI Client Layer                         │
│                                                             │
│   Parts 1–3: Claude Code        Part 4: C# Console Agent   │
│   (Claude Code harness)         (Microsoft.Agents.AI)       │
└──────────────────────────┬──────────────────────────────────┘
                           │  Model Context Protocol (MCP)
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                   SQLcl MCP Server                          │
│                                                             │
│   Tools: connect · sql_run · schema_information             │
│          sqlcl_run · disconnect · connections_list          │
│          request_status                                     │
└──────────────────────────┬──────────────────────────────────┘
                           │  JDBC
                           ▼
┌─────────────────────────────────────────────────────────────┐
│               Oracle XE (Docker)                            │
│                                                             │
│   HR Schema: EMPLOYEES · DEPARTMENTS · JOBS                 │
│              JOB_HISTORY · LOCATIONS · COUNTRIES · REGIONS  │
└─────────────────────────────────────────────────────────────┘
```

The SQLcl MCP server is the common layer. Whether you use Claude Code or a
custom C# agent, the same seven tools are available and the same saved
connections are used. Swap in any Oracle database — dev, staging, prod — by
updating the saved connection. No code changes needed.

---

## The Full Source

Docker setup, `.mcp.json`, Claude Skills, and the C# console agent are all
in one repo:

[github.com/workcontrolgit/oracle-sqlcl-ai-skills](https://github.com/workcontrolgit/oracle-sqlcl-ai-skills)

Start with [Part 1 →](part-1-setup.md)
