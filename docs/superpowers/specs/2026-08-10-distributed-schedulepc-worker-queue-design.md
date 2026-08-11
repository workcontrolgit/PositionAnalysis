# Design: Distributed Schedule PC Scoring Workers

## Goal

Allow any number of scheduled SchedulePC workers, including servers and authorized workstations, to score the shared Oracle `SCHEDULE_PC_EVAL` queue without evaluating the same position description twice.

## Scope

- Add an unattended `SchedulePC --process-all` execution mode.
- Replace the current read-all-then-score behavior with Oracle-backed, one-row-at-a-time work claims.
- Recover work abandoned by a terminated worker after a lease expires.
- Keep staging, document generation, and Excel export as desktop-only workflows.

## Non-Goals

- Stage records from a scheduled worker.
- Generate Word forms or Excel trackers from a scheduled worker.
- Partition workers by occupational series.
- Increase per-worker scoring concurrency beyond one PD at a time.

## Queue Contract

`SCHEDULE_PC_EVAL` becomes the shared queue. The schema gains:

- `WORKER_ID VARCHAR2(128)`: hostname and process/run identifier of the worker that claimed a row.
- `CLAIMED_AT TIMESTAMP`: time at which the row became `IN_PROGRESS`.
- `LEASE_EXPIRES_AT TIMESTAMP`: time after which an unfinished claim is eligible for recovery.

A worker claims exactly one row by selecting a `PENDING` row using `FOR UPDATE SKIP LOCKED`, setting it to `IN_PROGRESS`, assigning its worker ID and lease expiration, and committing before it invokes the Azure OpenAI model. The `SKIP LOCKED` clause ensures that concurrent workers do not wait on or receive the same row.

The lease duration is fifteen minutes. This is substantially longer than the observed scoring duration while allowing another worker to recover a row after a crash or connectivity failure. Before claiming new work, each worker returns rows with expired leases to `PENDING` and clears their claim metadata.

Completing or failing a claimed row must clear `WORKER_ID` and `LEASE_EXPIRES_AT`. A worker may only complete its own claim: the update predicate includes both the PD identity and its worker ID. A mismatched or expired lease is treated as a lost claim and must not overwrite a later worker's result.

## Application Flow

`ISchedulePCEvalRepository` exposes operations to recover expired work and claim a single pending evaluation. `ScoringOrchestrator.ScoreAllAsync` creates one stable worker ID for its run, recovers expired claims, then repeatedly claims, scores, and completes/fails a row until no work remains.

The MCP `process_all_pds` tool remains asynchronous for desktop chat behavior. The new `SchedulePC --process-all` mode starts its MCP child, requests `process_all_pds`, polls `get_processing_status`, and exits only after the shared queue has no `PENDING` or `IN_PROGRESS` rows. It exits non-zero if the final report contains failed rows.

## Deployment Model

Deploy the same published application to each approved server or workstation. Every machine runs the same scheduled command and connects to the same Oracle database and Azure OpenAI deployment. Each task runs a single worker process; horizontal capacity is increased by enabling the task on another machine.

Secrets are supplied to the scheduled-task identity through protected configuration or environment variables and are never copied into source-controlled settings. Each worker needs network access to Oracle and Azure OpenAI. The practical worker limit is Azure OpenAI quota, Oracle connection capacity, and cost.

## Failure Handling

- A scoring exception records `FAILED` and clears the claim.
- A terminated process leaves the row `IN_PROGRESS` until its lease expires; a later worker recovers it.
- No work is available when the claim operation returns no row. The worker exits successfully unless final status reports failed rows.
- A worker never overwrites a result it no longer owns.

## Validation

Automated tests will cover exclusive claims, expired-lease recovery, claim ownership on completion, and unattended-mode exit behavior. A controlled Oracle validation will run two workers against a small staged set and verify each PD is scored once. Existing full MCP tests and builds remain required.