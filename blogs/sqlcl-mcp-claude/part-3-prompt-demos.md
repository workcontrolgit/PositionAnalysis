# Talk to Oracle with Claude AI — Part 3: Prompt Demos

Parts 1 and 2 set up the tooling. Now it's time to use it. This part walks
through real prompts against the live Docker HR schema — showing exactly what
you type, what Claude does, and what comes back.

All demos use the `hr_local` connection to the Docker Oracle XE database
from Part 1.

**Series:**
- Part 1: Setup — VS Code extension, Docker HR schema
- Part 2: MCP Server Configuration + Claude Skills
- Part 3: Prompt Demos — Querying Oracle in Plain English ← you are here
- Part 4: C# Console Agent with Microsoft.Extensions.AI

← [Part 2: MCP Server + Claude Skills](#)

---

## Demo 1: Connect to the Database

**You type:**

```text
connect to oracle hr schema
```

**What Claude does:** Calls `connections_list` to find saved connections,
then calls `connect` with `hr_local`, then calls `schema_information` to
summarize what's available.

**Claude responds:**

```text
Connected to Oracle 21.3.0.0.0 as HR (hr_local).

HR schema objects:

Tables (8): EMPLOYEES, DEPARTMENTS, JOBS, JOB_HISTORY,
            LOCATIONS, COUNTRIES, REGIONS, DBTOOLS$MCP_LOG
```

One prompt. No connection string. No credentials in the chat.

---

## Demo 2: Explore the Schema

**You type:**

```text
show schema
```

**What Claude does:** Calls `schema_information` and presents the results
organized by object type.

**Result:**

```
TABLE     EMPLOYEES, DEPARTMENTS, JOBS, JOB_HISTORY,
          LOCATIONS, COUNTRIES, REGIONS, DBTOOLS$MCP_LOG
INDEX     9 indexes
SEQUENCE  1 sequence
```

Claude organizes and explains the schema — you don't need to know which
system views to query.

> 💡 `DBTOOLS$MCP_LOG` is an internal SQLcl MCP logging table created
> automatically when the MCP server starts. It's not part of the HR sample
> schema and can be ignored for data queries.

---

## Demo 3: Query the Employees Table

**You type:**

```text
show me records in employees table
```

**SQL Claude runs:**

```sql
SELECT EMPLOYEE_ID, FIRST_NAME, LAST_NAME, JOB_ID, SALARY, DEPARTMENT_ID
FROM employees
ORDER BY EMPLOYEE_ID
```

**Result:**

```
ID   First Name  Last Name  Job       Salary  Dept
---  ----------  ---------  --------  ------  ----
100  Steven      King       AD_PRES   24,000    10
101  Neena       Kochhar    AD_VP     17,000    10
102  Lex         De Haan    AD_VP     17,000    10
103  Alexander   Hunold     IT_PROG    9,000    60
104  Bruce       Ernst      IT_PROG    6,000    60
```

You asked in plain English. Claude figured out the table name, column
selection, and ordering. The Docker HR schema in this repo ships with
5 employees — a trimmed dataset focused on demonstrating the schema
structure rather than data volume.

---

## Demo 4: Understand Table Constraints

**You type:**

```text
/oracle-table-constraints employees
```

**SQL Claude runs:**

```sql
SELECT uc.constraint_name, uc.constraint_type, ucc.column_name,
       uc.r_constraint_name, uc.search_condition
FROM user_constraints uc
JOIN user_cons_columns ucc ON uc.constraint_name = ucc.constraint_name
WHERE uc.table_name = 'EMPLOYEES'
ORDER BY uc.constraint_type, uc.constraint_name, ucc.position
```

**Claude explains:**

**Primary Key**
- `SYS_C008318` — EMPLOYEE_ID — Unique row identifier

**Unique**
- `SYS_C008319` — EMAIL — No duplicate email addresses

**Foreign Keys**
- `FK_EMPLOYEES_DEPT` — DEPARTMENT_ID → DEPARTMENTS
- `FK_EMPLOYEES_JOB` — JOB_ID → JOBS
- `FK_EMPLOYEES_MGR` — MANAGER_ID → EMPLOYEES (self-reference)

**NOT NULL checks**
- `SYS_C008314` — FIRST_NAME — Required
- `SYS_C008315` — LAST_NAME — Required
- `SYS_C008316` — EMAIL — Required
- `SYS_C008317` — JOB_ID — Required

> 💡 `FK_EMPLOYEES_MGR` is a self-referencing foreign key — `MANAGER_ID`
> points back to `EMPLOYEE_ID` on the same table. This is how the org
> hierarchy is modelled: an employee's manager is also an employee.
> Claude calls this out explicitly and explains its significance.

---

## Demo 5: Map the Full Schema Relationships

**You type:**

```text
/oracle-table-relationships
```

**What Claude does:** Queries `user_constraints` joined with
`user_cons_columns` and `user_tables` to find all FK relationships across
the schema, then draws a relationship hierarchy.

**Claude responds:**

```text
REGIONS
  └── COUNTRIES.REGION_ID

COUNTRIES
  └── LOCATIONS.COUNTRY_ID

LOCATIONS
  └── DEPARTMENTS.LOCATION_ID

DEPARTMENTS
  ├── EMPLOYEES.DEPARTMENT_ID
  └── JOB_HISTORY.DEPARTMENT_ID

JOBS
  ├── EMPLOYEES.JOB_ID
  └── JOB_HISTORY.JOB_ID

EMPLOYEES ──(self)── EMPLOYEES.MANAGER_ID → EMPLOYEE_ID
  └── JOB_HISTORY.EMPLOYEE_ID
```

One prompt gives you a complete picture of the HR schema hierarchy — the
kind of diagram that would take 20 minutes to write manually from
`user_constraints` queries.

The hierarchy shows the canonical HR schema chain:
**REGIONS → COUNTRIES → LOCATIONS → DEPARTMENTS → EMPLOYEES → JOB_HISTORY**
with JOBS branching into both EMPLOYEES and JOB_HISTORY.

---

## Demo 6: Export Query Results

**You type:**

```text
export employees to csv
```

**What Claude does:** Uses the `oracle-export` skill, which runs SQLcl's
`SPOOL` command with `SQLFORMAT csv` to write the output directly to a file.

**SQLcl commands Claude runs:**

```sql
SET SQLFORMAT csv
SPOOL employees_export.csv
SELECT * FROM employees;
SPOOL OFF
SET SQLFORMAT default
```

**Result:** `employees_export.csv` is created in the project directory.

```text
"EMPLOYEE_ID","FIRST_NAME","LAST_NAME","EMAIL","PHONE_NUMBER","HIRE_DATE","JOB_ID","SALARY","COMMISSION_PCT","MANAGER_ID","DEPARTMENT_ID"
"100","Steven","King","SKING","515.123.4567","2003-06-17","AD_PRES","24000",,,"10"
"101","Neena","Kochhar","NKOCHHAR","515.123.4568","2005-09-21","AD_VP","17000",,"100","10"
"102","Lex","De Haan","LDEHAAN","515.123.4589","2001-01-13","AD_VP","17000",,"100","10"
"103","Alexander","Hunold","AHUNOLD","590.423.4567","2006-01-03","IT_PROG","9000",,"102","60"
"104","Bruce","Ernst","BERNST","590.423.4568","2007-05-21","IT_PROG","6000",,"103","60"
```

Standard CSV format — ready for Excel, Pandas, or any data tool. To export
as Excel instead, swap `csv` for `xlsx` in the prompt:

```text
export employees to excel
```

---

## Prompt Reference

Here are prompts you can use directly against the HR schema:

### Schema Exploration

```text
show schema
oracle-table-relationships
oracle-search-tables EMP
oracle-search-columns SALARY
```

### Table Details

```text
oracle-table-schema employees
oracle-table-constraints employees
oracle-table-indexes employees
```

### Data Queries

```text
show me all employees in department 60
what is the average salary by department?
who are the managers (employees with direct reports)?
show me job history for employee 101
```

### Export

```text
export employees to csv
export departments to excel
```

### Database Info

```text
what oracle version is this?
oracle-database-info
```

> 💡 You don't need the skill name prefix for most queries — Claude will
> use the right MCP tools automatically based on your plain-English request.
> The skill prefix (`oracle-table-constraints`, `oracle-table-relationships`)
> gives Claude focused guidance for schema-specific tasks and improves
> accuracy on structured output like constraint tables and relationship maps.

---

## Wrapping Up

This series showed how to:

1. Install SQLcl via the Oracle SQL Developer VS Code extension
2. Spin up a local Oracle HR schema with Docker
3. Configure Claude Code to use the SQLcl MCP server
4. Query Oracle in plain English using Claude skills

The pattern works for any Oracle database — swap the Docker connection
for your dev, staging, or prod instance and the same skills and prompts apply.
No changes to your MCP config, no new skills to write. Just update
`hr_local` with a saved connection pointing at your target database.

The full source — Docker setup, `.mcp.json`, and all Claude skills — is
available at: [github.com/workcontrolgit/oracle-sqlcl-ai-skills](https://github.com/workcontrolgit/oracle-sqlcl-ai-skills)
