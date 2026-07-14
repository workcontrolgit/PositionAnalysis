
# Oracle SQLcl Skills

**GitHub:** [github.com/workcontrolgit/oracle-sqlcl-ai-skills](https://github.com/workcontrolgit/oracle-sqlcl-ai-skills)

Claude AI skills for working with Oracle databases via the SQLcl MCP server. Includes a local Oracle XE HR schema in Docker for development and testing.

## Prerequisites

- Docker Desktop (Windows) with Linux containers enabled
- Docker Compose v2
- .NET 10 SDK (for the C# agent in `src/OracleSqlclAgent/`)

## Quick Start

1. Start Oracle XE and run schema initialization:

   ```powershell
   docker compose up -d
   ```

2. Follow container logs until database is ready:

   ```powershell
   docker compose logs -f oracle
   ```

3. Connect as HR user:

   - Host: `localhost`
   - Port: `1521`
   - Service: `XEPDB1`
   - Username: `hr`
   - Password: `HrUser_2026`

## Credentials

Default users and passwords in this workspace:

- `SYSTEM` user password: `OracleSys_2026`
- `HR` user password: `HrUser_2026`

How these passwords are set:

- `SYSTEM` (`SYS`/`SYSTEM`) password comes from `ORACLE_PASSWORD` in `docker-compose.yml`.
- `HR` password is set in `init-scripts/01-create-hr-user.sql` (`CREATE USER hr IDENTIFIED BY HrUser_2026`).
- `start-scripts/00-ensure-hr.sh` checks HR schema at container startup and runs the bootstrap SQL when HR is missing/incomplete, which also sets `HR` password from `01-create-hr-user.sql`.

Important behavior:

- `ORACLE_PASSWORD` is applied on first database initialization. If the volume already exists, Oracle ignores this env var on startup.
- To apply changed init/start scripts from scratch, run a full reset:

```powershell
docker compose down -v
docker compose up -d
```

How to change passwords without deleting data:

- Change `SYSTEM` password in running container:

```powershell
docker exec -it oracle-hr resetPassword <new-system-password>
```

- Change `HR` password in running DB:

```powershell
@"
ALTER USER hr IDENTIFIED BY <new-hr-password>;
EXIT;
"@ | docker exec -i oracle-hr sqlplus -s system/OracleSys_2026@//localhost:1521/XEPDB1
```

## Useful Commands

Start services:

```powershell
docker compose up -d
```

Stop services:

```powershell
docker compose down
```

Reset database (delete all persisted data and re-run init scripts):

```powershell
docker compose down -v
docker compose up -d
```

Run an SQL query from inside the container:

```powershell
docker exec -it oracle-hr sqlplus hr/HrUser_2026@//localhost:1521/XEPDB1
```

## SQLcl MCP Server

This project uses the **Oracle SQLcl MCP Server** (built into SQLcl 24+) to give Claude AI direct, structured access to the Oracle database — no Bash piping required.

### SQLcl Location

SQLcl is bundled with the VS Code Oracle SQL Developer extension:

```
C:\Users\<you>\.vscode\extensions\oracle.sql-developer-<version>-win32-x64\dbtools\sqlcl\bin\sql.exe
```

### MCP Server Tools

| Tool | Description | Parameters |
|------|-------------|------------|
| `list-connections` | List all saved Oracle connections | none |
| `connect` | Connect to a named saved connection | `connection` (name), `model`, `mcp_client` |
| `disconnect` | Close the current connection | none |
| `run-sql` | Execute SQL queries and PL/SQL blocks | `sql` (the SQL to execute) |
| `run-sqlcl` | Execute SQLcl-specific commands (SET, DDL, Liquibase) | `sql` (the SQLcl command) |

### MCP Server Setup (one-time)

1. **Save the HR connection** in SQLcl so the MCP server can connect automatically:

   ```powershell
   $SQLCL = "C:\Users\<you>\.vscode\extensions\oracle.sql-developer-<version>-win32-x64\dbtools\sqlcl\bin\sql.exe"
   "conn -save hr_local -savepwd hr/HrUser_2026@//localhost:1521/XEPDB1`nexit" | & $SQLCL /nolog
   ```

   Verify it works:

   ```powershell
   "conn -name hr_local`nSELECT user FROM dual;`nexit" | & $SQLCL /nolog
   ```

2. **MCP server config** is already in `.mcp.json` at the project root — Claude Code picks it up automatically on next session start.

   > **Note:** `.mcp.json` is the project-scoped MCP config (checked into git, shared with the team). Claude Code reads this file at startup. If the server doesn't appear in `/mcp`, reload the VS Code window (`Ctrl+Shift+P` → *Developer: Reload Window*) and approve the server trust prompt when asked.

   The SQLcl executable path in `.mcp.json` must match your local installation. Update it if your VS Code extension version differs:

   ```json
   {
     "mcpServers": {
       "sqlcl": {
         "type": "stdio",
         "command": "C:\\Users\\<you>\\.vscode\\extensions\\oracle.sql-developer-<version>-win32-x64\\dbtools\\sqlcl\\bin\\sql.exe",
         "args": ["-mcp"]
       }
     }
   }
   ```

### Claude AI Skills

The `.claude/skills/` folder contains Claude AI skills that use the MCP tools above:

| Skill | MCP Tools Used | Purpose |
|-------|---------------|---------|
| `oracle-database-info` | `list-connections`, `run-sql` | Database version, schema metadata, list saved connections |
| `oracle-sql-query` | `connect`, `run-sql`, `run-sqlcl`, `disconnect` | Run any SQL — HR data, custom queries, schema exploration |
| `oracle-search-tables` | `connect`, `run-sql`, `disconnect` | Find tables by name pattern |
| `oracle-search-columns` | `connect`, `run-sql`, `disconnect` | Find columns across all tables |
| `oracle-table-schema` | `connect`, `run-sql`, `run-sqlcl`, `disconnect` | Describe table structure (DESC + metadata) |
| `oracle-table-constraints` | `connect`, `run-sql`, `disconnect` | View PK/FK/check constraints |
| `oracle-table-indexes` | `connect`, `run-sql`, `disconnect` | View indexes and indexed columns |
| `oracle-table-relationships` | `connect`, `run-sql`, `disconnect` | Explore foreign key relationships |
| `oracle-export` | Bash → SQLcl directly | Export query results to CSV or Excel file |

### PowerShell Automation Scripts

The `scripts/oracle/` folder contains PowerShell scripts for CI/CD and dev automation:

| Folder | Purpose |
|--------|---------|
| `scripts/oracle/tier2/` | Dev support — migration status, schema conflicts, reset, permissions |
| `scripts/oracle/tier3/` | CI/CD automation — schema drift, pre-deploy checks, env sync |
| `scripts/oracle/shared/` | Shared PowerShell modules (OracleConnection, SchemaInspector, OutputFormatter) |
| `scripts/oracle/tests/` | Pester unit tests |
| `scripts/oracle/config/` | Environment configuration templates |

## C# Console Agent

`src/OracleSqlclAgent/` is a standalone .NET 10 console app that connects to
the SQLcl MCP server and answers Oracle questions in plain English — no Claude
Code required.

### Quick Start

```bash
cd src/OracleSqlclAgent

# Set machine-specific values in user secrets (not committed to git)
dotnet user-secrets set "SqlclMcp:Path" "C:\Users\<you>\.vscode\extensions\oracle.sql-developer-<version>-win32-x64\dbtools\sqlcl\bin\sql.exe"
dotnet user-secrets set "AI:Provider" "Anthropic"
dotnet user-secrets set "AI:Anthropic:ApiKey" "<your-api-key>"

dotnet run
```

### Switching to Local Ollama

```bash
dotnet user-secrets set "AI:Provider" "Ollama"
dotnet user-secrets set "AI:Ollama:Model" "gemma4:latest"
dotnet user-secrets set "AI:Ollama:NumCtx" "32768"
dotnet run
```

### Key Dependencies

| Package | Purpose |
|---------|---------|
| `Anthropic` | Claude AI backend |
| `OllamaSharp` | Local Ollama backend |
| `Microsoft.Extensions.AI` | `IChatClient` abstraction (works with both) |
| `ModelContextProtocol` | Connects to SQLcl MCP server via stdio |
| `Spectre.Console` | Terminal UI — spinner, styled prompts, three UI styles |
| `Markdig` | Parses markdown responses and renders as terminal widgets |
| `Serilog` | Error-only file logging to `logs/error-*.log` |

### Features

- **Dual AI provider** — Anthropic Claude or local Ollama, switchable via config
- **Three UI styles** — Structured (default), Minimal, Panels
- **Markdown rendering** — Model responses with tables, bold, and code blocks render natively in the terminal
- **5 Oracle skills** — sql-query, table-schema, table-constraints, table-relationships, database-info
- **Error logging** — Errors go to `logs/error-*.log`; nothing interrupts the TUI

---

## Project Structure

- `docker-compose.yml`: Oracle XE container configuration.
- `init-scripts/`: SQL scripts used for HR bootstrap.
- `start-scripts/`: Startup scripts executed on every container start to ensure HR schema exists.
- `src/OracleSqlclAgent/`: Standalone .NET 10 C# agent using SQLcl MCP + Microsoft.Extensions.AI.
- `scripts/oracle/`: PowerShell automation scripts for migrations, schema validation, and CI/CD.
- `.claude/skills/`: Claude AI skills for querying Oracle via MCP tools.
- `docs/schema-overview.md`: HR schema entities and relationships.
- `.vscode/tasks.json`: VS Code tasks for common Docker operations.

## Notes

- Startup scripts verify `HR` schema exists on each start and bootstrap it if missing.
- To apply script changes, use a full reset (`docker compose down -v`).
