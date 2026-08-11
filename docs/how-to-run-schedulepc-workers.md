# How to Run Schedule PC Scoring Workers

Run the Schedule PC scoring queue unattended, on a schedule, across one or more machines.

## What `--process-all` does

`SchedulePC.exe --process-all` starts the `SchedulePCMcp` child process, triggers the
`process_all_pds` MCP tool, then polls `get_queue_status` until the queue is drained. It
scores only — it does not stage PDs, generate documents, or export results.

Exit codes:

| Code | Meaning |
|------|---------|
| `0` | Queue drained with no failed PDs |
| `1` | Queue drained with one or more failed PDs, or the run itself failed |
| `2` | Cancelled before normal terminal completion |

## Deployment and Task Scheduler rules

- Deploy the same published `SchedulePC` and `SchedulePCMcp` build to every worker machine.
- Run `src/SchedulePCMcp/Database/ADD_SCHEDULE_PC_WORK_QUEUE.sql` once against the shared
  Oracle schema before enabling a second worker.
- Configure each scheduled task to run `SchedulePC.exe --process-all` under an identity
  with Oracle and Azure OpenAI access.
- Set the task's Start In directory to the deployment root, and do not run staging,
  document generation, or export on workers.
- Start with one worker per machine. Add workers only after confirming Azure OpenAI
  quota and reviewing Serilog logs.
- Inspect `IN_PROGRESS` rows and `lease_expires_at` timestamps when a worker is
  interrupted; the next run recovers only expired claims (default 15-minute lease).

## Scale-out

Any number of workers can run concurrently against the same `schedule_pc_eval` queue.
Each worker claims one `PENDING` row at a time with `FOR UPDATE SKIP LOCKED`, so rows
are never scored twice by workers that are alive and holding a valid lease.

## Recovery

If a worker crashes or is killed mid-claim, its claimed rows remain `IN_PROGRESS` with a
`lease_expires_at` timestamp. Any worker's next `--process-all` run calls
`RecoverExpiredClaimsAsync` first, resetting only rows whose lease has expired back to
`PENDING` before claiming new work. No manual intervention is required unless the lease
window itself needs to be shortened for faster recovery.
