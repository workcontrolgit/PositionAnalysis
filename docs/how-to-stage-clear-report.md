# How To: Stage PDs, Clear PDs, and Show Staging Report

This guide shows how to use the SchedulePC chat client to:

- Stage PD records into `SCHEDULE_PC_EVAL`
- Clear all staged records
- Show the staging report

## 1) Start the chat client

From the repository root (`C:\apps\oracle`):

```powershell
dotnet run --project src/schedulepc/SchedulePC.csproj
```

You should see chat mode with a prompt like:

- `You ›`

## 2) Stage PD records

Type one of these commands in chat.

Stage by series:

```text
stage series 0110
```

Stage by grade range:

```text
stage grade 13-15
```

Stage by series + grade + org:

```text
stage series 0110 grade 13-15 org ABC123
```

Expected result includes:

- `Staging complete. Run: RUN_YYYYMMDD_HHMMSS`
- A staging summary table

## 3) Show staging report

In chat, run:

```text
show stage report
```

You can also use:

```text
staging report
```

Expected columns:

- SERIES
- STAGED
- IN_PROGRESS
- COMPLETE
- FAILED

## 4) Clear all staged PD records

In chat, run:

```text
remove all records
```

You can also use:

```text
clear schedule pc eval
```

Expected result:

- `SCHEDULE_PC_EVAL cleared. Deleted rows: <number>`

## 5) Typical workflow example

```text
remove all records
stage series 0110
show stage report
exit
```

## 6) Optional database verification

After running stage/clear, you can verify row count using Oracle SQLcl MCP or SQL:

```sql
SELECT COUNT(*) AS row_count FROM schedule_pc_eval;
```

## Troubleshooting

- If you get file lock errors (`MSB3021` or `MSB3027`), stop running `SchedulePC` or `SchedulePCMcp` processes and run again.
- If stage fails with database errors, confirm table/view schema compatibility and that Oracle container is running.
