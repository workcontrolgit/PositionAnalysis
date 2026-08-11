- [x] Verify that the copilot-instructions.md file in the .github directory is created.
Summary: Created and maintained this file at .github/copilot-instructions.md.

- [x] Clarify Project Requirements
Summary: User requested a new Docker Oracle HR Schema workspace setup.

- [x] Scaffold the Project
Summary: Added docker-compose.yml, initialization SQL scripts in init-scripts/, schema docs, and VS Code tasks.

- [x] Customize the Project
Summary: Implemented HR schema creation and seed data for core entities (regions, countries, locations, departments, jobs, employees, job_history).

- [x] Install Required Extensions
Summary: No extensions were specified by project setup information.

- [x] Compile the Project
Summary: Validated docker-compose.yml via `docker compose config` successfully.

- [x] Create and Run Task
Summary: Created and executed task `Docker: Oracle Up (Generated)`. Task execution failed because Docker daemon is not running on this machine.

- [x] Launch the Project
Summary: Launch attempt was made through task execution but blocked by inactive Docker daemon.

- [x] Ensure Documentation is Complete
Summary: README.md exists and includes setup/run/reset instructions. This file has been cleaned of HTML comments.

- Work through each checklist item systematically.
- Keep communication concise and focused.
- Follow development best practices.
- Always use the Oracle MCP (SQLcl MCP server / oracle-sql-query and related
  skills) to query Oracle data. Do not use PowerShell scripts or the sqlcl/SQL
  client CLI to run ad hoc queries.
