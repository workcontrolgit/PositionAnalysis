# Design: Human Schedule P/C Rescore Tool

## Goal

Provide an MCP tool that evaluates exactly the current human-designated Schedule P/C baseline in `ACRS.TEMP_PD_SCHED_PC`, without processing unrelated staged or pending evaluations.

## Scope

The new MCP tool is named `rescore_human_schedule_pc_pds` and has no input parameters.

It selects PD numbers from the TEMP header source using:

```sql
SELECT pd_nbr
FROM temp_pd_sched_pc
WHERE schedule_pc_ind = 'Y'
ORDER BY pd_nbr
```

The tool requires the selection to contain exactly 90 PDs. If the count differs, it stops before any scoring work and returns an error that names the observed and expected counts.

## Execution

1. The handler requests the current human-designated PD numbers from a new `IPositionDescriptionRepository` query.
2. The handler validates the returned count is 90.
3. It calls the existing `IScoringOrchestrator.ScoreAsync(pdNbr)` once per selected PD, in deterministic PD-number order.
4. It continues after an unexpected per-PD exception, recording that PD as failed.
5. It returns the selected count, successful score count, failed count, and failed PD numbers.

`ScoreAsync` remains responsible for model invocation, result persistence, and routine failures such as unavailable PDs or malformed model responses. The handler only records exceptions that escape `ScoreAsync`.

## Safety Boundaries

- The tool reads `TEMP_PD_SCHED_PC`; it does not enumerate `SCHEDULE_PC_EVAL` pending rows.
- The tool does not invoke `process_all_pds`, `ScoreAllAsync`, `rescore_all_pds`, or series-wide rescoring.
- It cannot silently process a changed source population because the expected count is fixed at 90.
- It does not stage, clear, export, or generate documents.

## Registration

Register the handler as a scoped `IMcpToolHandler` in `SchedulePCMcp/Program.cs`, alongside the existing rescore tools.

## Tests

Focused handler tests will verify:

1. The exact MCP tool name and no-argument schema.
2. Ninety selected PDs are scored once each, in repository order, and the response reports 90 selected and 90 scored.
3. A non-90 selection returns an error and does not score any PD.
4. An exception for one PD is reported while subsequent selected PDs continue to score.

Repository contract coverage will verify that the new source query uses `temp_pd_sched_pc`, filters `schedule_pc_ind = 'Y'`, and orders by `pd_nbr`.