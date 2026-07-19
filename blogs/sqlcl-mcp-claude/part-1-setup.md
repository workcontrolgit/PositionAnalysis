# Talk to Oracle with Claude AI — Part 1: Setup

The Oracle SQL Developer extension for VS Code ships with a full SQLcl binary.
That binary includes a built-in MCP server — which means Claude AI can talk
directly to your Oracle database without any middleware, scripts, or API wrappers.

By the end of this series, you'll type a plain-English question in Claude and
get back live Oracle data. This first part gets the tooling running.

**Series:** [Talk to Oracle with Claude AI](https://medium.com/scrum-and-coke/talk-to-oracle-with-claude-ai-series-preface-19b31fdb782e)

---

## What is SQLcl?

SQLcl (SQL Developer Command Line) is Oracle's modern CLI replacement for SQL*Plus.
It supports scripting, Liquibase migrations, and — since version 24 — a built-in
**MCP server** that exposes Oracle database tools over the Model Context Protocol.

This means AI clients that support MCP (like Claude Code) can use SQLcl as a
structured bridge to your Oracle database.

You don't need to install SQLcl separately. The VS Code Oracle SQL Developer
extension bundles it.

---

## Install the Oracle SQL Developer Extension

1. Open VS Code
2. Go to the Extensions panel (`Ctrl+Shift+X`)
3. Search for **Oracle SQL Developer**
4. Install the extension published by **Oracle**

After install, SQLcl is available at:

```text
C:\Users\<you>\.vscode\extensions\oracle.sql-developer-<version>-win32-x64\dbtools\sqlcl\bin\sql.exe
```

Verify it works:

```powershell
$SQLCL = "C:\Users\<you>\.vscode\extensions\oracle.sql-developer-<version>-win32-x64\dbtools\sqlcl\bin\sql.exe"
& $SQLCL -V
# Output: SQLcl: Release 26.x.x.x Production Build: ...
```

> 💡 SQLcl is not added to your system PATH. Always use the full path when calling it from scripts.

---

## Clone the Demo Repo

This series uses a GitHub repo that provides a Docker Oracle XE instance
pre-loaded with the HR sample schema.

```bash
git clone https://github.com/workcontrolgit/oracle-sqlcl-ai-skills.git
cd oracle-sqlcl-ai-skills
```

The repo contains:
- `docker-compose.yml` — Oracle XE container config
- `init-scripts/` — HR schema SQL (tables, data, constraints)
- `start-scripts/` — Startup verification scripts
- `.mcp.json` — SQLcl MCP server config for Claude Code
- `.claude/skills/` — Claude AI skills for querying Oracle

---

## Spin Up Oracle XE with Docker

Prerequisites: Docker Desktop with Linux containers enabled.

```powershell
docker compose up -d
```

Follow logs until you see the database is ready:

```powershell
docker compose logs -f oracle
```

Wait for:
```
DATABASE IS READY TO USE!
```

This usually takes 2–3 minutes on first run (Oracle initializes the database files).

### What Gets Created

The init scripts automatically create:
- `HR` user with password `HrUser_2026`
- 7 tables: EMPLOYEES, DEPARTMENTS, JOBS, JOB_HISTORY, LOCATIONS, COUNTRIES, REGIONS
- Foreign key relationships and sample data

### Credentials

> ⚠️ These are the default credentials from the Docker Compose file. Change them before any non-local use.

- **HR** — `HrUser_2026` — Schema owner, use for queries
- **SYSTEM** — `OracleSys_2026` — Admin, use for DBA operations

To change them, open `docker-compose.yml` and update the `environment` block:

```yaml
environment:
  ORACLE_PASSWORD: OracleSys_2026   # SYSTEM password
  APP_USER: hr
  APP_USER_PASSWORD: HrUser_2026    # HR password
```

After editing, do a full reset so Oracle picks up the new values:

```powershell
docker compose down -v
docker compose up -d
```

> ⚠️ `ORACLE_PASSWORD` is only applied on first database initialization. If the volume already exists, Oracle ignores it — you must delete the volume (`down -v`) for the change to take effect.

Connection details:
- Host: `localhost`
- Port: `1521`
- Service: `XEPDB1`

---

## Save the HR Connection in SQLcl

The SQLcl MCP server uses named saved connections — you connect by name, not
by typing credentials every time. Save the HR connection once:

```powershell
$SQLCL = "C:\Users\<you>\.vscode\extensions\oracle.sql-developer-<version>-win32-x64\dbtools\sqlcl\bin\sql.exe"

# Save the connection (only needs to be done once)
"conn -save hr_local -savepwd hr/HrUser_2026@//localhost:1521/XEPDB1`nexit" | & $SQLCL /nolog
```

Verify it connects:

```powershell
"conn -name hr_local`nSELECT user FROM dual;`nexit" | & $SQLCL /nolog
```

Expected output:
```
Connected.

USER
-------
HR
```

> ✅ The connection `hr_local` is now saved locally. Claude will use this name
> to connect when you ask it to query the database.

---

## What's Next

In Part 2, you'll configure the SQLcl MCP server so Claude Code can discover
and use it, then install the Oracle query skills that turn plain-English
prompts into live database results.

→ [Part 2: MCP Server Configuration + Claude Skills](#)
